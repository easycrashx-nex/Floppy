using System;
using System.Collections.Generic;

namespace Floppy.HowToFish
{
    /// <summary>Cheats, die dauerhaft nachhelfen müssen. Wird jeden Frame vom Core gerufen.</summary>
    internal static class Loops
    {
        public static bool InfiniteHealth;
        public static bool NoHunger;
        public static bool KeepMoney;
        public static int KeepMoneyAmount;

        // Nicht jeden Frame anfassen - das sind SyncVars, die übers Netz gehen.
        private const int Interval = 15;
        private static int _frame;

        /// <summary>Wendet etwas auf dich an - oder auf alle, wenn der Cheat geteilt wird.</summary>
        private static void ForEachTarget(string optionId, Action<global::PlayerVitals> apply)
        {
            if (!Ownership.Shared(optionId))
            {
                var mine = Game.Vitals;
                if (mine != null) apply(mine);
                return;
            }

            var players = global::PlayerManager.AlivePlayers;
            if (players == null) return;

            foreach (var player in players)
            {
                if (player?.Vitals != null) apply(player.Vitals);
            }
        }

        public static void Tick()
        {
            if (!Game.IsHost) return;

            _frame++;
            if (_frame % Interval != 0) return;

            if (InfiniteHealth)
                ForEachTarget("player.infinitehealth", v => { if (v.Health < 100) v.Heal(100); });

            if (NoHunger)
                ForEachTarget("player.nohunger", v => { if (v.Fullness < 90) v.RestoreFullness(100); });

            if (KeepMoney && Game.CurrentMoney < KeepMoneyAmount)
                Game.SetMoney(KeepMoneyAmount);
        }
    }
}
