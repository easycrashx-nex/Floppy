using Floppy.Core;
using HarmonyLib;

namespace Floppy.Oddcore
{
    /// <summary>Zwei Dinge, die zusammengehören.
    ///
    /// ODDCORE merkt sich in endlessControl.hasCheated, ob geschummelt wurde. Das lässt
    /// sich zurücksetzen - dann bleibt der Spielstand unmarkiert.
    ///
    /// Nur: dieselbe Markierung ist vermutlich auch das, was verhindert, dass gefälschte
    /// Ergebnisse an Steam gehen. ODDCORE hat echte, öffentliche Bestenlisten, und dort
    /// drückt jede erfundene Punktzahl die Platzierung anderer nach unten. Deshalb fangen
    /// wir den Übertragungsweg gleich mit ab - eine einzige Stelle, PlatformSteam.SubmitScore.
    ///
    /// Beide Schalter stehen voreingestellt auf an.</summary>
    internal static class Stealth
    {
        /// <summary>hasCheated laufend zurücksetzen.</summary>
        public static bool VerbergeCheats = true;

        /// <summary>Punktzahlen gar nicht erst an Steam schicken.</summary>
        public static bool HaltePunkteZurueck = true;

        public static void Apply(Harmony harmony)
        {
            var submit = AccessTools.Method(typeof(global::Oddcore.Platforms.Steam.PlatformSteam),
                                            nameof(global::Oddcore.Platforms.Steam.PlatformSteam.SubmitScore));

            if (submit != null)
            {
                harmony.Patch(submit, prefix: new HarmonyMethod(typeof(Stealth), nameof(BeforeSubmitScore)));
                Log.Info("Bestenlisten-Übertragung liegt unter Kontrolle");
            }
            else
            {
                Log.Warning("SubmitScore nicht gefunden - Punktzahlen könnten ungefiltert an Steam gehen");
            }
        }

        /// <summary>false = das Original läuft gar nicht erst.</summary>
        private static bool BeforeSubmitScore()
        {
            if (!HaltePunkteZurueck) return true;

            Log.Info("Punktzahl zurückgehalten - nichts an die Bestenliste geschickt");
            return false;
        }

        /// <summary>Jeden Frame nachfassen: das Spiel setzt die Markierung selbst,
        /// sobald es etwas Verdächtiges bemerkt.</summary>
        public static void Tick()
        {
            if (!VerbergeCheats) return;

            if (Game.IrgendeineFahneGesetzt()) Game.SenkeAlleFahnen();
        }

        /// <summary>Einmalig zurücksetzen, etwa auf Knopfdruck.</summary>
        public static bool Zuruecksetzen(out string meldung)
        {
            if (Game.Runde == null)
            {
                meldung = "Keine laufende Runde";
                return false;
            }

            int n = Game.SenkeAlleFahnen();
            meldung = n == 0 ? "Es war nichts markiert" : n + " Markierung(en) zurückgesetzt";
            return true;
        }
    }
}
