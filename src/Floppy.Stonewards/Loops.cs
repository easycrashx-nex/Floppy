using Floppy.Core;
using Floppy.Core.Api;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Was dauerhaft nachgehalten werden muss.
    ///
    /// Zwei Dinge laufen sonst weg: Die Werte deiner Figur baut das Spiel bei jedem
    /// Klassenwechsel neu auf, und Leben lässt sich nicht einfrieren, sondern nur
    /// nachfüllen.</summary>
    internal static class Loops
    {
        public static bool LebenHalten;
        public static float LebenSchwelle = 90f;   // in Prozent

        public static bool AusdauerHalten;
        public static bool ManaHalten;

        // Nicht jeden Frame: die Suche nach Komponenten und das Nachtragen der
        // Zuschläge kosten Zeit, und nötig ist es auch nicht.
        private const int Abstand = 15;
        private static int _frame;

        /// <summary>Damit wir nicht bei jedem Tick einen Befehl ans Netz schicken.</summary>
        private static float _letzteHeilung;

        public static void Tick()
        {
            if (!Game.ImSpiel) return;

            _frame++;
            if (_frame % Abstand != 0) return;

            Werte.AlleAnwenden();
            FrischeAnzeigen();

            Welt.Tick();
            Ansicht.Tick();
            Graben.Tick();

            if (AusdauerHalten) Game.FuelleAusdauer();
            if (ManaHalten) Game.FuelleMana();

            if (LebenHalten && !Game.IstTot)
                HalteLeben();
        }

        /// <summary>Die Anzeigezeilen nachziehen.
        ///
        /// Anzeigen haben nichts zum Anklicken - ihr Text entsteht in derselben Funktion,
        /// die sonst auf eine Änderung reagiert. Also rufen wir sie hier einfach auf.</summary>
        private static void FrischeAnzeigen()
        {
            foreach (var option in Registry.AllOptions)
                if (option.Kind == OptionKind.Info && option.OnChanged != null)
                    option.OnChanged(option);
        }


        private static void HalteLeben()
        {
            float max = Game.MaxLeben;
            if (max <= 0f) return;

            float anteil = Game.Leben / max * 100f;
            if (anteil >= LebenSchwelle) return;

            // Höchstens dreimal pro Sekunde - der Server soll nicht zugeschüttet werden
            if (Time.unscaledTime - _letzteHeilung < 0.33f) return;
            _letzteHeilung = Time.unscaledTime;

            Game.AendereLeben(max - Game.Leben);
        }
    }
}
