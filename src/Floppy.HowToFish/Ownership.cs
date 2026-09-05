using System.Reflection;
using Floppy.Core;
using Floppy.Core.Api;
using HarmonyLib;

namespace Floppy.HowToFish
{
    /// <summary>Gehört das hier mir?
    ///
    /// Als Host läuft die Spiellogik für ALLE Mitspieler in deinem Prozess. Ein Patch an
    /// einer Server-Methode trifft deshalb ohne weiteres Zutun auch deine Freunde. Jeder
    /// Cheat, der sich sinnvoll auf eine Person eingrenzen lässt, fragt hier nach.</summary>
    internal static class Ownership
    {
        private static readonly FieldInfo VitalsPlayerField =
            AccessTools.Field(typeof(global::PlayerVitals), "_player");

        public static global::Player Local => global::Player.LocalPlayer;

        /// <summary>Steht der Umschalter "auch für Mitspieler" bei diesem Cheat auf an?</summary>
        public static bool Shared(string optionId)
        {
            var option = Registry.Find(optionId);
            return option != null
                && option.Scope == CheatScope.Selectable
                && option.ShareWithOthers;
        }

        /// <summary>Die Frage, die jeder Patch stellt: soll ich hier eingreifen?
        /// Entweder weil es meins ist - oder weil der Cheat für alle gelten soll.</summary>
        public static bool Affects(global::PlayerVitals vitals, string optionId)
        {
            return Shared(optionId) || IsMine(vitals);
        }

        public static bool Affects(global::Item item, string optionId)
        {
            return Shared(optionId) || IsMine(item);
        }

        public static bool Affects(global::Bait bait, string optionId)
        {
            return Shared(optionId) || IsMine(bait);
        }

        public static bool Affects(global::Player player, string optionId)
        {
            return Shared(optionId) || IsMe(player);
        }

        public static bool IsMe(global::Player player)
        {
            return player != null && Local != null && player == Local;
        }

        /// <summary>Zu welchem Spieler gehören diese Lebenswerte?</summary>
        public static bool IsMine(global::PlayerVitals vitals)
        {
            if (vitals == null) return false;
            return IsMe(VitalsPlayerField?.GetValue(vitals) as global::Player);
        }

        /// <summary>Hält oder hielt ich diesen Gegenstand zuletzt? Beim Grillen liegt der
        /// Fisch nicht in der Hand, deshalb zählt auch der letzte Träger.</summary>
        public static bool IsMine(global::Item item)
        {
            if (item == null) return false;
            return IsMe(item.Holder) || IsMe(item.SyncedHolder) || IsMe(item.LastHolder);
        }

        /// <summary>Hängt dieser Köder an meiner Angel?</summary>
        public static bool IsMine(global::Bait bait)
        {
            var rod = bait?.FishingRod;
            return rod != null && IsMine(rod);
        }
    }
}
