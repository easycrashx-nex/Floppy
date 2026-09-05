using System.Collections.Generic;
using Floppy.Core;
using UnityEngine;

namespace Floppy.Oddcore
{
    /// <summary>Cheats, die dauerhaft nachhelfen müssen - und die Nachführung der Anzeige.
    ///
    /// Vieles in ODDCORE hängt an Komponenten, die beim Szenen- oder Wellenwechsel neu
    /// entstehen. Einmal setzen reicht deshalb nicht.</summary>
    internal static class Loops
    {
        public static bool Unverwundbar;
        public static bool EnergieHalten;
        public static bool GeldHalten;
        public static bool ZeitAnhalten;

        public static float Tempo;
        public static float Feuerrate;
        public static float SchadenMulti = 1f;
        public static float BossSchaden;
        public static int Spruenge;

        // Die Suche nach Komponenten in der Szene kostet Zeit - nicht jeden Frame
        private const int Abstand = 20;
        private static int _frame;

        /// <summary>Wann der Nutzer einen Regler zuletzt angefasst hat.
        ///
        /// Ohne das würde die Nachführung gegen die Eingabe kämpfen: man zieht den Regler,
        /// und im nächsten Moment springt er auf den Spielwert zurück.</summary>
        private static readonly Dictionary<string, float> _zuletztBenutzt =
            new Dictionary<string, float>();

        public static void Benutzt(string id)
        {
            _zuletztBenutzt[id] = Time.unscaledTime;
        }

        /// <summary>Holt die echten Spielwerte in die Oberfläche - aber nur dort, wo
        /// gerade niemand am Regler dreht.</summary>
        private static void Nachfuehren()
        {
            foreach (var option in Registry.AllOptions)
            {
                // Anzeigezeilen laufen immer mit - da dreht niemand dran
                string info = Game.LiesInfo(option.Id);
                if (info != null)
                {
                    option.TextValue = info;
                    continue;
                }

                float wert = Game.LiesAnzeige(option.Id);
                if (float.IsNaN(wert)) continue;

                float zuletzt;
                if (_zuletztBenutzt.TryGetValue(option.Id, out zuletzt) &&
                    Time.unscaledTime - zuletzt < 2f)
                    continue;

                if (!Mathf.Approximately(option.NumberValue, wert))
                    option.NumberValue = wert;
            }
        }

        public static void Tick()
        {
            if (!Game.ImSpiel) return;

            _frame++;
            if (_frame % Abstand != 0) return;

            Nachfuehren();

            if (Unverwundbar) Game.SetzeUnverwundbar(true);
            if (EnergieHalten) Game.FuelleEnergie();
            if (ZeitAnhalten) Game.SetzeRundeSchalter("pauseTimer", true);

            if (SchadenMulti > 1f || BossSchaden > 0f)
                Game.SetzeGegnerSchaden(SchadenMulti, BossSchaden);

            if (Tempo > 0f)
            {
                Game.SetzeBewegung("MoveSpeed", Tempo);
                Game.SetzeBewegung("baseMoveSpeed", Tempo);
            }

            if (Feuerrate > 0f) Game.SetzeFeuerrate(Feuerrate);
            if (Spruenge > 1) Game.SetzeSpruenge(Spruenge);

            if (GeldHalten)
            {
                var o = Registry.Find("money.amount");
                int ziel = o == null ? 0 : (int)o.NumberValue;
                if (Game.Geld < ziel) Game.SetzeGeld(ziel);
            }
        }
    }
}
