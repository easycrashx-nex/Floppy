using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;

namespace Floppy.Unrailed2
{
    /// <summary>Externer Adapter: Zahlen, Spielstände und Fortschritt über den Webdebugger;
    /// ByteBool-Schalter über den versionsgeprüften Spieleingabe-Zustand.
    /// Änderungen der Simulation gelten für die gemeinsame Runde.</summary>
    public class Unrailed2Module : IGameModule
    {
        public string ProductName => "Unrailed2";
        public string DisplayName => "Unrailed! 2";

        public void Initialize()
        {
            NativeCheats.Reset();
            Cheats.VergissSnapshot();
            Muttern.Vergiss();
            ZugAktionen.Vergiss();
            _art = null;
            _auswahlWelt = null;
            _felder = new List<Feld>();
        }

        public void Update()
        {
            Welt.Aktualisiere();
            Cheats.AktualisiereSnapshot();
            Muttern.Aktualisiere();
            ZugAktionen.Aktualisiere();
            ListenPflegen();
            foreach (var option in Registry.AllOptions)
            {
                if (option.Kind != OptionKind.Info || option.OnChanged == null) continue;
                try { option.OnChanged(option); }
                catch (Exception ex) { Log.Warning("Unrailed2: Anzeige " + option.Id + " - " + ex.Message); }
            }
        }

        public void SetMenuOpen(bool open) { }

        public bool IsReady(out string status)
        {
            if (!Spiel.Angeschaltet)
            {
                status = "Entwicklerzugang noch nicht angeschaltet - siehe Rubrik \"Zugang\"";
                return false;
            }

            if (!Spiel.Laeuft)
            {
                status = "Unrailed 2 läuft nicht";
                return false;
            }

            if (!Debugger.Erreichbar)
            {
                status = Debugger.Zustand;
                return false;
            }

            if (Schutz.An && Schutz.Gewerteter_Lauf)
            {
                status = "Gewerteter Lauf - Floppy hält sich hier raus";
                return false;
            }

            status = !Welt.ImSpiel ? "Verbunden - noch im Menü"
                : Cheats.Verfuegbar ? "Bereit - " + Welt.Lage
                : Muttern.Verfuegbar ? "Bereit - " + Welt.Lage + " (zusätzliche Entwickleroptionen nicht verfügbar)"
                : Welt.Lage + " - noch keine verfügbaren Teamfunktionen";
            return true;
        }

        public List<CheatCategory> BuildCategories()
        {
            return new List<CheatCategory>
            {
                Zugang(),
                Zug(),
                Cheats.Bauen(),
                Muttern.Kategorie(),
                Cheats.Automatik(),
                Werte(),
                Spielstaende(),
                Fortschritt()
            };
        }

        private static Func<bool> ImSpiel => () => Debugger.Erreichbar && Welt.ImSpiel;

        private static CheatCategory Zug()
        {
            var k = ZugAktionen.Kategorie();
            k.Options.AddRange(Cheats.Zug().Options);
            return k;
        }

        // ==================================================================== Zugang

