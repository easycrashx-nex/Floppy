using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Floppy.App;

internal static class Program
{
    private static int _checks;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        var app = new Floppy.App.App();
        app.InitializeComponent();
        typeof(Application).GetField("_startupUri", Private)!.SetValue(app, null);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        app.Dispatcher.BeginInvoke(async () =>
        {
            string folder = Path.Combine(Path.GetTempPath(), "floppy-settings-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(folder);
                CheckPersistence(folder);
                await CheckWindows(folder, args.FirstOrDefault());
                Console.WriteLine($"{_checks} settings checks passed (offline; no game or update server)");
                app.Shutdown(0);
            }
            catch (Exception error) { Console.Error.WriteLine(error); app.Shutdown(1); }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        });
        return app.Run();
    }

    private static void CheckPersistence(string folder)
    {
        string path = Path.Combine(folder, "app.json");
        File.WriteAllText(path, """
            {"Width":1250,"LastGame":"fixture","Favorites":{"fixture":["one"]},
             "Shortcuts":{"fixture":{"one":"Ctrl+H"}},"GameFolders":{"fixture":"C:\\fixture"}}
            """);
        var settings = UserSettings.Load(path);
        Check(settings.GetPreferences() == new AppPreferences(true, false, true, "F1", true), "legacy files receive compatible app defaults");
        settings.ApplyPreferences(new(false, true, false, " f11 ", false));
        Check(settings.Save(out _, path), "settings save atomically to isolated file");
        settings = UserSettings.Load(path);
        Check(settings.GetPreferences() == new AppPreferences(false, true, false, "F11", false), "all five app preferences survive restart");
        Check(settings.Favorites["fixture"].Single() == "one" && settings.Shortcuts["fixture"]["one"] == "Ctrl+H"
              && settings.GameFolders["fixture"] == @"C:\fixture" && settings.Width == 1250 && settings.LastGame == "fixture",
              "preferences preserve favorite, shortcuts, game folder, selection and geometry");
        foreach (string? key in new[] { null, "", "F0", "F12", "F13", "Ctrl+F1", "F01", "bad" })
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new { OverlayHotkey = key }));
            Check(UserSettings.Load(path).OverlayHotkey == "F1", "invalid saved overlay key safely falls back: " + key);
        }
        for (int number = 1; number <= 11; number++)
            Check(UserSettings.NormalizeHotkey("f" + number) == "F" + number, "valid function key " + number);
        File.WriteAllText(path, "{invalid");
        Check(UserSettings.Load(path).GetPreferences() == new UserSettings().GetPreferences(), "damaged app settings recover defaults");
        Check(!settings.Save(out string error, Path.Combine(path, "blocked.json")) && error.Length > 0,
            "filesystem failure is reported without overwriting previous file");
    }

    private static async Task CheckWindows(string folder, string? screenshot)
    {
        var owner = new MainWindow(offline: true) { ShowActivated = false, Opacity = 0 };
        owner.Show();
        var updates = new FakeUpdates();
        Set(owner, "_updates", updates);
        var settings = (UserSettings)typeof(MainWindow).GetField("_settings", Private)!.GetValue(owner)!;
        settings.Favorites["fixture"] = new() { "keep" };
        string path = Path.Combine(folder, "window-settings.json");
        int saves = 0;
        string? Save(AppPreferences preferences)
        {
            saves++;
            settings.ApplyPreferences(preferences);
            return settings.Save(out string error, path) ? null : error;
        }

        Check(Named<Button>(owner, "SettingsButton").IsEnabled && Named<MenuItem>(owner, "SettingsMenuItem").IsEnabled,
            "settings are reachable without an installed or connected game");
        Check(typeof(MainWindow).GetField("_updates", Private)!.GetValue(new MainWindow(offline: true)) == null,
            "offline main window never creates update service");
        var window = NewWindow(owner, settings, updates, Save);
        Check(updates.Checks == 0 && updates.Downloads == 0, "opening settings does not check or download updates");
        Check(Named<ComboBox>(window, "OverlayHotkeyChoice").Items.Count == 11, "overlay selection offers F1 through F11, excluding Windows-reserved F12");
        Check(!Named<Button>(window, "DownloadUpdateButton").IsEnabled, "download unavailable without update offer");
        Set(owner, "_imOverlay", true);
        await (Task)Invoke(owner, "OverlayUmschaltenAsync")!;
        Check((bool)typeof(MainWindow).GetField("_imOverlay", Private)!.GetValue(owner)!,
            "owned settings window blocks overlay toggle instead of closing or minimizing it");
        Set(owner, "_imOverlay", false);
        SetControls(window);
        Click(window, "CloseSettingsButton");
        Check(saves == 0 && settings.GetPreferences() == new UserSettings().GetPreferences(), "closing unsaved window preserves all five original settings");
        Check(updates.Subscribers == 0, "closed window unsubscribes update callbacks");

        window = NewWindow(owner, settings, updates, Save);
        SetControls(window);
        Click(window, "SaveSettingsButton");
        Check(saves == 1 && settings.GetPreferences() == new AppPreferences(false, true, false, "F9", false), "save applies exactly the chosen app settings");
        Check(settings.Favorites["fixture"].Single() == "keep" && UserSettings.Load(path).OverlayHotkey == "F9",
            "UI save persists overlay key and preserves unrelated user data");
        Check(Named<TextBlock>(window, "SaveStatus").Text == "Gespeichert.", "successful save gives visible feedback");

        Click(window, "CheckUpdatesButton");
        Check(updates.Checks == 1 && !Named<Button>(window, "CheckUpdatesButton").IsEnabled, "single check action disables repeat clicks");
        updates.CompleteCheck();
        await Drain();
        Check(Named<Button>(window, "DownloadUpdateButton").IsEnabled, "available release enables download");
        await Task.Run(() => updates.SetState(true, "Wird heruntergeladen …", 0.42, false));
        await Drain();
        Check(Named<ProgressBar>(window, "UpdateProgress").Value == 0.42
              && Named<TextBlock>(window, "UpdateStatus").Text == "Wird heruntergeladen …",
              "worker-thread update event refreshes status and real progress");
        Check(!Named<Button>(window, "CheckUpdatesButton").IsEnabled && !Named<Button>(window, "DownloadUpdateButton").IsEnabled,
            "busy service blocks check and download controls");
        updates.SetState(false, "Floppy 1.5.1 ist verfügbar · 84 MB", null, true);
        await Drain();

        if (screenshot != null)
        {
            await Screenshot(window, new Size(700, 870), Path.GetFullPath(screenshot));
            await Screenshot(window, new Size(570, 540), Path.ChangeExtension(Path.GetFullPath(screenshot), "small.png"));
        }
        Click(window, "DownloadUpdateButton");
        await Drain();
        Check(updates.Downloads == 1, "explicit download click is forwarded once");
        Click(window, "ReleasesButton");
        Check(updates.Releases == 1, "release history invokes supplied service only");
        window.Close();

        window = NewWindow(owner, settings, updates, _ => "Speichern fehlgeschlagen: Testfehler");
        Click(window, "SaveSettingsButton");
        Check(Named<TextBlock>(window, "SaveStatus").Text.Contains("Testfehler"), "save error remains visible without closing settings");
        window.Close();

        window = NewWindow(owner, settings, updates, Save, () => false);
        Check(!Named<Button>(window, "DownloadUpdateButton").IsEnabled, "download waits while game installation is running");
        Click(window, "DownloadUpdateButton");
        Check(updates.Downloads == 1, "download handler also rejects busy installation even if invoked directly");
        window.Close();

        Invoke(owner, "RefreshOverlayHotkeyLabels");
        Check(Equals(Named<Button>(owner, "OverlayButton").Content, "Overlay · F9")
              && Equals(Named<Button>(owner, "OverlayCloseButton").Content, "Zurück zum Spiel · F9")
              && Named<TextBlock>(owner, "OverlayHotkeyHint").Text == "F9  Overlay",
              "main view, overlay close and status hint show selected key");
        Check((Key)typeof(MainWindow).GetProperty("OverlayKey", Private)!.GetValue(owner)! == Key.F9,
            "hotkey registration and shortcut reservation use selected key");
        await (Task)Invoke(owner, "TryConnectAsync", true)!;
        Check(!((IpcClient)typeof(MainWindow).GetField("_ipc", Private)!.GetValue(owner)!).Connected,
            "disabled auto-connect returns without connecting or starting a host");

        double oldWidth = owner.Width;
        settings.Width = 1500; settings.Left = 7; settings.Top = 11;
        Invoke(owner, "RestoreWindow");
        Check(owner.Width == oldWidth, "disabled window memory ignores saved geometry");
        Invoke(owner, "ApplyStartupSettings");
        Check(owner.WindowState == WindowState.Minimized && !owner.ShowActivated, "start minimized uses taskbar and does not activate window");
        owner.WindowState = WindowState.Normal;
        Invoke(owner, "ApplyStartupSettings");
        Check(owner.WindowState == WindowState.Normal, "startup preference is applied exactly once");

        Set(owner, "_selectedGame", new SupportedGame { Installed = true, Name = "Fixture", ProductName = "Fixture" });
        updates.SetState(true, "Prüfe Update …", null, false);
        Invoke(owner, "OnMainUpdatesChanged", null, EventArgs.Empty);
        Check(!Named<Button>(owner, "LaunchButton").IsEnabled && !Named<MenuItem>(owner, "RestartElevatedItem").IsEnabled
              && !Named<MenuItem>(owner, "RepairItem").IsEnabled && !Named<MenuItem>(owner, "RestoreItem").IsEnabled,
              "update busy blocks launch, elevated restart, repair and restore");
        updates.SetState(false, "Fertig", null, true);
        Invoke(owner, "OnMainUpdatesChanged", null, EventArgs.Empty);
        Check(Named<Button>(owner, "LaunchButton").IsEnabled && Named<MenuItem>(owner, "RepairItem").IsEnabled,
            "update completion releases operation controls");
        Set(owner, "_selectedGame", null);
        Click(owner, "SettingsButton");
        var opened = (SettingsWindow)typeof(MainWindow).GetField("_settingsWindow", Private)!.GetValue(owner)!;
        Check(opened.Owner == owner && opened.IsVisible, "desktop settings action opens independent owned window");
        opened.Close();
        Named<MenuItem>(owner, "SettingsMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        opened = (SettingsWindow)typeof(MainWindow).GetField("_settingsWindow", Private)!.GetValue(owner)!;
        Check(opened.IsVisible, "overlay tools settings action opens same window without a game selection");

        Invoke(owner, "SaveOverlayPlacement");
        Set(owner, "_imOverlay", true);
        Invoke(owner, "SetOverlayView", true);
        opened.Topmost = true;
        Set(owner, "_installing", true);
        Task prepare = (Task)Invoke(owner, "PrepareUpdateRestartAsync")!;
        Check(!prepare.IsCompleted && !owner.IsEnabled && (bool)typeof(MainWindow).GetField("_closing", Private)!.GetValue(owner)!,
            "update preparation blocks new operations while existing installation drains");
        Set(owner, "_installing", false);
        await prepare;
        Check(!owner.IsEnabled && !(bool)typeof(MainWindow).GetField("_closed", Private)!.GetValue(owner)!,
            "prepared restart keeps app alive until helper accepts handoff");
        Invoke(owner, "CancelUpdateRestart");
        Check(owner.IsEnabled && !(bool)typeof(MainWindow).GetField("_closing", Private)!.GetValue(owner)!,
            "helper failure re-enables existing app without exiting");
        Check(!(bool)typeof(MainWindow).GetField("_imOverlay", Private)!.GetValue(owner)! && opened.IsVisible && !opened.Topmost,
            "failed overlay update returns to desktop and retains visible settings without topmost");
        opened.Close();
        ((DispatcherTimer)typeof(MainWindow).GetField("_poll", Private)!.GetValue(owner)!).Stop();
        Invoke(owner, "StopOverlay");
        Set(owner, "_closing", true);
        Invoke(owner, "CancelUpdateRestart");
        Check((bool)typeof(MainWindow).GetField("_closing", Private)!.GetValue(owner)!,
            "update cancellation never reverses unrelated normal shutdown");
        Set(owner, "_closing", false);
        Invoke(owner, "DisposeSettings");
        Check(updates.Disposed && updates.Subscribers == 0, "final shutdown disposes update service and releases window callbacks");
        owner.Close();
    }

    private static SettingsWindow NewWindow(MainWindow owner, UserSettings settings, FakeUpdates updates,
        Func<AppPreferences, string?> save, Func<bool>? canRestart = null)
    {
        var window = new SettingsWindow(settings, updates, save, canRestart)
        { Owner = owner, ShowActivated = false, Opacity = 0 };
        window.Show();
        return window;
    }
    private static void SetControls(SettingsWindow window)
    {
        Named<CheckBox>(window, "AutoConnectToggle").IsChecked = false;
        Named<CheckBox>(window, "StartMinimizedToggle").IsChecked = true;
        Named<CheckBox>(window, "RememberWindowToggle").IsChecked = false;
        Named<CheckBox>(window, "CheckUpdatesToggle").IsChecked = false;
        Named<ComboBox>(window, "OverlayHotkeyChoice").SelectedItem = "F9";
    }
    private static async Task Screenshot(SettingsWindow window, Size size, string path)
    {
        window.Width = size.Width; window.Height = size.Height;
        await Drain();
        var root = (Grid)window.Content;
        root.UpdateLayout();
        size = root.RenderSize;
        foreach (string button in new[] { "SaveSettingsButton", "CloseSettingsButton" })
        {
            var control = Named<Button>(window, button);
            var bounds = control.TransformToAncestor(root).TransformBounds(new Rect(control.RenderSize));
            Check(bounds.Left >= 0 && bounds.Right <= size.Width && bounds.Top >= 0 && bounds.Bottom <= size.Height,
                button + " remains reachable at " + size);
        }
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var output = File.Create(path); encoder.Save(output);
    }
    private static Task Drain() => Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;
    private static T Named<T>(Window window, string name) where T : class => (T)window.FindName(name);
    private static void Click(Window window, string name) => Named<Button>(window, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, Private)!.SetValue(window, value);
    private static object? Invoke(MainWindow window, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, Private)!.Invoke(window, args);
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
        _checks++; Console.WriteLine("PASS " + message);
    }

    private sealed class FakeUpdates : IFloppyUpdates, IDisposable
    {
        private EventHandler? _changed;
        private TaskCompletionSource? _check;
        public string CurrentVersion => "1.5.0";
        public string Status { get; private set; } = "Noch nicht nach Updates gesucht.";
        public double? Progress { get; private set; }
        public bool CanDownload { get; private set; }
        public bool Busy { get; private set; }
        public int Checks, Downloads, Releases;
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public bool Disposed;
        public event EventHandler? Changed
        { add { _changed += value; } remove { _changed -= value; } }
        public Task CheckAsync()
        {
            Checks++; _check = new(); SetState(true, "Suche nach Updates …", null, false); return _check.Task;
        }
        public void CompleteCheck() { SetState(false, "Floppy 1.5.1 ist verfügbar", null, true); _check!.SetResult(); }
        public Task DownloadAndRestartAsync() { Downloads++; return Task.CompletedTask; }
        public void OpenReleases() => Releases++;
        public void SetState(bool busy, string status, double? progress, bool canDownload)
        { Busy = busy; Status = status; Progress = progress; CanDownload = canDownload; _changed?.Invoke(this, EventArgs.Empty); }
        public void Dispose() => Disposed = true;
    }
}
