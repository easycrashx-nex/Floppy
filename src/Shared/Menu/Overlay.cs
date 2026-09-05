using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Floppy.Core.Api;
using UnityEngine;

namespace Floppy.Core.Menu
{
    /// <summary>Das Menü im Spiel. Zeichnet genau die Cheats, die das Modul angemeldet hat -
    /// neue Cheats erscheinen hier ohne Änderung an dieser Datei.
    ///
    /// Kommt ohne Unitys GUILayout aus: Unity schneidet beim Bauen ungenutzten Code heraus,
    /// und Spiele, die selbst kein IMGUI verwenden, haben das Layout-System gar nicht mehr
    /// drin. Alle Rechtecke werden deshalb selbst ausgerechnet.</summary>
    public class Overlay
    {
        private const int WindowId = 0x0B0B;
        private const float Width = 900f;
        private const float Height = 600f;
        private const float Rand = 14f;

        private const float ZeilenHoehe = 26f;
        private const float ListenHoeheMax = 240f;

        private bool _visible;

        /// <summary>Beim Umschalten wird das Modul benachrichtigt, damit es die
        /// Spielsteuerung sperren kann.</summary>
        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (_visible == value) return;
                _visible = value;

                if (Registry.Module != null)
                {
                    try { Registry.Module.SetMenuOpen(value); }
                    catch (Exception ex) { Log.Error("SetMenuOpen: " + ex); }
                }

