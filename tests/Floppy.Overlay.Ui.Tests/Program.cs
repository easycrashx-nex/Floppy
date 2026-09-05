using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Floppy.App;

internal static class Program
{
    private const string GameId = "Overlay-UI-Fixture";
    private static int _checks;
    private static int _skips;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--game-fixture")) return GameFixture();
        var app = new Floppy.App.App();
        app.InitializeComponent();
        // Keep the production resources, but never launch App.xaml's online MainWindow.
        // StartupUri's public setter rejects null, so the isolated harness clears its backing field.
        var startup = typeof(Application).GetField("_startupUri", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Cannot disable the production startup window safely");
        startup.SetValue(app, null);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        int result = 1;
        app.Dispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                await Run(args.FirstOrDefault());
                Console.WriteLine($"PASS {_checks} overlay UI checks; {_skips} explicitly skipped focus/hotkey checks");
                result = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { app.Shutdown(result); }
        }));
        app.Run();
        return result;
    }

    private static async Task Run(string? screenshot)
    {
        using var child = StartChild();
        await using var backend = new Backend(child.Id);
        using var ipc = new IpcClient(backend.Port, TimeSpan.FromSeconds(3));
        var window = new MainWindow(offline: true)
        {
            Opacity = 0, ShowActivated = false, Left = 100, Top = 100, Width = 1180, Height = 760
        };
        Window? other = null;
        try
        {
            string? childReady = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
            Check(childReady == "READY", "separate transparent game fixture starts");
            ((IpcClient)Get(window, "_ipc")).Dispose();
            Set(window, "_ipc", ipc);
            Set(window, "_games", new List<SupportedGame>
            {
                new() { Name = "Overlay UI Fixture · Beispieldaten", ProductName = GameId, Installed = true,
                    InstallDir = Path.GetDirectoryName(Environment.ProcessPath!) }
            });
            window.Show();
            Invoke(window, "OverlayVorbereiten");
            Check(await ipc.ConnectAsync(), "real IpcClient connects to isolated ephemeral port");
            await Call(window, "LoadSchemaAsync");
            Check((int)Get(window, "_serverProcessId") == child.Id && (bool)Get(window, "_remoteOverlay"),
                "real schema binds child PID, session and external-overlay capability");
            Check(Named<Button>(window, "OverlayButton").IsEnabled, "real GameWindow discovery enables overlay");
            Check(((DispatcherTimer)Get(window, "_overlayPulse")).IsEnabled, "actual overlay lifecycle timer is wired");

            var bounds = new Rect(window.Left, window.Top, window.Width, window.Height);
            var style = window.WindowStyle;
            var min = new Size(window.MinWidth, window.MinHeight);
            Invoke(window, "SaveOverlayPlacement");
            Invoke(window, "SetOverlayView", true);
            Set(window, "_imOverlay", true);
            window.MinWidth = 0; window.MinHeight = 0; window.Width = 640; window.Height = 420;
            window.UpdateLayout();
            Invoke(window, "ScaleOverlay");
            Check(((Grid)window.Content).LayoutTransform is ScaleTransform { ScaleX: < 1 }, "real overlay grid scales for a small own window");
            if (!string.IsNullOrWhiteSpace(screenshot)) SavePreview((Grid)window.Content, screenshot);
            Invoke(window, "RestoreOverlayPlacement", true, false);
            CheckPlacement(window, bounds, style, min);

            IntPtr appHwnd = new WindowInteropHelper(window).Handle;
            if (!Focus(appHwnd))
            {
                Skip("Windows denied foreground permission; native Open/Renew/Close/focus checks not bypassed");
                return;
            }
            Invoke(window, "TasteNachfuehren");
            bool registered = (bool)Get(window, "_tasteAngemeldet");
            if (registered)
            {
                Check(PostMessage(appHwnd, 0x0312, new IntPtr(0x4653), new IntPtr(0x00700000)), "F1 registration accepts the own WM_HOTKEY fixture message");
                await Until(() => (bool)Get(window, "_imOverlay"), "real F1 hook opens overlay");
            }
            else
            {
                Skip("F1 is already registered elsewhere; button/method path remains tested");
                await Call(window, "OverlayUmschaltenAsync");
            }
            Check((bool)Get(window, "_imOverlay"), "actual Open succeeds with matching backend lease acknowledgement");
            Check(window.WindowStyle == WindowStyle.None && window.Topmost && !window.ShowInTaskbar,
                "actual Open sets the desktop overlay window mode");
            Check(Named<FrameworkElement>(window, "LibraryPane").Visibility == Visibility.Collapsed &&
                Named<FrameworkElement>(window, "OverlayCloseButton").Visibility == Visibility.Visible,
                "actual Open collapses library and exposes overlay close actions");
            if (!string.IsNullOrWhiteSpace(screenshot)) SavePreview((Grid)window.Content, screenshot);
            await Until(() => backend.Commands.Count(c => c) >= 2, "actual lifecycle timer renews the input lease", 2400);
            Check(backend.Commands.All(c => c), "lease stays open before closing");

            var release = backend.DelayRelease();
            var closing = Call(window, "OverlayVerlassenAsync", true, false, false);
            await release.Seen.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!(bool)Get(window, "_imOverlay") && window.WindowState == WindowState.Minimized,
                "Close restores desktop state before waiting for backend acknowledgement");
            other = new Window { Opacity = 0, ShowActivated = false, Width = 180, Height = 100, Left = 10, Top = 10 };
            other.Show();
            IntPtr otherHwnd = new WindowInteropHelper(other).Handle;
            bool movedFocus = Focus(otherHwnd);
            release.Allow.TrySetResult(true);
            await closing;
            if (movedFocus) Check(GetForegroundWindow() == otherHwnd, "delayed close acknowledgement does not steal focus back");
            else Skip("Windows denied own focus fixture; delayed-ACK focus assertion skipped");
            int requestsAfterClose = backend.Commands.Count;
            await Task.Delay(250);
            Check(backend.Commands.Count == requestsAfterClose && !backend.Commands.Last(), "Close releases lease with no later queued reopen");
            other.Close(); other = null;

            await child.StandardInput.WriteLineAsync("focus");
            string? focusReply = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            if (focusReply != "FOCUSED")
            {
                Skip("Windows denied child foreground; reopen-from-minimized/lost-lease checks skipped");
                return;
            }
            await Call(window, "OverlayUmschaltenAsync");
            Check((bool)Get(window, "_imOverlay"), "actual Open works again from the minimized desktop state");
            await Call(window, "OverlayVerlassenAsync", false, true, false);
            CheckPlacement(window, bounds, style, min);

            if (!Focus(appHwnd)) { Skip("Foreground changed; lost-lease assertion skipped"); return; }
            await Call(window, "OverlayUmschaltenAsync");
            Check((bool)Get(window, "_imOverlay"), "third actual Open starts a fresh lease");
            backend.WrongSessionOnRenew = true;
            await Call(window, "RenewOverlayAsync");
            Check(!(bool)Get(window, "_imOverlay") && !backend.Commands.Last(), "wrong-session renewal acknowledgement closes and releases the lease");
            CheckPlacement(window, bounds, style, min);
        }
        finally
        {
            backend.ReleaseAnyWait();
            Invoke(window, "StopOverlay");
            Check(!(bool)Get(window, "_tasteAngemeldet"), "teardown unregisters F1");
            other?.Close();
            window.Close();
            await child.StandardInput.WriteLineAsync("quit");
            try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { if (!child.HasExited) child.Kill(); }
        }
    }

    private static void CheckPlacement(MainWindow window, Rect expected, WindowStyle style, Size min)
    {
        Check(window.WindowState == WindowState.Normal && window.WindowStyle == style && !window.Topmost && window.ShowInTaskbar,
            "desktop window state/style/taskbar/topmost restored");
        Check(Math.Abs(window.Left - expected.Left) < 1 && Math.Abs(window.Top - expected.Top) < 1 &&
            Math.Abs(window.Width - expected.Width) < 1 && Math.Abs(window.Height - expected.Height) < 1 &&
            window.MinWidth == min.Width && window.MinHeight == min.Height, "desktop geometry and minimum size restored");
        Check(Named<FrameworkElement>(window, "LibraryPane").Visibility == Visibility.Visible &&
            ((Grid)window.Content).LayoutTransform.Value.IsIdentity, "desktop library and layout transform restored");
    }

    private static void SavePreview(Grid content, string path)
    {
        content.UpdateLayout();
        Rect rendered = content.LayoutTransform.TransformBounds(new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        int width = (int)Math.Ceiling(rendered.Width), height = (int)Math.Ceiling(rendered.Height);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path); png.Save(stream);
        Check(width > 0 && height > 0, "real overlay layout rendered with fixture data; native lifecycle checked separately");
    }

    private static Process StartChild()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("--game-fixture");
        return Process.Start(start) ?? throw new InvalidOperationException("Own fixture child failed to start");
    }

    private static int GameFixture()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var game = new Window { Title = "Own overlay game fixture", Opacity = 0, ShowActivated = false,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Width = 1000, Height = 720, Left = 50, Top = 50 };
        game.Show();
        Console.WriteLine("READY");
        _ = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is { } command)
            {
                if (command == "quit") break;
                if (command == "focus") await app.Dispatcher.InvokeAsync(() =>
                    Console.WriteLine(Focus(new WindowInteropHelper(game).Handle) ? "FOCUSED" : "DENIED"));
            }
            await app.Dispatcher.InvokeAsync(app.Shutdown);
        });
        app.Run();
        return 0;
    }

    private static async Task Until(Func<bool> condition, string description, int timeoutMs = 1800)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMs) await Task.Delay(20);
        Check(condition(), description);
    }
    private static bool Focus(IntPtr hwnd) => SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd;
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    private static void Skip(string message) { _skips++; Console.WriteLine("SKIP " + message); }
    private static T Named<T>(MainWindow w, string name) => (T)w.FindName(name);
    private static object Get(MainWindow w, string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
    private static void Set(MainWindow w, string name, object value) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(w, value);
    private static object? Invoke(MainWindow w, string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, args);
    private static Task Call(MainWindow w, string name, params object[] args) => (Task)Invoke(w, name, args)!;
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);

    private sealed class Backend : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly int _pid;
        private readonly string _session = "isolated-overlay-ui-" + Guid.NewGuid().ToString("N");
        private readonly Task _worker;
        private TcpClient? _client;
        private ReleaseWait? _release;
        public ConcurrentQueue<bool> Commands { get; } = new();
        public bool WrongSessionOnRenew;
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public Backend(int pid) { _pid = pid; _listener.Start(); _worker = Serve(); }
        public sealed class ReleaseWait
        {
            public TaskCompletionSource<bool> Seen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Allow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public ReleaseWait DelayRelease() => _release = new ReleaseWait();
        public void ReleaseAnyWait() => _release?.Allow.TrySetResult(true);
        private async Task Serve()
        {
            try
            {
                _client = await _listener.AcceptTcpClientAsync(_stop.Token);
                using var stream = _client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    var request = JsonNode.Parse(line)!;
                    JsonObject response;
                    if (request["cmd"]?.GetValue<string>() == "overlay")
                    {
                        bool open = request["open"]!.GetValue<bool>(); Commands.Enqueue(open);
                        if (!open && _release is { } release)
                        { release.Seen.TrySetResult(true); await release.Allow.Task; _release = null; }
                        response = new JsonObject { ["ok"] = true, ["overlayOpen"] = open,
                            ["sessionId"] = open && WrongSessionOnRenew ? "wrong-fixture-session" : _session };
                        WrongSessionOnRenew = false;
                    }
                    else response = new JsonObject
                    {
                        ["ok"] = true, ["ready"] = true, ["status"] = "Beispieldaten · kein echtes Spiel",
                        ["game"] = "Overlay UI Fixture · Beispieldaten", ["gameId"] = GameId, ["schemaVersion"] = 1,
                        ["processId"] = _pid, ["externalOverlay"] = true, ["sessionId"] = _session,
                        ["categories"] = new JsonArray(new JsonObject { ["name"] = "Overlay-Test", ["options"] = new JsonArray(
                            new JsonObject { ["id"] = "fixture.info", ["label"] = "Isolierte Vorschau", ["kind"] = "Info", ["description"] = "Automatischer UI-Test mit eigenem Testfenster. Kein echtes Spiel verbunden." }) }),
                        ["values"] = new JsonObject { ["fixture.info"] = new JsonObject { ["text"] = "Beispieldaten · keine Spielaktionen", ["available"] = true, ["active"] = false } }
                    };
                    await writer.WriteLineAsync(response.ToJsonString());
                }
            }
            catch (Exception ex) when (_stop.IsCancellationRequested && ex is OperationCanceledException or ObjectDisposedException or IOException or SocketException) { }
        }
        public async ValueTask DisposeAsync()
        {
            ReleaseAnyWait(); _stop.Cancel(); _listener.Stop(); _client?.Dispose();
            await _worker; _stop.Dispose();
        }
    }
}
