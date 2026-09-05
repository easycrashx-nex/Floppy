using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Fliegen.
    ///
    /// Das Spiel bewegt die Figur über einen CharacterController und zieht sie jeden
    /// Frame nach unten. Wir schalten nur diesen Zug ab (siehe Patches) - Laufen,
    /// Springen und Kollision bleiben, wie sie sind. Hoch und runter kommt hier dazu.
    ///
    /// Läuft ausschließlich bei dir: Deine Position schickt das Spiel ohnehin selbst
    /// ans Netz, für die anderen sieht es aus, als würdest du schweben.</summary>
    internal static class Flug
    {
        public static bool Aktiv;
        public static float Tempo = 8f;

        public static KeyCode Hoch = KeyCode.Space;
        public static KeyCode Runter = KeyCode.LeftControl;

        public static void Tick()
        {
            if (!Aktiv) return;

            var spieler = Game.LocalPlayer;
            if (spieler == null) return;

            var controller = spieler.Controller;
            if (controller == null || !controller.enabled) return;

            float richtung = 0f;
            if (Input.GetKey(Hoch)) richtung += 1f;
            if (Input.GetKey(Runter)) richtung -= 1f;

            if (richtung == 0f) return;

            controller.Move(Vector3.up * richtung * Tempo * Time.deltaTime);
        }
    }
}