                if (!value) _openList = "";
            }
        }

        private Rect _window = new Rect(120f, 90f, Width, Height);
        private int _selectedCategory;
        private float _scroll;
        private string _suche = "";

        private string _openList = "";
        private Rect _listAnchor;
        private float _listScroll;

        private CursorLockMode _previousLock;
        private bool _previousCursorVisible;
        private bool _cursorTaken;

        private Theme _theme;

        // Einmal je Frame festgehalten - siehe Frameanfang()
        private bool _snapBereit;
        private string _snapStatus = "";
        private bool _snapApp;
        private string _snapHinweis = "";
        private readonly Dictionary<string, bool> _snapVerfuegbar = new Dictionary<string, bool>();

        private readonly Dictionary<string, float> _switchProgress = new Dictionary<string, float>();

        public void Draw()
        {
            if (!Visible)
            {
                ReleaseCursor();
                return;
            }

            TakeCursor();

            if (_theme == null) _theme = new Theme();
            _theme.Apply();

            _window.width = Mathf.Min(Width, Mathf.Max(320f, Screen.width - 20f));
            _window.height = Mathf.Min(Height, Mathf.Max(240f, Screen.height - 20f));
            _window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, Screen.width - _window.width));
            _window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, Screen.height - _window.height));

            _window = Compat.Window(WindowId, _window, DrawWindow, _theme.Window);

            if (_window.Contains(Event.current.mousePosition))
                GUI.FocusWindow(WindowId);
        }

        /// <summary>Werte, die sich mitten im Frame ändern könnten, einmal festhalten.
        /// Sonst zeichnet der zweite Durchlauf etwas anderes als der erste.</summary>
        private void Frameanfang()
        {
            if (Event.current.type != EventType.Layout) return;

            _snapBereit = Registry.Ready(out _snapStatus);
            _snapApp = IpcServer.ConnectedClients > 0;
            _snapHinweis = Time.realtimeSinceStartup < _noticeUntil ? _notice : "";

            _snapVerfuegbar.Clear();
            foreach (var option in Registry.AllOptions)
                _snapVerfuegbar[option.Id] = SafeAvailable(option);
        }

        private bool Verfuegbar(CheatOption option)
        {
            bool wert;
            return !_snapVerfuegbar.TryGetValue(option.Id, out wert) || wert;
        }

        private void DrawWindow(int id)
        {
            Frameanfang();

            // Zuerst: hat eine offene Liste Anspruch auf die Maus? Dann verbraucht sie
            // den Klick hier, bevor irgendein Bedienelement ihn zu sehen bekommt.
            HandleListInput();

            var flaeche = new Rect(Rand, Rand, _window.width - Rand * 2f, _window.height - Rand * 2f);
            var l = new Layouter(flaeche, 8f);

            DrawHeader(l.Zeile(30f));
            var suche = l.Zeile(28f);
            GUI.Label(new Rect(suche.x, suche.y, 62f, suche.height), "Suche", _theme.Label);
            string eingabe = Eingabefeld("floppy.suche", new Rect(suche.x + 64f, suche.y, suche.width - 100f, suche.height), _suche);
            if (GUI.Button(new Rect(suche.xMax - 30f, suche.y, 30f, suche.height), "X", _theme.Action)) eingabe = "";
            if (eingabe != _suche) { _suche = eingabe; _scroll = 0; _openList = ""; }
            DrawTabs(ref l);

            float restY = l.Y;
            DrawContent(new Rect(flaeche.x, restY, flaeche.width, flaeche.yMax - restY));

            // Zuletzt gezeichnet heißt in IMGUI: liegt oben.
            DrawOpenList();

            GUI.DragWindow(new Rect(0f, 0f, _window.width, 40f));
        }

        // ---------------------------------------------------------------- Kopfzeile

        private void DrawHeader(Rect r)
        {
            GUI.Label(new Rect(r.x, r.y, 110f, r.height), "Floppy", _theme.Title);
            GUI.Label(new Rect(r.x + 112f, r.y + 6f, 260f, r.height), Registry.GameName, _theme.Subtitle);

            // Von rechts nach links auffüllen
            var schliessen = new Rect(r.xMax - 30f, r.y, 30f, 26f);
            if (GUI.Button(schliessen, "X", _theme.CloseButton)) Visible = false;

            float rechts = schliessen.x - 10f;

            if (!string.IsNullOrEmpty(_snapHinweis))
            {
                float b = Mathf.Min(300f, Text.Breite(_theme.StatusWarn, _snapHinweis) + 8f);
                GUI.Label(new Rect(rechts - b, r.y + 5f, b, 22f), _snapHinweis, _theme.StatusWarn);
                rechts -= b + 12f;
            }

            if (_snapApp)
            {
                GUI.Label(new Rect(rechts - 100f, r.y + 5f, 100f, 22f), "App verbunden", _theme.StatusOk);
                rechts -= 112f;
            }

            float sb = Mathf.Min(320f, Text.Breite(_theme.StatusOk, _snapStatus) + 8f);
            GUI.Label(new Rect(rechts - sb, r.y + 5f, sb, 22f), _snapStatus,
                      _snapBereit ? _theme.StatusOk : _theme.StatusWarn);
        }

        // ---------------------------------------------------------------- Kategorien

        private void DrawTabs(ref Layouter l)
        {
            float x = 0f;
            const float zeilenHoehe = 30f;
            var zeile = l.Eng(zeilenHoehe);

            for (int i = 0; i < Registry.Categories.Count; i++)
            {
                var kategorie = Registry.Categories[i];
                float breite = Text.Breite(_theme.Action, kategorie.Name) + 16f;

                if (x + breite > l.Breite)
                {
                    l.Luecke(6f);
                    zeile = l.Eng(zeilenHoehe);
                    x = 0f;
                }

                var r = new Rect(zeile.x + x, zeile.y, breite, zeile.height);
                bool aktiv = i == _selectedCategory;

                if (GUI.Button(r, kategorie.Name, aktiv ? _theme.AccentAction : _theme.Action))
                {
                    _selectedCategory = i;
                    _scroll = 0f;
                    _openList = "";
                }

                x += breite + 6f;
            }

            l.Luecke(10f);
        }

        // ---------------------------------------------------------------- Cheatliste

        private void DrawContent(Rect flaeche)
        {
            if (Event.current.type == EventType.Repaint)
                _theme.Panel.Draw(flaeche, false, false, false, false);

            var innen = new Rect(flaeche.x + 10f, flaeche.y + 10f,
                                 flaeche.width - 20f, flaeche.height - 20f);

            if (Registry.Categories.Count == 0)
            {
                GUI.Label(new Rect(innen.x, innen.y + innen.height / 2f - 12f, innen.width, 24f),
                          "Für dieses Spiel ist kein Modul geladen.", _theme.EmptyTitle);
                return;
            }

            _selectedCategory = Mathf.Clamp(_selectedCategory, 0, Registry.Categories.Count - 1);
            var optionen = _suche.Length == 0 ? Registry.Categories[_selectedCategory].Options
                : Registry.AllOptions.Where(o => (o.Label ?? "").IndexOf(_suche, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (o.Description ?? "").IndexOf(_suche, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            if (optionen.Count == 0)
                GUI.Label(new Rect(innen.x, innen.y, innen.width, 28f), "Keine passenden Funktionen gefunden", _theme.Label);

            // Gesamthöhe vorab bestimmen, damit wir rollen können
            float breiteInhalt = innen.width - 12f;   // Platz für den Rollbalken
            float gesamt = 0f;
            for (int i = 0; i < optionen.Count; i++)
                gesamt += ZeilenhoeheVon(optionen[i], breiteInhalt) + 6f;

            float maxScroll = Mathf.Max(0f, gesamt - innen.height);
            _scroll = Mathf.Clamp(_scroll, 0f, maxScroll);

            if (Event.current.type == EventType.ScrollWheel && innen.Contains(Event.current.mousePosition)
                && string.IsNullOrEmpty(_openList))
            {
                _scroll = Mathf.Clamp(_scroll + Event.current.delta.y * 20f, 0f, maxScroll);
                Event.current.Use();
            }

            GUI.BeginGroup(innen);

            float y = -_scroll;
            for (int i = 0; i < optionen.Count; i++)
            {
                float h = ZeilenhoeheVon(optionen[i], breiteInhalt);

                // Nur zeichnen, was sichtbar ist
                if (y + h >= 0f && y <= innen.height)
                    DrawOption(optionen[i], new Rect(0f, y, breiteInhalt, h), innen);

                y += h + 6f;
            }

            GUI.EndGroup();

            if (maxScroll > 0f) DrawScrollbar(innen, gesamt, maxScroll);
        }

        private void DrawScrollbar(Rect innen, float gesamt, float maxScroll)
        {
            if (Event.current.type != EventType.Repaint) return;

            float anteil = innen.height / gesamt;
            float griff = Mathf.Max(24f, innen.height * anteil);
            float y = innen.y + (_scroll / maxScroll) * (innen.height - griff);

            GUI.color = Theme.AccentSoft;
            _theme.SliderTrack.Draw(new Rect(innen.xMax - 6f, y, 5f, griff), false, false, false, false);
            GUI.color = Color.white;
        }

        /// <summary>Wie hoch eine Cheat-Zeile wird - Bedienelement, Umschalter,
        /// Warnhinweis und Beschreibung zusammengerechnet.</summary>
        private float ZeilenhoeheVon(CheatOption option, float breite)
        {
            float innen = breite - 24f;
            float h = 12f + 28f + 10f;   // Polster oben, Bedienelement, Polster unten

            if (option.Scope == CheatScope.Selectable) h += 24f;
            if (option.Scope == CheatScope.Everyone) h += 18f;

            if (!string.IsNullOrEmpty(option.Description))
                h += Text.Hoehe(_theme.Hint, option.Description, innen) + 4f;

            return h;
        }

        private void DrawOption(CheatOption option, Rect r, Rect gruppe)
        {
            bool available = Verfuegbar(option);
            GUI.enabled = available;

            if (Event.current.type == EventType.Repaint)
                _theme.Row.Draw(r, false, false, false, false);

            var innen = new Rect(r.x + 12f, r.y + 12f, r.width - 24f, r.height - 22f);
            var l = new Layouter(innen, 0f);

            DrawControl(option, l.Eng(28f), gruppe);

            if (option.Scope == CheatScope.Selectable)
                DrawShareToggle(option, l.Eng(24f));

            if (option.Scope == CheatScope.Everyone)
                GUI.Label(l.Eng(18f), "betrifft alle in der Runde", _theme.ScopeWarn);

            if (!string.IsNullOrEmpty(option.Description))
            {
                float h = Text.Hoehe(_theme.Hint, option.Description, innen.width);
                GUI.Label(l.Eng(h + 4f), option.Description, _theme.Hint);
            }

            GUI.enabled = true;
        }

        private void DrawControl(CheatOption option, Rect r, Rect gruppe)
        {
            switch (option.Kind)
            {
                case OptionKind.Toggle: DrawToggle(option, r); break;
                case OptionKind.Button: DrawButton(option, r); break;
                case OptionKind.Slider: DrawSlider(option, r); break;
                case OptionKind.Number: DrawNumber(option, r); break;
                case OptionKind.Text: DrawText(option, r); break;
                case OptionKind.Choice: DrawChoice(option, r, gruppe); break;
                case OptionKind.Info: DrawInfo(option, r); break;
            }
        }

        // ---------------------------------------------------------------- Bedienelemente

        /// <summary>Ein Schiebeschalter wie in der Desktop-App.</summary>
        private bool Switch(string key, bool value, Rect r, string label, GUIStyle labelStil, float scale = 1f)
        {
            float breite = 40f * scale;
            float hoehe = 22f * scale;
            var bahn = new Rect(r.x, r.y + (r.height - hoehe) / 2f, breite, hoehe);

            if (Event.current.type == EventType.MouseDown && GUI.enabled
                && bahn.Contains(Event.current.mousePosition))
            {
                value = !value;
                Event.current.Use();
            }

            float ziel = value ? 1f : 0f;
            float fortschritt;
            if (!_switchProgress.TryGetValue(key, out fortschritt)) fortschritt = ziel;

            if (Event.current.type == EventType.Repaint)
            {
                fortschritt = Mathf.MoveTowards(fortschritt, ziel, Time.unscaledDeltaTime * 7f);
                _switchProgress[key] = fortschritt;

                // GUI.color setzen und schlicht zeichnen - die DrawTexture-Überladung mit
                // eigenem Farbparameter übergeht GUI.color.
                GUI.color = Color.Lerp(Theme.SwitchOff, Theme.Accent, fortschritt);
                _theme.SwitchTrack.Draw(bahn, false, false, false, false);

                float rand = 3f * scale;
                float durchmesser = hoehe - rand * 2f;
                float x = bahn.x + rand + fortschritt * (breite - durchmesser - rand * 2f);

                GUI.color = Color.Lerp(Theme.KnobOff, Color.white, fortschritt);
                GUI.DrawTexture(new Rect(x, bahn.y + rand, durchmesser, durchmesser), _theme.Circle);
                GUI.color = Color.white;
            }

            GUI.Label(new Rect(bahn.xMax + 10f, r.y, r.width - breite - 10f, r.height), label, labelStil);
            return value;
        }

        /// <summary>Welches Eingabefeld gerade den Fokus hat, und was darin steht.</summary>
        private string _focusField = "";
        private readonly Dictionary<string, string> _eingabe = new Dictionary<string, string>();

        /// <summary>Ein eigenes Eingabefeld.
        ///
        /// Unitys GUI.TextField ist - wie das Layout-System - in manchen Spielen
        /// wegoptimiert. Also zeichnen wir den Rahmen selbst und sammeln die Tastendrücke
        /// aus dem Ereignisstrom ein. Reicht für Zahlen und kurze Befehle.</summary>
        private string Eingabefeld(string key, Rect r, string wert)
        {
            var e = Event.current;
            bool fokus = _focusField == key;

            if (e.type == EventType.MouseDown && GUI.enabled)
            {
                if (r.Contains(e.mousePosition)) { _focusField = key; e.Use(); }
                else if (fokus) _focusField = "";
            }

            if (fokus && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Backspace)
                {
                    if (wert.Length > 0) wert = wert.Substring(0, wert.Length - 1);
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter ||
                         e.keyCode == KeyCode.Escape)
                {
                    _focusField = "";
                    e.Use();
                }
                else if (e.character != ' ' && !char.IsControl(e.character))
                {
                    wert += e.character;
                    e.Use();
                }
            }

            if (e.type == EventType.Repaint)
            {
                _theme.TextField.Draw(r, false, false, fokus, false);

                // Blinkender Strich, damit man sieht wo man tippt
                bool strich = fokus && ((int)(Time.unscaledTime * 2f) % 2 == 0);
                var inhalt = new GUIContent(wert + (strich ? "|" : ""));
                _theme.Label.Draw(new Rect(r.x + 10f, r.y, r.width - 20f, r.height),
                                  inhalt, false, false, false, false);
            }

            return wert;
        }

        /// <summary>Der Text, der gerade in einem Feld steht - während des Tippens darf
        /// er auch mal unfertig sein, deshalb ein eigener Zwischenspeicher.</summary>
        private string Puffer(string key, string vorgabe)
        {
            string wert;
            if (!_eingabe.TryGetValue(key, out wert) && _focusField != key)
            {
                wert = vorgabe;
                _eingabe[key] = wert;
            }
            else if (_focusField != key)
            {
                wert = vorgabe;
                _eingabe[key] = wert;
            }
            return wert;
        }

        /// <summary>Reine Anzeige: Bezeichnung links, aktueller Wert rechts.</summary>
        private void DrawInfo(CheatOption option, Rect r)
        {
            Rect links, rechts;
            Layouter.Teile(r, 200f, 8f, out links, out rechts);

            GUI.Label(links, option.Label, _theme.Label);
            GUI.Label(rechts, option.TextValue ?? "", _theme.InfoWert);
        }

        private void DrawToggle(CheatOption option, Rect r)
        {
            bool next = Switch(option.Id, option.BoolValue, r, option.Label, _theme.Label);
            if (next != option.BoolValue)
            {
                option.BoolValue = next;
                Notify(option);
            }
        }

        private void DrawShareToggle(CheatOption option, Rect r)
        {
            bool share = Switch(option.Id + ".share", option.ShareWithOthers, r,
                                "auch für Mitspieler", _theme.ShareLabel, 0.8f);

            if (share != option.ShareWithOthers)
            {
                option.ShareWithOthers = share;
                Notify(option);
            }
        }

        private void DrawButton(CheatOption option, Rect r)
        {
            var b = new Rect(r.x, r.y, Mathf.Min(260f, r.width), r.height);
            if (GUI.Button(b, option.Label, _theme.Action)) Invoke(option);
        }

        private void DrawSlider(CheatOption option, Rect r)
        {
            Rect links, rest;
            Layouter.Teile(r, 190f, 8f, out links, out rest);

            Rect bahn, wert;
            Layouter.TeileRechts(rest, 52f, 8f, out bahn, out wert);

            GUI.Label(links, option.Label, _theme.Label);

            float next = GUI.HorizontalSlider(bahn, option.NumberValue, option.Min, option.Max,
                                              GUIStyle.none, GUIStyle.none);

            // Nicht einrasten - nur auf zwei Nachkommastellen runden, damit
            // Zwischenwerte wie 1,67 erreichbar bleiben.
            next = Mathf.Round(next * 100f) / 100f;

            if (Event.current.type == EventType.Repaint)
            {
                float anteil = Mathf.InverseLerp(option.Min, option.Max, next);
                var spur = new Rect(bahn.x, bahn.y + bahn.height / 2f - 2.5f, bahn.width, 5f);

                GUI.color = Theme.RowHover;
                _theme.SliderTrack.Draw(spur, false, false, false, false);

                GUI.color = Theme.Accent;
                _theme.SliderTrack.Draw(new Rect(spur.x, spur.y, spur.width * anteil, spur.height),
                                        false, false, false, false);

                float knopf = 14f;
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(bahn.x + anteil * (bahn.width - knopf),
                                         bahn.y + bahn.height / 2f - knopf / 2f, knopf, knopf),
                                _theme.Circle);
            }

            GUI.Label(wert, next.ToString("0.##"), _theme.Value);

            if (!Mathf.Approximately(next, option.NumberValue))
            {
                option.NumberValue = next;
                Notify(option);
            }
        }

        private void DrawNumber(CheatOption option, Rect r)
        {
            Rect links, rest;
            Layouter.Teile(r, 170f, 8f, out links, out rest);

            GUI.Label(links, option.Label, _theme.Label);

            var feld = new Rect(rest.x, rest.y, Mathf.Min(140f, rest.width), rest.height);

            string vorher = Puffer(option.Id, option.NumberValue.ToString("0.##"));
            string roh = Eingabefeld(option.Id, feld, vorher);
            _eingabe[option.Id] = roh;

            float parsed;
            if (float.TryParse(roh.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) &&
                !float.IsNaN(parsed) && !float.IsInfinity(parsed) && !Mathf.Approximately(parsed, option.NumberValue))
            {
                option.NumberValue = Mathf.Clamp(parsed, option.Min, option.Max);
                Notify(option);
            }
        }

        private void DrawText(CheatOption option, Rect r)
        {
            Rect links, rest;
            Layouter.Teile(r, 120f, 8f, out links, out rest);

            Rect feld, knopf;
            Layouter.TeileRechts(rest, 110f, 8f, out feld, out knopf);

            GUI.Label(links, option.Label, _theme.Label);
            option.TextValue = Eingabefeld(option.Id, feld, option.TextValue ?? "");

            bool enter = Event.current.type == EventType.KeyDown &&
                         (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);

            if (GUI.Button(knopf, "Ausführen", _theme.Action) || enter) Invoke(option);
        }

        private void DrawChoice(CheatOption option, Rect r, Rect gruppe)
        {
            Rect links, rest;
            Layouter.Teile(r, 180f, 8f, out links, out rest);

            GUI.Label(links, option.Label, _theme.Label);

            bool leer = option.Choices == null || option.Choices.Length == 0;
            int index = leer ? 0 : Mathf.Clamp(option.ChoiceIndex, 0, option.Choices.Length - 1);
            string anzeige = leer ? "(leer)" : option.Choices[index];

            if (GUI.Button(rest, anzeige, _theme.Dropdown) && !leer)
            {
                bool warOffen = _openList == option.Id;
                _openList = warOffen ? "" : option.Id;

                // Die Liste liegt im Fenster, das Feld in einer verschobenen Gruppe -
                // deshalb den Ursprung der Gruppe dazurechnen.
                _listAnchor = new Rect(rest.x + gruppe.x, rest.y + gruppe.y, rest.width, rest.height);

                if (!warOffen) _listScroll = Mathf.Max(0f, (index - 3) * ZeilenHoehe);
            }

            if (Event.current.type == EventType.Repaint && !leer)
            {
                var pfeil = new Rect(rest.xMax - 24f, rest.y, 16f, rest.height);
                GUI.Label(pfeil, _openList == option.Id ? "▲" : "▼", _theme.Chevron);
            }
        }

        // ---------------------------------------------------------------- Aufklappliste

        private Rect ListenRahmen(CheatOption option)
        {
            float hoehe = Mathf.Min(option.Choices.Length * ZeilenHoehe + 8f, ListenHoeheMax);
            var rahmen = new Rect(_listAnchor.x, _listAnchor.yMax + 4f, _listAnchor.width, hoehe);

            if (rahmen.yMax > _window.height - 10f)
                rahmen.y = _listAnchor.y - hoehe - 4f;

            return rahmen;
        }

        /// <summary>Wertet die Maus für die offene Liste aus - und zwar VOR allem anderen,
        /// damit der Klick nicht durch die Liste hindurch bei den Zeilen darunter landet.</summary>
        private void HandleListInput()
        {
            if (string.IsNullOrEmpty(_openList)) return;

            var option = Registry.Find(_openList);
            if (option == null || option.Choices == null || option.Choices.Length == 0)
            {
                _openList = "";
                return;
            }

            var e = Event.current;
            Rect rahmen = ListenRahmen(option);
            bool drin = rahmen.Contains(e.mousePosition);

            float sichtbar = rahmen.height - 8f;
            float gesamt = option.Choices.Length * ZeilenHoehe;
            float maxScroll = Mathf.Max(0f, gesamt - sichtbar);

            if (e.type == EventType.ScrollWheel && drin)
            {
                _listScroll = Mathf.Clamp(_listScroll + e.delta.y * 20f, 0f, maxScroll);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDown)
            {
                if (drin)
                {
                    float y = e.mousePosition.y - (rahmen.y + 4f) + _listScroll;
                    int index = Mathf.FloorToInt(y / ZeilenHoehe);

                    if (index >= 0 && index < option.Choices.Length && index != option.ChoiceIndex)
                    {
                        option.ChoiceIndex = index;
                        Notify(option);
                    }
                }

                _openList = "";
                e.Use();
            }
        }

        private void DrawOpenList()
        {
            if (string.IsNullOrEmpty(_openList) || Event.current.type != EventType.Repaint) return;

            var option = Registry.Find(_openList);
            if (option == null || option.Choices == null || option.Choices.Length == 0) return;

            Rect rahmen = ListenRahmen(option);
            _theme.ListBox.Draw(rahmen, false, false, false, false);

            var innen = new Rect(rahmen.x + 4f, rahmen.y + 4f, rahmen.width - 8f, rahmen.height - 8f);
            GUI.BeginGroup(innen);

            int erste = Mathf.Max(0, Mathf.FloorToInt(_listScroll / ZeilenHoehe));
            int letzte = Mathf.Min(option.Choices.Length - 1,
                                   erste + Mathf.CeilToInt(innen.height / ZeilenHoehe));

            for (int i = erste; i <= letzte; i++)
            {
                var zeile = new Rect(0f, i * ZeilenHoehe - _listScroll, innen.width, ZeilenHoehe - 2f);
                var stil = i == option.ChoiceIndex ? _theme.ListItemActive : _theme.ListItem;
                stil.Draw(zeile, new GUIContent(option.Choices[i]), false, false, false, false);
            }

            GUI.EndGroup();

            float gesamt = option.Choices.Length * ZeilenHoehe;
            if (gesamt > innen.height)
            {
                float anteil = innen.height / gesamt;
                float griff = Mathf.Max(20f, innen.height * anteil);
                float maxScroll = gesamt - innen.height;
                float y = innen.y + (_listScroll / maxScroll) * (innen.height - griff);

                GUI.color = Theme.AccentSoft;
                _theme.SliderTrack.Draw(new Rect(innen.xMax - 5f, y, 4f, griff), false, false, false, false);
                GUI.color = Color.white;
            }
        }

        // ---------------------------------------------------------------- Hilfen

        private static bool SafeAvailable(CheatOption option)
        {
            try { return option.Available; }
            catch { return false; }
        }

        private static void Notify(CheatOption option)
        {
            try { option.NotifyChanged(); }
            catch (Exception ex) { Log.Error("Cheat " + option.Id + " ist ausgestiegen: " + ex); }
        }

        private static string _notice = "";
        private static float _noticeUntil;

        private static void Invoke(CheatOption option)
        {
            try
            {
                option.Fire();

                string message = option.TakeMessage();
                if (!string.IsNullOrEmpty(message))
                {
                    _notice = message;
                    _noticeUntil = Time.realtimeSinceStartup + 4f;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Cheat " + option.Id + " ist ausgestiegen: " + ex);
                _notice = "Fehler: " + ex.Message;
                _noticeUntil = Time.realtimeSinceStartup + 6f;
            }
        }

        // ---------------------------------------------------------------- Mauszeiger

        private void TakeCursor()
        {
            if (_cursorTaken) return;
            _previousLock = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
            _cursorTaken = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ReleaseCursor()
        {
            if (!_cursorTaken) return;
            _cursorTaken = false;
            Cursor.lockState = _previousLock;
            Cursor.visible = _previousCursorVisible;
        }
    }
}
