using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Floppy.Core.Api;

namespace Floppy.Unrailed2
{
    /// <summary>Explizite Aktionen auf vorhandenen Zügen; keine Abhängigkeit vom CheatSingleton.</summary>
    internal static class ZugAktionen
    {
        private const string ZugTyp = "919923075";
        private const string WagenTyp = "1418987843";
        private const string ZustandFeld = "-762103952";
        private const string HitzeFeld = "806557303";
        private const string WagenZugFeld = "-2134332611";
        private static string _welt;
        private static long _zug = -1;
        private static string _zustand;
        private static string _meldung = "Keine laufende Partie";
        private static (string Welt, long Zug)? _eigenerHalt;

        public static void Vergiss()
        {
            _welt = null;
            _zug = -1;
            _zustand = null;
            _eigenerHalt = null;
            _meldung = "Keine laufende Partie";
        }

        public static void Aktualisiere()
        {
            if (!Welt.ImSpiel || !Debugger.Erreichbar || string.IsNullOrEmpty(Welt.Kennung))
            {
                Vergiss();
                return;
            }

            if (_welt != Welt.Kennung) _eigenerHalt = null;
            _welt = Welt.Kennung;
            var zuege = Welt.Felder(ZugTyp).GroupBy(f => f.Entitaet).Where(g => g.Key >= 0).ToArray();
            _zug = -1;
            _zustand = null;
            if (!Debugger.Erreichbar || zuege.Length != 1)
            {
                _eigenerHalt = null;
                _meldung = !Debugger.Erreichbar ? "Zugdaten nicht erreichbar"
                    : zuege.Length == 0 ? "Kein Zug in der laufenden Welt"
                    : "Mehrere Züge vorhanden – keine eindeutige Zugauswahl";
                return;
            }

            _zug = zuege[0].Key;
            _zustand = zuege[0].FirstOrDefault(f => f.Schluessel == ZustandFeld)?.Wert;
            if (_eigenerHalt.HasValue && (_eigenerHalt.Value.Welt != _welt ||
                _eigenerHalt.Value.Zug != _zug || _zustand != "Pause")) _eigenerHalt = null;
            _meldung = _zustand switch
            {
                "Running" => "Zug fährt",
                "Pause" when _eigenerHalt.HasValue => "Zug durch Floppy angehalten",
                "Pause" => "Zug pausiert bereits – kein eigener Halt zum Zurücknehmen",
                null => "Zugzustand nicht lesbar",
                _ => "Zugzustand: " + _zustand
            };
        }

        private static bool Verfuegbar => _zug >= 0 && _welt == Welt.Kennung && Welt.ImSpiel &&
            Debugger.Erreichbar && Schutz.Erlaubt(out _);

        public static CheatCategory Kategorie()
        {
            var k = new CheatCategory("Zug");
            k.Add(new CheatOption { Id = "zug.direktinfo", Label = "Zugaktionen", Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = _meldung });
            k.Add(new CheatOption { Id = "zug.anhalten", Label = "Zug anhalten", Kind = OptionKind.Button,
                Scope = CheatScope.Everyone, IsAvailable = () => Verfuegbar && _zustand == "Running",
                Description = "Pausiert den eindeutig erkannten Zug. Betrifft die gemeinsame Runde.",
                OnInvoke = Anhalten });
            k.Add(new CheatOption { Id = "zug.weiterfahren", Label = "Zug weiterfahren", Kind = OptionKind.Button,
                Scope = CheatScope.Everyone, IsAvailable = () => Verfuegbar && _eigenerHalt.HasValue && _zustand == "Pause",
                Description = "Nimmt nur einen von Floppy bestätigten Halt desselben Zugs in derselben Welt zurück.",
                OnInvoke = Weiterfahren });
            k.Add(new CheatOption { Id = "zug.abkuehlen", Label = "Zug abkühlen", Kind = OptionKind.Button,
                Scope = CheatScope.Everyone, IsAvailable = () => Verfuegbar,
                Description = "Setzt die aktuelle Hitze der an diesem Zug befestigten Wagen einmalig auf null.",
                OnInvoke = Abkuehlen });
            return k;
        }

