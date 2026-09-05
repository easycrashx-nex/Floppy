using HarmonyLib;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Wie viel Boden ein Schlag wegnimmt.
    ///
    /// Das Spiel hält dafür einen einzigen Wert vor: diggSize in den Grabeinstellungen.
    /// Er ist gleichzeitig die Größe des Abbaus und das Raster, auf das der Treffer
    /// gelegt wird - beides aus derselben Zahl. Deshalb genügt es, sie zu ändern:
    /// Abbau und Raster bleiben von selbst zueinander passend.
    ///
    /// Das Loch ist danach für alle da - Gelände ist gemeinsam. Der Cheat ist deshalb
    /// als "betrifft alle" gekennzeichnet, auch wenn nur deine Schläge größer werden.</summary>
    internal static class Graben
    {
        /// <summary>0 heißt: Finger weg, das Spiel entscheidet.</summary>
        public static float Radius;

        // Der Ausgangswert, um ihn beim Ausschalten zurückgeben zu können
        private static float _original = -1f;

        private static DiggingSettingsSO Einstellungen
        {
            get
            {
                if (DiggingManager.Instance == null) return null;

                return AccessTools.Field(typeof(DiggingManager), "diggingSettingsSO")
                    .GetValue(DiggingManager.Instance) as DiggingSettingsSO;
            }
        }

        public static void Tick()
        {
            var einstellungen = Einstellungen;
            if (einstellungen == null) return;

            // Einmal merken, was das Spiel vorgesehen hat
            if (_original < 0f) _original = einstellungen.diggSize;

            float ziel = Radius > 0f ? Radius : _original;

            if (!Mathf.Approximately(einstellungen.diggSize, ziel))
                einstellungen.diggSize = ziel;
        }

        public static string Text()
        {
            var einstellungen = Einstellungen;
            if (einstellungen == null) return "noch nicht geladen";

            string normal = _original < 0f ? "?" : _original.ToString("0.##");
            return einstellungen.diggSize.ToString("0.##") + "   (normal " + normal + ")";
        }
    }
}
