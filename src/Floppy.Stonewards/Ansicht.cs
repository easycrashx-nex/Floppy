using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Wie das Spiel bei dir aussieht: Sichtfeld, Nebel, Helligkeit.
    ///
    /// Rein örtlich. Das sind Einstellungen der Darstellung auf deinem Rechner - am
    /// Spielgeschehen ändert sich nichts, und kein Mitspieler sieht davon etwas.</summary>
    internal static class Ansicht
    {
        public static float Sichtfeld;        // 0 = nicht anfassen
        public static bool NebelAus;
        public static float Helligkeit;       // 0 = nicht anfassen

        // Was das Spiel eingestellt hatte, damit wir es zurückgeben können
        private static bool _gemerkt;
        private static bool _nebelVorher;
        private static float _helligkeitVorher;

        private static void Merken()
        {
            if (_gemerkt) return;
            _gemerkt = true;

            _nebelVorher = RenderSettings.fog;
            _helligkeitVorher = RenderSettings.ambientIntensity;
        }

        public static void Tick()
        {
            Merken();

            // Das Sichtfeld setzt das Spiel selbst immer wieder - deshalb jeden Takt neu.
            if (Sichtfeld > 0f)
            {
                var kamera = Camera.main;
                if (kamera != null && !Mathf.Approximately(kamera.fieldOfView, Sichtfeld))
                    kamera.fieldOfView = Sichtfeld;
            }

            RenderSettings.fog = NebelAus ? false : _nebelVorher;

            RenderSettings.ambientIntensity =
                Helligkeit > 0f ? Helligkeit : _helligkeitVorher;
        }

        public static string Text()
        {
            var kamera = Camera.main;
            return string.Format("Sichtfeld {0:0}   |   Nebel {1}   |   Helligkeit {2:0.##}",
                kamera == null ? 0f : kamera.fieldOfView,
                RenderSettings.fog ? "an" : "aus",
                RenderSettings.ambientIntensity);
        }
    }
}
