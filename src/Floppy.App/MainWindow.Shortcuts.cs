using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Floppy.App;

public partial class MainWindow
{
    private readonly Dictionary<int, string> _shortcutIds = new();
    private readonly HashSet<string> _runningShortcuts = new();
    private bool _shortcutFocusActive;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private void AssignShortcut(OptionInfo option)
    {
        string game = _connectedGameId;
        string session = _sessionId;
        var window = new Window { Owner = this, Title = "Tastenkürzel · " + option.Label, Width = 480, Height = 190, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (Brush)FindResource("BgPanel") };
        var hint = new TextBlock { Text = "Drücke Strg oder Alt zusammen mit einer Taste.\nF1 und Strg+F bleiben für Menü und Suche reserviert.\nEsc bricht ab.", Margin = new Thickness(24), TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Text") };
        window.Content = hint;
        window.PreviewKeyDown += (_, e) =>
        {
            if (_closing || _closed || session != _sessionId || game != _connectedGameId)
            { window.Close(); return; }
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { window.Close(); return; }
            var modifiers = Keyboard.Modifiers;
            if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 ||
                key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
            e.Handled = true;
            if (key == Key.F1 || (key == Key.F && modifiers == ModifierKeys.Control)) return;
            string gesture = new KeyGestureConverter().ConvertToInvariantString(new KeyGesture(key, modifiers))!;
            if (!_settings.Shortcuts.TryGetValue(game, out var shortcuts))
                _settings.Shortcuts[game] = shortcuts = new();
            if (shortcuts.Any(p => p.Key != option.Id && p.Value == gesture))
            { hint.Text = gesture + " ist bereits einer anderen Funktion zugewiesen. Bitte eine andere Kombination wählen."; return; }
            shortcuts[option.Id] = gesture;
            SaveSettings(); RegisterShortcuts(); window.Close();
        };
        window.ShowDialog();
    }

    private void RegisterShortcuts()
    {
        RemoveShortcuts();
        if (!_ipc.Connected || !ShortcutForeground()) return;
        _shortcutFocusActive = true;
        if (!_settings.Shortcuts.TryGetValue(_connectedGameId, out var shortcuts)) return;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        int id = 0x7000;
        foreach (var pair in shortcuts)
        {
            if (!_byId.TryGetValue(pair.Key, out var option) || option.Kind is not ("Toggle" or "Button")) continue;
            try
            {
                if (new KeyGestureConverter().ConvertFromInvariantString(pair.Value) is not KeyGesture gesture) continue;
                if ((gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0 || gesture.Key == Key.F1 ||
                    (gesture.Key == Key.F && gesture.Modifiers == ModifierKeys.Control)) continue;
                if (RegisterHotKey(handle, id, (uint)gesture.Modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key)))
                    _shortcutIds[id++] = option.Id;
                else SetStatus("Tastenkürzel " + pair.Value + " ist bereits von einer anderen Anwendung belegt", true, transient: true);
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
            { SetStatus("Ungültiges gespeichertes Tastenkürzel: " + pair.Value, true, transient: true); }
        }
    }

    private void RemoveShortcuts()
    {
        _shortcutFocusActive = false;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
            foreach (int id in _shortcutIds.Keys) UnregisterHotKey(handle, id);
        _shortcutIds.Clear();
        if (_closing && _tasteAngemeldet) { UnregisterHotKey(handle, HOTKEY_ID); _tasteAngemeldet = false; }
    }

    private void RefreshShortcutFocus()
    {
        bool active = _ipc.Connected && ShortcutForeground();
        if (active == _shortcutFocusActive) return;
        if (active) RegisterShortcuts(); else RemoveShortcuts();
    }

    private bool ShortcutForeground()
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == new WindowInteropHelper(this).Handle) return true;
        var game = _games.FirstOrDefault(g => g.ProductName == _connectedGameId);
        if (game?.InstallDir is not string directory) return false;
        try
        {
            GetWindowThreadProcessId(foreground, out uint pid);
            using var process = Process.GetProcessById((int)pid);
            string? path = process.MainModule?.FileName;
            return path != null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    private async Task ExecuteShortcut(string id)
    {
        if (_closing || _closed || !_ipc.Connected || !ShortcutForeground() || !_byId.TryGetValue(id, out var option) || !option.Available || !_runningShortcuts.Add(id)) return;
        try
        {
            bool success = option.Kind == "Toggle"
                ? ReportIfFailed(await _ipc.SetBoolAsync(id, !option.BoolValue))
                : ReportIfFailed(await _ipc.InvokeAsync(id));
            if (success) { RenderCategoryList(); SetStatus(option.Label + (option.Kind == "Toggle" ? (option.BoolValue ? ": an" : ": aus") : ": ausgeführt"), false, transient: true); }
        }
        catch (Exception ex) { SetStatus(ex.Message, true, transient: true); }
        finally { _runningShortcuts.Remove(id); }
    }
}
