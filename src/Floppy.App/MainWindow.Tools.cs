using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Floppy.App;

public partial class MainWindow
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        // Native caption controls and resizing stay with Windows; only their colors change.
        // https://learn.microsoft.com/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute
        int dark = 1;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        foreach (var (attribute, resource) in new[] { (35, "BgTief"), (36, "Text") })
        {
            var color = ((SolidColorBrush)FindResource(resource)).Color;
            int rgb = color.R | (color.G << 8) | (color.B << 16);
            _ = DwmSetWindowAttribute(handle, attribute, ref rgb, sizeof(int));
        }
    }

    private readonly Dictionary<string, NumberSend> _numberSends = new();
    private sealed class NumberSend { public double Value; public int Revision; }

    private async Task SendLatestNumber(OptionInfo option, double value)
    {
        if (_numberSends.TryGetValue(option.Id, out var pending))
        { pending.Value = value; pending.Revision++; return; }
        pending = new NumberSend { Value = value };
        _numberSends[option.Id] = pending;
        string session = _sessionId;
        try
        {
            while (_ipc.Connected && session == _sessionId &&
                   _numberSends.TryGetValue(option.Id, out var current) && ReferenceEquals(current, pending))
            {
                await Task.Delay(100);
                if (!_numberSends.TryGetValue(option.Id, out current) || !ReferenceEquals(current, pending) || session != _sessionId) break;
                int revision = pending.Revision;
                if (!ReportIfFailed(await _ipc.SetNumberAsync(option.Id, pending.Value))) break;
                if (revision == pending.Revision) break;
            }
        }
        finally
        {
            if (_numberSends.TryGetValue(option.Id, out var current) && ReferenceEquals(current, pending))
                _numberSends.Remove(option.Id);
        }
    }

    private void RestoreWindow()
    {
        Width = double.IsFinite(_settings.Width) ? Math.Clamp(_settings.Width, MinWidth, Math.Max(MinWidth, SystemParameters.VirtualScreenWidth)) : 1180;
        Height = double.IsFinite(_settings.Height) ? Math.Clamp(_settings.Height, MinHeight, Math.Max(MinHeight, SystemParameters.VirtualScreenHeight)) : 760;
        if (_settings.Left is double left && _settings.Top is double top && double.IsFinite(left) && double.IsFinite(top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Math.Clamp(left, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width));
            Top = Math.Clamp(top, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height));
        }
        if (_settings.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveSettings()
    {
        if (!_settings.Save(out string error)) SetStatus("Einstellungen konnten nicht gespeichert werden: " + error, true, transient: true);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closed) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        StopOverlay();
        IsEnabled = false;
        _poll.Stop();
        // Ein laufender Dateiaustausch muss seine Transaktion abschließen können.
        while (_installing) await Task.Delay(100);
        _numberSends.Clear();
        _ipc.Disconnect();
        RemoveShortcuts();
        var bounds = _imOverlay ? new Rect(_linksVorher, _obenVorher, _breiteVorher, _hoeheVorher) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.Left = bounds.Left; _settings.Top = bounds.Top;
            _settings.Width = bounds.Width; _settings.Height = bounds.Height;
        }
        _settings.Maximized = (_imOverlay || WindowState == WindowState.Minimized ? _zustandVorher : WindowState) == WindowState.Maximized;
        SaveSettings();
        try
        {
            while (true)
            {
                var stopped = await Task.WhenAll(Floppy.Unreal.Host.StoppeAsync(), Floppy.Unrailed2.Host.StoppeAsync());
                if (stopped.All(value => value)) break;
                SetStatus("Warte auf den Abschluss des laufenden Spielbefehls…", false, transient: true);
            }
        }
        finally
        {
            _closed = true;
            Close();
        }
    }

    private bool IsFavorite(OptionInfo option) => _settings.Favorites.TryGetValue(_connectedGameId, out var favorites) && favorites.Contains(option.Id);

    private UIElement WithOptionTools(OptionInfo option, UIElement content)
    {
        var panel = new DockPanel();
        var star = new Button
        {
            Content = IsFavorite(option) ? "★" : "☆",
            ToolTip = "Favorit umschalten · Rechtsklick für Tastenkürzel",
            Style = (Style)FindResource("GhostButton"),
            Foreground = (Brush)FindResource(IsFavorite(option) ? "Accent" : "Muted"),
            Width = 28, Height = 28, Padding = new Thickness(0),
            Margin = new Thickness(10, -3, -4, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        System.Windows.Automation.AutomationProperties.SetName(star, option.Label + " als Favorit markieren");
        star.Click += (_, _) =>
        {
            if (!_settings.Favorites.TryGetValue(_connectedGameId, out var favorites))
                _settings.Favorites[_connectedGameId] = favorites = new();
            if (!favorites.Remove(option.Id)) favorites.Add(option.Id);
            SaveSettings();
            star.Content = IsFavorite(option) ? "★" : "☆";
            star.Foreground = (Brush)FindResource(IsFavorite(option) ? "Accent" : "Muted");
            RenderCategoryList();
            if (_selectedCategory == RubrikFavoriten) RenderOptions();
        };
        var menu = new ContextMenu();
        var assign = new MenuItem { Header = "Tastenkürzel zuweisen…", IsEnabled = option.Kind is "Toggle" or "Button" };
        assign.Click += (_, _) => AssignShortcut(option);
        menu.Items.Add(assign);
        var remove = new MenuItem { Header = "Tastenkürzel entfernen" };
        remove.Click += (_, _) =>
        {
            if (_settings.Shortcuts.TryGetValue(_connectedGameId, out var keys)) keys.Remove(option.Id);
            SaveSettings(); RegisterShortcuts();
        };
        menu.Items.Add(remove);
        star.ContextMenu = menu;
        panel.ContextMenu = menu;
        DockPanel.SetDock(star, Dock.Right);
        panel.Children.Add(star);
        panel.Children.Add(content);
        return panel;
    }

    private void RenderFavorites()
    {
        PaneTitle.Text = "Favoriten";
        var favorites = _categories.SelectMany(c => c.Options).Where(IsFavorite).ToList();
        PaneCount.Text = favorites.Count + " Einträge";
        if (favorites.Count == 0)
            OptionsPanel.Children.Add(new TextBlock { Text = "Mit dem Stern an einer Funktion legst du sie hier ab. Deine Favoriten werden pro Spiel gespeichert.", Style = (Style)FindResource("Hint") });
        for (int i = 0; i < favorites.Count; i++) OptionsPanel.Children.Add(BuildRow(favorites[i], i));
    }

    private void OnToolsClicked(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        using var identity = WindowsIdentity.GetCurrent();
        RestartElevatedItem.Visibility = (_connectedGameId == "MortalShell2" || _selectedGame?.ProductName == "MortalShell2")
            && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)
            ? Visibility.Visible : Visibility.Collapsed;
        RestartElevatedItem.IsEnabled = !_installing && !_closing;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void OnRestartElevated(object sender, RoutedEventArgs e)
    {
        if (_installing || _closing) return;
        try
        {
            SaveSettings();
            using var next = Process.Start(ElevatedRestartInfo());
            if (next != null) Close();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { SetStatus("Neustart abgebrochen. Floppy bleibt geöffnet.", false, transient: true); }
        catch (Exception ex)
        { SetStatus("Neustart nicht möglich: " + ex.Message, true, transient: true); }
    }

    internal static ProcessStartInfo ElevatedRestartInfo()
    {
        // The single-file host may have a different name and lives outside its extraction cache.
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Der Pfad der laufenden Floppy-EXE ist nicht verfügbar.");
        return new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executable)!
        };
    }

    private void OnDiagnose(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();
        text.AppendLine("Floppy " + typeof(MainWindow).Assembly.GetName().Version);
        text.AppendLine("Spiel: " + (_selectedGame?.Name ?? "keins ausgewählt"));
        text.AppendLine("Verbindung: " + (_ipc.Connected ? _connectedGameId : "getrennt"));
        text.AppendLine("Letzte Antwort: " + (_ipc.LastResponse?.ToString("HH:mm:ss") ?? "noch keine"));
        text.AppendLine("Letzter Verbindungsfehler: " + (string.IsNullOrEmpty(_ipc.LastError) ? "keiner" : _ipc.LastError));
        text.AppendLine("Schema: " + _schemaVersion);
        text.AppendLine("Overlay: " + (OverlaySupported ? "extern · Fenster/randloses Vollbild" : "Spielmodul aktualisieren"));
        text.AppendLine("Backend-Prozess: " + _serverProcessId);
        text.AppendLine("Spielfenster: " + (_gameWindow?.ProcessId.ToString() ?? "nicht erreichbar"));
        if (_selectedGame?.InstallDir is string directory)
        { text.AppendLine(); text.AppendLine(Installer.Diagnose(directory, _selectedGame.ProductName)); }
        if (_settings.Shortcuts.TryGetValue(_connectedGameId, out var shortcuts) && shortcuts.Count > 0)
        {
            text.AppendLine("\nTastenkürzel:");
            foreach (var key in shortcuts) text.AppendLine(key.Value + " — " + (_byId.TryGetValue(key.Key, out var option) ? option.Label : key.Key));
        }
        text.AppendLine("\nLetzte Meldungen:");
        foreach (string line in _events.Reverse()) text.AppendLine(line);
        var window = new Window { Owner = this, Title = "Floppy · Diagnose", Width = 720, Height = 540, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("BgPanel") };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var openLog = new Button { Content = "Spielprotokoll öffnen", Style = (Style)FindResource("FlatButton"), Margin = new Thickness(0, 0, 0, 10) };
        openLog.Click += (_, _) =>
        {
            var path = _selectedGame?.InstallDir is string dir ? Path.Combine(dir, "BepInEx", "LogOutput.log") : "";
            if (File.Exists(path)) OpenPath(path);
            else SetStatus("Für dieses Spiel liegt kein BepInEx-Protokoll vor", true, transient: true);
        };
        DockPanel.SetDock(openLog, Dock.Top); panel.Children.Add(openLog);
        panel.Children.Add(new TextBox { Text = text.ToString(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = (Brush)FindResource("BgPanel"), Foreground = (Brush)FindResource("Text") });
        window.Content = panel; window.ShowDialog();
    }

    private async void OnRepair(object sender, RoutedEventArgs e) => await InstallAction(false);
    private async void OnRestore(object sender, RoutedEventArgs e) => await InstallAction(true);
    private bool _installing;
    private async Task InstallAction(bool restore)
    {
        if (_installing || _closing || _closed) return;
        if (_selectedGame?.InstallDir is not string path)
        { SetStatus("Bitte zuerst einen Spielordner wählen", true, transient: true); return; }
        var game = _selectedGame;
        _installing = true;
        LaunchButton.IsEnabled = false;
        GameList.IsEnabled = false;
        SetStatus(restore ? "Stelle die Installation wieder her…" : "Prüfe und repariere Floppy…", false, transient: true);
        try
        {
            var result = await Task.Run(() =>
            {
                string message;
                bool success = restore ? Installer.Wiederherstellen(path, out message) : Installer.Einrichten(path, out message, game.ProductName);
                return (success, message);
            });
            SetStatus(result.message, !result.success, transient: true);
            RefreshGameStatuses(); BindGameList();
        }
        catch (Exception ex) { SetStatus(ex.Message, true, transient: true); }
        finally { _installing = false; GameList.IsEnabled = true; UpdateLaunchButton(); }
    }

    private void OnChooseGameFolder(object sender, RoutedEventArgs e)
    {
        if (_selectedGame == null) return;
        var dialog = new OpenFolderDialog { Title = "Ordner mit der Spiel-EXE wählen", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        string? directory = SteamLibrary.FindGameDirectory(dialog.FolderName, _selectedGame.AppId);
        if (directory == null)
        { SetStatus("Spieldateien von " + _selectedGame.Name + " nicht gefunden. Bitte dessen Installationsordner wählen.", true, transient: true); return; }
        _settings.GameFolders[_selectedGame.ProductName] = directory;
        SaveSettings(); ScanLibrary();
    }

    private void OnAutomaticGameFolder(object sender, RoutedEventArgs e)
    {
        if (_selectedGame == null) return;
        _settings.GameFolders.Remove(_selectedGame.ProductName);
        SaveSettings(); ScanLibrary();
    }

    private void OnOpenProfiles(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Floppy", "Profile");
        Directory.CreateDirectory(path); OpenPath(path);
    }

    private void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus(ex.Message, true, transient: true); }
    }
}
