using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Floppy.HowToFish
{
    /// <summary>Streiche für Mitspieler - und der Grund, warum sie auch als Gast gehen.
    ///
    /// "How to Fish" schickt vieles über Netzwerkbefehle, die mit
    /// [ServerRpc(RequireOwnership = false)] ausgezeichnet sind. Das heißt: Der Server
    /// nimmt sie von jedem Mitspieler entgegen, nicht nur vom Gastgeber - und mehrere
    /// davon nehmen einen fremden Spieler als Ziel entgegen. Wer hostet, spielt hier
    /// also keine Rolle.
    ///
    /// Das ist eine Nachlässigkeit im Spiel, keine Kunst von uns. Wir rufen nur auf, was
    /// ohnehin offensteht.
    ///
    /// Alles hier ist ausdrücklich als "betrifft andere" gekennzeichnet, und es gibt
    /// immer ein Ziel: entweder eine bestimmte Person oder ausdrücklich alle. Was eine
    /// Runde beenden oder Fortschritt vernichten würde, ist nicht dabei.</summary>
    internal static class Mitspieler
    {
        private static global::Server Server => global::Server.Instance;

        /// <summary>Die Mitspieler, ohne dich selbst.</summary>
        public static List<global::Player> Andere
        {
            get
            {
                var raus = new List<global::Player>();

                foreach (var spieler in global::PlayerManager.Players)
                {
                    if (spieler == null) continue;
                    if (spieler == global::Player.LocalPlayer) continue;

                    raus.Add(spieler);
                }

                return raus;
            }
        }

        public static string[] Namen()
        {
            var namen = new List<string> { "Alle" };

            foreach (var spieler in Andere)
                namen.Add(Name(spieler));

            return namen.ToArray();
        }

        private static string Name(global::Player spieler)
        {
            if (spieler == null) return "?";

            string name = spieler.SteamName;
            return string.IsNullOrWhiteSpace(name) ? "Mitspieler" : name;
        }

        /// <summary>Wen der Cheat trifft. Index 0 in der Auswahl heißt "Alle".</summary>
        public static List<global::Player> Ziele(int auswahl)
        {
            var andere = Andere;
            if (andere.Count == 0) return andere;

            if (auswahl <= 0) return andere;

            int index = Mathf.Clamp(auswahl - 1, 0, andere.Count - 1);
            return new List<global::Player> { andere[index] };
        }

        /// <summary>Führt etwas für jedes Ziel aus und meldet zurück, wie oft.</summary>
        public static string FuerJeden(int auswahl, string was, Action<global::Player> tue)
        {
            if (Server == null) return "Keine Verbindung zum Spiel";

            var ziele = Ziele(auswahl);
            if (ziele.Count == 0) return "Du bist allein in der Runde";

            int gemacht = 0;

            foreach (var spieler in ziele)
            {
                if (spieler == null) continue;

                try { tue(spieler); gemacht++; }
                catch (Exception ex) { Floppy.Core.Log.Warning("Streich: " + ex.Message); }
            }

            if (gemacht == 0) return "Ging nicht";
            return gemacht == 1 ? was + ": " + Name(ziele[0]) : was + ": " + gemacht + " Leute";
        }

        // ---------------------------------------------------------------- Schubsen

        /// <summary>Jemanden durch die Luft schleudern - ohne ihm wehzutun.
        ///
        /// Mein erster Versuch lief ueber Server.HitPlayer mit Schaden 0. Das tut
        /// nichts: Die Serverseite steigt bei `damage != 0` sofort wieder aus, ein
        /// Schlag ohne Schaden kommt also gar nicht erst an.
        ///
        /// Der richtige Weg ist PlayerMovement.RPCKnockback. Und der hat eine Eigenart:
        /// Move() baut die waagerechte Geschwindigkeit jeden Physikschritt neu auf und
        /// klemmt sie auf Gehtempo (`ClampMagnitude(_curVel, curMoveSpeed)`), die
        /// senkrechte uebernimmt es dagegen aus der Physik. Seitliche Wucht verpufft
        /// deshalb in einem Tick - genau dafuer benutzt das Spiel sie beim Waffenrueckstoss.
        /// Ein Schubser muss also nach oben gehen, sonst ist es nur ein Stolperer.</summary>
        public static string Schubse(int auswahl, float kraft, bool nurHoch)
        {
            return FuerJeden(auswahl, "Geschleudert", spieler =>
            {
                Vector3 richtung = Vector3.up;

                if (!nurHoch && Ownership.Local != null)
                {
                    Vector3 weg = (spieler.Transform.position - Ownership.Local.Transform.position);
                    weg.y = 0f;

                    // Zwei Drittel nach oben: nur so bleibt vom Stoss etwas uebrig
                    richtung = (weg.normalized * 0.5f + Vector3.up).normalized;
                }

                spieler.Movement.RPCKnockback(spieler.Owner, richtung * kraft);
            });
        }

        /// <summary>Blut, Aufschrei, Trefferwirkung - und keinerlei Schaden.
        ///
        /// ObserverHit macht nur die Wirkung, nicht den Schaden. Mit playerWhoHit = null
        /// bleibt sogar die Schadenszahl aus, weil das Spiel sie daran festmacht.</summary>
        public static string FalscherTreffer(int auswahl, int art)
        {
            var typ = (global::DamageType)Mathf.Clamp(art, 1, 3);

            return FuerJeden(auswahl, "Erschrocken", spieler =>
                spieler.Vitals.ObserverHit(null,
                    spieler.Transform.position + Vector3.up * 1.2f,
                    UnityEngine.Random.onUnitSphere,
                    1, typ));
        }

        public static string Umsetzen(int auswahl, Vector3 ziel)
        {
            return FuerJeden(auswahl, "Umgesetzt", spieler =>
                Server.TeleportPlayer(spieler, ziel, spieler.Transform.eulerAngles.y));
        }

        public static string ZuMirHolen(int auswahl)
        {
            var ich = Ownership.Local;
            if (ich == null) return "Keine Figur";

            // Einen Schritt vor mich, nicht in mich hinein
            Vector3 ziel = ich.Transform.position + ich.Transform.forward * 1.5f + Vector3.up * 0.2f;
            return Umsetzen(auswahl, ziel);
        }

        public static string InDieLuft(int auswahl, float hoehe)
        {
            return FuerJeden(auswahl, "Hochgesetzt", spieler =>
                Server.TeleportPlayer(spieler, spieler.Transform.position + Vector3.up * hoehe,
                                      spieler.Transform.eulerAngles.y));
        }

        // ---------------------------------------------------------------- Sachen

        public static string AllesFallenLassen(int auswahl)
        {
            return FuerJeden(auswahl, "Hat alles fallen lassen", spieler =>
                Server.DropAllItems(spieler, spieler.Transform.position + Vector3.up * 0.5f,
                                    Quaternion.identity));
        }

        public static string Platzwechsel(int auswahl, int platz)
        {
            return FuerJeden(auswahl, "Platz gewechselt", spieler =>
                Server.SelectInvSlot(spieler, platz));
        }

        public static string Ducken(int auswahl, bool geduckt)
        {
            return FuerJeden(auswahl, geduckt ? "Geduckt" : "Aufgerichtet", spieler =>
                Server.UpdatePlayerCrouching(spieler, geduckt));
        }

        public static string Taschen(int auswahl)
        {
            return FuerJeden(auswahl, "Taschen aufgemacht", spieler =>
            {
                for (byte platz = 0; platz < 6; platz++)
                    Server.UnlockPocket(spieler, platz);
            });
        }

        public static string KoederWechseln(int auswahl, int koeder)
        {
            return FuerJeden(auswahl, "Köder gewechselt", spieler =>
                Server.ChangeBait(spieler, (byte)Mathf.Clamp(koeder, 0, 20)));
        }

        // ---------------------------------------------------------------- Zustand

        public static string AlsAbwesend(int auswahl, bool abwesend)
        {
            return FuerJeden(auswahl, abwesend ? "Als abwesend geführt" : "Wieder da", spieler =>
                Server.SetIsAfk(spieler, abwesend));
        }

        public static string Essen(int auswahl, bool isst)
        {
            return FuerJeden(auswahl, isst ? "Isst jetzt" : "Fertig mit Essen", spieler =>
                Server.ToggleEatCreature(spieler, isst));
        }

        public static string AnsSteuer(int auswahl)
        {
            var ziele = Ziele(auswahl);
            if (ziele.Count == 0) return "Du bist allein in der Runde";

            Server.SetDriver(ziele[0]);
            return Name(ziele[0]) + " steuert jetzt das Boot";
        }

        // ---------------------------------------------------------------- Reden

        /// <summary>Etwas in den Rundenchat schreiben.
        ///
        /// Frueher nahm SendChatMessage die Absenderkennung als Parameter entgegen -
        /// damit haette man im Namen eines anderen schreiben koennen. Der Entwickler hat
        /// das inzwischen geschlossen: Der Absender kommt jetzt aus der Verbindung.
        /// Bleibt also nur, im eigenen Namen zu reden.</summary>
        public static string Sagen(string text)
        {
            if (Server == null) return "Keine Verbindung zum Spiel";
            if (string.IsNullOrWhiteSpace(text)) return "Nichts zu sagen";

            Server.SendChatMessage(text);
            return "Gesagt";
        }

        // ---------------------------------------------------------------- Rundherum

        public static string BootLackieren(int farbe)
        {
            if (Server == null) return "Keine Verbindung zum Spiel";

            Server.SetBoatSkin((byte)Mathf.Clamp(farbe, 0, 20));
            return "Boot umlackiert";
        }

        /// <summary>Alle Radios im Umkreis auf dieselbe Frequenz stellen.</summary>
        public static string RadioStellen(float frequenz)
        {
            if (Server == null) return "Keine Verbindung zum Spiel";

            var radios = UnityEngine.Object.FindObjectsOfType<global::Radio>();
            if (radios == null || radios.Length == 0) return "Kein Radio in der Nähe";

            foreach (var radio in radios)
                Server.SetRadioFrequency(radio, frequenz);

            return radios.Length + " Radios umgestellt";
        }
    }
}
