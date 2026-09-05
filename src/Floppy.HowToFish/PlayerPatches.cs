using HarmonyLib;

namespace Floppy.HowToFish
{
    /// <summary>Gottmodus, der wirklich nur dich schützt.
    ///
    /// Das Spiel bringt zwar einen mit (PlayerManager.InGodMode), der ist aber eine globale
    /// Variable und wird in der Schadensberechnung JEDES Spielers geprüft. Als Host würde
    /// er damit auch deine Mitspieler unverwundbar machen. Deshalb gehen wir hier direkt an
    /// den Schadenseingang und schlucken nur den, der dich selbst trifft.</summary>
    internal static class PlayerPatches
    {
        public static bool GodMode;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::PlayerVitals), "TakeDamage"),
                prefix: new HarmonyMethod(typeof(PlayerPatches), nameof(BeforeTakeDamage)));
        }

        private static bool BeforeTakeDamage(global::PlayerVitals __instance)
        {
            if (!GodMode) return true;

            // Standardmäßig nur meinen eigenen Schaden verschlucken. Steht der Umschalter
            // auf "auch für Mitspieler", ist die ganze Gruppe unverwundbar.
            return !Ownership.Affects(__instance, "player.godmode");
        }
    }
}