        private CheatCategory Zugang()
        {
            var k = new CheatCategory("Zugang");

            k.Add(new CheatOption
            {
                Id = "u2.info",
                Label = "Verbindung",
                Kind = OptionKind.Info,
                OnChanged = o =>
                {
                    o.TextValue = !Spiel.Angeschaltet ? "nicht angeschaltet"
                        : !Spiel.Laeuft ? "angeschaltet, Spiel läuft nicht"
                        : Debugger.Erreichbar ? Schutz.Lagebericht()
                        : Debugger.Zustand;
                }
            });

            k.Add(new CheatOption
            {
                Id = "u2.funktionen",
                Label = "Zusätzliche Entwickleroptionen",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Cheats.Zustand
            });

            k.Add(new CheatOption
            {
                Id = "u2.anschalten",
                Label = "Entwicklerzugang anschalten",
                Description = "Aktiviert die Entwicklerfunktionen für den nächsten Spielstart. " +
                              "Die bisherigen Einstellungen werden vorher gesichert.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => !Spiel.Laeuft && !Spiel.Angeschaltet,
                OnInvoke = o => o.Message = Spiel.Anschalten()
            });

            k.Add(new CheatOption
            {
                Id = "u2.ausschalten",
                Label = "Wieder ausschalten",
                Description = "Entfernt die Entwicklerschalter. Andere aktuelle Einstellungen bleiben erhalten.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => !Spiel.Laeuft && Spiel.Angeschaltet,
                OnInvoke = o => o.Message = Spiel.Ausschalten()
            });

            k.Add(new CheatOption
            {
                Id = "u2.wiederherstellen",
                Label = "Einstellungen wiederherstellen",
                Description = "Stellt die gesamte Einstellungsdatei vor der ersten Floppy-Einrichtung wieder her. " +
                              "Spätere Änderungen an den Spieleinstellungen werden dabei zurückgenommen.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => !Spiel.Laeuft && Spiel.HatSicherung,
                OnInvoke = o => o.Message = Spiel.Wiederherstellen()
            });

            k.Add(new CheatOption
            {
                Id = "u2.bestenliste",
                Label = "Bestenlisten-Schutz",
                Description = "Unrailed 2 hat eine öffentliche Rangliste, und der Spielstand " +
                              "vermerkt nicht, ob gecheatet wurde. Solange das hier an ist, " +
                              "schreibt Floppy in einem gewerteten Lauf gar nichts. " +
                              "Geschichte und eigener Startwert bleiben frei.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                BoolValue = true,
                Ruhebool = true,
                OnChanged = o => Schutz.An = o.BoolValue
            });

            k.Add(new CheatOption
            {
                Id = "u2.hinweis",
                Label = "Wichtig zum Mitspielen",
                Kind = OptionKind.Info,
                Scope = CheatScope.Everyone,
                OnChanged = o => o.TextValue =
                    "Kein Cheat gilt nur für dich - alle verändern die gemeinsame Welt"
            });

            k.Add(new CheatOption
            {
                Id = "u2.allesaus",
                Label = "Alle Cheats zurücknehmen",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Cheats.Verfuegbar,
                OnInvoke = o =>
                {
                    if (Cheats.AllesAus(out string message)) o.Message = message;
                    else o.Fail(message);
                }
            });

            return k;
        }

        // ===================================================================== Werte

        private static Komponentenart _art;
        private static string _auswahlWelt;
        private static List<Feld> _felder = new List<Feld>();

        private CheatCategory Werte()
        {
            var k = new CheatCategory("Werte");

            k.Add(new CheatOption
            {
                Id = "wert.hinweis",
                Label = "Was das ist",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue =
                    "Direktzugriff auf die Zahlenwerte der laufenden Welt"
            });

            k.Add(new CheatOption
            {
                Id = "wert.gruppe",
                Label = "Bereich",
                Description = "Was es in der laufenden Partie gerade gibt. Die Liste kommt " +
                              "vom Spiel selbst - sie stimmt also auch nach einem Update noch.",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone,
                IsAvailable = ImSpiel,
                Choices = new[] { "(keine Partie)" },
                OnChanged = o =>
                {
                    var arten = Welt.Arten();

                    AktualisiereFelder(o.ChoiceIndex > 0 && o.ChoiceIndex <= arten.Count
                        ? arten[o.ChoiceIndex - 1] : null);
                }
            });

            k.Add(new CheatOption
            {
                Id = "wert.feld",
                Label = "Wert",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone,
                IsAvailable = ImSpiel,
                Choices = new[] { "(Wert wählen)" }
            });

            k.Add(new CheatOption
            {
                Id = "wert.jetzt",
                Label = "Steht gerade auf",
                Kind = OptionKind.Info,
                IsAvailable = ImSpiel,
                OnChanged = o =>
                {
                    var f = GewaehltesFeld();

                    o.TextValue = f == null ? "-"
                        : Kurz(f.Wert) + "   (" + AnzahlTraeger() + "x vorhanden)";
                }
            });

            k.Add(new CheatOption
            {
                Id = "wert.neu",
                Label = "Neuer Wert",
                Kind = OptionKind.Number,
                Scope = CheatScope.Everyone,
                IsAvailable = ImSpiel,
                Min = -100000f, Max = 100000f, Step = 1f
            });

            k.Add(new CheatOption
            {
                Id = "wert.setzen",
                Label = "Setzen",
                Description = "Schreibt die Zahl bei allen Entitäten mit diesem Zahlenfeld und liest " +
                              "danach zurück, ob das Spiel ihn übernommen hat.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Debugger.Erreichbar && Welt.ImSpiel && _auswahlWelt == Welt.Kennung &&
                    _art != null && GewaehltesFeld() != null,
                OnInvoke = o =>
                {
                    if (!Schutz.Erlaubt(out string grund)) { o.Fail(grund); return; }

                    string welt = _auswahlWelt;
                    string gewaehlterSchluessel = GewaehltesFeld()?.Schluessel;
                    if (!PruefeAuswahlWelt(o, welt)) return;
                    AktualisiereFelder(_art);
                    var f = GewaehltesFeld();
                    if (f == null || _art == null || f.Schluessel != gewaehlterSchluessel)
                    { o.Fail("Bitte Bereich und Wert neu auswählen"); return; }

                    var eingabe = Registry.Find("wert.neu");
                    double wert = eingabe?.NumberValue ?? float.NaN;
                    if (double.IsNaN(wert) || double.IsInfinity(wert) || wert < eingabe.Min || wert > eingabe.Max)
                    { o.Fail("Bitte eine gültige Zahl im angezeigten Bereich eingeben"); return; }

                    var eids = _felder.Where(x => x.Schluessel == f.Schluessel).Select(x => x.Entitaet).Distinct().ToList();
                    string typ = ArtIdentitaet(_art);
                    if (!PruefeAuswahlWelt(o, welt)) return;
                    if (!Schutz.Erlaubt(out grund)) { o.Fail(grund); return; }

                    int sitzt = Welt.SetzeZahlMehrere(eids, typ, f.Schluessel, wert);
                    if (!PruefeAuswahlWelt(o, welt)) return;

                    if (sitzt != eids.Count)
                        o.Fail("Änderung nicht vollständig bestätigt (" + sitzt + " von " + eids.Count + "); Spielzustand prüfen");
                    else
                        o.Message = f.Anzeige + " = " + wert + "   (" + sitzt + " von " + eids.Count + ")";

                    AktualisiereFelder(_art);
                }
            });

            return k;
        }

