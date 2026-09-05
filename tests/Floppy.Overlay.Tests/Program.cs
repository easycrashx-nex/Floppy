using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Floppy.App;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Own transparent WPF fixture only; no real game or other app is touched.
        var window = new Window
        {
            Title = "Floppy geometry fixture", Width = 320, Height = 240, Left = 40, Top = 40,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, Opacity = 0
        };
        try
        {
            string name = Process.GetCurrentProcess().ProcessName;
            string directory = Path.GetDirectoryName(Environment.ProcessPath!)!;
            window.Show();
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            IntPtr dpiBefore = GetThreadDpiAwarenessContext();
            var snapshot = GameWindow.Find(Environment.ProcessId, directory, name);
            Assert(snapshot != null && snapshot.Hwnd == hwnd, "PID and path must locate the own fixture window");
            Assert(GetThreadDpiAwarenessContext() == dpiBefore, "Geometry reads must restore the caller's DPI context");
            var scale = PresentationSource.FromVisual(window)!.CompositionTarget!.TransformToDevice;
            Assert(Math.Abs(snapshot!.ClientRect.Width - window.ActualWidth * scale.M11) <= 1 &&
                Math.Abs(snapshot.ClientRect.Height - window.ActualHeight * scale.M22) <= 1,
                "Client bounds must use physical pixels");
            Assert(snapshot.MonitorBounds.Width > 0 && snapshot.MonitorBounds.Height > 0, "Monitor pixel bounds missing");
            Assert(snapshot.Valid(), "Live matching identity must validate");
            Assert(GameWindow.Find(Environment.ProcessId, directory + "-wrong", name) == null, "Executable outside installDir must be rejected");
            Assert(GameWindow.Find(Environment.ProcessId, directory, null) == null, "App PID must not select itself without a game-name fallback");
            Assert(GameWindow.Find(int.MaxValue, directory, name) == null, "Unknown IPC PID must not fall back to an unrelated process");
            var reused = new GameWindow.Snapshot(hwnd, snapshot.ProcessId, snapshot.StartTimeUtcTicks + 1,
                snapshot.ClientRect, snapshot.MonitorBounds, directory);
            Assert(!reused.Valid(), "PID reused with another start time must be rejected");

            window.Width = 400;
            window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert(snapshot.Refresh() is { } resized && resized.ClientRect.Width > snapshot.ClientRect.Width,
                "Refresh must follow client-size changes");
            window.WindowState = WindowState.Minimized;
            Assert(!snapshot.Valid(), "Minimized windows must be rejected");
            window.WindowState = WindowState.Normal;
            window.Hide();
            Assert(!snapshot.Valid(), "Hidden windows must be rejected");
            window.Close();
            Assert(!snapshot.Valid(), "Closed HWND must be rejected");
            Console.WriteLine("PASS GameWindow: PID/start time, path/fallback, physical pixels, monitor, resize, minimized/hidden/closed, DPI context restoration");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { window.Close(); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
}
