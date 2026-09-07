using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Floppy.App;

/// <summary>Finds a game's own window and reads physical screen pixels without changing it.</summary>
internal static class GameWindow
{
    internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
    {
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    internal sealed record Snapshot
    {
        internal Snapshot(IntPtr hwnd, int processId, long startTimeUtcTicks, PixelRect clientRect,
            PixelRect monitorBounds, string? installDir)
        {
            Hwnd = hwnd;
            ProcessId = processId;
            StartTimeUtcTicks = startTimeUtcTicks;
            ClientRect = clientRect;
            MonitorBounds = monitorBounds;
            _installDir = installDir;
        }

        private readonly string? _installDir;
        public IntPtr Hwnd { get; }
        public int ProcessId { get; }
        public long StartTimeUtcTicks { get; }
        public PixelRect ClientRect { get; }
        public PixelRect MonitorBounds { get; }

        public bool Valid() => Refresh() != null;
        public Snapshot? Refresh() => Read(Hwnd, ProcessId, StartTimeUtcTicks, _installDir);
    }

    public static Snapshot? Find(int processId, string? installDir, string? fallbackProcessName)
    {
        if (processId <= 0) return null;
        try
        {
            installDir = string.IsNullOrWhiteSpace(installDir) ? null : Path.GetFullPath(installDir);
            if (processId != Environment.ProcessId) return FindProcess(processId, installDir);

            // An external adapter serves IPC inside Floppy itself. Never attach to Floppy's UI.
            if (string.IsNullOrWhiteSpace(fallbackProcessName)) return null;
            Snapshot? best = null;
            foreach (var process in Process.GetProcessesByName(fallbackProcessName))
                using (process)
                {
                    var candidate = FindProcess(process.Id, installDir);
                    if (candidate != null && Area(candidate) > Area(best)) best = candidate;
                }
            return best;
        }
        catch (Exception ex) when (Unavailable(ex)) { return null; }
    }

    private static long Area(Snapshot? window) => window == null ? 0 : (long)window.ClientRect.Width * window.ClientRect.Height;

    private static Snapshot? FindProcess(int processId, string? installDir)
    {
        if (!Identity(processId, installDir, out long started)) return null;
        Snapshot? best = null;
        Native.EnumWindowsCallback callback = (hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint owner);
            if (owner != processId || !Geometry(hwnd, out var client, out var monitor)) return true;
            var candidate = new Snapshot(hwnd, processId, started, client, monitor, installDir);
            if (Area(candidate) > Area(best)) best = candidate;
            return true;
        };
        if (!Native.EnumWindows(callback, IntPtr.Zero)) return null;
        // Revalidate after enumeration: the process/window may have exited meanwhile.
        return best?.Refresh();
    }

    private static Snapshot? Read(IntPtr hwnd, int processId, long started, string? installDir)
    {
        if (!Identity(processId, installDir, out long currentStart) || currentStart != started) return null;
        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != processId || !Geometry(hwnd, out var client, out var monitor)) return null;
        Native.GetWindowThreadProcessId(hwnd, out owner);
        return owner == processId ? new Snapshot(hwnd, processId, started, client, monitor, installDir) : null;
    }

    private static bool Identity(int processId, string? installDir, out long started)
    {
        started = 0;
        try
        {
            // MainModule requires memory access and fails for an elevated game. Window
            // identity needs only metadata; keep path and creation time on the same handle.
            using var process = Native.OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
            if (process.IsInvalid || !Native.GetExitCodeProcess(process, out uint exitCode) || exitCode != 259 ||
                !Native.GetProcessTimes(process, out long created, out _, out _, out _)) return false;
            started = DateTime.FromFileTimeUtc(created).Ticks;
            if (installDir == null) return true;
            var path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            if (!Native.QueryFullProcessImageNameW(process, 0, path, ref length) || length == 0) return false;
            string prefix = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path.ToString()).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (Unavailable(ex)) { return false; }
    }

    private static bool Unavailable(Exception ex) => ex is Win32Exception or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException;

    private static bool Geometry(IntPtr hwnd, out PixelRect client, out PixelRect monitor)
    {
        client = default;
        monitor = default;
        if (!Native.IsWindow(hwnd) || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) ||
            Native.GetWindow(hwnd, 4) != IntPtr.Zero) return false; // GW_OWNER: no dialogs/tool windows.
        if (Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;

        // Only this synchronous read scope changes the caller's DPI context, never the game.
        // Restore it before WPF can create/render another window on this thread.
        IntPtr previous = Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
        if (previous == IntPtr.Zero) return false;
        try
        {
            if (!Native.GetClientRect(hwnd, out var rect)) return false;
            var first = new Native.Point { X = rect.Left, Y = rect.Top };
            var last = new Native.Point { X = rect.Right, Y = rect.Bottom };
            if (!Native.ClientToScreen(hwnd, ref first) || !Native.ClientToScreen(hwnd, ref last)) return false;
            client = new PixelRect(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
                Math.Max(first.X, last.X), Math.Max(first.Y, last.Y));
            if (client.Width <= 0 || client.Height <= 0) return false;
            IntPtr screen = Native.MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (screen == IntPtr.Zero || !Native.GetMonitorInfo(screen, ref info)) return false;
            monitor = new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
            return monitor.Width > 0 && monitor.Height > 0;
        }
        finally { Native.SetThreadDpiAwarenessContext(previous); }
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder path, ref uint size);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
        internal delegate bool EnumWindowsCallback(IntPtr hwnd, IntPtr parameter);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
