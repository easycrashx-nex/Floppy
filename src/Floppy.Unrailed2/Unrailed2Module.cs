using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;

namespace Floppy.Unrailed2
{
    /// <summary>Unrailed 2 - der Sonderfall unter Floppys Spielen.
    ///
    /// Bei jedem anderen Spiel bringen wir unseren Code irgendwie ins Spiel hinein: als
    /// BepInEx-Erweiterung, als Skript in der .pck, oder wir lesen von aussen im
    /// Speicher. Hier braucht es nichts davon.
    ///
    /// Unrailed 2 hat ein vollstaendiges Entwicklerwerkzeug an Bord, das die Entwickler
    /// nur hinter einem Schalter versteckt haben. Steht EnableWebDebug in der
    /// Einstellungsdatei, oeffnet das Spiel beim Start selbst einen kleinen Webserver
    /// auf localhost:8001. Darueber laesst sich die laufende Welt auslesen, jedes Feld
    /// jeder Entitaet beschreiben, der Weltzustand sichern und zurueckspielen, und der
    /// Fortschritt verwalten.
    ///
    /// Das ist aus zwei Gruenden der bessere Weg als alles Selbstgebaute:
    ///
    /// Erstens veraendern wir keine einzige Spieldatei und haengen uns in nichts ein -
    /// wir legen einen Schalter um, den das Spiel selbst mitbringt.
    ///
    /// Zweitens ist es bei diesem Spiel der einzig gangbare. Unrailed 2 rechnet
    /// deterministisch: Jeder Mitspieler simuliert dieselbe Welt aus denselben
    /// Eingaben. Wer von aussen in den Speicher schreibt, hat im selben Augenblick eine
    /// andere Welt als alle anderen, und die Runde faellt auseinander. Die
    /// Anforderungen des Entwicklerwerkzeugs gehen dagegen durch den Eingabestrom -
    /// dieselbe Warteschlange, durch die auch ein Tastendruck laeuft.
    ///
    /// Daraus folgt eine Eigenheit, die man beim Bedienen wissen muss: Es gibt hier
    /// kein "nur fuer mich". Jeder Cheat aendert die gemeinsame Welt.</summary>
    public class Unrailed2Module : IGameModule
    {
        public string ProductName => "Unrailed2";
        public string DisplayName => "Unrailed! 2";

        public void Initialize()
        {
            Cheats.VergissSnapshot();
            Muttern.Vergiss();
            ZugAktionen.Vergiss();
            _art = null;
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
                OnInvoke = o => o.Message = Cheats.AllesAus()
            });

            return k;
        }

        // ===================================================================== Werte

        private static Komponentenart _art;
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
                    "Direktzugriff auf jeden Wert der laufenden Welt"
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
                Description = "Schreibt den Wert bei allen Entitäten dieses Bereichs und liest " +
                              "danach zurück, ob das Spiel ihn übernommen hat.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Debugger.Erreichbar && Welt.ImSpiel && _art != null && GewaehltesFeld() != null,
                OnInvoke = o =>
                {
                    if (!Schutz.Erlaubt(out string grund)) { o.Message = grund; return; }

                    string gewaehlterSchluessel = GewaehltesFeld()?.Schluessel;
                    AktualisiereFelder(_art);
                    var f = GewaehltesFeld();
                    if (f == null || _art == null || f.Schluessel != gewaehlterSchluessel)
                    { o.Fail("Bitte Bereich und Wert neu auswählen"); return; }

                    double wert = Registry.Find("wert.neu")?.NumberValue ?? 0;

                    var eids = _felder.Select(x => x.Entitaet).Distinct().ToList();
                    string typ = ArtIdentitaet(_art);

                    int sitzt = Welt.SetzeZahlMehrere(eids, typ, f.Schluessel, wert);

                    o.Message = sitzt == 0
                        ? "Das Spiel hat den Wert nicht übernommen"
                        : f.Anzeige + " = " + wert + "   (" + sitzt + " von " + eids.Count + ")";

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
            return _felder.Select(f => f.Entitaet).Distinct().Count();
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

        private static void AktualisiereFelder(Komponentenart art)
        {
            string gewaehlt = ArtIdentitaet(_art) == ArtIdentitaet(art) ? GewaehltesFeld()?.Schluessel : null;
            _art = art;
            _felder = art == null || !Welt.ImSpiel || !Debugger.Erreichbar
                ? new List<Feld>() : Welt.Felder(ArtIdentitaet(art));

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
                OnInvoke = o => o.Message = Debugger.Loese("createDump?name=")
                    ? "Gesichert" : "Ging nicht"
            });

            k.Add(new CheatOption
            {
                Id = "zust.welcher",
                Label = "Welcher",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone,
                Choices = new[] { "-" }
            });

            k.Add(new CheatOption
            {
                Id = "zust.laden",
                Label = "Zurückspielen",
                Description = "Setzt die ganze Runde auf diesen Stand zurück - für alle.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o =>
                {
                    string name = Gewaehlt("zust.welcher");
                    if (name == null) { o.Message = "Nichts ausgewählt"; return; }

                    o.Message = Debugger.Loese("openDump?name=" + Debugger.Verpacke(name))
                        ? "Zurückgespielt: " + name : "Ging nicht";
                }
            });

            k.Add(new CheatOption
            {
                Id = "zust.loeschen",
                Label = "Löschen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o =>
                {
                    string name = Gewaehlt("zust.welcher");
                    if (name == null) { o.Message = "Nichts ausgewählt"; return; }

                    o.Message = Debugger.Loese("deleteDump?name=" + Debugger.Verpacke(name))
                        ? "Gelöscht" : "Ging nicht";
                }
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
                OnInvoke = o => o.Message = Debugger.Loese("unlockAll")
                    ? "Freigeschaltet" : "Ging nicht"
            });

            k.Add(new CheatOption
            {
                Id = "fort.tutorialfertig",
                Label = "Alle Tutorials als erledigt",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Debugger.Loese("finishTutorials")
                    ? "Erledigt" : "Ging nicht"
            });

            k.Add(new CheatOption
            {
                Id = "fort.tutorialzurueck",
                Label = "Tutorials zurücksetzen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Debugger.Loese("clearTutorial")
                    ? "Zurückgesetzt" : "Ging nicht"
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
                IsAvailable = () => Registry.Find("fort.riegel")?.BoolValue == true,
                OnInvoke = o =>
                {
                    var riegel = Registry.Find("fort.riegel");

                    if (riegel == null || !riegel.BoolValue)
                    {
                        o.Message = "Erst den Schalter darüber umlegen";
                        return;
                    }

                    bool gut = Debugger.Loese("clearProgress");
                    riegel.BoolValue = false;

                    o.Message = gut ? "Fortschritt gelöscht" : "Ging nicht";
                }
            });

            return k;
        }

        // ================================================================== Kleinkram

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

                var zust = Registry.Find("zust.welcher");

                if (zust != null)
                {
                    var namen = Namen("listDumps");
                    var anzeige = namen.Count == 0 ? new[] { "-" } : namen.ToArray();

                    if (!anzeige.SequenceEqual(zust.Choices))
                    {
                        zust.Choices = anzeige;
                        if (zust.ChoiceIndex >= anzeige.Length) zust.ChoiceIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Unrailed2: Listen - " + ex.Message);
            }
        }
    }
}