        private static string Kurz(string wert)
        {
            if (wert == null) return "-";
            return wert.Length <= 60 ? wert : wert.Substring(0, 57) + "...";
        }

        private static int AnzahlTraeger()
        {
            string feld = GewaehltesFeld()?.Schluessel;
            return _felder.Where(f => f.Schluessel == feld).Select(f => f.Entitaet).Distinct().Count();
        }

        private static bool PruefeAuswahlWelt(CheatOption option, string welt)
        {
            Welt.Aktualisiere();
            if (Debugger.Erreichbar && Welt.ImSpiel && !string.IsNullOrEmpty(welt) && welt == Welt.Kennung)
                return true;
            AktualisiereFelder(null);
            var gruppe = Registry.Find("wert.gruppe");
            if (gruppe != null) gruppe.ChoiceIndex = 0;
            option.Fail("Welt gewechselt oder Verbindung verloren; bitte Bereich und Wert neu auswählen");
            return false;
        }

        private static Feld GewaehltesFeld()
        {
            var wahl = Registry.Find("wert.feld");
            if (wahl == null) return null;

            var namen = Feldnamen();
            int index = wahl.ChoiceIndex - 1;
            if (index < 0 || index >= namen.Count) return null;

            string name = namen[index];
            return _felder.FirstOrDefault(f => f.Anzeige == name);
        }

        /// <summary>Die Feldnamen des gewaehlten Bereichs, jeder genau einmal - eine
        /// Komponente sitzt an vielen Entitaeten, ihre Felder heissen aber gleich.</summary>
        private static List<string> Feldnamen()
        {
            return _felder.Select(f => f.Anzeige).Distinct().OrderBy(n => n).ToList();
        }

        private static string ArtIdentitaet(Komponentenart art) => art?.NameHash ?? art?.Schluessel;

        private static bool IstNumerisch(Feld feld) => feld.Entitaet >= 0 && feld.IsNumber &&
            feld.Zahl is double zahl && !double.IsNaN(zahl) && !double.IsInfinity(zahl);

