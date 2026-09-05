using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Sperrt die Spielsteuerung, solange das Overlay offen ist.
    ///
    /// Das Spiel hat dafür bereits eine Weiche: Player.BlockInputs. Sie wird von Kamera,
    /// Bewegung, Inventar, Schlagen, Essen und Aufheben abgefragt - also von allem, was
    /// stören würde. Nur ist sie eine reine Abfrage ohne Setzer (sie meldet true bei Tod,
    /// Pause, Chat-Eingabe oder Bootsfahrt), deshalb biegen wir ihr Ergebnis um.
    ///
    /// Dazu kommt der Mauszeiger: die Kamera dreht nur, wenn _mouseLocked gesetzt ist.
    /// Das Spiel schaltet das über ToggleMouse() - dieselbe Stelle nutzen wir.</summary>
    internal static class InputLock
    {
        public static bool MenuOpen { get; private set; }

        private static readonly MethodInfo ToggleMouse =
            AccessTools.Method(typeof(global::PlayerCamera), "ToggleMouse");

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(global::Player), "BlockInputs"),
                postfix: new HarmonyMethod(typeof(InputLock), nameof(AfterBlockInputs)));
        }

        /// <summary>Solange das Menü offen ist, gilt für dich dasselbe wie im Pausemenü.</summary>
        private static void AfterBlockInputs(global::Player __instance, ref bool __result)
        {
            if (!MenuOpen) return;
            if (!Ownership.IsMe(__instance)) return;   // Mitspieler bleiben steuerbar

            __result = true;
        }

        public static void SetMenuOpen(bool open)
        {
            MenuOpen = open;
            MausUmschalten(open);
        }

        /// <summary>Jeden Frame nachfassen - das Spiel greift selbst nach dem Mauszeiger,
        /// etwa wenn das Fenster den Fokus zurückbekommt.</summary>
        public static void Tick()
        {
            if (!MenuOpen) return;

            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                MausUmschalten(true);
        }

        private static void MausUmschalten(bool frei)
        {
            var kamera = global::Player.LocalPlayer != null ? global::Player.LocalPlayer.Camera : null;

            if (kamera != null && ToggleMouse != null)
            {
                // Über die Spielfunktion, damit auch dessen eigenes Flag stimmt -
                // sonst dreht die Kamera weiter, obwohl der Zeiger frei ist.
                ToggleMouse.Invoke(kamera, new object[] { frei });
                return;
            }

            // Ohne Kamera (z.B. im Hauptmenü) wenigstens den Zeiger freigeben
            Cursor.lockState = frei ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = frei;
        }
    }
}
