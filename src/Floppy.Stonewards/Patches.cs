using System;
using HarmonyLib;

namespace Floppy.Stonewards
{
    /// <summary>Eingriffe in den Spielcode.
    ///
    /// Jeder Eingriff prüft, ob er die eigene Figur betrifft. Auf dem Rechner des
    /// Gastgebers laufen dieselben Methoden auch für alle Mitspieler - ohne diese
    /// Prüfung würde ein Cheat für dich stillschweigend für die ganze Runde gelten.</summary>
    internal static class Patches
    {
        public static bool UnbegrenzteAusdauer;
        public static bool UnbegrenztesMana;
        public static bool Unverwundbar;
        public static bool KeineStatuseffekte;

        public static void Apply(Harmony harmony)
        {
            harmony.PatchAll(typeof(AusdauerPatch));
            harmony.PatchAll(typeof(ManaPatch));
            harmony.PatchAll(typeof(SchadenPatch));
            harmony.PatchAll(typeof(StatusPatch));
            harmony.PatchAll(typeof(SchwerkraftPatch));
        }

        /// <summary>Betrifft dieser Aufruf deine eigene Figur?</summary>
        private static bool BinIch(PlayerStats stats)
        {
            var meine = Game.Stats;
            return meine != null && ReferenceEquals(meine, stats);
        }

        [HarmonyPatch(typeof(PlayerStats), "ConsumeStamina")]
        private static class AusdauerPatch
        {
            private static bool Prefix(PlayerStats __instance)
            {
                // false heißt: der Verbrauch findet gar nicht erst statt
                return !(UnbegrenzteAusdauer && BinIch(__instance));
            }
        }

        [HarmonyPatch(typeof(PlayerStats), "ConsumeMana")]
        private static class ManaPatch
        {
            private static bool Prefix(PlayerStats __instance)
            {
                return !(UnbegrenztesMana && BinIch(__instance));
            }
        }

        /// <summary>Schaden abfangen.
        ///
        /// Der Schaden wird beim Gastgeber verrechnet. Hostest du selbst, fangen wir ihn
        /// hier ab - aber nur für deine Figur. Bist du Gast, läuft diese Methode gar
        /// nicht auf deinem Rechner; dann bleibt "Leben halten" der richtige Weg.</summary>
        [HarmonyPatch(typeof(PlayerStats), "UserCode_ServerApplyDamage__DamageResult__WeaponType__NetworkIdentity")]
        private static class SchadenPatch
        {
            private static bool Prefix(PlayerStats __instance)
            {
                return !(Unverwundbar && BinIch(__instance));
            }
        }

        /// <summary>Beim Fliegen den Zug nach unten weglassen.
        ///
        /// ApplyGravity läuft nur auf dem Rechner, dem die Figur gehört - das betrifft
        /// also von sich aus nur dich.</summary>
        [HarmonyPatch(typeof(FirstPersonController), "ApplyGravity")]
        private static class SchwerkraftPatch
        {
            private static bool Prefix(FirstPersonController __instance)
            {
                if (!Flug.Aktiv) return true;
                return !ReferenceEquals(__instance, Game.LocalPlayer);
            }
        }

        /// <summary>Gift, Feuer, Frost und Verlangsamung abwehren.
        ///
        /// Die Effekte werden ebenfalls beim Gastgeber vergeben. Der Effektträger sitzt
        /// am selben Objekt wie die Figur - darüber finden wir heraus, ob es deiner ist.</summary>
        [HarmonyPatch(typeof(StatusEffectHandler), "AddEffect")]
        private static class StatusPatch
        {
            private static bool Prefix(StatusEffectHandler __instance)
            {
                if (!KeineStatuseffekte) return true;

                var meiner = Game.LocalPlayer;
                if (meiner == null) return true;

                var traeger = __instance.GetComponentInParent<FirstPersonController>();
                return !ReferenceEquals(traeger, meiner);
            }
        }
    }
}
