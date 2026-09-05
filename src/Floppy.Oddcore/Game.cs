using System;
using Floppy.Core;
using UnityEngine;

namespace Floppy.Oddcore
{
    /// <summary>Alle Berührungspunkte mit dem Spielcode an einem Ort.
    ///
    /// Wichtig beim Suchen von Feldern: ODDCORE hat zu vielen Werten ZWEI Fassungen -
    /// eine großgeschriebene Eigenschaft in einer verschachtelten Speicherklasse
    /// (CurWave, Money, EnergyAm) und das kleingeschriebene Feld am Objekt selbst
    /// (curWave, money, playerEnergyAm). Der laufende Wert ist immer das kleine.</summary>
    internal static class Game
    {
        // ---------------------------------------------------------------- Zugänge

        public static global::endlessControl Runde
        {
            get { return global::endlessControl.Instance; }
        }

        public static global::FPSController.playerStats Werte
        {
            get { return global::FPSController.playerStats.Instance; }
        }

        public static global::FPSController.PlayerController Spieler
        {
            get { return global::FPSController.PlayerController.Instance; }
        }

        public static global::playerEnergy Energie
        {
            get { return Erste<global::playerEnergy>(); }
        }

        public static bool ImSpiel
        {
            get { return Runde != null && Werte != null; }
        }

