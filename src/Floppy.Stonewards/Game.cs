using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Alle Berührungspunkte mit dem Spielcode an einer Stelle.
    ///
    /// Stonewards ist ein Mirror-Spiel: einer hostet, die anderen sind Gäste. Was wo
    /// gilt, entscheidet sich daran, wo der Wert liegt:
    ///
    ///   - Die Werte deiner Figur (CharacterStat) liegen auf deinem Rechner. Ändern
    ///     wir sie, betrifft das nur dich - Schaden rechnet der Angreifer selbst aus.
    ///   - Leben liegt beim Server. Es gibt aber Befehle ohne Rechteprüfung
    ///     (requiresAuthority = false), die auch ein Gast schicken darf.
    ///   - Alles, was am Server hängt (Wellen, König), geht nur, wenn du selbst hostest.</summary>
    internal static class Game
    {
        // ---------------------------------------------------------------- Grundlagen

        public static GameManager Manager => GameManager.Instance;

        public static FirstPersonController LocalPlayer
        {
            get
            {
                var manager = Manager;
                return manager == null ? null : manager.LocalPlayer;
            }
        }

        public static PlayerStats Stats
        {
            get
            {
                var player = LocalPlayer;
                return player == null ? null : player.PlayerStats;
            }
        }

        public static bool ImSpiel => Stats != null;

        /// <summary>Hostest du selbst? Nur dann greifen die serverseitigen Sachen.</summary>
        public static bool IstHost => NetworkServer.active;

        public static string Rolle
        {
            get
            {
                if (!ImSpiel) return "Warte auf Spielstart";
                return IstHost ? "Bereit (du hostest)" : "Bereit (du bist Gast)";
            }
        }

        // ---------------------------------------------------------------- Leben

        public static float Leben => Stats == null ? 0f : Stats.GetCurrentHealth();
        public static float MaxLeben => Stats == null ? 0f : Stats.MaxHealth.Value;
        public static bool IstTot => Stats != null && Stats.isDead;

        /// <summary>Leben um einen Betrag ändern.
        ///
        /// CmdUpdateHealth ist ein Befehl ohne Rechteprüfung - er funktioniert deshalb
        /// auch, wenn du nur Gast bist, und wirkt ausschließlich auf deine eigene Figur.</summary>
        public static void AendereLeben(float betrag)
        {
            var stats = Stats;
            if (stats == null || betrag == 0f) return;
            stats.CmdUpdateHealth(betrag);
        }

        public static void VollHeilen()
        {
            var stats = Stats;
            if (stats == null) return;
            AendereLeben(stats.MaxHealth.Value - stats.GetCurrentHealth());
        }

        // ---------------------------------------------------------------- Ausdauer und Mana

        // Beide liegen als private Felder in PlayerStats. Lesen geht nur so.
        private static readonly AccessTools.FieldRef<PlayerStats, float> _ausdauer =
            AccessTools.FieldRefAccess<PlayerStats, float>("currentStamina");

        private static readonly AccessTools.FieldRef<PlayerStats, float> _mana =
            AccessTools.FieldRefAccess<PlayerStats, float>("currentMana");

        public static float Ausdauer => Stats == null ? 0f : _ausdauer(Stats);
        public static float MaxAusdauer => Stats == null ? 0f : Stats.MaxStamina.Value;
        public static float Mana => Stats == null ? 0f : _mana(Stats);
        public static float MaxMana => Stats == null ? 0f : Stats.MaxMana.Value;

        public static void FuelleAusdauer()
        {
            var stats = Stats;
            if (stats != null) stats.RestoreStamina(stats.MaxStamina.Value);
        }

        public static void FuelleMana()
        {
            var stats = Stats;
            if (stats != null) stats.RestoreMana(stats.MaxMana.Value);
        }

        // ---------------------------------------------------------------- Währung

        public static int MetaWaehrung =>
            ResourceManager.Instance == null ? 0 : ResourceManager.Instance.GetCurrentMetaCurrency();

        public static void GibWaehrung(int betrag)
        {
            if (ResourceManager.Instance == null || betrag == 0) return;

            if (betrag > 0) ResourceManager.Instance.AddMetaCurrency(betrag);
            else ResourceManager.Instance.RemoveMetaCurrency(-betrag);
        }

        // ---------------------------------------------------------------- Welle

        public static NetworkHelper Wellen => NetworkHelper.Instance;

        public static string WellenText()
        {
            var helfer = Wellen;
            if (helfer == null) return "keine Runde";

            return string.Format("Welle {0}   |   {1} von {2} erledigt   |   noch {3:0}s",
                helfer.currentWaveIndex + 1,
                helfer.NetworksyncEnemiesKilled,
                helfer.NetworksyncTotalEnemyInWave,
                Mathf.Max(0f, helfer.syncCurrentWaveCountdown));
        }

        // ---------------------------------------------------------------- König

        public static King Koenig
        {
            get
            {
                var koenig = UnityEngine.Object.FindObjectOfType<King>();
                return koenig;
            }
        }

        public static string KoenigText()
        {
            var koenig = Koenig;
            if (koenig == null) return "nicht in dieser Runde";

            // currentHealth ist privat - der Prozentsatz ist öffentlich und reicht.
            return string.Format("{0:0}%   ({1:0} Maximum)",
                koenig.GetHealthPercentage() * 100f, koenig.currentMaxHealth);
        }

        // ---------------------------------------------------------------- Gegenstände

        private static ItemDatabaseSO _datenbank;

        /// <summary>Die Gegenstandsliste des Spiels.
        ///
        /// Sie steckt als privates Feld im ItemManager. So bleibt die Liste immer die
        /// echte - auch nach einem Spiel-Update, das Gegenstände hinzufügt.</summary>
        public static ItemDatabaseSO Datenbank
        {
            get
            {
                if (_datenbank != null) return _datenbank;
                if (ItemManager.Instance == null) return null;

                _datenbank = AccessTools.Field(typeof(ItemManager), "itemDataBase")
                    .GetValue(ItemManager.Instance) as ItemDatabaseSO;

                return _datenbank;
            }
        }

        /// <summary>Gegenstände nach Seltenheit gebündelt, jeweils (Kennung, Anzeigename).
        ///
        /// Seltenheit null bedeutet: alles, was es gibt - Erze, Werkstoffe, Werkzeuge,
        /// einfach der gesamte Bestand.</summary>
        public static List<KeyValuePair<string, string>> GegenstaendeMitSeltenheit(Rarity? seltenheit)
        {
            var liste = new List<KeyValuePair<string, string>>();
            var datenbank = Datenbank;
            if (datenbank == null || datenbank.items == null) return liste;

            foreach (var eintrag in datenbank.items)
            {
                if (eintrag == null) continue;
                if (seltenheit.HasValue && eintrag.Rarity != seltenheit.Value) continue;

                liste.Add(new KeyValuePair<string, string>(eintrag.itemID, Name(eintrag)));
            }

            return liste.OrderBy(e => e.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Tragbares: Kisten, Fässer und alles, was man schleppt.
        ///
        /// Das ist eine eigene Liste im Spiel mit einem eigenen Befehl zum Erzeugen -
        /// solche Sachen sind keine Gegenstände fürs Inventar.</summary>
        public static List<KeyValuePair<string, string>> Tragbares()
        {
            var liste = new List<KeyValuePair<string, string>>();
            var datenbank = Datenbank;
            if (datenbank == null || datenbank.carriables == null) return liste;

            foreach (var eintrag in datenbank.carriables)
            {
                if (eintrag == null || string.IsNullOrEmpty(eintrag.Id)) continue;

                string name;
                try
                {
                    name = eintrag.GetLocalizedName();
                    if (string.IsNullOrEmpty(name) || name.Contains("_NAME")) name = eintrag.Id;
                }
                catch (Exception) { name = eintrag.Id; }

                liste.Add(new KeyValuePair<string, string>(eintrag.Id, name));
            }

            return liste.OrderBy(e => e.Value, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Anzeigename, mit der Kennung als Rückfallebene.
        ///
        /// GetLocalizedName greift auf die Übersetzungstabellen zu; sind die noch nicht
        /// geladen, kommt Leeres zurück - dann ist die Kennung immer noch besser als nichts.</summary>
        private static string Name(ItemDataSO eintrag)
        {
            try
            {
                string name = eintrag.GetLocalizedName();
                if (!string.IsNullOrEmpty(name) && !name.Contains("_NAME")) return name;
            }
            catch (Exception)
            {
                // Übersetzung noch nicht bereit - Kennung tut es auch
            }

            return eintrag.itemID;
        }

        /// <summary>Wohin der Gegenstand fällt: knapp vor dich, in Blickrichtung.</summary>
        public static Vector3 SpawnOrt()
        {
            var player = LocalPlayer;
            if (player == null) return Vector3.zero;

            var kamera = Camera.main;
            Vector3 richtung = kamera != null ? kamera.transform.forward : player.transform.forward;

            return player.transform.position + richtung * 2f + Vector3.up * 1.2f;
        }

        /// <summary>Legt einen Gegenstand vor dich.
        ///
        /// Der Befehl prüft keine Rechte, funktioniert also auch als Gast. Er landet für
        /// alle sichtbar in der Welt - deshalb ist der Cheat als "betrifft alle" markiert.</summary>
        public static string Spawne(string itemId, int anzahl, bool tragbar = false)
        {
            if (ItemManager.Instance == null) return "Noch keine Runde";
            if (string.IsNullOrEmpty(itemId)) return "Nichts ausgewählt";

            Vector3 ort = SpawnOrt();
            int gemacht = 0;

            for (int i = 0; i < Mathf.Max(1, anzahl); i++)
            {
                Vector3 versatz = new Vector3(
                    UnityEngine.Random.Range(-0.4f, 0.4f),
                    0.3f * i,
                    UnityEngine.Random.Range(-0.4f, 0.4f));

                if (tragbar)
                    ItemManager.Instance.CmdInstantiateCarriableObject(itemId, ort + versatz);
                else
                    ItemManager.Instance.CmdInstantiatePickableNewItem(itemId, 1, ort + versatz);

                gemacht++;
            }

            return gemacht + "x da";
        }
    }
}
