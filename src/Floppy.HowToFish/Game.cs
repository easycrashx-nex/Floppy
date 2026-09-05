using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Alle Berührungspunkte mit dem Spielcode an einem Ort. Wenn ein Update
    /// etwas umbenennt, muss nur diese Datei angefasst werden.</summary>
    internal static class Game
    {
        public static bool InGame => global::Player.LocalPlayer != null;

        public static bool IsHost =>
            global::Server.Instance != null && global::Server.Instance.IsServerInitialized;

        public static bool CheatsEnabled => global::ClientSettings.CheatsEnabled;

        public static global::PlayerVitals Vitals =>
            global::Player.LocalPlayer != null ? global::Player.LocalPlayer.Vitals : null;

        // --- Cheatsystem des Spiels ---

        public static void SetCheatsEnabled(bool enabled)
        {
            global::ClientSettings.ToggleCheats(enabled);
            Plugin.Log.LogInfo("Dev-Cheats " + (enabled ? "an" : "aus"));
        }

        /// <summary>Schickt einen der eingebauten Entwicklerbefehle los (ohne Schrägstrich).</summary>
        public static void RunCommand(string command)
        {
            if (!IsHost)
            {
                Plugin.Log.LogWarning("Befehl '" + command + "' ignoriert - du bist nicht Host.");
                return;
            }

            // Das Spiel prüft diese Sperre selbst, also vorher aufschließen.
            if (!CheatsEnabled)
                SetCheatsEnabled(true);

            global::DazedCommands.IsServerCommand("/" + command);
        }

        // --- Spieler ---

        public static void SetGodMode(bool enabled)
        {
            if (global::PlayerManager.InGodMode != enabled)
                global::PlayerManager.ToggleGodMode();
        }

        public static void HealLocalPlayer()
        {
            Apply("player.heal", vitals =>
            {
                vitals.Heal(100);
                vitals.RestoreFullness(100);
            });
        }

        public static void Cleanse()
        {
            Apply("player.cleanse", vitals =>
            {
                vitals._syncedPoison.Value = 0;
                vitals._syncedFire.Value = 0;
            });
        }

        /// <summary>Auf mich - oder auf alle, wenn der Cheat auf "auch für Mitspieler" steht.</summary>
        private static void Apply(string optionId, Action<global::PlayerVitals> action)
        {
            if (!Ownership.Shared(optionId))
            {
                var mine = Vitals;
                if (mine != null) action(mine);
                return;
            }

            var players = global::PlayerManager.AlivePlayers;
            if (players == null) return;

            foreach (var player in players)
            {
                if (player?.Vitals != null) action(player.Vitals);
            }
        }

        // --- Kampf ---

        public static void SetOneShot(bool enabled)
        {
            var settings = global::ServerSettings.Instance;
            if (settings == null) return;
            if (global::ServerSettings.OneShotEnabled != enabled)
                settings.ToggleOneShot();
        }

        public static void SetFriendlyFire(bool enabled)
        {
            var settings = global::ServerSettings.Instance;
            if (settings == null) return;
            settings.ToggleFriendlyFire(enabled);
        }

        public static void SetDifficulty(int index)
        {
            var settings = global::ServerSettings.Instance;
            if (settings == null) return;

            // Reihenfolge wie in der Auswahlliste: Normal, Leicht, Schwer
            var difficulty = index == 1 ? global::Difficulty.Easy
                           : index == 2 ? global::Difficulty.Hard
                           : global::Difficulty.Default;

            settings.SetDifficulty(difficulty);
        }

        // --- Geld ---

        public static int CurrentMoney => global::MoneyManager.Money;

        public static void AddMoney(int amount)
        {
            if (amount <= 0) return;
            global::MoneyManager.AddMoney(amount, global::Player.LocalPlayer);
        }

        public static void RemoveMoney(int amount)
        {
            if (amount <= 0) return;

            // Sonst würde "kostenlos einkaufen" unseren eigenen Abzug schlucken.
            EconomyPatches.AllowMoneyRemoval = true;
            try { global::MoneyManager.RemoveMoney(amount, global::Player.LocalPlayer); }
            finally { EconomyPatches.AllowMoneyRemoval = false; }
        }

        /// <summary>Setzt den Kontostand direkt - das Spiel selbst kann nur addieren und abziehen.</summary>
        public static void SetMoney(int amount)
        {
            var manager = global::MoneyManager.Instance;
            if (manager == null || !manager.IsServerInitialized) return;
            manager._money.Value = Mathf.Max(0, amount);
        }

        // --- Welt ---

        public static void UnlockBoat()
        {
            if (global::BoatManager.Boat != null)
                global::BoatManager.Boat.UnlockBoat();
        }

        public static void UnlockBoatRadar()
        {
            if (global::BoatManager.Boat != null)
                global::BoatManager.Boat.UnlockBoatRadar();
        }

        // --- Spawnen ---

        private static readonly System.Reflection.FieldInfo NameToSpawnable =
            AccessTools.Field(typeof(global::GameInfo), "_nameToSpawnable");

        /// <summary>Die Rubriken der Spawn-Liste. Reihenfolge muss zur Auswahlliste passen.</summary>
        public static readonly string[] SpawnKategorien =
            { "Alle", "Fische und Tiere", "Waffen", "Werkzeuge", "Sonstiges" };

        /// <summary>In welche Rubrik gehoert diese Vorlage?
        ///
        /// Nicht ueber Item.Type - das setzt das Spiel erst in Awake(), und die Vorlagen
        /// in der Spawn-Tabelle sind nie erwacht. Dort steht deshalb bei allen derselbe
        /// Standardwert. Verlaesslich ist nur, welche Bauteile am Objekt haengen.</summary>
        private static int Rubrik(global::Item vorlage)
        {
            if (vorlage == null) return 4;

            if (vorlage.GetComponentInChildren<global::Creature>(true) != null) return 1;
            if (vorlage.GetComponentInChildren<global::Weapon>(true) != null) return 2;

            // Weapon erbt von Tool - deshalb erst nach den Waffen fragen
            if (vorlage.GetComponentInChildren<global::Tool>(true) != null) return 3;

            return 4;
        }

        /// <summary>Die Namen der spawnbaren Dinge, gefiltert nach Rubrik.</summary>
        public static string[] SpawnableNames(int kategorie = 0)
        {
            try
            {
                var table = NameToSpawnable?.GetValue(null) as Dictionary<string, global::Item>;
                if (table == null || table.Count == 0) return new string[0];

                IEnumerable<KeyValuePair<string, global::Item>> auswahl = table;

                if (kategorie >= 1 && kategorie <= 4)
                    auswahl = table.Where(p => Rubrik(p.Value) == kategorie);

                return auswahl
                    .Select(p => p.Key)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Spawn-Liste nicht lesbar: " + ex.Message);
                return new string[0];
            }
        }

        public static void Spawn(string name, bool dead, bool drip)
        {
            if (!IsHost || string.IsNullOrEmpty(name)) return;

            var prefab = global::GameInfo.GetSpawnable(name.Replace(" ", "").ToLower());
            if (prefab == null)
            {
                Plugin.Log.LogWarning("Kein spawnbarer Gegenstand namens '" + name + "'");
                return;
            }

            var camera = global::GameInfo.CurCamera;
            if (camera == null) return;

            // Zwei Meter vor die Kamera, genau wie der eingebaute Spawn-Befehl.
            Vector3 position = camera.transform.position + camera.transform.forward * 2f;

            var item = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);

            if (drip && item.Creature != null)
                item.Creature.SetDrip();

            if (dead && item.Creature != null)
                item.Creature.ServerKillOnSpawn();

            global::Server.Instance.Spawn(item.gameObject);
        }
    }
}
