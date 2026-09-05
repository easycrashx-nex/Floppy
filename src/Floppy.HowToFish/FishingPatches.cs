using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Das Kernstück des Spiels: wann etwas anbeißt und was.
    ///
    /// Ein Fisch beißt, sobald der Köder länger als RandomizedCatchTime unter Wasser war.
    /// Danach würfelt GetRandomItem() nach Gewichten aus, was es wird - je kleiner das
    /// Gewicht, desto seltener der Fang.</summary>
    internal static class FishingPatches
    {
        public static bool InstantBite;

        /// <summary>0 = Zufall, 1 = seltenster Eintrag, 2 = wertvollster Eintrag.</summary>
        public static int CatchMode;

        /// <summary>Name eines Fangs, der immer kommen soll. Leer = aus.</summary>
        public static string ForcedCatch = "";

        /// <summary>Was an der aktuellen Angelstelle überhaupt beißen kann - für die Auswahlliste.</summary>
        public static string[] KnownCatches = new string[0];

        private static readonly PropertyInfo CatchTimeProperty =
            AccessTools.Property(typeof(global::Bait), "RandomizedCatchTime");

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::CreatureManager), "FindFishForBait"),
                prefix: new HarmonyMethod(typeof(FishingPatches), nameof(BeforeFindFish)));

            harmony.Patch(
                original: AccessTools.Method(typeof(global::CreatureManager), "GetRandomItem"),
                postfix: new HarmonyMethod(typeof(FishingPatches), nameof(AfterGetRandomItem)));
        }

        /// <summary>Merkt sich, ob der Köder, der gerade geprüft wird, meiner ist.
        /// GetRandomItem() bekommt den Köder nicht mit, wird aber direkt aus dieser
        /// Methode heraus gerufen - deshalb reicht ein Merker.</summary>
        private static bool _currentBaitIsMine;

        /// <summary>Wartezeit auf null drücken. Die übrigen Bedingungen (Köder im Wasser,
        /// nichts dran hängend) lassen wir in Ruhe - sonst beißt es an Land.</summary>
        private static void BeforeFindFish(global::Bait bait)
        {
            _currentBaitIsMine = Ownership.Affects(bait, "fish.instant");

            if (!InstantBite || bait == null) return;
            if (!_currentBaitIsMine) return; // Mitspieler warten normal

            CatchTimeProperty?.SetValue(bait, 0f);
        }

        private static void AfterGetRandomItem(ref global::Fishable __result, List<global::ItemInfoWeight> weights)
        {
            if (weights == null || weights.Count == 0) return;

            // An fremden Angeln nichts drehen, solange der Cheat nicht geteilt wird.
            if (!_currentBaitIsMine && !Ownership.Shared("fish.mode")) return;

            var candidates = weights.Where(w => w?.Fishable != null && w.Fishable.ItemToSpawn != null).ToList();
            if (candidates.Count == 0) return;

            // Nebenbei die Liste für die Oberfläche mitführen.
            KnownCatches = candidates
                .Select(w => w.Fishable.ItemToSpawn.name)
                .Distinct()
                .OrderBy(n => n)
                .ToArray();

            if (!string.IsNullOrEmpty(ForcedCatch))
            {
                var forced = candidates.FirstOrDefault(w =>
                    w.Fishable.ItemToSpawn.name.Equals(ForcedCatch, System.StringComparison.OrdinalIgnoreCase));

                if (forced != null)
                {
                    __result = forced.Fishable;
                    return;
                }
            }

            switch (CatchMode)
            {
                case 1: // Seltenster: kleinstes Ziehungsgewicht
                    __result = candidates.OrderBy(w => w.Weight).First().Fishable;
                    break;

                case 2: // Wertvollster: höchster Grundwert
                    __result = candidates.OrderByDescending(w => w.Fishable.ItemToSpawn.DefaultWorth).First().Fishable;
                    break;
            }
        }
    }
}
