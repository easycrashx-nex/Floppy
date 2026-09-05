using System.Collections.Generic;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Alles, was nicht nur dich betrifft: Gegner, Mauern, König.
    ///
    /// Diese Dinge leben beim Gastgeber. Als Gast lässt sich davon nur das anfassen,
    /// wofür das Spiel ausdrücklich einen Befehl ohne Rechteprüfung anbietet - beim
    /// Rest passiert schlicht nichts, deshalb sind sie als "nur als Gastgeber"
    /// gekennzeichnet und bei Gästen ausgegraut.</summary>
    internal static class Welt
    {
        public static bool GegnerEinfrieren;
        public static float GegnerBremse;        // 0 bis 1
        public static bool MauernAuffuellen;

        // Das Absuchen der Szene kostet Zeit - die Ergebnisse halten wir kurz.
        private static EnemyController[] _gegner = new EnemyController[0];
        private static WallStats[] _mauern = new WallStats[0];
        private static float _zuletztGesucht;

        private static void Suche()
        {
            if (Time.unscaledTime - _zuletztGesucht < 1f) return;
            _zuletztGesucht = Time.unscaledTime;

            _gegner = Object.FindObjectsOfType<EnemyController>();
            _mauern = Object.FindObjectsOfType<WallStats>();
        }

        public static int LebendeGegner
        {
            get
            {
                Suche();
                int n = 0;
                foreach (var gegner in _gegner)
                    if (gegner != null && !gegner.isDead) n++;
                return n;
            }
        }

        /// <summary>Alle Gegner auf einen Schlag umlegen.
        ///
        /// Läuft über denselben Befehl, den auch eine echte Waffe benutzt - und der
        /// verlangt keine Rechte. Das geht deshalb auch als Gast, und die Abschüsse
        /// werden dir gutgeschrieben, zählen also für Aufträge mit.</summary>
        public static string ToeteAlle()
        {
            Suche();

            var stats = Game.Stats;
            if (stats == null) return "Keine Runde";

            int getroffen = 0;
            var schaden = new DamageResult(999999f, false);

            foreach (var gegner in _gegner)
            {
                if (gegner == null || gegner.isDead) continue;

                gegner.ServerApplyDamage(schaden,
                    WeaponDataSO.WeaponType.MELEE, stats.NetworkIdentity);
                getroffen++;
            }

            return getroffen == 0 ? "Gerade keine Gegner da" : getroffen + " erledigt";
        }

        /// <summary>Gegner anhalten oder bremsen. Läuft beim Gastgeber.</summary>
        public static void HalteGegner()
        {
            if (!Game.IstHost) return;
            Suche();

            foreach (var gegner in _gegner)
            {
                if (gegner == null || gegner.isDead) continue;

                if (GegnerEinfrieren)
                {
                    gegner.SetMovementEnabled(false);
                    continue;
                }

                if (GegnerBremse > 0f)
                    gegner.SetSlowPercentage(Mathf.Clamp01(GegnerBremse));
            }
        }

        // ---------------------------------------------------------------- Mauern

        public static string MauerText()
        {
            Suche();
            if (_mauern.Length == 0) return "keine in dieser Runde";

            int heil = 0, kaputt = 0;
            foreach (var mauer in _mauern)
            {
                if (mauer == null) continue;
                if (mauer.IsDestroyed) kaputt++;
                else heil++;
            }

            return heil + " stehen, " + kaputt + " zerstört";
        }

        /// <summary>Füllt stehende Mauern wieder auf.
        ///
        /// Zerstörte lassen wir bewusst in Ruhe: Sie wieder auf "heil" zu setzen würde
        /// nur den Zahlenwert ändern, während Modell und Animation zerstört bleiben.
        /// Die baut man im Spiel selbst wieder auf.</summary>
        public static string FuelleMauern()
        {
            if (!Game.IstHost) return "Nur als Gastgeber";
            Suche();

            int geheilt = 0;
            foreach (var mauer in _mauern)
            {
                if (mauer == null || mauer.IsDestroyed) continue;
                if (mauer.currentHealth >= mauer.currentMaxHealth) continue;

                mauer.NetworkcurrentHealth = mauer.currentMaxHealth;
                geheilt++;
            }

            return geheilt == 0 ? "Alles schon heil" : geheilt + " aufgefüllt";
        }

        // ---------------------------------------------------------------- König

        public static string StaerkeKoenig(float betrag)
        {
            if (!Game.IstHost) return "Nur als Gastgeber";

            var koenig = Game.Koenig;
            if (koenig == null) return "Kein König in dieser Runde";

            // Das Spiel hebt beim Erhöhen des Maximums auch das aktuelle Leben mit an -
            // ein eigener Heilaufruf ist deshalb nicht nötig.
            koenig.AddHealthModifier(betrag, StatModType.FLAT, "Floppy_" + Time.frameCount);
            return "Jetzt " + Game.KoenigText();
        }

        // ---------------------------------------------------------------- Zeit

        public static string ZeitText()
        {
            var tag = DayNightManager.Instance;
            if (tag == null) return "keine Runde";

            return string.Format("Tag {0:0}s   |   Nacht {1:0}s   |   Spieltempo {2:0.##}x",
                tag.NetworkdayDuration, tag.NetworknightDuration, Time.timeScale);
        }

        public static void SetzeTagLaenge(float sekunden)
        {
            if (!Game.IstHost || DayNightManager.Instance == null) return;
            DayNightManager.Instance.NetworkdayDuration = Mathf.Max(1f, sekunden);
        }

        public static void SetzeNachtLaenge(float sekunden)
        {
            if (!Game.IstHost || DayNightManager.Instance == null) return;
            DayNightManager.Instance.NetworknightDuration = Mathf.Max(1f, sekunden);
        }

        /// <summary>Spieltempo. Nur als Gastgeber sinnvoll - als Gast liefe deine
        /// Darstellung schneller als die Wahrheit auf dem Server.</summary>
        public static void SetzeTempo(float faktor)
        {
            if (!Game.IstHost) return;
            Time.timeScale = Mathf.Clamp(faktor, 0.1f, 5f);
        }

        /// <summary>Die Aufwertungsauswahl sofort anbieten, ohne die Ladung zu sammeln.</summary>
        public static string BieteAufwertung()
        {
            if (!Game.IstHost) return "Nur als Gastgeber";
            if (NetworkHelper.Instance == null) return "Keine Runde";

            NetworkHelper.Instance.StartRogueUpgradePhase();
            return "Auswahl läuft";
        }

        // ---------------------------------------------------------------- Bauen

        public static float Bautempo;

        /// <summary>Die Schmiede schneller machen.
        ///
        /// Die Bauzeit ist Rezeptzeit geteilt durch das Tempo der Schmiede - also
        /// setzen wir dort einen Zuschlag an, statt an der laufenden Bauzeit zu drehen.
        /// Das Spiel rechnet dann von selbst richtig, auch für die Fortschrittsanzeige.</summary>
        public static void SetzeBautempo()
        {
            if (!Game.IstHost || Bautempo <= 0f) return;

            var schmiede = Object.FindObjectOfType<Crafter>();
            if (schmiede == null || schmiede.ProductionSpeed == null) return;

            schmiede.ProductionSpeed.AddOrReplaceModifier(
                new StatModifier(Bautempo, StatModType.FLAT, "Floppy_bautempo"));
        }

        public static string BautempoText()
        {
            var schmiede = Object.FindObjectOfType<Crafter>();
            if (schmiede == null || schmiede.ProductionSpeed == null) return "keine Schmiede";
            return schmiede.ProductionSpeed.Value.ToString("0.##") + "x";
        }

        // ---------------------------------------------------------------- Truhen

        public static string OeffneTruhen()
        {
            var truhen = Object.FindObjectsOfType<Chest>();
            int offen = 0;

            foreach (var truhe in truhen)
            {
                if (truhe == null || truhe.NetworkisOpened) continue;
                truhe.CmdOpenChest();
                offen++;
            }

            return offen == 0 ? "Keine geschlossene Truhe gefunden" : offen + " geöffnet";
        }

        // ---------------------------------------------------------------- Wellen

        /// <summary>Den Countdown bis zur nächsten Welle auf null setzen.</summary>
        public static string NaechsteWelle()
        {
            if (!Game.IstHost) return "Nur als Gastgeber";
            if (NetworkHelper.Instance == null) return "Keine Runde";

            NetworkHelper.Instance.ServerSetCountdown(0f);
            return "Welle kommt";
        }

        public static string SetzeCountdown(float sekunden)
        {
            if (!Game.IstHost) return "Nur als Gastgeber";
            if (NetworkHelper.Instance == null) return "Keine Runde";

            NetworkHelper.Instance.ServerSetCountdown(Mathf.Max(0f, sekunden));
            return "Countdown steht auf " + Mathf.RoundToInt(sekunden) + "s";
        }

        // ---------------------------------------------------------------- Haustiere

        public static string RetteHaustiere()
        {
            var spieler = Game.LocalPlayer;
            if (spieler == null) return "Keine Runde";

            var tiere = Object.FindObjectsOfType<RescuePet>();
            int gerettet = 0;

            foreach (var tier in tiere)
            {
                if (tier == null) continue;

                try
                {
                    tier.CmdInteract(spieler);
                    gerettet++;
                }
                catch (System.Exception)
                {
                    // Einzelne, die gerade nicht ansprechbar sind, übergehen wir
                }
            }

            return gerettet == 0 ? "Keins gefunden" : gerettet + " gerettet";
        }

        public static void Tick()
        {
            if (Bautempo > 0f) SetzeBautempo();
            if (GegnerEinfrieren || GegnerBremse > 0f) HalteGegner();
            if (MauernAuffuellen) FuelleMauern();
        }
    }
}
