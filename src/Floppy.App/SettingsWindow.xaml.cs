using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Floppy.App;

public partial class SettingsWindow : Window
{
    private readonly IFloppyUpdates _updates;
    private readonly Func<AppPreferences, string?> _save;
    private readonly Func<bool> _canRestart;
    private bool _working;
    private bool _closed;

    public SettingsWindow(UserSettings settings, IFloppyUpdates updates, Func<AppPreferences, string?> save,
        Func<bool>? canRestart = null)
    {
        _updates = updates;
        _save = save;
        _canRestart = canRestart ?? (() => true);
        InitializeComponent();
        var preferences = settings.GetPreferences();
        AutoConnectToggle.IsChecked = preferences.AutoConnect;
        StartMinimizedToggle.IsChecked = preferences.StartMinimized;
        RememberWindowToggle.IsChecked = preferences.RememberWindowPosition;
        CheckUpdatesToggle.IsChecked = preferences.CheckUpdatesOnStartup;
        OverlayHotkeyChoice.ItemsSource = Enumerable.Range(1, 11).Select(number => "F" + number).ToArray();
        OverlayHotkeyChoice.SelectedItem = preferences.OverlayHotkey;
        _updates.Changed += OnUpdatesChanged;
        SourceInitialized += (_, _) => MainWindow.ApplyWindowColors(this);
        Closed += (_, _) => { _closed = true; _updates.Changed -= OnUpdatesChanged; };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || OverlayHotkeyChoice.IsDropDownOpen) return;
            e.Handled = true;
            Close();
        };
        RefreshUpdates();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var preferences = new AppPreferences(AutoConnectToggle.IsChecked == true, StartMinimizedToggle.IsChecked == true,
            RememberWindowToggle.IsChecked == true, OverlayHotkeyChoice.SelectedItem as string ?? "F1",
            CheckUpdatesToggle.IsChecked == true);
        string? error = _save(preferences);
        SaveStatus.Text = error ?? "Gespeichert.";
        SaveStatus.Foreground = (Brush)FindResource(error == null ? "Accent" : "Warn");
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnUpdatesChanged(object? sender, EventArgs e)
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) RefreshUpdates();
        else _ = Dispatcher.BeginInvoke(new Action(RefreshUpdates));
    }

    public void RefreshUpdates()
    {
        if (_closed) return;
        VersionLabel.Text = "v" + _updates.CurrentVersion;
        UpdateStatus.Text = _updates.Status;
        UpdateStatus.Foreground = (Brush)FindResource("Text");
        UpdateProgress.Visibility = _updates.Progress.HasValue ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgress.Value = _updates.Progress is double progress && double.IsFinite(progress) ? Math.Clamp(progress, 0, 1) : 0;
        CheckUpdatesButton.IsEnabled = !_working && !_updates.Busy;
        DownloadUpdateButton.IsEnabled = !_working && !_updates.Busy && _updates.CanDownload && _canRestart();
        DownloadUpdateButton.ToolTip = _canRestart() ? "Nach dem Download wird das Update geprüft und Floppy neu gestartet."
            : "Bitte warten, bis die laufende Einrichtung abgeschlossen ist.";
    }

    private async void OnCheckUpdates(object sender, RoutedEventArgs e) => await RunUpdateAction(_updates.CheckAsync);
    private async void OnDownloadUpdate(object sender, RoutedEventArgs e)
    {
        if (!_canRestart() || !_updates.CanDownload) return;
        await RunUpdateAction(_updates.DownloadAndRestartAsync);
    }

    private async Task RunUpdateAction(Func<Task> action)
    {
        if (_working || _updates.Busy) return;
        _working = true;
        RefreshUpdates();
        string? error = null;
        try { await action(); }
        catch (Exception ex) { error = ex.Message; }
        finally
        {
            _working = false;
            RefreshUpdates();
            if (!_closed && error != null)
            { UpdateStatus.Text = "Update nicht möglich: " + error; UpdateStatus.Foreground = (Brush)FindResource("Warn"); }
        }
    }

    private void OnOpenReleases(object sender, RoutedEventArgs e)
    {
        try { _updates.OpenReleases(); }
        catch (Exception ex)
        { UpdateStatus.Text = "Releaseverlauf konnte nicht geöffnet werden: " + ex.Message; }
    }
}
