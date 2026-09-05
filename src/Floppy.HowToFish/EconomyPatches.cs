using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Geld, Kaufen, Kochen und Fischgewicht.
    ///
    /// Der Verkaufswert eines Gegenstands ist Grundwert x Gewicht x Garkurve x Wetteinsatz x
    /// Killscore. An drei dieser Faktoren kommen wir sauber heran.</summary>
    internal static class EconomyPatches
    {
        public static bool FreeShopping;
        public static float WorthMultiplier = 1f;
        public static float CookSpeedMultiplier = 1f;

        /// <summary>Alles auf dem Grill rastet sofort auf die wertvollste Garstufe ein
        /// und bleibt dort - anbrennen ist damit unmöglich.</summary>
        public static bool AlwaysPerfectCookness;

        /// <summary>Setzt gehaltene Gegenstände laufend auf Bestwerte.</summary>
        public static bool AutoPerfectItems;

        /// <summary>Muss gesetzt sein, damit unsere eigenen Geld-Cheats trotz
        /// "kostenlos einkaufen" noch Geld abziehen können.</summary>
        public static bool AllowMoneyRemoval;

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::MoneyManager), "CanAfford"),
                prefix: new HarmonyMethod(typeof(EconomyPatches), nameof(BeforeCanAfford)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::MoneyManager), "RemoveMoney"),
                prefix: new HarmonyMethod(typeof(EconomyPatches), nameof(BeforeRemoveMoney)));

            harmony.Patch(
                original: AccessTools.PropertyGetter(typeof(global::Item), "TotalWorth"),
                postfix: new HarmonyMethod(typeof(EconomyPatches), nameof(AfterTotalWorth)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::Item), "CookItem"),
                prefix: new HarmonyMethod(typeof(EconomyPatches), nameof(BeforeCookItem)),
                postfix: new HarmonyMethod(typeof(EconomyPatches), nameof(AfterCookItem)));
        }

        /// <summary>Alle Kaufvorgänge im Spiel fragen hier nach. Ein true genügt.</summary>
        private static bool BeforeCanAfford(ref bool __result)
        {
            if (!FreeShopping) return true;
            __result = true;
            return false; // Original überspringen
        }

        private static bool BeforeRemoveMoney()
        {
            // Beim kostenlosen Einkaufen den Abzug schlucken - außer wir ziehen selbst ab.
            if (FreeShopping && !AllowMoneyRemoval) return false;
            return true;
        }

        private static void AfterTotalWorth(global::Item __instance, ref int __result)
        {
            if (Mathf.Approximately(WorthMultiplier, 1f)) return;
            if (!Ownership.Affects(__instance, "money.worth")) return;
            __result = Mathf.RoundToInt(__result * WorthMultiplier);
        }

        private static void BeforeCookItem(global::Item __instance, ref float amount)
        {
            if (!Ownership.Affects(__instance, "fish.cookspeed")) return;
            amount *= Mathf.Max(0f, CookSpeedMultiplier);
        }

        /// <summary>Nach jedem Grill-Tick zurück auf den Bestwert ziehen. Das Spiel bewegt
        /// die Garstufe immer nur nach oben, deshalb reicht es, sie hier festzuhalten.</summary>
        private static void AfterCookItem(global::Item __instance)
        {
            if (!AlwaysPerfectCookness || __instance == null) return;
            if (!__instance.IsServerInitialized) return;
            if (!Ownership.Affects(__instance, "fish.perfectcook")) return;

            float best = BestCookness();
            if (!Mathf.Approximately(__instance._cookness.Value, best))
                __instance._cookness.Value = best;
        }

        // --- Gegenstände aufwerten ---

        /// <summary>Die Garstufe mit dem höchsten Wert. Die Kurve stammt aus dem Spiel,
        /// deshalb tasten wir sie ab statt einen festen Wert zu raten.</summary>
        public static float BestCookness()
        {
            var curve = global::GameInfo.CooknessWorthCurve;
            if (curve == null) return 0.5f;

            float best = 0f;
            float bestValue = float.MinValue;

            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;
                float value = curve.Evaluate(t);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>Setzt einen Gegenstand auf Bestgewicht und perfekte Garung.</summary>
        public static void Perfect(global::Item item, float weight)
        {
            if (item == null || !item.IsServerInitialized) return;
            item._syncedRandomWeight.Value = Mathf.Max(0.01f, weight);
            item._cookness.Value = BestCookness();
        }

        public static bool PerfectHeldItem(float weight, out string message)
        {
            var item = HeldItem();
            if (item == null)
            {
                message = "Nichts in der Hand";
                return false;
            }

            Perfect(item, weight);
            message = item.name + " aufgewertet";
            return true;
        }

        public static global::Item HeldItem()
        {
            var player = global::Player.LocalPlayer;
            return player?.Holding?.HeldItem;
        }
    }
}