        /// <summary>Die erste Komponente ihrer Art in der Szene. Manche Dinge hängen
        /// nicht an einem Singleton, sondern irgendwo im Aufbau.</summary>
        private static T Erste<T>() where T : UnityEngine.Object
        {
            try
            {
                var alle = UnityEngine.Object.FindObjectsOfType<T>();
                return alle != null && alle.Length > 0 ? alle[0] : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- Cheat-Kennzeichen

        /// <summary>ODDCORE führt an mehreren Stellen Buch. Wer nur eines zurücksetzt,
        /// bleibt anderswo markiert - setActiveIfCheated etwa liest playerEnergy,
        /// nicht endlessControl.</summary>
        public static int SenkeAlleFahnen()
        {
            int gesenkt = 0;

            var r = Runde;
            if (r != null && r.hasCheated) { r.hasCheated = false; gesenkt++; }

            var e = Energie;
            if (e != null && e.hasCheated) { e.hasCheated = false; gesenkt++; }

            return gesenkt;
        }

        public static bool IrgendeineFahneGesetzt()
        {
            var r = Runde;
            if (r != null && r.hasCheated) return true;

            var e = Energie;
            if (e != null && e.hasCheated) return true;

            return false;
        }

        /// <summary>Getrennt behandelt: das ist eher eine Freischaltung des spieleigenen
        /// Cheatmenüs als ein Erkennungsmerkmal - deshalb nur auf ausdrücklichen Wunsch.</summary>
        public static void SetzeCheatFreischaltung(bool an)
        {
            var w = Werte;
            if (w != null) w.hasCheatUnlocked = an;
        }

        // ---------------------------------------------------------------- Spielerwerte

        public static int Geld
        {
            get { return Werte != null ? Werte.money : 0; }
        }

        /// <summary>Geld setzen - und dabei den Deckel mitziehen.
        ///
        /// ODDCORE begrenzt money auf maxMoney. Steht der Deckel auf 13, bleibt Geld bei 13,
        /// egal was man hineinschreibt. Genau daran ist der Cheat zuerst gescheitert.</summary>
        public static void SetzeGeld(int betrag)
        {
            var w = Werte;
            if (w == null) return;

            betrag = Mathf.Max(0, betrag);
            if (w.maxMoney < betrag) w.maxMoney = betrag;
            w.money = betrag;
        }

        public static void GibGeld(int betrag)
        {
            var w = Werte;
            if (w == null) return;

            SetzeGeld(w.money + betrag);
        }

        /// <summary>Ganzzahlige Werte am Spielerprofil.</summary>
        public static void SetzeWert(string feld, int wert)
        {
            var w = Werte;
            if (w == null) return;

            switch (feld)
            {
                case "money": w.money = wert; break;
                case "maxMoney": w.maxMoney = wert; break;
                case "tokens": w.tokens = wert; break;
                case "tickets": w.tickets = wert; break;
                case "health": w.health = wert; break;
                case "jumpAm": w.jumpAm = wert; break;
                case "totalCoins": w.totalCoins = wert; break;
                case "totalEnemiesKilled":
                    // Wird an zwei Stellen geführt - die Runde überschreibt sonst zurück
                    w.totalEnemiesKilled = wert;
                    var r = Runde;
                    if (r != null) r.totalEnemiesKilled = wert;
                    break;
                case "totalWins": w.totalWins = wert; break;
            }
        }

        public static int LiesWert(string feld)
        {
            var w = Werte;
            if (w == null) return 0;

            switch (feld)
            {
                case "money": return w.money;
                case "maxMoney": return w.maxMoney;
                case "tokens": return w.tokens;
                case "tickets": return w.tickets;
                case "health": return w.health;
                case "jumpAm": return w.jumpAm;
                case "totalCoins": return w.totalCoins;
                case "totalEnemiesKilled":
                    var rr = Runde;
                    return rr != null ? rr.totalEnemiesKilled : w.totalEnemiesKilled;
                case "totalWins": return w.totalWins;
                default: return 0;
            }
        }

        public static void SetzeFeuerrate(float multi)
        {
            var w = Werte;
            if (w != null) w.fireRateMulti = multi;
        }

        public static float LiesFeuerrate()
        {
            var w = Werte;
            return w != null ? w.fireRateMulti : 1f;
        }

        /// <summary>Kurztexte für die Anzeigezeilen - damit man sieht, wo man steht,
        /// bevor man etwas verstellt.</summary>
        public static string LiesInfo(string id)
        {
            var r = Runde;
            var w = Werte;

            switch (id)
            {
                case "round.info.wave":
                    if (r == null) return "-";
                    return "Welle " + r.curWave +
                           (string.IsNullOrEmpty(r.curRoundType) ? "" : "  (" + r.curRoundType + ")");

                case "round.info.time":
                    if (r == null) return "-";
                    return Zeit(r.curTimer) + " von " + Zeit(r.maxTime);

                case "round.info.enemies":
                    if (r == null) return "-";
                    return r.nextWaveEnemies + " in der nächsten Welle, +" +
                           r.nextWaveAddEnemies + " pro Welle";

                case "round.info.boss":
                    if (r == null) return "-";
                    return r.isBossArea
                        ? (r.hasKilledBoss ? "Bossgebiet - besiegt" : "Bossgebiet - lebt noch")
                        : "kein Bossgebiet";

                case "round.info.shop":
                    if (r == null) return "-";
                    return "Laden alle " + r.shopRoundInterval + " Wellen, " +
                           r.freeShopCount + " davon kostenlos";

                case "money.info":
                    if (w == null) return "-";
                    return w.money + " von " + w.maxMoney +
                           "   |   " + w.tokens + " Marken, " + w.tickets + " Tickets";

                case "player.info":
                    if (w == null) return "-";
                    string e = Energie != null
                        ? "  |  Energie " + Mathf.RoundToInt(Energie.playerEnergyAm) +
                          " von " + Mathf.RoundToInt(Energie.maxEnergy)
                        : "";
                    return "Leben " + w.health + e;

                default: return null;
            }
        }

        private static string Zeit(float sekunden)
        {
            if (sekunden < 0f) sekunden = 0f;
            int m = Mathf.FloorToInt(sekunden / 60f);
            int sek = Mathf.FloorToInt(sekunden % 60f);
            return m + ":" + sek.ToString("00");
        }

        /// <summary>Aktuelle Werte, um die Regler in der Oberfläche nachzuführen.</summary>
        public static float LiesAnzeige(string id)
        {
            var w = Werte;
            var r = Runde;

            switch (id)
            {
                case "player.health": return w != null ? w.health : 0f;
                case "money.max": return w != null ? w.maxMoney : 0f;
                case "money.tokens": return w != null ? w.tokens : 0f;
                case "money.tickets": return w != null ? w.tickets : 0f;
                case "weapon.firerate": return w != null ? w.fireRateMulti : 1f;
                case "round.time": return r != null ? r.curTimer : 0f;
                case "round.maxtime": return r != null ? r.maxTime : 0f;
                case "round.wave": return r != null ? r.curWave : 0f;
                case "round.freeshop": return r != null ? r.freeShopCount : 0f;
                case "round.tokens": return r != null ? r.tokensToGive : 0f;
                case "enemy.next": return r != null ? r.nextWaveEnemies : 0f;
                case "stat.kills": return r != null ? r.totalEnemiesKilled : 0f;
                case "stat.coins": return w != null ? w.totalCoins : 0f;
                case "stat.wins": return w != null ? w.totalWins : 0f;
                case "move.speed": return Spieler != null ? Spieler.MoveSpeed : 0f;
                case "move.sprint": return Spieler != null ? Spieler.SprintSpeed : 0f;
                case "move.jump": return Spieler != null ? Spieler.JumpHeight : 0f;
                case "player.energy.max": return Energie != null ? Energie.maxEnergy : 0f;
                default: return float.NaN;
            }
        }

        // ---------------------------------------------------------------- Bewegung

        public static void SetzeBewegung(string feld, float wert)
        {
            var p = Spieler;
            if (p == null) return;

            switch (feld)
            {
                case "MoveSpeed": p.MoveSpeed = wert; break;
                case "baseMoveSpeed": p.baseMoveSpeed = wert; break;
                case "SprintSpeed": p.SprintSpeed = wert; break;
                case "JumpHeight": p.JumpHeight = wert; break;
                case "Gravity": p.Gravity = wert; break;
            }
        }

        public static void SetzeSpruenge(int anzahl)
        {
            var p = Spieler;
            if (p != null) p.canJumpAm = anzahl;
        }

        // ---------------------------------------------------------------- Schaden

        /// <summary>Der Schutzschalter des Spiels. Er sitzt auf Komponenten, die beim
        /// Szenenwechsel neu entstehen - deshalb wird er regelmäßig nachgesetzt.</summary>
        public static void SetzeUnverwundbar(bool an)
        {
            try
            {
                var alle = UnityEngine.Object.FindObjectsOfType<global::damagePlayer>();
                if (alle == null) return;

                foreach (var d in alle)
                    d.noDamage = an;
            }
            catch (Exception) { }
        }

        /// <summary>Schaden, den DU an Gegnern anrichtest.</summary>
        public static void SetzeGegnerSchaden(float zusatzMulti, float bossZusatz)
        {
            try
            {
                var alle = UnityEngine.Object.FindObjectsOfType<global::damageEnemy>();
                if (alle == null) return;

                foreach (var d in alle)
                {
                    d.addDamageMulti = zusatzMulti;
                    d.bossDamageAdd = bossZusatz;
                }
            }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- Energie

        public static void FuelleEnergie()
        {
            var e = Energie;
            if (e == null) return;
            e.playerEnergyAm = e.maxEnergy;
        }

        public static void SetzeEnergie(string feld, float wert)
        {
            var e = Energie;
            if (e == null) return;

            switch (feld)
            {
                case "playerEnergyAm": e.playerEnergyAm = wert; break;
                case "maxEnergy": e.maxEnergy = wert; break;
                case "totalMaxEnergyCap": e.totalMaxEnergyCap = wert; break;
                case "soulReturnDamage": e.soulReturnDamage = wert; break;
            }
        }

        public static float LiesEnergie(string feld)
        {
            var e = Energie;
            if (e == null) return 0f;

            switch (feld)
            {
                case "playerEnergyAm": return e.playerEnergyAm;
                case "maxEnergy": return e.maxEnergy;
                default: return 0f;
            }
        }

        // ---------------------------------------------------------------- Runde

        public static void SetzeRundeZahl(string feld, int wert)
        {
            var r = Runde;
            if (r == null) return;

            switch (feld)
            {
                case "curWave": r.curWave = wert; break;
                case "freeShopCount": r.freeShopCount = wert; break;
                case "tokensToGive": r.tokensToGive = wert; break;
                case "shopChance": r.shopChance = wert; break;
                case "shopRoundInterval": r.shopRoundInterval = wert; break;
                case "nightmareRoundInterval": r.nightmareRoundInterval = wert; break;
                case "nextWaveEnemies": r.nextWaveEnemies = wert; break;
                case "nextWaveAddEnemies": r.nextWaveAddEnemies = wert; break;
                case "totalEnemiesKilled": r.totalEnemiesKilled = wert; break;
            }
        }

        public static int LiesRundeZahl(string feld)
        {
            var r = Runde;
            if (r == null) return 0;

            switch (feld)
            {
                case "curWave": return r.curWave;
                case "freeShopCount": return r.freeShopCount;
                case "tokensToGive": return r.tokensToGive;
                case "nextWaveEnemies": return r.nextWaveEnemies;
                case "totalEnemiesKilled": return r.totalEnemiesKilled;
                default: return 0;
            }
        }

        public static void SetzeRundeZahl(string feld, float wert)
        {
            var r = Runde;
            if (r == null) return;

            switch (feld)
            {
                case "curTimer": r.curTimer = wert; break;
                case "maxTime": r.maxTime = wert; break;
                case "timer": r.timer = wert; break;
                case "timeDrainRate": r.timeDrainRate = wert; break;
                case "frenzyChance": r.frenzyChance = wert; break;
            }
        }

        public static void SetzeRundeSchalter(string feld, bool an)
        {
            var r = Runde;
            if (r == null) return;

            switch (feld)
            {
                case "permaTimeSlow": r.permaTimeSlow = an; break;
                case "pauseTimer": r.pauseTimer = an; break;
                case "shouldCountUpTime": r.shouldCountUpTime = an; break;
                case "hasKilledBoss": r.hasKilledBoss = an; break;
                case "hasCheated": r.hasCheated = an; break;
            }
        }
    }
}
