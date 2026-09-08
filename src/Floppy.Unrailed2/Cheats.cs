using System;
using System.Collections.Generic;
using System.Globalization;
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
        private static byte[] _native;
        private static ulong _identity;
        private static string _world;
        private static string _nativeReason = "";
        private static readonly Dictionary<string, (string Id, int Offset)> Fields = new Dictionary<string, (string, int)>
        {
            [DisableTrainCrash] = ("zug.kaputt", 4), [DisableTrainBurning] = ("zug.brennt", 5),
            [DisableTrainRunning] = ("zug.haltan", 3),
            [DisablePhysics] = ("bau.physik", 2), [SkipBeacons] = ("bau.beacons", 9),
            [AutoPlay] = ("auto.an", 8), [StopAutoPlayAfterTracks] = ("auto.stopp", 17),
            [AutoPlayPreferDown] = ("auto.unten", 16), [SyncMapgen] = ("auto.mapgen", 49),
            [MinAutoPlayStagesToBoss] = ("auto.bosse", 52)
        };

        internal static void VergissSnapshot()
        {
            _snapshot = Array.Empty<Feld>();
            _native = null; _identity = 0; _world = null; _nativeReason = "";
        }

        /// <summary>Einmal im Modultakt lesen; Verfügbarkeit und Anzeigen lösen kein HTTP aus.</summary>
        internal static void AktualisiereSnapshot()
        {
            VergissSnapshot();
            if (Welt.ImSpiel && Debugger.Erreichbar)
            {
                _world = Welt.Kennung;
                _snapshot = Welt.Felder(Singleton).Where(f => f.Entitaet >= 0).ToArray();
                // ByteBool is serialized as {} by current NativeAOT builds. Its native
                // input builder carries the actual values, including before the first cheat.
                if (NativeCheats.TryRead(out var values, out _identity, out _nativeReason)) _native = values;
                foreach (var field in Fields)
                {
                    var option = Registry.Find(field.Value.Id);
                    if (option == null) continue;
                    string value = Lies(field.Key);
                    // Refresh displayed state without invoking a write callback.
                    if (option.Kind == OptionKind.Toggle && bool.TryParse(value, out bool flag)) option.BoolValue = flag;
                    if (option.Kind == OptionKind.Slider && double.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double number) && double.IsFinite(number)) option.NumberValue = (float)number;
                }
            }
        }

        private static bool NativeWritable => _native != null && NativeCheats.CanWrite;
        private static bool VerfuegbarFuer(string field) => Welt.ImSpiel && Debugger.Erreichbar &&
            (NativeWritable || _snapshot.Any(f => f.Schluessel == field &&
                (field == MinAutoPlayStagesToBoss ? f.IsNumber : bool.TryParse(f.Wert, out _))));

        public static bool Verfuegbar => Fields.Keys.Any(VerfuegbarFuer);

        public static string Zustand => !Debugger.Erreichbar ? Debugger.Zustand
            : !Welt.ImSpiel ? "Noch keine laufende Partie"
            : Verfuegbar ? Fields.Keys.Count(VerfuegbarFuer) + " von " + Fields.Count + " Entwickleroptionen verfügbar"
                + (Fields.Keys.All(VerfuegbarFuer) ? "" : ". " + _nativeReason)
            : "Runde erkannt; zusätzliche Entwickleroptionen nicht verfügbar. " + _nativeReason;

        // ------------------------------------------------------------------ Schreiben

        private static void Schalte(CheatOption option, string feld, string name)
        {
            string world = _world;
            Welt.Aktualisiere();
            if (!Welt.ImSpiel || world != Welt.Kennung) { option.Fail("Die Runde hat sich geändert; bitte erneut auswählen"); return; }
            if (!Schutz.Erlaubt(out string grund)) { option.Fail(grund); return; }
            if (!VerfuegbarFuer(feld)) { option.Fail(Zustand); return; }

            if (NativeWritable)
            {
                if (!NativeCheats.TrySet(Fields[feld].Offset, new[] { option.BoolValue ? (byte)255 : (byte)0 }, _identity, out grund))
                { option.Fail(grund); return; }
                option.Message = name + (option.BoolValue ? ": an" : ": aus");
                AktualisiereSnapshot();
                return;
            }

            long eid = Traeger();
            if (eid < 0) { option.Fail("Cheat-Zustand nicht mehr verfügbar"); return; }

            bool an = option.BoolValue;
            if (!Welt.Setze(eid, Singleton, feld, an ? "true" : "false", leseVersuche: 4))
            {
                option.Fail("Änderung nicht bestätigt; der Spielzustand ist unklar");
                return;
            }
            option.Message = name + (an ? ": an" : ": aus");
        }

        private static void SetzeZahl(CheatOption option, string feld, string name)
        {
            string world = _world;
            Welt.Aktualisiere();
            if (!Welt.ImSpiel || world != Welt.Kennung) { option.Fail("Die Runde hat sich geändert; bitte erneut auswählen"); return; }
            if (!Schutz.Erlaubt(out string grund)) { option.Fail(grund); return; }
            if (!VerfuegbarFuer(feld)) { option.Fail(Zustand); return; }

            if (NativeWritable)
            {
                double value = option.NumberValue;
                if (!double.IsFinite(value) || value < 0 || value > 30 || value != Math.Truncate(value))
                { option.Fail("Bitte eine ganze Zahl zwischen 0 und 30 wählen"); return; }
                if (!NativeCheats.TrySet(Fields[feld].Offset, BitConverter.GetBytes((int)value), _identity, out grund))
                { option.Fail(grund); return; }
                option.Message = name + ": " + value;
                AktualisiereSnapshot();
                return;
            }

            long eid = Traeger();
            if (eid < 0) { option.Fail("Cheat-Zustand nicht mehr verfügbar"); return; }

            double wert = option.NumberValue;
            if (!Welt.Setze(eid, Singleton, feld, wert.ToString("R", CultureInfo.InvariantCulture), leseVersuche: 4))
            {
                option.Fail("Änderung nicht bestätigt; der Spielzustand ist unklar");
                return;
            }
            option.Message = name + ": " + wert;
        }

        private static string Lies(string feld)
        {
            if (_native != null && (NativeWritable || feld != MinAutoPlayStagesToBoss) && Fields.TryGetValue(feld, out var field))
                return field.Offset == 52 ? BitConverter.ToInt32(_native, 52).ToString(CultureInfo.InvariantCulture)
                    : _native[field.Offset] != 0 ? "true" : "false";
            string value = _snapshot.FirstOrDefault(f => f.Schluessel == feld)?.Wert;
            return value != null && (feld == MinAutoPlayStagesToBoss || bool.TryParse(value, out _)) ? value : "-";
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
                IsAvailable = () => VerfuegbarFuer(DisableTrainCrash),
                OnChanged = o => Schalte(o, DisableTrainCrash, "Unkaputtbar")
            });

            k.Add(new CheatOption
            {
Id = "zug.brennt",
                Label = "Zug brennt nicht",
                Description = "Kein Überhitzen mehr - der Wassertank ist damit nebensächlich.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(DisableTrainBurning),
                OnChanged = o => Schalte(o, DisableTrainBurning, "Feuerfest")
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
                IsAvailable = () => VerfuegbarFuer(DisableTrainRunning),
                OnChanged = o => Schalte(o, DisableTrainRunning, "Angehalten")
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
Id = "bau.physik",
                Label = "Keine Hindernisse",
                Description = "Schaltet die Kollisionsberechnung ab. Sieht wild aus und kann " +
                              "auch mal etwas verhaken - deshalb eher zum Ausprobieren als " +
                              "für eine ernste Runde.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(DisablePhysics),
                OnChanged = o => Schalte(o, DisablePhysics, "Physik")
            });

            k.Add(new CheatOption
            {
Id = "bau.beacons",
                Label = "Stationen überspringen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(SkipBeacons),
                OnChanged = o => Schalte(o, SkipBeacons, "Stationen überspringen")
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
                IsAvailable = () => VerfuegbarFuer(AutoPlay),
                OnChanged = o => Schalte(o, AutoPlay, "Automatik")
            });

            k.Add(new CheatOption
            {
Id = "auto.stopp",
                Label = "Nach dem Anschließen anhalten",
                Description = "Die Automatik hört auf, sobald die Gleise verbunden sind.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(StopAutoPlayAfterTracks),
                OnChanged = o => Schalte(o, StopAutoPlayAfterTracks, "Anhalten")
            });

            k.Add(new CheatOption
            {
Id = "auto.unten",
                Label = "Lieber nach unten bauen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(AutoPlayPreferDown),
                OnChanged = o => Schalte(o, AutoPlayPreferDown, "Richtung")
            });

            k.Add(new CheatOption
            {
Id = "auto.bosse",
                Label = "Abschnitte bis zum Boss",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(MinAutoPlayStagesToBoss),
                Min = 0f, Max = 30f, Step = 1f,
                NumberValue = 0f,
                Ruhewert = 0f,
                OnChanged = o => SetzeZahl(o, MinAutoPlayStagesToBoss, "Abschnitte")
            });

            k.Add(new CheatOption
            {
Id = "auto.mapgen",
                Label = "Kartenerzeugung gleichschalten",
                Description = "Ein Entwicklerschalter für die Kartenerzeugung. Nur anfassen, " +
                              "wenn beim Erzeugen etwas klemmt.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = () => VerfuegbarFuer(SyncMapgen),
                OnChanged = o => Schalte(o, SyncMapgen, "Gleichschaltung")
            });

            return k;
        }

        /// <summary>Alles auf einmal aus - fuer den schnellen Rueckweg zum normalen Spiel.</summary>
        public static bool AllesAus(out string message)
        {
            AktualisiereSnapshot();
            if (!Schutz.Erlaubt(out message)) return false;
            int confirmed = 0;
            foreach (var field in Fields.Where(f => f.Value.Offset != 52))
            {
                if (Lies(field.Key) == "false") { confirmed++; continue; }
                var change = new CheatOption { Kind = OptionKind.Toggle, BoolValue = false };
                Schalte(change, field.Key, field.Value.Id);
                if (!change.MessageIsError) confirmed++;
            }
            AktualisiereSnapshot();
            int total = Fields.Count(f => f.Value.Offset != 52);
            message = confirmed == total ? "Alle Cheats zurückgenommen" : confirmed + " von " + total + " zurückgenommen";
            return confirmed == total;
        }
    }
}
