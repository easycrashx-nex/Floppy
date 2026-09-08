using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;

namespace Floppy.Unrailed2
{
    /// <summary>Die Cheats, die das Spiel selbst mitbringt.
    ///
    /// Unrailed 2 hat eine eigene Cheat-Struktur im Weltzustand: die Komponente
    /// CheatSingleton mit sechzehn Feldern. Die Entwickler benutzen sie beim Testen -
    /// Zug unkaputtbar machen, Muttern dazugeben, das Spiel sich selbst spielen lassen.
    ///
    /// Sie ist Teil der gerechneten Welt und wird mitserialisiert. Das ist der Grund,
    /// warum diese Cheats in einer Runde mit Freunden ueberhaupt funktionieren: Sie
    /// gelten fuer alle gleichzeitig, die Simulation bleibt bei allen dieselbe, und
    /// niemand fliegt wegen Abweichung aus der Runde.
    ///
    /// Umgekehrt heisst das aber auch: Es gibt hier kein "nur fuer mich". Jeder dieser
    /// Cheats veraendert die gemeinsame Welt, und jeder Mitspieler merkt es. Deshalb
    /// tragen sie alle die Kennzeichnung "betrifft alle".
    ///
    /// Die Zahlen unten sind die Hashwerte, mit denen das Spiel Typen und Felder
    /// benennt. Sie stammen aus res://Src/TypeNames.json im Spielpaket - der Tabelle,
    /// die das Spiel selbst zum Uebersetzen benutzt.</summary>
    internal static class Cheats
    {
        // common.Unrailed2.Modules.Base.CheatSingleton
        public const string Singleton = "-486817492";

        private const string DisableTrainCrash = "953417568";
        private const string DisableTrainBurning = "-1773927608";
        private const string DisableTrainRunning = "1439690848";
        private const string DisablePhysics = "-973793006";
        private const string BuildPathWithoutTracks = "657403926";
        private const string SkipBeacons = "-1677779445";
        private const string SyncMapgen = "-162511644";
        private const string AutoPlay = "693253938";
        private const string AutoPlayPreferDown = "-557360116";
        private const string StopAutoPlayAfterTracks = "-812172858";
        private const string MinAutoPlayStagesToBoss = "338793709";

        /// <summary>Die Entitaet, die den Cheat-Zustand traegt. Es gibt genau eine, aber
        /// ihre Nummer wechselt mit jeder Karte - deshalb bei Bedarf frisch nachfragen
        /// statt sie sich zu merken.</summary>
        private static long Traeger()
        {
            return Welt.Traeger(Singleton);
        }

        private static IReadOnlyList<Feld> _snapshot = Array.Empty<Feld>();

        internal static void VergissSnapshot() => _snapshot = Array.Empty<Feld>();

        /// <summary>Einmal im Modultakt lesen; Verfügbarkeit und Anzeigen lösen kein HTTP aus.</summary>
        internal static void AktualisiereSnapshot()
        {
            VergissSnapshot();
            if (Welt.ImSpiel && Debugger.Erreichbar)
                _snapshot = Welt.Felder(Singleton).Where(f => f.Entitaet >= 0).ToArray();
        }

        public static bool Verfuegbar => Welt.ImSpiel && Debugger.Erreichbar && _snapshot.Count > 0;

        public static string Zustand => !Debugger.Erreichbar ? Debugger.Zustand
            : !Welt.ImSpiel ? "Noch keine laufende Partie"
            : Verfuegbar ? "Zug-, Bau- und Automatikoptionen verfügbar"
            : "Runde erkannt; zusätzliche Entwickleroptionen werden vom Spiel nicht bereitgestellt";

        // ------------------------------------------------------------------ Schreiben

        private static string Schalte(string feld, bool an, string name)
        {
            if (!Schutz.Erlaubt(out string grund)) return grund;

            long eid = Traeger();
            if (eid < 0) return Zustand;

            return Welt.SetzeSchalter(eid, Singleton, feld, an)
                ? name + (an ? ": an" : ": aus")
                : "Ging nicht - das Spiel hat es nicht übernommen";
        }

        private static string SetzeZahl(string feld, double wert, string name)
        {
            if (!Schutz.Erlaubt(out string grund)) return grund;

            long eid = Traeger();
            if (eid < 0) return Zustand;

            return Welt.SetzeZahl(eid, Singleton, feld, wert)
                ? name + ": " + wert
                : "Ging nicht - das Spiel hat es nicht übernommen";
        }

        private static string Lies(string feld)
        {
            return Verfuegbar ? _snapshot.FirstOrDefault(f => f.Schluessel == feld)?.Wert ?? "-" : "-";
        }

        // ------------------------------------------------------------------- Rubriken