        private static void AktualisiereFelder(Komponentenart art)
        {
            string gewaehlt = _auswahlWelt == Welt.Kennung && ArtIdentitaet(_art) == ArtIdentitaet(art)
                ? GewaehltesFeld()?.Schluessel : null;
            _art = art;
            _auswahlWelt = art == null ? null : Welt.Kennung;
            _felder = art == null || !Welt.ImSpiel || !Debugger.Erreichbar
                ? new List<Feld>() : Welt.Felder(ArtIdentitaet(art)).Where(IstNumerisch).ToList();

            var feld = Registry.Find("wert.feld");
            if (feld == null) return;
            var namen = Feldnamen();
            feld.Choices = new[] { "(Wert wählen)" }.Concat(namen).ToArray();
            var vorher = _felder.FirstOrDefault(f => f.Schluessel == gewaehlt);
            feld.ChoiceIndex = vorher == null ? 0 : namen.IndexOf(vorher.Anzeige) + 1;
        }

        // ============================================================== Spielstaende

        private CheatCategory Spielstaende()
        {
            var k = new CheatCategory("Spielstände");

            k.Add(new CheatOption
            {
                Id = "zust.info",
                Label = "Was das ist",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue =
                    "Zwischenstände: die ganze Welt sichern und jederzeit zurückspielen"
            });

            k.Add(new CheatOption
            {
                Id = "zust.sichern",
                Label = "Jetzigen Stand sichern",
                Description = "Legt einen Zwischenstand der laufenden Welt an. Den Namen " +
                              "vergibt das Spiel selbst.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = ImSpiel,
                OnInvoke = o => Anfrage(o, "createDump?name=", "Gesichert", partie: true)
            });

            k.Add(new CheatOption
            {
                Id = "zust.welcher",
                Label = "Welcher",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Debugger.Erreichbar,
                Choices = new[] { "-" }
            });

            k.Add(new CheatOption
            {
                Id = "zust.laden",
                Label = "Zurückspielen",
                Description = "Setzt die ganze Runde auf diesen Stand zurück - für alle.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Debugger.Erreichbar && Gewaehlt("zust.welcher") != null,
                OnInvoke = o => StandAktion(o, "openDump", "Zurückgespielt: ")
            });

            k.Add(new CheatOption
            {
                Id = "zust.loeschen",
                Label = "Löschen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => Debugger.Erreichbar && Gewaehlt("zust.welcher") != null,
                OnInvoke = o => StandAktion(o, "deleteDump", "Gelöscht: ")
            });

            return k;
        }

        // ================================================================ Fortschritt

        private CheatCategory Fortschritt()
        {
            var k = new CheatCategory("Fortschritt");

            k.Add(new CheatOption
            {
                Id = "fort.freischalten",
                Label = "Alles freischalten",
                Description = "Der Befehl des Spiels selbst - schaltet frei, was es zu " +
                              "entsperren gibt.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => Debugger.Erreichbar,
                OnInvoke = o => Anfrage(o, "unlockAll", "Freigeschaltet")
            });

            k.Add(new CheatOption
            {
                Id = "fort.tutorialfertig",
                Label = "Alle Tutorials als erledigt",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => Debugger.Erreichbar,
                OnInvoke = o => Anfrage(o, "finishTutorials", "Erledigt")
            });

            k.Add(new CheatOption
            {
                Id = "fort.tutorialzurueck",
                Label = "Tutorials zurücksetzen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => Debugger.Erreichbar,
                OnInvoke = o => Anfrage(o, "clearTutorial", "Zurückgesetzt")
            });

            k.Add(new CheatOption
            {
                Id = "fort.staende",
                Label = "Fortschrittsdateien",
                Kind = OptionKind.Info,
                OnChanged = o =>
                {
                    var n = Namen("listProgress");
                    o.TextValue = n.Count == 0 ? "-" : string.Join(", ", n);
                }
            });

            // Ab hier wird es unwiderruflich. Deshalb ein Riegel davor: Der Knopf tut
            // nichts, solange der Schalter darueber nicht umgelegt ist. Ein Fehlklick
            // soll keinen echten Fortschritt vernichten koennen.

            k.Add(new CheatOption
            {
                Id = "fort.riegel",
                Label = "Löschen erlauben",
                Description = "Sicherung für den Knopf darunter. Geht nach Gebrauch von " +
                              "allein wieder zu.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe
            });

            k.Add(new CheatOption
            {
                Id = "fort.loeschen",
                Label = "Fortschritt löschen",
                Description = "Löscht den Spielfortschritt. Nicht rückgängig zu machen.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                IsAvailable = () => Debugger.Erreichbar && Registry.Find("fort.riegel")?.BoolValue == true,
                OnInvoke = o =>
                {
                    var riegel = Registry.Find("fort.riegel");

                    if (riegel == null || !riegel.BoolValue)
                    {
                        o.Fail("Erst den Schalter darüber umlegen");
                        return;
                    }

                    Anfrage(o, "clearProgress", "Fortschritt gelöscht");
                    riegel.BoolValue = false;
                }
            });

            return k;
        }

