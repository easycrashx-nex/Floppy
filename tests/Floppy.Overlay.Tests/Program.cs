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
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--probe-pid")
        {
            // Opt-in metadata/geometry only: never activate, resize or write to this window.
            int pid = int.Parse(args[1]);
            string? processName = null;
            try
            {
                using var process = Process.GetProcessById(pid);
                processName = process.ProcessName;
                Console.WriteLine("MainModule baseline: " + process.MainModule?.FileName);
            }
            catch (Exception ex) { Console.WriteLine("MainModule baseline denied: " + ex.Message); }
            var found = GameWindow.Find(pid, args[2], null);
            if (found == null) { Console.WriteLine("FAIL read-only window discovery for PID " + pid); return 1; }
            Assert(found.ProcessId == pid && found.Valid(), "Requested live PID and start time must validate");
            Assert(GameWindow.Find(pid, args[2] + "-wrong", null) == null, "Wrong install directory must remain rejected");
            Assert(processName != null && GameWindow.Find(Environment.ProcessId, args[2], processName) is { } external &&
                external.ProcessId == pid && external.Hwnd == found.Hwnd, "External-host process-name fallback must find the validated target");
            Console.WriteLine($"PASS read-only target HWND=0x{found.Hwnd:X} PID={found.ProcessId} StartUtcTicks={found.StartTimeUtcTicks} Client={found.ClientRect} Monitor={found.MonitorBounds}");
            return 0;
        }
        if (args.Length != 0) throw new ArgumentException("Use --probe-pid <PID> <install directory>, or no arguments for own-window tests.");
        // Own transparent WPF fixture only; no real game or other app is touched.
        var window = new Window
        {
            Title = "Floppy geometry fixture", Width = 320, Height = 240, Left = 40, Top = 40,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowActivated = false, Opacity = 0
        };
        try
        {
            using var ownProcess = Process.GetCurrentProcess();
            string name = ownProcess.ProcessName;
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
            Assert(snapshot.StartTimeUtcTicks == ownProcess.StartTime.ToUniversalTime().Ticks,
                "Limited-query creation time must retain the existing UTC-ticks contract");
            Assert(GameWindow.Find(Environment.ProcessId, directory + "-wrong", name) == null, "Executable outside installDir must be rejected");
            Assert(GameWindow.Find(Environment.ProcessId, directory, null) == null, "App PID must not select itself without a game-name fallback");
            Assert(GameWindow.Find(int.MaxValue, directory, name) == null, "Unknown IPC PID must not fall back to an unrelated process");
            var reused = new GameWindow.Snapshot(hwnd, snapshot.ProcessId, snapshot.StartTimeUtcTicks + 1,
                snapshot.ClientRect, snapshot.MonitorBounds, directory);
            Assert(!reused.Valid(), "PID reused with another start time must be rejected");
            var wrongPath = new GameWindow.Snapshot(hwnd, snapshot.ProcessId, snapshot.StartTimeUtcTicks,
                snapshot.ClientRect, snapshot.MonitorBounds, directory + "-wrong");
            Assert(!wrongPath.Valid(), "Refresh must preserve install-directory validation");

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