        public static CheatCategory Zug()
        {
            var k = new CheatCategory("Zug");

            k.Add(new CheatOption
            {
                Id = "zug.kaputt",
                Label = "Zug geht nicht kaputt",
                Description = "Der Zug entgleist nicht mehr. Der Cheat des Spiels selbst - " +
                              "er gilt für die ganze Runde, nicht nur für dich.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(DisableTrainCrash, o.BoolValue, "Unkaputtbar")
            });

            k.Add(new CheatOption
            {
                Id = "zug.brennt",
                Label = "Zug brennt nicht",
                Description = "Kein Überhitzen mehr - der Wassertank ist damit nebensächlich.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(DisableTrainBurning, o.BoolValue, "Feuerfest")
            });

            k.Add(new CheatOption
            {
                Id = "zug.haltan",
                Label = "Zug fährt nicht los",
                Description = "Hält den Zug an, egal wie viel Gleis liegt. Nimmt allen " +
                              "Zeitdruck raus - das ist das eigentliche Spiel, also gut " +
                              "überlegen.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(DisableTrainRunning, o.BoolValue, "Angehalten")
            });

            k.Add(new CheatOption
            {
                Id = "zug.zustand",
                Label = "Steht gerade auf",
                Kind = OptionKind.Info,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.TextValue =
                    "kaputt: " + Lies(DisableTrainCrash) +
                    "   brennt: " + Lies(DisableTrainBurning) +
                    "   hält: " + Lies(DisableTrainRunning)
            });

            return k;
        }

        public static CheatCategory Bauen()
        {
            var k = new CheatCategory("Bauen");

            k.Add(new CheatOption
            {
                Id = "bau.ohnegleise",
                Label = "Gleise ohne Material",
                Description = "Strecke bauen, ohne Schienen im Gepäck zu haben.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(BuildPathWithoutTracks, o.BoolValue, "Gratis bauen")
            });

            k.Add(new CheatOption
            {
                Id = "bau.physik",
                Label = "Keine Hindernisse",
                Description = "Schaltet die Kollisionsberechnung ab. Sieht wild aus und kann " +
                              "auch mal etwas verhaken - deshalb eher zum Ausprobieren als " +
                              "für eine ernste Runde.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(DisablePhysics, o.BoolValue, "Physik")
            });

            k.Add(new CheatOption
            {
                Id = "bau.beacons",
                Label = "Stationen überspringen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(SkipBeacons, o.BoolValue, "Stationen überspringen")
            });

            return k;
        }

        public static CheatCategory Automatik()
        {
            var k = new CheatCategory("Automatik");

            k.Add(new CheatOption
            {
                Id = "auto.info",
                Label = "Was das ist",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue =
                    "Das Spiel spielt sich selbst - die Testautomatik der Entwickler"
            });

            k.Add(new CheatOption
            {
                Id = "auto.an",
                Label = "Selbst spielen lassen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(AutoPlay, o.BoolValue, "Automatik")
            });

            k.Add(new CheatOption
            {
                Id = "auto.stopp",
                Label = "Nach dem Anschließen anhalten",
                Description = "Die Automatik hört auf, sobald die Gleise verbunden sind.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(StopAutoPlayAfterTracks, o.BoolValue, "Anhalten")
            });

            k.Add(new CheatOption
            {
                Id = "auto.unten",
                Label = "Lieber nach unten bauen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(AutoPlayPreferDown, o.BoolValue, "Richtung")
            });

            k.Add(new CheatOption
            {
                Id = "auto.bosse",
                Label = "Abschnitte bis zum Boss",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                Min = 0f, Max = 30f, Step = 1f,
                NumberValue = 3f,
                Ruhewert = 3f,
                OnChanged = o => o.Message = SetzeZahl(MinAutoPlayStagesToBoss, o.NumberValue, "Abschnitte")
            });

            k.Add(new CheatOption
            {
                Id = "auto.mapgen",
                Label = "Kartenerzeugung gleichschalten",
                Description = "Ein Entwicklerschalter für die Kartenerzeugung. Nur anfassen, " +
                              "wenn beim Erzeugen etwas klemmt.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => Verfuegbar,
                OnChanged = o => o.Message = Schalte(SyncMapgen, o.BoolValue, "Gleichschaltung")
            });

            return k;
        }

        /// <summary>Alles auf einmal aus - fuer den schnellen Rueckweg zum normalen Spiel.</summary>
        public static string AllesAus()
        {
            if (!Schutz.Erlaubt(out string grund)) return grund;

            long eid = Traeger();
            if (eid < 0) return Zustand;

            var felder = new[]
            {
                DisableTrainCrash, DisableTrainBurning, DisableTrainRunning,
                DisablePhysics, BuildPathWithoutTracks, SkipBeacons,
                AutoPlay, StopAutoPlayAfterTracks, AutoPlayPreferDown, SyncMapgen
            };

            foreach (string f in felder)
                Welt.SetzeSchalter(eid, Singleton, f, false);

            // Den Gesamtzustand nach allen Schreibversuchen noch einmal prüfen.
            var gelesen = Welt.Felder(Singleton);
            int aus = felder.Count(f => gelesen.Any(w => w.Entitaet == eid && w.Schluessel == f &&
                string.Equals(w.Wert?.Trim(), "false", StringComparison.OrdinalIgnoreCase)));

            return aus == felder.Length
                ? "Alle Cheats zurückgenommen"
                : aus + " von " + felder.Length + " zurückgenommen";
        }
    }
}
