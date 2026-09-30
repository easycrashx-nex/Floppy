using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Floppy.App;

public partial class MainWindow : Window
{
    private readonly IpcClient _ipc = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private readonly UserSettings _settings;
    private bool _tickRunning;
    private bool _connecting;
    private bool _closing;
    private bool _closed;
    private string _connectedGameId = "";
    private string _sessionId = "";
    private int _schemaVersion;
    private DateTime _noticeUntil;
    private readonly Queue<string> _events = new();
    private const int RubrikFavoriten = -2;

    private List<SupportedGame> _games = new();
    private SupportedGame? _selectedGame;

    private List<CategoryInfo> _categories = new();

    /// <summary>Welche Rubrik gerade offen ist. -1 steht für "Aktiv" - die Liste aller
    /// gerade eingeschalteten Cheats, quer über alle Rubriken.</summary>
    private int _selectedCategory;

    private const int RubrikAktiv = -1;

    /// <summary>Der Suchtext. Solange er gesetzt ist, zählt er mehr als die Rubrik:
    /// gesucht wird über den ganzen Bestand, nicht nur in der offenen Rubrik.</summary>
    private string _suche = "";

    /// <summary>Welche Cheats du in dieser Sitzung angefasst hast.
    ///
    /// "Eingeschaltet" lässt sich nicht am Wert ablesen: Ein Attribut wie Poise steht
    /// von Haus aus auf 5, und Leben ändert sich im Kampf von selbst. Was zählt, ist
    /// also nicht der Wert, sondern ob du daran gedreht hast.</summary>
    private readonly HashSet<string> _angefasst = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, OptionInfo> _byId = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Steuerelemente, die beim Abgleich mit dem Spiel aktualisiert werden.</summary>
    private readonly Dictionary<string, Action<OptionInfo>> _refreshers = new();

    /// <summary>Verhindert, dass ein Abgleich aus dem Spiel wieder Befehle ins Spiel schickt.</summary>
    private bool _suppressEvents;

    /// <summary>Wann zuletzt das Schema geholt wurde. Ohne Drosselung würde die
    /// Nachfrage nach noch leeren Auswahllisten die Oberfläche im Sekundentakt neu bauen.</summary>
    private DateTime _lastSchemaLoad = DateTime.MinValue;

    public MainWindow() : this(false) { }

    // Offline wird nur für den isolierten UI-Test verwendet: keine Spiele, keine gespeicherten Einstellungen.
    public MainWindow(bool offline)
    {
        _settings = offline ? new UserSettings() : UserSettings.Load();
        InitializeComponent();
        RestoreWindow();
        RefreshOverlayHotkeyLabels();
        if (offline) return;
        ApplyStartupSettings();
        _poll.Tick += async (_, _) => await TickAsync();

        Loaded += async (_, _) =>
        {
            if (_startupLoaded) return;
            _startupLoaded = true;
            ShowActivated = true;
            OverlayVorbereiten();

            if (_settings.CheckUpdatesOnStartup) _ = Updates.CheckAsync();

            GameCatalog.WriteTemplateIfMissing();
            ScanLibrary();
            await TryConnectAsync(quiet: true);
            _poll.Start();
        };
        Closing += OnClosing;
        Closed += (_, _) => { _closed = true; _poll.Stop(); _ipc.Dispose(); RemoveShortcuts(); DisposeSettings(); };
    }

    // ---------------------------------------------------------------- Spielebibliothek

    /// <summary>Durchsucht die Steam-Bibliothek nach Spielen, für die es ein Modul gibt.</summary>
    private void ScanLibrary()
    {
        string previous = _selectedGame?.ProductName ?? _settings.LastGame;
        _games = GameCatalog.Scan(_settings.GameFolders);
        RefreshGameStatuses();

        int installed = _games.Count(g => g.Installed);

        LibraryHint.Text = installed == 0
            ? "Keins der unterstützten Spiele gefunden. Steht Steam woanders? Dann hilft \"Neu suchen\"."
            : $"{installed} von {_games.Count} unterstützten Spielen installiert.";

        // Läuft schon eins? Dann das auswählen - sonst das erste installierte.
        _selectedGame = _games.FirstOrDefault(g => g.ProductName == previous)
                          ?? _games.FirstOrDefault(g => g.Installed && IsRunning(g))
                          ?? _games.FirstOrDefault(g => g.Installed)
                          ?? _games.FirstOrDefault();

        BindGameList();
    }

    /// <summary>Schreibt die Zeile unter jedem Spielnamen neu.</summary>
    private void RefreshGameStatuses()
    {
        string? connectedGame = _ipc.Connected ? _connectedGameId : null;

        foreach (var game in _games)
        {
            if (connectedGame != null &&
                string.Equals(game.ProductName, connectedGame, StringComparison.OrdinalIgnoreCase))
            {
                game.Status = "verbunden";
            }
            else if (!game.Installed)
            {
                game.Status = "nicht installiert";
            }
            else if (IsRunning(game))
            {
                game.Status = "läuft";
            }
            else if (game.InstallDir != null && !Installer.Bereit(game.InstallDir, game.ProductName))
            {
                game.Status = "wird beim Starten eingerichtet";
            }
            else
            {
                game.Status = "bereit";
            }
        }
    }