        private static bool VorAktion(CheatOption option)
        {
            string welt = _welt;
            long zug = _zug;
            Welt.Aktualisiere();
            Aktualisiere();
            if (welt != _welt || zug != _zug)
            {
                option.Fail("Welt oder Zug haben gewechselt. Bitte den aktuellen Zug prüfen und die Aktion erneut auswählen.");
                return false;
            }
            if (!Schutz.Erlaubt(out string grund)) { option.Fail(grund); return false; }
            if (!Verfuegbar) { option.Fail(_meldung); return false; }
            return true;
        }

        private static bool GleicheWelt(string kennung)
        {
            Welt.Aktualisiere();
            if (Welt.ImSpiel && Debugger.Erreichbar && Welt.Kennung == kennung) return true;
            Vergiss();
            return false;
        }

        private static void Anhalten(CheatOption option)
        {
            if (!VorAktion(option)) return;
            if (_zustand != "Running") { option.Fail("Der Zug fährt gerade nicht"); return; }
            string welt = _welt;
            long zug = _zug;
            bool bestaetigt = Welt.Setze(zug, ZugTyp, ZustandFeld, JsonSerializer.Serialize("Pause"), leseVersuche: 4);
            bool gleicheWelt = GleicheWelt(welt);
            if (!bestaetigt || !gleicheWelt)
            {
                option.Fail("Anhalten nicht bestätigt. Der Ausgang ist unklar; bitte den Spielzustand prüfen.");
                return;
            }
            _eigenerHalt = (welt, zug);
            _zustand = "Pause";
            _meldung = option.Message = "Zug angehalten";
        }

        private static void Weiterfahren(CheatOption option)
        {
            if (!VorAktion(option)) return;
            if (!_eigenerHalt.HasValue || _zustand != "Pause")
            { option.Fail("Für diesen Zug ist kein eigener Halt zum Zurücknehmen gespeichert"); return; }
            string welt = _welt;
            bool bestaetigt = Welt.Setze(_zug, ZugTyp, ZustandFeld, JsonSerializer.Serialize("Running"), leseVersuche: 4);
            bool gleicheWelt = GleicheWelt(welt);
            if (!bestaetigt || !gleicheWelt)
            {
                option.Fail("Weiterfahren nicht bestätigt. Der Ausgang ist unklar; bitte den Spielzustand prüfen.");
                return;
            }
            _eigenerHalt = null;
            _zustand = "Running";
            _meldung = option.Message = "Zug fährt weiter";
        }

        private static void Abkuehlen(CheatOption option)
        {
            if (!VorAktion(option)) return;
            string welt = _welt;
            var wagen = Welt.Felder(WagenTyp).GroupBy(f => f.Entitaet)
                .Where(g => g.Key >= 0 && long.TryParse(g.FirstOrDefault(f => f.Schluessel == WagenZugFeld)?.Wert,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out long zug) && zug == _zug)
                .Select(g => g.FirstOrDefault(f => f.Schluessel == HitzeFeld))
                .Where(f => f != null && f.Zahl.HasValue && double.IsFinite(f.Zahl.Value) && f.Zahl.Value >= 0).ToArray();
            if (!GleicheWelt(welt))
            {
                option.Fail("Die Welt hat beim Lesen der Wagen gewechselt oder antwortet nicht. Abkühlen wurde nicht ausgeführt.");
                return;
            }
            if (wagen.Length == 0) { option.Fail("Keine lesbaren Hitze-Werte an diesem Zug gefunden"); return; }
            var heiss = wagen.Where(f => f.Zahl.Value > 0).ToArray();
            if (heiss.Length == 0) { option.Message = "Der Zug ist bereits kühl"; return; }
            int bestaetigt = 0;
            foreach (var feld in heiss)
            {
                bool gelesen = Welt.Setze(feld.Entitaet, WagenTyp, HitzeFeld, "0", leseVersuche: 4);
                if (!GleicheWelt(welt))
                {
                    option.Fail("Die Welt hat gewechselt oder antwortet nicht. Weiteres Abkühlen abgebrochen; Ausgang unklar.");
                    return;
                }
                if (gelesen) bestaetigt++;
            }
            if (bestaetigt != heiss.Length)
                option.Fail($"Abkühlen bei {bestaetigt} von {heiss.Length} Wagen bestätigt. Übriger Ausgang unklar; bitte den Spielzustand prüfen.");
            else option.Message = $"Zug abgekühlt ({bestaetigt} Wagen)";
        }
    }
}