        // ================================================================== Kleinkram

        private static void Anfrage(CheatOption option, string pfad, string meldung, bool partie = false)
        {
            if (!Debugger.Erreichbar || (partie && !Welt.ImSpiel))
            { option.Fail(partie ? "Keine erreichbare laufende Partie" : "Spielzugang nicht erreichbar"); return; }
            if (!Debugger.Loese(pfad))
            { option.Fail("Anfrage nicht bestätigt: " + Debugger.Zustand); return; }
            option.Message = meldung;
        }

        private static void StandAktion(CheatOption option, string befehl, string meldung)
        {
            if (!Debugger.Erreichbar) { option.Fail("Spielzugang nicht erreichbar"); return; }
            string name = Gewaehlt("zust.welcher");
            if (name == null) { option.Fail("Bitte einen Spielstand auswählen"); return; }
            if (!AktualisiereStaende().Contains(name, StringComparer.Ordinal))
            { option.Fail("Spielstand nicht mehr verfügbar; bitte erneut auswählen"); return; }
            Anfrage(option, befehl + "?name=" + Debugger.Verpacke(name), meldung + name);
        }

        private static List<string> AktualisiereStaende()
        {
            var zust = Registry.Find("zust.welcher");
            if (zust == null) return new List<string>();
            string vorher = Gewaehlt("zust.welcher");
            var namen = Namen("listDumps");
            var anzeige = new[] { "-" }.Concat(namen.Distinct(StringComparer.Ordinal)).ToArray();
            if (!anzeige.SequenceEqual(zust.Choices)) zust.Choices = anzeige;
            zust.ChoiceIndex = Math.Max(0, Array.IndexOf(anzeige, vorher));
            return namen;
        }

        /// <summary>Die Namen aus einer Antwort der Form {"files":[{"name":...}]}.</summary>
        private static List<string> Namen(string pfad)
        {
            var raus = new List<string>();

            using (var doc = Debugger.Frage(pfad))
            {
                if (doc == null) return raus;

                try
                {
                    if (!doc.RootElement.TryGetProperty("files", out var dateien)) return raus;

                    foreach (var d in dateien.EnumerateArray())
                        if (d.TryGetProperty("name", out var n) && n.GetString() is string s)
                            raus.Add(s);
                }
                catch { }
            }

            return raus;
        }

        private static string Gewaehlt(string id)
        {
            var o = Registry.Find(id);
            if (o == null || o.ChoiceIndex < 0 || o.ChoiceIndex >= o.Choices.Length) return null;

            string wahl = o.Choices[o.ChoiceIndex];
            return wahl == "-" ? null : wahl;
        }

        /// <summary>Fuehrt die Auswahllisten nach. Aus dem Modultakt gerufen, weil
        /// sich beim Kartenwechsel alles aendert.</summary>
        internal static void ListenPflegen()
        {
            try
            {
                var gruppe = Registry.Find("wert.gruppe");

                if (gruppe != null)
                {
                    if (_art != null && _auswahlWelt != Welt.Kennung) AktualisiereFelder(null);
                    var arten = Welt.ImSpiel && Debugger.Erreichbar
                        ? Welt.Arten() : new List<Komponentenart>();

                    var anzeige = arten.Count == 0
                        ? new[] { "(keine Partie)" }
                        : new[] { "(Bereich wählen)" }.Concat(arten.Select(a => a.Anzeige + "  (" + a.Anzahl + ")")).ToArray();

                    int index = arten.FindIndex(a => ArtIdentitaet(a) == ArtIdentitaet(_art));
                    if (!anzeige.SequenceEqual(gruppe.Choices)) gruppe.Choices = anzeige;
                    gruppe.ChoiceIndex = index + 1;
                    AktualisiereFelder(index < 0 ? null : arten[index]);
                }

                AktualisiereStaende();
            }
            catch (Exception ex)
            {
                Log.Warning("Unrailed2: Listen - " + ex.Message);
            }
        }
    }
}
