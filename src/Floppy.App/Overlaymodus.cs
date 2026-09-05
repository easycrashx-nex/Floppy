using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Floppy.App;

/// <summary>Ein externes WPF-Overlay für Fenster und randloses Vollbild.
/// Exklusives Vollbild benötigt einen Wechsel in den randlosen Modus im Spiel.</summary>
public partial class MainWindow
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x4653;
    private const uint VK_F1 = 0x70;
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);

    private HwndSource? _quelle;
    private bool _tasteAngemeldet;
    private bool _imOverlay;
    private bool _overlayTransition;
    private bool _overlayRenewing;
    private bool _remoteOverlay;
    private int _serverProcessId;
    private int _overlayEpoch;
    private long _overlayLeaseAck;
    private long _overlayLastRenew;
    private long _lastHotkeyAttempt;
    private string _overlaySession = "";
    private GameWindow.Snapshot? _gameWindow;
    private GameWindow.Snapshot? _overlayTarget;
    private readonly DispatcherTimer _overlayPulse = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private WindowState _zustandVorher;
    private WindowStyle _stilVorher;
    private ResizeMode _resizeVorher;
    private bool _topmostVorher;
    private bool _taskbarVorher;
    private double _obenVorher, _linksVorher, _breiteVorher, _hoeheVorher;
    private double _minWidthVorher, _minHeightVorher;
    private GridLength _libraryWidthVorher;
    private Transform _layoutVorher = Transform.Identity;

    private bool HasExternalHost => _connectedGameId is "MortalShell2" or "Unrailed2";
    private bool OverlaySupported => _remoteOverlay || HasExternalHost;

    private void OverlayVorbereiten()
    {
        _quelle = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _quelle?.AddHook(FensterNachricht);
        _overlayPulse.Tick += (_, _) => OverlayPulseTick();
        _overlayPulse.Start();
        PreviewKeyDown += (_, e) =>
        {
            if (!_imOverlay || e.Key != Key.Escape || HasOwnedDialog()) return;
            if (Keyboard.FocusedElement is ComboBox { IsDropDownOpen: true }) return;
            e.Handled = true;
            _ = OverlayVerlassenAsync(returnFocus: true);
        };
        SizeChanged += (_, _) => { if (_imOverlay) ScaleOverlay(); };
        StateChanged += (_, _) =>
        {
            if (!_imOverlay && !_overlayTransition && WindowState != WindowState.Minimized)
                _zustandVorher = WindowState;
        };
    }

    private bool HasOwnedDialog() => OwnedWindows.Cast<Window>().Any(w => w.IsVisible);

    private void UpdateOverlayTarget()
    {
        string? fallback = _connectedGameId switch
        {
            "MortalShell2" => Floppy.Unreal.Spiel.Prozess,
            "Unrailed2" => Floppy.Unrailed2.Host.Prozessname,
            _ => null
        };
        var game = _games.FirstOrDefault(g => g.ProductName == _connectedGameId);
        _gameWindow = _ipc.Connected && OverlaySupported
            ? GameWindow.Find(_serverProcessId, game?.InstallDir, fallback) : null;
        OverlayButton.IsEnabled = _gameWindow != null && !_overlayTransition;
        OverlayButton.ToolTip = !_ipc.Connected ? "Zuerst mit einem Spiel verbinden."
            : !OverlaySupported ? "Spiel beenden und Floppy unter Werkzeuge reparieren, um das Spielmodul zu aktualisieren."
            : _gameWindow == null ? "Kein erreichbares Spielfenster gefunden. Öffne das Spiel oder stelle sein minimiertes Fenster wieder her."
            : "F1 öffnet das externe Overlay. Für Vollbild im Spiel den Modus Randloses Vollbild wählen.";
        TasteNachfuehren();
    }

    private bool OwnForeground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == Environment.ProcessId;
    }

    private bool GameForeground(GameWindow.Snapshot? game)
    {
        if (game == null) return false;
        GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
        return pid == game.ProcessId;
    }

    private void TasteNachfuehren()
    {
        bool needed = !_closing && _ipc.Connected && OverlaySupported && _gameWindow != null
            && (_imOverlay || OwnForeground() || GameForeground(_gameWindow));
        if (!needed) _lastHotkeyAttempt = 0;
        if (needed == _tasteAngemeldet) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (needed)
        {
            if (_lastHotkeyAttempt != 0 && Stopwatch.GetElapsedTime(_lastHotkeyAttempt).TotalSeconds < 5) return;
            _lastHotkeyAttempt = Stopwatch.GetTimestamp();
            _tasteAngemeldet = RegisterHotKey(handle, HOTKEY_ID, 0x4000, VK_F1);
            if (!_tasteAngemeldet) SetStatus("F1 ist bereits belegt. Das Overlay lässt sich über den Knopf öffnen.", true, transient: true);
        }
        else { UnregisterHotKey(handle, HOTKEY_ID); _tasteAngemeldet = false; }
    }

    private IntPtr FensterNachricht(IntPtr window, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (message != WM_HOTKEY) return IntPtr.Zero;
        if (_shortcutIds.TryGetValue(wparam.ToInt32(), out string? option))
        { _ = ExecuteShortcut(option); handled = true; return IntPtr.Zero; }
        if (wparam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            if (_imOverlay || OwnForeground() || GameForeground(_gameWindow)) _ = OverlayUmschaltenAsync();
        }
        return IntPtr.Zero;
    }

    private async void OnOverlayClicked(object sender, RoutedEventArgs e) => await OverlayUmschaltenAsync();
    private async void OnOverlayClose(object sender, RoutedEventArgs e) => await OverlayVerlassenAsync(returnFocus: true);
    private async void OnOverlayDesktop(object sender, RoutedEventArgs e) => await OverlayVerlassenAsync(returnFocus: false, desktop: true, activateDesktop: true);

    private async Task OverlayUmschaltenAsync()
    {
        if (_closing || _closed || _overlayTransition || HasOwnedDialog()) return;
        if (_imOverlay) { await OverlayVerlassenAsync(returnFocus: true); return; }
        UpdateOverlayTarget();
        var target = _gameWindow?.Refresh();
        if (target == null)
        { SetStatus(OverlayButton.ToolTip?.ToString() ?? "Kein sichtbares Spielfenster gefunden.", true, transient: true); return; }
        _overlayTransition = true;
        int epoch = ++_overlayEpoch;
        int generation = _ipc.Generation;
        string session = _sessionId;
        bool lease = false;
        try
        {
            if (_remoteOverlay)
            {
                var response = await _ipc.SetOverlayAsync(true, session,
                    () => !_closing && epoch == _overlayEpoch && generation == _ipc.Generation
                        && (OwnForeground() || GameForeground(target)));
                lease = response?["ok"]?.GetValue<bool>() == true
                    && response["overlayOpen"]?.GetValue<bool>() == true
                    && response["sessionId"]?.GetValue<string>() == session;
                if (!lease)
                {
                    if (response?["ok"]?.GetValue<bool>() == true)
                        SetStatus("Das Spiel hat die Overlay-Eingabesicherung nicht bestätigt. Bitte das Modul aktualisieren.", true, transient: true);
                    else ReportIfFailed(response);
                    return;
                }
            }
            if (_closing || epoch != _overlayEpoch || generation != _ipc.Generation || session != _sessionId
                || !target.Valid() || (!OwnForeground() && !GameForeground(target))) return;
            var connectedGame = _games.FirstOrDefault(g => g.ProductName == _connectedGameId);
            if (connectedGame != null) { _selectedGame = connectedGame; BindGameList(); }
            SaveOverlayPlacement();
            _overlaySession = session;
            _overlayTarget = target;
            _overlayLeaseAck = _overlayLastRenew = Stopwatch.GetTimestamp();
            _imOverlay = true;
            SetOverlayView(true);
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 0; MinHeight = 0;
            ShowInTaskbar = false;
            Topmost = true;
            Show();
            PositionOverlay(target);
            Activate();
            SetForegroundWindow(new WindowInteropHelper(this).Handle);
            ScaleOverlay();
            SetStatus("Externes Overlay · F1 oder Esc zurück zum Spiel", false, transient: true);
        }
        catch (Exception ex)
        {
            if (_imOverlay) RestoreOverlayPlacement(desktop: true);
            SetStatus("Overlay konnte nicht geöffnet werden: " + ex.Message, true, transient: true);
        }
        finally
        {
            _overlayTransition = false;
            if (_remoteOverlay && !_imOverlay && generation == _ipc.Generation && _ipc.Connected)
                await _ipc.SetOverlayAsync(false, session);
            OverlayButton.IsEnabled = _gameWindow != null;
        }
    }

    private void SaveOverlayPlacement()
    {
        if (WindowState != WindowState.Minimized) _zustandVorher = WindowState;
        Rect bounds = RestoreBounds;
        if (bounds.IsEmpty) bounds = new Rect(Left, Top, Width, Height);
        _linksVorher = bounds.Left; _obenVorher = bounds.Top;
        _breiteVorher = bounds.Width; _hoeheVorher = bounds.Height;
        _stilVorher = WindowStyle; _resizeVorher = ResizeMode;
        _minWidthVorher = MinWidth; _minHeightVorher = MinHeight;
        _topmostVorher = Topmost; _taskbarVorher = ShowInTaskbar;
        var root = (Grid)Content;
        _libraryWidthVorher = root.ColumnDefinitions[0].Width;
        _layoutVorher = root.LayoutTransform;
    }

    private void SetOverlayView(bool overlay)
    {
        var root = (Grid)Content;
        LibraryPane.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        root.ColumnDefinitions[0].Width = overlay ? new GridLength(0) : _libraryWidthVorher;
        LaunchButton.Visibility = ConnectButton.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        OverlayCloseButton.Visibility = OverlayDesktopButton.Visibility = overlay ? Visibility.Visible : Visibility.Collapsed;
        OverlayButton.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        HeaderCaption.Text = overlay ? "Floppy Overlay" : "Dein Spiel";
    }

    private void ScaleOverlay()
    {
        if (!_imOverlay || ActualWidth <= 0 || ActualHeight <= 0) return;
        double scale = Math.Min(1, Math.Min(ActualWidth / 760, ActualHeight / 520));
        ((Grid)Content).LayoutTransform = scale < .999 ? new ScaleTransform(scale, scale) : Transform.Identity;
    }

    private void PositionOverlay(GameWindow.Snapshot target)
    {
        var r = target.ClientRect;
        var monitor = target.MonitorBounds;
        int left = Math.Max(r.Left, monitor.Left), top = Math.Max(r.Top, monitor.Top);
        int areaWidth = Math.Min(r.Right, monitor.Right) - left;
        int areaHeight = Math.Min(r.Bottom, monitor.Bottom) - top;
        if (areaWidth < 320 || areaHeight < 240) throw new InvalidOperationException("Das Spielfenster ist zu klein für das Overlay.");
        var handle = new WindowInteropHelper(this).Handle;
        double dpi = GetDpiForWindow(handle) / 96.0;
        if (dpi <= 0) dpi = 1;
        int width = Math.Min(areaWidth - 24, (int)Math.Round(Math.Clamp(_breiteVorher - 160, 800, 1100) * dpi));
        int height = Math.Min(areaHeight - 24, (int)Math.Round(Math.Clamp(_hoeheVorher, 560, 780) * dpi));
        if (!SetWindowPos(handle, new IntPtr(-1), left + (areaWidth - width) / 2, top + (areaHeight - height) / 2, width, height, 0x0010))
            throw new InvalidOperationException("Overlay konnte nicht positioniert werden.");
    }

    private void OverlayPulseTick()
    {
        if (_closing || _closed) return;
        TasteNachfuehren();
        if (!_imOverlay || _overlayTransition) return;
        bool lost = !_ipc.Connected || _overlaySession != _sessionId || _overlayTarget?.Valid() != true;
        bool expired = _remoteOverlay && Stopwatch.GetElapsedTime(_overlayLeaseAck).TotalSeconds > 2;
        if (lost || expired || !OwnForeground())
        {
            _ = OverlayVerlassenAsync(returnFocus: false, desktop: lost);
            if (expired) SetStatus("Overlay geschlossen: Eingabesicherung konnte nicht rechtzeitig bestätigt werden.", true, transient: true);
            else if (lost && _ipc.Connected)
                SetStatus("Spielfenster nicht sichtbar. Für Vollbild im Spiel den Modus Randloses Vollbild wählen.", true, transient: true);
            return;
        }
        var current = _overlayTarget?.Refresh();
        if (current != null && !HasOwnedDialog())
        {
            // Recenter only if the game moved/resized, not after every UI tick.
            if (!current.ClientRect.Equals(_overlayTarget!.ClientRect) || !current.MonitorBounds.Equals(_overlayTarget.MonitorBounds))
            { try { PositionOverlay(current); } catch { _ = OverlayVerlassenAsync(returnFocus: false, desktop: true); } }
            _overlayTarget = current;
        }
        if (_remoteOverlay && !_overlayRenewing && Stopwatch.GetElapsedTime(_overlayLastRenew).TotalMilliseconds >= 650)
            _ = RenewOverlayAsync();
    }

    private async Task RenewOverlayAsync()
    {
        _overlayRenewing = true;
        _overlayLastRenew = Stopwatch.GetTimestamp();
        int epoch = _overlayEpoch;
        string session = _overlaySession;
        try
        {
            var response = await _ipc.SetOverlayAsync(true, session, () => _imOverlay && epoch == _overlayEpoch && !_closing);
            if (!_imOverlay || epoch != _overlayEpoch) return;
            if (response?["ok"]?.GetValue<bool>() == true && response["overlayOpen"]?.GetValue<bool>() == true
                && response["sessionId"]?.GetValue<string>() == session) _overlayLeaseAck = Stopwatch.GetTimestamp();
            else await OverlayVerlassenAsync(returnFocus: false, desktop: true);
        }
        finally { _overlayRenewing = false; }
    }

    private async Task OverlayVerlassenAsync(bool returnFocus, bool desktop = false, bool activateDesktop = false)
    {
        if (!_imOverlay || _overlayTransition) return;
        _overlayTransition = true;
        int generation = _ipc.Generation;
        string session = _overlaySession;
        var target = _overlayTarget;
        bool release = _remoteOverlay;
        ++_overlayEpoch; // A queued renewal must not reopen the backend after this close.
        try
        {
            foreach (var dialog in OwnedWindows.Cast<Window>().Where(w => w.IsVisible).ToArray()) dialog.Close();
            RestoreOverlayPlacement(desktop, activateDesktop);
            // Return focus before any network wait; a later Alt-Tab must stay with the user.
            if (returnFocus && target?.Valid() == true && !_closing) SetForegroundWindow(target.Hwnd);
            if (release && generation == _ipc.Generation && _ipc.Connected)
                await _ipc.SetOverlayAsync(false, session);
        }
        finally { _overlayTransition = false; }
    }

    private void RestoreOverlayPlacement(bool desktop, bool activateDesktop = false)
    {
        _imOverlay = false;
        Hide();
        Topmost = _topmostVorher;
        WindowStyle = _stilVorher; ResizeMode = _resizeVorher;
        ShowInTaskbar = _taskbarVorher;
        ((Grid)Content).LayoutTransform = _layoutVorher;
        SetOverlayView(false);
        MinWidth = _minWidthVorher; MinHeight = _minHeightVorher;
        WindowState = WindowState.Normal;
        Width = _breiteVorher; Height = _hoeheVorher;
        Left = _linksVorher; Top = _obenVorher;
        WindowState = desktop ? _zustandVorher : WindowState.Minimized;
        ShowActivated = desktop && activateDesktop;
        Show();
        ShowActivated = true;
        _overlayTarget = null;
    }

    private void StopOverlay()
    {
        ++_overlayEpoch;
        _overlayPulse.Stop();
        if (_tasteAngemeldet) UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_ID);
        _tasteAngemeldet = false;
        _quelle?.RemoveHook(FensterNachricht);
    }
}
