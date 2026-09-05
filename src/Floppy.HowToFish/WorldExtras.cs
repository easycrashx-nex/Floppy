using System.Linq;
using System.Reflection;
using Floppy.Core;
using HarmonyLib;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Casino, Bewegung und Teleport - Sachen, die keine eigene Patchdatei brauchen.</summary>
    internal static class WorldExtras
    {
        // --- Bewegung ---

        public static bool FlyEnabled;
        public static float FlySpeed = 12f;
        public static float TimeScale = 1f;

        public static void ApplyTimeScale()
        {
            Time.timeScale = Mathf.Clamp(TimeScale, 0.1f, 5f);
        }

        public static void ResetTimeScale()
        {
            Time.timeScale = 1f;
        }

        /// <summary>Freies Fliegen. Wird jeden Frame gerufen, solange der Schalter an ist.
        /// Wir setzen die Position direkt - das Spiel rechnet weiter seine Schwerkraft,
        /// wird aber jeden Frame überstimmt.</summary>
        public static void TickFly()
        {
            if (!FlyEnabled) return;

            var player = global::Player.LocalPlayer;
            if (player == null || player.BlockInputs) return;

            var camera = global::GameInfo.CurCamera;
            if (camera == null) return;

            Vector3 forward = camera.transform.forward;
            Vector3 right = camera.transform.right;
            Vector3 move = Vector3.zero;

            if (Input.GetKey(KeyCode.W)) move += forward;
            if (Input.GetKey(KeyCode.S)) move -= forward;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (Input.GetKey(KeyCode.Space)) move += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) move -= Vector3.up;

            if (move == Vector3.zero) return;

            float speed = FlySpeed * (Input.GetKey(KeyCode.LeftShift) ? 2.5f : 1f);
            player.Transform.position += move.normalized * speed * Time.deltaTime;
        }

        // --- Teleport ---

        public static string[] OtherPlayerNames()
        {
            var local = global::Player.LocalPlayer;
            if (global::PlayerManager.AlivePlayers == null) return new string[0];

            return global::PlayerManager.AlivePlayers
                .Where(p => p != null && p != local)
                .Select(PlayerName)
                .ToArray();
        }

        public static bool TeleportToPlayer(string name, out string message)
        {
            var local = global::Player.LocalPlayer;
            if (local == null)
            {
                message = "Du bist nicht im Spiel";
                return false;
            }

            var target = global::PlayerManager.AlivePlayers?
                .FirstOrDefault(p => p != null && p != local && PlayerName(p) == name);

            if (target == null)
            {
                message = "Mitspieler nicht gefunden";
                return false;
            }

            // Etwas Abstand, damit ihr nicht ineinander steckt.
            local.Transform.position = target.Transform.position + Vector3.up * 1.5f;
            message = "Zu " + name + " teleportiert";
            return true;
        }

        private static string PlayerName(global::Player player)
        {
            string name = player.Owner?.ClientId.ToString() ?? "?";
            return "Spieler " + name;
        }

        /// <summary>Holt das Boot zu dir statt dich zum Boot.</summary>
        public static bool CallBoat(out string message)
        {
            var boat = global::BoatManager.Boat;
            var player = global::Player.LocalPlayer;

            if (boat == null || player == null)
            {
                message = "Kein Boot da";
                return false;
            }

            Vector3 forward = player.Transform.forward;
            boat.transform.position = player.Transform.position + forward * 6f + Vector3.up * 1f;
            message = "Boot geholt";
            return true;
        }

        // --- Casino ---

        private static readonly FieldInfo CurBetColorField =
            AccessTools.Field(typeof(global::CasinoManager), "_curBetColor");

        /// <summary>Lässt die Roulette-Runde auf der Farbe enden, auf die gesetzt wurde.</summary>
        public static bool WinRoulette(out string message)
        {
            var casino = global::CasinoManager.Instance;
            if (casino == null || !casino.IsServerInitialized)
            {
                message = "Kein Casino in der Nähe";
                return false;
            }

            if (!global::CasinoManager.HasPlacedBet)
            {
                message = "Es liegt kein Einsatz auf dem Tisch";
                return false;
            }

            if (CurBetColorField?.GetValue(casino) is not global::BetColor color)
            {
                message = "Einsatzfarbe nicht lesbar";
                return false;
            }

            casino.ServerRouletteResult(color);
            message = "Roulette auf " + color + " gedreht";
            return true;
        }
    }
}