    /// <summary>Hilfsprogramme, die viele Engines mitbringen. Würde man auf die hören,
    /// meldete jedes zweite Unity-Spiel fälschlich "läuft".</summary>
    private static readonly HashSet<string> SharedHelpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityCrashHandler64", "UnityCrashHandler32", "crashpad_handler",
        "UnityPlayer", "CrashReportClient", "vc_redist", "DXSETUP", "steam_api"
    };

    /// <summary>Läuft dieses Spiel gerade? Die Exe-Namen stehen im Installationsordner.</summary>
    private static bool IsRunning(SupportedGame game)
    {
        if (game.InstallDir == null) return false;

        try
        {
            return System.IO.Directory
                .EnumerateFiles(game.InstallDir, "*.exe", System.IO.SearchOption.AllDirectories)
                .Select(System.IO.Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrEmpty(name) && !SharedHelpers.Contains(name!))
                .Distinct()
                .Any(name => Process.GetProcessesByName(name).Length > 0);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Liste neu befüllen. Die Einträge melden Aenderungen nicht selbst,
    /// deshalb wird sie im Ganzen ersetzt.</summary>
    private void BindGameList()
    {
        _suppressEvents = true;
        try
        {
            GameList.ItemsSource = null;
            GameList.ItemsSource = _games;
            GameList.SelectedItem = _selectedGame;
        }
        finally
        {
            _suppressEvents = false;
        }

        UpdateLaunchButton();
    }

    private void OnGameSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (GameList.SelectedItem is not SupportedGame game) return;

        _selectedGame = game;
        _settings.LastGame = game.ProductName;
        UpdateLaunchButton();
        UpdateEmptyHint();
    }

    private void OnRescanLibrary(object sender, RoutedEventArgs e)
    {
        ScanLibrary();
        UpdateEmptyHint();
        SetStatus("Bibliothek neu durchsucht", warn: false);
    }

    private void UpdateLaunchButton()
    {
        LaunchButton.IsEnabled = _selectedGame?.Installed == true && !_installing && !_closing && !UpdateBusy;
        LaunchButton.Content = _selectedGame == null ? "Spiel starten" : "Starten";
        _settingsWindow?.RefreshUpdates();
    }

    private async void OnLaunchGame(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is not { Installed: true, InstallDir: not null } game || _installing || _closing || _closed || UpdateBusy) return;
        _installing = true;
        _settingsWindow?.RefreshUpdates();
        LaunchButton.IsEnabled = false;
        GameList.IsEnabled = false;
        try
        {
            var setup = await Task.Run(() =>
            {
                string message = "Floppy ist aktuell";
                bool ok = Installer.Bereit(game.InstallDir, game.ProductName) || Installer.Einrichten(game.InstallDir, out message, game.ProductName);
                return (ok, message);
            });
            SetStatus(setup.message, !setup.ok, transient: true);
            if (!setup.ok || _closing) return;
            RefreshGameStatuses();
            BindGameList();
            Process.Start(new ProcessStartInfo("steam://rungameid/" + game.AppId) { UseShellExecute = true });
            SetStatus(game.Name + " wird gestartet…", false, transient: true);
        }
        catch (Exception ex) { SetStatus("Start fehlgeschlagen: " + ex.Message, true, transient: true); }
        finally { _installing = false; GameList.IsEnabled = true; UpdateLaunchButton(); }
    }
    // ---------------------------------------------------------------- Verbindung

    private async void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        if (_ipc.Connected)
        {
            _manuellGetrennt = true;
            Disconnect();
            return;
        }

        _manuellGetrennt = false;
        await TryConnectAsync(quiet: false);
    }

    /// <summary>Spiele ohne eigenes Modul im Spiel bedienen wir aus dem Client heraus.
    ///
    /// Mortal Shell II ist Unreal - dort liegt kein Floppy im Spielordner. Der Dienst,
    /// mit dem sich die Oberfläche sonst verbindet, läuft deshalb hier bei uns. Für
    /// alles Weitere ist es dieselbe Verbindung wie immer.</summary>
    private void StarteEigenenDienstFallsNoetig()
    {
        bool mortalShell = Process.GetProcessesByName(Floppy.Unreal.Spiel.Prozess).Length > 0;
        bool unrailed = Process.GetProcessesByName(Floppy.Unrailed2.Host.Prozessname).Length > 0;
        bool dungeons = Process.GetProcessesByName(Floppy.Dungeons2.DungeonsModule.ProcessName).Length > 0;

        // Unrailed 2 ist der eine Fall, in dem das Modul auch ohne laufendes Spiel
        // gebraucht wird: Der Schalter, der den Entwicklerzugang anschaltet, lässt sich
        // nur bei geschlossenem Spiel setzen - beim Beenden schreibt es die
        // Einstellungsdatei sonst wieder über unsere Zeilen.
        bool unrailedGewaehlt = _selectedGame?.ProductName == "Unrailed2";

        if (Floppy.Dungeons2.Host.Laeuft)
        {
            if (!dungeons) Floppy.Dungeons2.Host.Stoppe();
            return;
        }

        if (Floppy.Unreal.Host.Laeuft)
        {
            // Beim Spielende wieder abräumen. Sonst bliebe der Dienst mit einem toten
            // Spiel dahinter stehen - und mit ihm die systemweit belegte F1-Taste.
            if (!mortalShell) Floppy.Unreal.Host.Stoppe();
            return;
        }

        if (Floppy.Unrailed2.Host.Laeuft)
        {
            if (!unrailed && !unrailedGewaehlt) Floppy.Unrailed2.Host.Stoppe();
            return;
        }

        // Beide teilen sich denselben Port, es kann also nur einer laufen. Ein
        // tatsächlich laufendes Spiel hat Vorrang vor einem bloß ausgewählten.
        if (dungeons && _selectedGame?.ProductName == "MinecraftDungeons2") { Floppy.Dungeons2.Host.Starte(out _); return; }
        if (mortalShell) { Floppy.Unreal.Host.Starte(out _); return; }
        if (dungeons) { Floppy.Dungeons2.Host.Starte(out _); return; }
        if (unrailed || unrailedGewaehlt) Floppy.Unrailed2.Host.Starte(out _);
    }

    private async Task TryConnectAsync(bool quiet)
    {
        if (_connecting || _closing || _closed || (quiet && !_settings.AutoConnect)) return;
        _connecting = true;
        try
        {
        ConnectButton.IsEnabled = false;
        ConnectButton.Content = "Verbinde...";

        StarteEigenenDienstFallsNoetig();
        TasteNachfuehren();

        bool connected = await _ipc.ConnectAsync();

        if (quiet && !_settings.AutoConnect)
        { _ipc.Disconnect(); ConnectButton.Content = "Verbinden"; return; }

        ConnectButton.IsEnabled = true;

        if (!connected)
        {
            ConnectButton.Content = "Verbinden";
            SetStatus("Spiel nicht gefunden – Details unter Werkzeuge / Diagnose", warn: true);
            GameLabel.Text = "nicht verbunden";
            UpdateEmptyHint();

            if (!quiet)
            {
                MessageBox.Show(this,
                    "Es läuft kein Spiel mit dem Plugin.\n\n" +
                    "Starte eins aus der Liste und warte, bis du im Hauptmenü bist. " +
                    "Danach hier auf Verbinden klicken.",
                    "Keine Verbindung", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        ConnectButton.Content = "Trennen";
        await LoadSchemaAsync();
        }
        finally { _connecting = false; ConnectButton.IsEnabled = !_closing; }
    }

    private void Disconnect()
    {
        if (_imOverlay) _ = OverlayVerlassenAsync(returnFocus: false, desktop: true);
        // Der Takt laeuft weiter - er haelt die Statuszeile aktuell und verbindet
        // von selbst neu, sobald das Spiel wieder da ist.
        _ipc.Disconnect();
        _numberSends.Clear();
        RemoveShortcuts();
        _connectedGameId = "";
        _schemaVersion = 0;
        _sessionId = "";
        _remoteOverlay = false;
        _serverProcessId = 0;
        UpdateOverlayTarget();
        ConnectButton.Content = "Verbinden";
        GameLabel.Text = "nicht verbunden";
        SetStatus("getrennt", warn: true);

        OptionsPanel.Children.Clear();
        CategoryList.Children.Clear();
        _refreshers.Clear();
        _byId.Clear();
        _categories.Clear();

        // Sonst zählte "Aktiv" noch die Cheats mit, die im vorigen Spiel an waren
        _angefasst.Clear();

        RefreshGameStatuses();
        BindGameList();
        UpdateEmptyHint();
    }

    // ---------------------------------------------------------------- Daten holen

    private async Task LoadSchemaAsync()
    {
        int generation = _ipc.Generation;
        var response = await _ipc.GetSchemaAsync();
        if (generation != _ipc.Generation || _closing || _closed) return;
        if (response == null || response["ok"]?.GetValue<bool>() != true)
        {
            SetStatus("Spiel antwortet nicht", warn: true);
            return;
        }

        _lastSchemaLoad = DateTime.UtcNow;

        // Die gewählte Kategorie über das Neuladen hinweg behalten - sonst springt
        // die Ansicht bei jedem Abgleich zurück auf die erste.
        int previousSpecial = _selectedCategory;
        string? previousCategory = _categories.Count > 0 && _selectedCategory >= 0
            ? _categories[Math.Clamp(_selectedCategory, 0, _categories.Count - 1)].Name
            : null;

        GameLabel.Text = response["game"]?.GetValue<string>() ?? "";
        _connectedGameId = response["gameId"]?.GetValue<string>()
            ?? _games.FirstOrDefault(g => g.ProductName == GameLabel.Text || g.Name == GameLabel.Text)?.ProductName
            ?? GameLabel.Text;
        string session = response["sessionId"]?.GetValue<string>() ?? _connectedGameId;
        if (_sessionId != session)
        {
            if (_imOverlay) await OverlayVerlassenAsync(returnFocus: false, desktop: true);
            if (generation != _ipc.Generation || _closing || _closed) return;
            _angefasst.Clear();
        }
        _sessionId = session;
        _serverProcessId = response["processId"]?.GetValue<int>() ?? 0;
        _remoteOverlay = response["externalOverlay"]?.GetValue<bool>() ?? false;
        _schemaVersion = response["schemaVersion"]?.GetValue<int>() ?? 0;

        _categories = ParseCategories(response["categories"] as JsonArray);
        _byId.Clear();
        foreach (var option in _categories.SelectMany(c => c.Options))
            _byId[option.Id] = option;

        ApplyValues(response["values"] as JsonObject);
        ApplyStatus(response);

        // Das verbundene Spiel in der Bibliothek hervorheben.
        var match = _games.FirstOrDefault(g =>
            string.Equals(g.ProductName, _connectedGameId, StringComparison.OrdinalIgnoreCase));

        if (match != null) _selectedGame = match;

        RefreshGameStatuses();
        BindGameList();

        _selectedCategory = previousSpecial < 0 ? previousSpecial : previousCategory == null
            ? 0
            : Math.Max(0, _categories.FindIndex(c => c.Name == previousCategory));

        RenderCategoryList();
        RenderOptions();
        UpdateEmptyHint();
        RegisterShortcuts();
        UpdateOverlayTarget();
    }

    private static List<CategoryInfo> ParseCategories(JsonArray? array)
    {
        var result = new List<CategoryInfo>();
        if (array == null) return result;

        foreach (var node in array.OfType<JsonObject>())
        {
            var category = new CategoryInfo { Name = node["name"]?.GetValue<string>() ?? "?" };

            foreach (var entry in (node["options"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                category.Options.Add(new OptionInfo
                {
                    Id = entry["id"]?.GetValue<string>() ?? "",
                    Label = entry["label"]?.GetValue<string>() ?? "",
                    Description = entry["description"]?.GetValue<string>() ?? "",
                    Kind = entry["kind"]?.GetValue<string>() ?? "Toggle",
                    Scope = entry["scope"]?.GetValue<string>() ?? "OnlyMe",
                    Min = entry["min"]?.GetValue<double>() ?? 0,
                    Max = entry["max"]?.GetValue<double>() ?? 100,
                    Step = entry["step"]?.GetValue<double>() ?? 1,
                    ResetNumber = entry["resetNumber"]?.GetValue<double>(),
                    ResetChoice = entry["resetChoice"]?.GetValue<int>(),
                    Choices = (entry["choices"] as JsonArray ?? new JsonArray())
                        .Select(c => c?.GetValue<string>() ?? "").ToArray()
                });
            }

            result.Add(category);
        }
        return result;
    }

    /// <summary>Wann zuletzt eine Verbindung versucht wurde.</summary>
    private DateTime _lastConnectTry = DateTime.MinValue;

    /// <summary>Ob der Nutzer selbst getrennt hat - dann nicht von allein neu verbinden.</summary>
    private bool _manuellGetrennt;

    private async Task TickAsync()
    {
        if (_tickRunning || _connecting || _closing || _closed) return;
        _tickRunning = true;
        try
        {
        if (_settings.AutoConnect || _ipc.Connected) StarteEigenenDienstFallsNoetig();
        TasteNachfuehren();
        if (_ipc.Connected)
        {
            UpdateOverlayTarget();
            RefreshShortcutFocus();
            await RefreshStateAsync();
            return;
        }

        await LeerlaufAsync();
        }
        catch (Exception ex) { SetStatus(ex.Message, true, transient: true); }
        finally { _tickRunning = false; }
    }

    /// <summary>Ohne Verbindung: Zustand der Spiele auffrischen, Statuszeile ehrlich
    /// halten und ab und zu selbst eine Verbindung versuchen.</summary>
    private async Task LeerlaufAsync()
    {
        string vorher = string.Join("|", _games.Select(g => g.Status));
        RefreshGameStatuses();

        // Nur neu binden, wenn sich wirklich etwas geändert hat - sonst flackert die Liste
        if (string.Join("|", _games.Select(g => g.Status)) != vorher)
            BindGameList();

        // Auf JEDES laufende Spiel achten, nicht nur auf das ausgewählte. Sonst bleibt
        // die App stumm, während nebenan ein anderes unterstütztes Spiel läuft.
        bool laeuft = _games.Any(g => g.Installed && IsRunning(g));

        if (_settings.AutoConnect && laeuft && !_manuellGetrennt &&
            DateTime.UtcNow - _lastConnectTry > TimeSpan.FromSeconds(3))
        {
            _lastConnectTry = DateTime.UtcNow;
            StarteEigenenDienstFallsNoetig();
            TasteNachfuehren();

            await TryConnectAsync(quiet: true);
            if (_ipc.Connected) return;
        }

        SetStatus(!_settings.AutoConnect ? "Automatische Verbindung aus · Zum Verbinden auf Verbinden klicken"
                  : laeuft ? "Spiel läuft - warte auf das Plugin" : "Kein Spiel gestartet",
                  warn: _settings.AutoConnect);
    }

    private async Task RefreshStateAsync()
    {
        if (_closing || _closed) return;
        if (!_ipc.Connected) { Disconnect(); return; }

        int generation = _ipc.Generation;
        var response = await _ipc.GetStateAsync();
        if (generation != _ipc.Generation)
        {
            // Ein Fehler kann diese Verbindung beendet haben; eine neue bleibt unangetastet.
            if (!_ipc.Connected && !_connecting) Disconnect();
            return;
        }
        if (_closing || _closed) return;
        if (response == null) { Disconnect(); return; }
        if (response["ok"]?.GetValue<bool>() != true) { ReportIfFailed(response); return; }
        if ((response["schemaVersion"]?.GetValue<int>() ?? 0) != _schemaVersion ||
            (response["sessionId"]?.GetValue<string>() ?? _sessionId) != _sessionId)
        { await LoadSchemaAsync(); return; }

        string previousActive = string.Join("|", AlleAktiven().Select(o => o.Id));
        ApplyValues(response["values"] as JsonObject);
        ApplyStatus(response);
        if (previousActive != string.Join("|", AlleAktiven().Select(o => o.Id)))
        {
            RenderCategoryList();
            if (_selectedCategory == RubrikAktiv && !OptionsPanel.IsKeyboardFocusWithin) RenderOptions();
        }

        // Auswahllisten können sich im Spiel ändern - sie füllen sich beim Rundenstart,
        // und die Spawn-Liste hängt an der gewählten Rubrik. Weicht die gemeldete Anzahl
        // von unserer ab, holen wir das Schema neu.
        if (ChoiceListsVeraltet(response["values"] as JsonObject))
            await LoadSchemaAsync();
    }

    /// <summary>Hat sich im Spiel eine Auswahlliste geändert?
    ///
    /// Schickt das Spiel die Liste selbst mit, wird sie verglichen. Sonst bleibt nur
    /// die Anzahl - die reicht bei abhängigen Listen aber nicht: wechselt man von einer
    /// Rubrik auf eine gleich lange, ändert sich nur der Inhalt.</summary>
    private bool ChoiceListsVeraltet(JsonObject? values)
    {
        if (values == null) return false;

        foreach (var pair in values)
        {
            if (!_byId.TryGetValue(pair.Key, out var option)) continue;
            if (option.Kind != "Choice") continue;
            if (pair.Value is not JsonObject state) continue;

            if (state["choices"] is JsonArray gemeldeteListe)
            {
                if (gemeldeteListe.Count != option.Choices.Length) return true;

                for (int i = 0; i < option.Choices.Length; i++)
                    if ((gemeldeteListe[i]?.GetValue<string>() ?? "") != option.Choices[i])
                        return true;

                continue;
            }

            int gemeldet = state["choiceCount"]?.GetValue<int>() ?? option.Choices.Length;
            if (gemeldet != option.Choices.Length) return true;
        }

        return false;
    }

    private void ApplyValues(JsonObject? values)
    {
        if (values == null) return;

        _suppressEvents = true;
        try
        {
            foreach (var pair in values)
            {
                if (!_byId.TryGetValue(pair.Key, out var option)) continue;
                if (pair.Value is not JsonObject state) continue;

                option.BoolValue = state["bool"]?.GetValue<bool>() ?? false;
                option.NumberValue = state["number"]?.GetValue<double>() ?? 0;
                option.TextValue = state["text"]?.GetValue<string>() ?? "";
                option.ChoiceIndex = state["choice"]?.GetValue<int>() ?? 0;
                option.ShareWithOthers = state["share"]?.GetValue<bool>() ?? false;
                option.Available = state["available"]?.GetValue<bool>() ?? true;
                option.Active = state["active"]?.GetValue<bool>();

                if (_refreshers.TryGetValue(option.Id, out var refresh))
                    refresh(option);
            }
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplyStatus(JsonObject response)
    {
        bool ready = response["ready"]?.GetValue<bool>() ?? false;
        string status = response["status"]?.GetValue<string>() ?? "";
        SetStatus(status, warn: !ready);
    }

    private void SetStatus(string text, bool warn, bool transient = false)
    {
        if (_closed) return;
        if (!transient && DateTime.UtcNow < _noticeUntil) return;
        if (transient)
        {
            _noticeUntil = DateTime.UtcNow.AddSeconds(8);
            _events.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            while (_events.Count > 40) _events.Dequeue();
        }
        StatusLabel.Text = text;
        StatusLabel.ToolTip = text;

        var farbe = warn ? (Brush)FindResource("Warn") : (Brush)FindResource("Ok");
        StatusLabel.Foreground = farbe;
        StatusDot.Fill = string.IsNullOrEmpty(text) ? (Brush)FindResource("Muted") : farbe;
    }

    // ---------------------------------------------------------------- Suchen und Zählen

    private void OnFocusSearch(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _suche = SearchBox.Text.Trim();
        RenderCategoryList();
        RenderOptions();
    }

    /// <summary>Ist der Cheat gerade eingeschaltet?
    ///
    /// Ein Schalter zählt, wenn er an ist; ein Regler oder ein Zahlenfeld, wenn es nicht
    /// auf dem Ruhewert steht. Knöpfe und Anzeigen haben keinen Zustand, den man
    /// vergessen könnte - die zählen nie mit.</summary>
    private bool IstAn(OptionInfo option)
    {
        if (option.Active.HasValue) return option.Active.Value;
        // Ein Schalter zählt nur, solange er an ist - ausgeschaltet ist ausgeschaltet.
        if (option.Kind == "Toggle") return option.BoolValue;

        // Alles andere zählt, sobald du es angefasst hast.
        return _angefasst.Contains(option.Id);
    }

    private void Merke(OptionInfo option)
    {
        if (option.Kind == "Button" || option.Kind == "Info") return;

        // Beim Tippen in ein Zahlenfeld kommt das hier je Anschlag vorbei. Nur wenn
        // wirklich ein neuer Cheat dazukommt, wird die Rubrikenliste neu gezeichnet -
        // sonst springt sie einem bei jedem Tastendruck unter dem Finger weg.
        if (!_angefasst.Add(option.Id)) return;

        RenderCategoryList();
    }

    private IEnumerable<OptionInfo> AlleAktiven() =>
        _categories.SelectMany(c => c.Options).Where(IstAn);

    private bool PasstZurSuche(OptionInfo option)
    {
        if (_suche.Length == 0) return true;

        return option.Label.Contains(_suche, StringComparison.CurrentCultureIgnoreCase)
               || option.Description.Contains(_suche, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>Alles ausschalten - über den Cheat, den der Kern dafür mitbringt.</summary>
    private async void OnAllOff(object sender, RoutedEventArgs e)
    {
        if (!_ipc.Connected || _closing || _closed) return;
        int generation = _ipc.Generation;
        _numberSends.Clear();

        if (_byId.ContainsKey("profil.zuruecksetzen"))
        {
            if (!ReportIfFailed(await _ipc.InvokeAsync("profil.zuruecksetzen"))) return;
        }
        else
        {
            // Project P.I.T.T. bringt die Profilverwaltung nicht mit - dort ist der
            // Godot-Mod eigener Code. Dann legen wir die Schalter eben einzeln um.
            int aus = 0;

            foreach (var option in _categories.SelectMany(c => c.Options))
            {
                if (generation != _ipc.Generation) return;
                if (option.Kind != "Toggle" || !option.BoolValue) continue;

                if (!ReportIfFailed(await _ipc.SetBoolAsync(option.Id, false))) return;
                aus++;
            }

            SetStatus(aus == 0 ? "Es war nichts an" : aus + " Schalter ausgeschaltet", warn: false, transient: true);
        }

        _angefasst.Clear();
        _numberSends.Clear();
        await RefreshStateAsync();
        RenderCategoryList();
    }

    // ---------------------------------------------------------------- Oberfläche bauen

    /// <summary>Die Rubrikenleiste.
    ///
    /// Rubriken zeigen ihre Anzahl; Mint kennzeichnet Bereiche mit aktiven Optionen.
    /// Favoriten und Aktiv sammeln Funktionen aus allen Rubriken.</summary>
    private void RenderCategoryList()
    {
        var focusedCategory = CategoryList.Children.OfType<Button>().FirstOrDefault(b => b.IsKeyboardFocusWithin)?.Tag;
        CategoryList.Children.Clear();
        if (_categories.Count == 0) return;

        int aktiv = AlleAktiven().Count();
        int favorites = _categories.SelectMany(c => c.Options).Count(IsFavorite);
        CategoryList.Children.Add(BuildCategoryEntry("Favoriten", RubrikFavoriten, favorites, 0, true));
        CategoryList.Children.Add(BuildCategoryEntry("Aktiv", RubrikAktiv, aktiv, aktiv, true));

        CategoryList.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)FindResource("Line"),
            Margin = new Thickness(6, 6, 6, 6)
        });

        for (int i = 0; i < _categories.Count; i++)
        {
            var kategorie = _categories[i];

            int treffer = _suche.Length == 0
                ? kategorie.Options.Count
                : kategorie.Options.Count(PasstZurSuche);

            // Beim Suchen leere Rubriken weglassen - sonst sucht man in der Liste der Listen
            if (_suche.Length > 0 && treffer == 0) continue;

            CategoryList.Children.Add(BuildCategoryEntry(
                kategorie.Name, i, treffer, kategorie.Options.Count(IstAn), false));
        }
        if (focusedCategory is int focused)
            CategoryList.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, focused))?.Focus();
    }

    private Button BuildCategoryEntry(string name, int index, int anzahl, int aktiv, bool betont)
    {
        bool selected = index == _selectedCategory;
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = name, FontSize = 12.5,
            FontWeight = selected || betont ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = (Brush)FindResource(selected ? "Accent" : "Muted"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0)
        };
        var count = new TextBlock
        {
            Text = anzahl.ToString(), FontSize = 10.5,
            FontFamily = (FontFamily)FindResource("Mono"),
            Foreground = (Brush)FindResource(aktiv > 0 ? "Accent" : "Muted"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(count, 1);
        line.Children.Add(title); line.Children.Add(count);
        var button = new Button
        {
            Content = line, Tag = index, Style = (Style)FindResource("GhostButton"),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(10, 11, 10, 11), Margin = new Thickness(0, 0, 0, 4),
            Background = (Brush)FindResource(selected ? "AccentDim" : "Bg"),
            ToolTip = aktiv > 0 ? $"{anzahl} Funktionen · {aktiv} aktiv" : $"{anzahl} Funktionen"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += (_, _) =>
        {
            _selectedCategory = index;
            RenderCategoryList();
            RenderOptions();
        };
        return button;
    }

    /// <summary>Blendet den Hinweis ein, solange es nichts anzuzeigen gibt - und erklärt,
    /// woran es gerade hängt.</summary>
    private void UpdateEmptyHint()
    {
        bool hasContent = _categories.Count > 0 && _ipc.Connected;

        EmptyHint.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;

        if (hasContent) return;
        PaneTitle.Text = "Übersicht";
        PaneCount.Text = "";

        if (_games.Count(g => g.Installed) == 0)
        {
            EmptyTitle.Text = "Kein unterstütztes Spiel gefunden";
            EmptyBody.Text = "Deine Steam-Bibliothek wurde durchsucht, aber keins der unterstützten " +
                             "Spiele entdeckt. Mit \"Neu suchen\" läuft die Suche nochmal.";
        }
        else if (_selectedGame == null)
        {
            EmptyTitle.Text = "Wähle ein Spiel";
            EmptyBody.Text = "Wähle links ein Spiel aus deiner Bibliothek. Die passenden Optionen werden nach dem Verbinden geladen.";
        }
        else
        {
            EmptyTitle.Text = "Bereit für " + _selectedGame.Name;
            EmptyBody.Text = "Starte das Spiel und öffne das Hauptmenü. Floppy verbindet sich automatisch und lädt deine verfügbaren Optionen.";
        }
    }

    private void RenderOptions()
    {
        OptionsPanel.Children.Clear();
        _refreshers.Clear();

        if (_categories.Count == 0) return;

        // Beim Suchen zählt die Rubrik nicht - gesucht wird über den ganzen Bestand.
        if (_suche.Length > 0) { RenderSuchergebnis(); return; }

        if (_selectedCategory == RubrikAktiv) { RenderAktive(); return; }
        if (_selectedCategory == RubrikFavoriten) { RenderFavorites(); return; }

        _selectedCategory = Math.Clamp(_selectedCategory, 0, _categories.Count - 1);
        var category = _categories[_selectedCategory];

        PaneTitle.Text = category.Name;
        PaneCount.Text = category.Options.Count + " Einträge";

        int index = 0;
        foreach (var option in category.Options)
            OptionsPanel.Children.Add(BuildRow(option, index++));
    }

    /// <summary>Alles, was gerade eingeschaltet ist - quer über alle Rubriken.
    ///
    /// Das ist bei so vielen Cheats die eigentliche Frage: nicht "wo finde ich den
    /// Schalter", sondern "was habe ich vorhin angelassen".</summary>
    private void RenderAktive()
    {
        PaneTitle.Text = "Aktiv";

        var aktive = AlleAktiven().ToList();
        PaneCount.Text = aktive.Count == 0 ? "nichts an" : aktive.Count + " eingeschaltet";

        if (aktive.Count == 0)
        {
            OptionsPanel.Children.Add(new TextBlock
            {
                Text = "Gerade ist nichts eingeschaltet. Was du anschaltest, steht hier - " +
                       "mit der Rubrik, aus der es kommt.",
                Style = (Style)FindResource("Hint"),
                Margin = new Thickness(4, 12, 4, 0)
            });

            return;
        }

        int index = 0;
        foreach (var kategorie in _categories)
        {
            var treffer = kategorie.Options.Where(IstAn).ToList();
            if (treffer.Count == 0) continue;

            OptionsPanel.Children.Add(Abschnitt(kategorie.Name));

            foreach (var option in treffer)
                OptionsPanel.Children.Add(BuildRow(option, index++));
        }
    }

    private void RenderSuchergebnis()
    {
        PaneTitle.Text = "Suche";

        int gesamt = 0;
        int index = 0;

        foreach (var kategorie in _categories)
        {
            var treffer = kategorie.Options.Where(PasstZurSuche).ToList();
            if (treffer.Count == 0) continue;

            OptionsPanel.Children.Add(Abschnitt(kategorie.Name));

            foreach (var option in treffer)
            {
                OptionsPanel.Children.Add(BuildRow(option, index++));
                gesamt++;
            }
        }

        PaneCount.Text = gesamt == 0 ? "nichts gefunden" : gesamt + " Treffer";

        if (gesamt == 0)
        {
            OptionsPanel.Children.Add(new TextBlock
            {
                Text = "Keine passende Funktion gefunden. Versuche einen Teil des Namens, etwa " +
                       "\"leben\", \"tempo\", \"gold\".",
                Style = (Style)FindResource("Hint"),
                Margin = new Thickness(4, 12, 4, 0)
            });
        }
    }

    /// <summary>Eine Zwischenüberschrift, wenn Zeilen aus mehreren Rubriken kommen.</summary>
    private TextBlock Abschnitt(string name)
    {
        return new TextBlock
        {
            Text = name,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Muted"),
            Margin = new Thickness(4, 12, 0, 6)
        };
    }

    /// <summary>Lässt eine Zeile sanft von unten einschweben. Der leichte Versatz je Zeile
    /// erzeugt beim Kategoriewechsel eine Welle statt eines harten Umschlags.</summary>
    private static void FadeIn(FrameworkElement element, int index)
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var shift = new TranslateTransform(0, 4);
        element.RenderTransform = shift;

        double targetOpacity = element.Opacity;
        element.Opacity = 0;

        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 6) * 12);

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = targetOpacity,
            Duration = TimeSpan.FromMilliseconds(140),
            BeginTime = delay
        });

        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = 4,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(160),
            BeginTime = delay,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private Border BuildRow(OptionInfo option, int index)
    {
        var content = new StackPanel();
        content.Children.Add(BuildControl(option));

        if (option.Scope == "Selectable")
            content.Children.Add(BuildShareToggle(option));

        if (option.Scope == "Everyone")
        {
            content.Children.Add(new TextBlock
            {
                Text = "betrifft alle in der Runde",
                Foreground = (Brush)FindResource("Warn"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }

        if (!string.IsNullOrWhiteSpace(option.Description))
        {
            content.Children.Add(new TextBlock
            {
                Text = option.Description,
                Style = (Style)FindResource("Hint")
            });
        }

        var row = new Border
        {
            Background = (Brush)FindResource("BgRow"),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = (Brush)FindResource(IstAn(option) ? "AccentDim" : "BgRow"),
            Padding = new Thickness(14, 14, 14, 14),
            Margin = new Thickness(0, 0, 0, 8),
            Child = WithOptionTools(option, content)
        };

        // Nicht verfügbare Cheats bleiben sichtbar, aber ausgegraut.
        Register(option, state =>
        {
            row.Opacity = state.Available ? 1.0 : 0.5;
            content.IsEnabled = state.Available;
            row.BorderBrush = (Brush)FindResource(IstAn(state) ? "AccentDim" : "BgRow");
        });
        row.Opacity = option.Available ? 1.0 : 0.45;
        content.IsEnabled = option.Available;

        FadeIn(row, index);
        return row;
    }

    /// <summary>Der Umschalter "nur ich / auch die anderen" für Cheats, die beides können.</summary>
    private UIElement BuildShareToggle(OptionInfo option)
    {
        var toggle = new CheckBox
        {
            Content = "auch für Mitspieler",
            IsChecked = option.ShareWithOthers,
            Style = (Style)FindResource("SmallSwitch"),
            Margin = new Thickness(0, 8, 0, 0)
        };

        async Task Send(bool value)
        {
            if (_suppressEvents) return;
            toggle.Foreground = (Brush)FindResource(value ? "Warn" : "Muted");
            ReportIfFailed(await _ipc.SetShareAsync(option.Id, value));
        }

        toggle.Checked += async (_, _) => await Send(true);
        toggle.Unchecked += async (_, _) => await Send(false);

        Register(option, state =>
        {
            toggle.IsChecked = state.ShareWithOthers;
            toggle.Foreground = (Brush)FindResource(state.ShareWithOthers ? "Warn" : "Muted");
        });

        return toggle;
    }

    private UIElement BuildControl(OptionInfo option) => option.Kind switch
    {
        "Button" => BuildButton(option),
        "Slider" => BuildSlider(option),
        "Number" => BuildNumber(option),
        "Text" => BuildText(option),
        "Choice" => BuildChoice(option),
        "Info" => BuildInfo(option),
        _ => BuildToggle(option)
    };

    /// <summary>Reine Anzeige: Bezeichnung links, laufender Wert rechts.</summary>
    private Grid LabeledControl(string text, FrameworkElement control)
    {
        var panel = new Grid();
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0)
        };
        Grid.SetColumn(control, 1);
        panel.Children.Add(label); panel.Children.Add(control);
        System.Windows.Automation.AutomationProperties.SetName(control, text);
        return panel;
    }

    private UIElement BuildInfo(OptionInfo option)
    {
        var value = new TextBlock
        {
            Text = option.TextValue, FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Accent"), FontFamily = (FontFamily)FindResource("Mono"),
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right
        };
        Register(option, state => value.Text = state.TextValue);
        return LabeledControl(option.Label, value);
    }

    private UIElement BuildToggle(OptionInfo option)
    {
        var toggle = new CheckBox
        {
            Content = option.Label,
            IsChecked = option.BoolValue,
            Style = (Style)FindResource("SwitchToggle")
        };

        toggle.Checked += async (_, _) => await SendBool(option, true);
        toggle.Unchecked += async (_, _) => await SendBool(option, false);

        Register(option, state => toggle.IsChecked = state.BoolValue);
        return toggle;
    }

    private UIElement BuildButton(OptionInfo option)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            Style = (Style)FindResource("FlatButton"),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 140,
            MaxWidth = 320
        };
        System.Windows.Automation.AutomationProperties.SetName(button, option.Label);

        button.Click += async (_, _) =>
        {
            if (!_runningShortcuts.Add(option.Id)) return;
            button.IsEnabled = false;
            try { ReportIfFailed(await _ipc.InvokeAsync(option.Id)); }
            finally { _runningShortcuts.Remove(option.Id); button.IsEnabled = option.Available; }
        };

        Register(option, state => button.IsEnabled = state.Available && !_runningShortcuts.Contains(option.Id));
        return button;
    }

    private UIElement BuildSlider(OptionInfo option)
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 0), FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center };

        var slider = new Slider
        {
            Minimum = option.Min,
            Maximum = option.Max,
            Value = option.NumberValue,
            // Nicht einrasten: der Regler soll jeden Zwischenwert treffen können.
            // Die Schrittweite dient nur noch den Pfeiltasten.
            IsSnapToTickEnabled = false,
            SmallChange = option.Step <= 0 ? 0.01 : option.Step,
            LargeChange = (option.Step <= 0 ? 0.01 : option.Step) * 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 2)
        };
        System.Windows.Automation.AutomationProperties.SetName(slider, option.Label);

        var value = new TextBlock
        {
            Text = option.NumberValue.ToString("0.##", CultureInfo.InvariantCulture),
            FontFamily = (FontFamily)FindResource("Mono"),
            Foreground = (Brush)FindResource("Accent"),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        slider.ValueChanged += async (_, e) =>
        {
            // Auf zwei Nachkommastellen runden, sonst steht da 1,6700000000000002
            double gerundet = Math.Round(e.NewValue, 2);

            value.Text = gerundet.ToString("0.##", CultureInfo.InvariantCulture);
            if (_suppressEvents) return;
            Merke(option);
            await SendLatestNumber(option, gerundet);
        };

        Grid.SetColumn(label, 0);
        Grid.SetRow(slider, 1);
        Grid.SetColumnSpan(slider, 2);
        Grid.SetColumn(value, 1);
        panel.Children.Add(label);
        panel.Children.Add(slider);
        panel.Children.Add(value);

        Register(option, state =>
        {
            slider.IsEnabled = state.Available;
            if (!slider.IsMouseCaptureWithin && !_numberSends.ContainsKey(option.Id) && Math.Abs(slider.Value - state.NumberValue) > 0.001)
                slider.Value = state.NumberValue;
        });

        return panel;
    }

    private UIElement BuildNumber(OptionInfo option)
    {
        var box = new TextBox
        {
            Text = option.NumberValue.ToString("0.##", CultureInfo.InvariantCulture),
            Width = 110, HorizontalAlignment = HorizontalAlignment.Right,
            FontFamily = (FontFamily)FindResource("Mono")
        };

        async Task Commit()
        {
            if (!NumberInput.TryParse(box.Text, out double parsed))
            { SetStatus("Bitte eine gültige Zahl eingeben (z. B. 1,5 oder 1.5)", true, transient: true); return; }
            if (parsed < option.Min || parsed > option.Max)
            { SetStatus($"Erlaubter Bereich: {option.Min} bis {option.Max}", true, transient: true); return; }

            Merke(option);
            ReportIfFailed(await _ipc.SetNumberAsync(option.Id, parsed));
        }

        box.LostFocus += async (_, _) => { if (!_suppressEvents) await Commit(); };
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !_suppressEvents) await Commit(); };

        Register(option, state =>
        {
            box.IsEnabled = state.Available;
            if (!box.IsFocused)
                box.Text = state.NumberValue.ToString("0.##", CultureInfo.InvariantCulture);
        });

        return LabeledControl(option.Label, box);
    }

    private UIElement BuildText(OptionInfo option)
    {
        var panel = new StackPanel();
        var input = new DockPanel();

        var label = new TextBlock
        {
            Text = option.Label,
            TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var run = new Button
        {
            Content = "Ausführen",
            Style = (Style)FindResource("FlatButton"),
            Margin = new Thickness(8, 0, 0, 0),
            MinWidth = 110
        };

        var box = new TextBox { Text = option.TextValue };

        async Task Send()
        {
            if (!_runningShortcuts.Add(option.Id)) return;
            run.IsEnabled = false;
            try
            {
            int generation = _ipc.Generation;
            if (!ReportIfFailed(await _ipc.SetTextAsync(option.Id, box.Text ?? ""))) return;
            if (generation != _ipc.Generation) return;
            ReportIfFailed(await _ipc.InvokeAsync(option.Id));
            }
            finally { _runningShortcuts.Remove(option.Id); run.IsEnabled = option.Available; }
        }

        run.Click += async (_, _) => await Send();
        box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await Send(); };

        DockPanel.SetDock(run, Dock.Right);
        input.Children.Add(run); input.Children.Add(box);
        panel.Children.Add(label); panel.Children.Add(input);
        System.Windows.Automation.AutomationProperties.SetName(box, option.Label);

        Register(option, state =>
        {
            box.IsEnabled = state.Available;
            run.IsEnabled = state.Available && !_runningShortcuts.Contains(option.Id);
        });

        return panel;
    }

    private UIElement BuildChoice(OptionInfo option)
    {
        if (_connectedGameId == "MortalShell2" && option.Id == "item.was")
        {
            var picker = new ItemPicker(ItemCatalog.FromChoices(option.Choices), option.ChoiceIndex);
            picker.SelectionChanged += async originalIndex =>
            {
                if (_suppressEvents) return;
                Merke(option);
                ReportIfFailed(await _ipc.SetChoiceAsync(option.Id, originalIndex));
            };
            Register(option, state =>
            {
                picker.IsEnabled = state.Available;
                picker.SetSelectedIndex(state.ChoiceIndex);
            });
            return picker;
        }

        var combo = new ComboBox
        {
            ItemsSource = option.Choices,
            SelectedIndex = option.Choices.Length == 0
                ? -1
                : Math.Clamp(option.ChoiceIndex, 0, option.Choices.Length - 1),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        combo.SelectionChanged += async (_, _) =>
        {
            if (_suppressEvents || combo.SelectedIndex < 0) return;
            Merke(option);
            ReportIfFailed(await _ipc.SetChoiceAsync(option.Id, combo.SelectedIndex));
        };

        Register(option, state =>
        {
            combo.IsEnabled = state.Available;

            // Die Auswahlliste kann sich nachträglich füllen (z.B. Spawn-Namen).
            if (state.Choices.Length > 0 && combo.Items.Count != state.Choices.Length)
                combo.ItemsSource = state.Choices;

            if (combo.Items.Count > 0)
                combo.SelectedIndex = Math.Clamp(state.ChoiceIndex, 0, combo.Items.Count - 1);
        });

        return LabeledControl(option.Label, combo);
    }

    private void Register(OptionInfo option, Action<OptionInfo> refresh)
    {
        var existing = _refreshers.TryGetValue(option.Id, out var inner) ? inner : null;
        _refreshers[option.Id] = state =>
        {
            existing?.Invoke(state);
            refresh(state);
        };
    }

    private async Task SendBool(OptionInfo option, bool value)
    {
        if (_suppressEvents) return;

        Merke(option);
        ReportIfFailed(await _ipc.SetBoolAsync(option.Id, value));
    }

    /// <summary>Zeigt Fehler des Spiels in der Statuszeile. Gibt true zurück, wenn alles glatt lief.</summary>
    private bool ReportIfFailed(JsonObject? response)
    {
        if (response == null)
        {
            SetStatus(string.IsNullOrEmpty(_ipc.LastError) ? "Verbindung verloren" : _ipc.LastError, warn: true, transient: true);
            return false;
        }

        if (response["ok"]?.GetValue<bool>() == true)
        {
            // Der Cheat kann trotz "ok" etwas mitzuteilen haben - etwa dass nichts
            // in der Hand liegt oder kein Einsatz auf dem Tisch.
            string? message = response["message"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(message)) SetStatus(message, warn: false, transient: true);
            ApplyValues(response["values"] as JsonObject);
            RenderCategoryList();

            return true;
        }

        SetStatus(response["error"]?.GetValue<string>() ?? "Fehler", warn: true, transient: true);
        return false;
    }
}
