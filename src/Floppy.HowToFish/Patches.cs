using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Eingriffe, die sich nicht über die normalen Spielfunktionen lösen lassen.</summary>
    internal static class Patches
    {
        /// <summary>1 = unverändertes Lauftempo.</summary>
        public static float SpeedMultiplier = 1f;

        private static readonly FieldInfo CurMoveSpeed =
            AccessTools.Field(typeof(global::PlayerMovement), "_curMoveSpeed");

        public static void Apply(Harmony harmony)
        {
            harmony.Patch(
                original: AccessTools.Method(typeof(global::PlayerMovement), "UpdateMoveSpeed"),
                postfix: new HarmonyMethod(typeof(Patches), nameof(AfterUpdateMoveSpeed)));
        }

        /// <summary>Das Spiel rechnet sein Tempo selbst aus - wir skalieren das Ergebnis danach.</summary>
        private static void AfterUpdateMoveSpeed(global::PlayerMovement __instance)
        {
            if (Mathf.Approximately(SpeedMultiplier, 1f)) return;

            // Nur den eigenen Charakter beschleunigen, nicht die Mitspieler.
            var local = global::Player.LocalPlayer;
            if (local == null || local.Movement != __instance) return;

            float current = (float)CurMoveSpeed.GetValue(__instance);
            CurMoveSpeed.SetValue(__instance, current * SpeedMultiplier);
        }
    }
}
