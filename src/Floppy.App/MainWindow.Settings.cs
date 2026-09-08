using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Floppy.App;

public partial class MainWindow
{
    private IFloppyUpdates? _updates;
    private SettingsWindow? _settingsWindow;
    private bool _startupSettingsApplied;
    private bool _startupLoaded;
    private bool _updateRestartPreparing;
    private bool UpdateBusy => _updates?.Busy == true;
    private Key OverlayKey => Enum.Parse<Key>(UserSettings.NormalizeHotkey(_settings.OverlayHotkey));

    private IFloppyUpdates Updates
    {
        get
        {
            if (_updates != null) return _updates;
            _updates = new UpdateService(() =>
            {
                void Restart()
                {
                    _settingsWindow?.Close();
                    _updateRestartPreparing = false;
                    _closing = false;
                    Close();
                }
                if (Dispatcher.CheckAccess()) Restart();
                else _ = Dispatcher.BeginInvoke(new Action(Restart));
            }, PrepareUpdateRestartAsync, CancelUpdateRestart);
            _updates.Changed += OnMainUpdatesChanged;
            return _updates;
        }
    }

    private async Task PrepareUpdateRestartAsync()
    {
        if (_closing || _closed) throw new InvalidOperationException("Floppy wird bereits beendet.");
        _updateRestartPreparing = true;
        _closing = true;
        StopOverlay();
        IsEnabled = false;
        _poll.Stop();
        while (_installing) await Task.Delay(100);
        _numberSends.Clear();
        _ipc.Disconnect();
        RemoveShortcuts();
        await DrainHostsAsync();
    }

    private void CancelUpdateRestart()
    {
        if (!_updateRestartPreparing || _closed) return;
        _updateRestartPreparing = false;
        _closing = false;
        IsEnabled = true;
        // Preparation disconnected IPC. Keep the update error visible instead of
        // letting the next lost-connection pulse close every owned dialog.
        if (_imOverlay)
        {
            if (_settingsWindow != null) _settingsWindow.Topmost = false;
            RestoreOverlayPlacement(desktop: true);
        }
        _quelle?.AddHook(FensterNachricht);
        _overlayPulse.Start();
        _poll.Start();
        UpdateLaunchButton();
    }

    private void ApplyStartupSettings()
    {
        if (_startupSettingsApplied) return;
        _startupSettingsApplied = true;
        // Restoring a minimized window later should use its normal saved state.
        _zustandVorher = WindowState;
        if (_settings.StartMinimized)
        {
            ShowActivated = false;
            WindowState = WindowState.Minimized;
        }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        if (_closing || _closed) return;
        if (_settingsWindow != null) { _settingsWindow.Activate(); return; }
        var window = new SettingsWindow(_settings, Updates, ApplyAppPreferences, () => !_installing && !_closing && !_closed)
        { Owner = this, Topmost = _imOverlay };
        _settingsWindow = window;
        window.Closed += (_, _) => _settingsWindow = null;
        window.Show();
    }

    private string? ApplyAppPreferences(AppPreferences preferences)
    {
        if (_closing || _closed) return "Floppy wird gerade beendet.";
        var previous = _settings.GetPreferences();
        string key = UserSettings.NormalizeHotkey(preferences.OverlayHotkey);
        bool keyChanged = key != previous.OverlayHotkey;
        bool wasRegistered = _tasteAngemeldet;
        var handle = new WindowInteropHelper(this).Handle;
        if (keyChanged)
        {
            if (wasRegistered) UnregisterHotKey(handle, HOTKEY_ID);
            _tasteAngemeldet = RegisterHotKey(handle, HOTKEY_ID, 0x4000, (uint)KeyInterop.VirtualKeyFromKey(Enum.Parse<Key>(key)));
            if (!_tasteAngemeldet)
            {
                if (wasRegistered)
                    _tasteAngemeldet = RegisterHotKey(handle, HOTKEY_ID, 0x4000, (uint)KeyInterop.VirtualKeyFromKey(OverlayKey));
                return key + " ist bereits belegt. Bitte eine andere Overlay-Taste wählen.";
            }
        }
        _settings.ApplyPreferences(preferences);
        if (!_settings.Save(out string error))
        {
            _settings.ApplyPreferences(previous);
            if (keyChanged)
            {
                UnregisterHotKey(handle, HOTKEY_ID);
                _tasteAngemeldet = wasRegistered && RegisterHotKey(handle, HOTKEY_ID, 0x4000, (uint)KeyInterop.VirtualKeyFromKey(OverlayKey));
            }
            return "Speichern fehlgeschlagen: " + error;
        }
        if (!previous.AutoConnect && _settings.AutoConnect)
        { _manuellGetrennt = false; _lastConnectTry = DateTime.MinValue; }
        if (keyChanged)
        {
            _lastHotkeyAttempt = 0;
            RegisterShortcuts();
            TasteNachfuehren();
        }
        RefreshOverlayHotkeyLabels();
        return null;
    }

    private void RefreshOverlayHotkeyLabels()
    {
        string key = UserSettings.NormalizeHotkey(_settings.OverlayHotkey);
        OverlayButton.Content = "Overlay · " + key;
        OverlayCloseButton.Content = "Zurück zum Spiel · " + key;
        OverlayHotkeyHint.Text = key + "  Overlay";
        if (_gameWindow != null && OverlaySupported)
            OverlayButton.ToolTip = key + " öffnet das externe Overlay. Für Vollbild im Spiel den Modus Randloses Vollbild wählen.";
    }

    private void OnMainUpdatesChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        void Refresh()
        {
            if (_closed) return;
            UpdateLaunchButton();
            RestartElevatedItem.IsEnabled = RepairItem.IsEnabled = RestoreItem.IsEnabled = !_installing && !_closing && !UpdateBusy;
        }
        if (Dispatcher.CheckAccess()) Refresh();
        else _ = Dispatcher.BeginInvoke(new Action(Refresh));
    }

    private void DisposeSettings()
    {
        _settingsWindow?.Close();
        if (_updates == null) return;
        _updates.Changed -= OnMainUpdatesChanged;
        (_updates as IDisposable)?.Dispose();
    }
}
