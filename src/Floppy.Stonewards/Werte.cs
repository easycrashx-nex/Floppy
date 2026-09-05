using System;
using System.Collections.Generic;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Die Werte deiner Figur.
    ///
    /// Stonewards rechnet jeden Wert aus einem Grundwert plus einer Liste von
    /// Zuschlägen (StatModifier). Genau da hängen wir uns ein, statt den Grundwert zu
    /// überschreiben: Ein Zuschlag mit eigener Quelle lässt sich sauber ersetzen und
    /// wieder entfernen, und die Anzeige des Spiels rechnet ihn von selbst mit ein.
    ///
    /// Das Ganze liegt auf deinem Rechner. Schaden rechnet in diesem Spiel der
    /// Angreifer aus - deine Zuschläge wirken also nur auf deine eigenen Treffer,
    /// nicht auf die deiner Mitspieler.</summary>
    internal static class Werte
    {
        /// <summary>Woran das Spiel unsere Zuschläge wiedererkennt.</summary>
        private const string Quelle = "Floppy";

        internal sealed class Eintrag
        {
            public string Id;
            public string Label;
            public Func<PlayerStats, CharacterStat> Hole;
            public float Min;
            public float Max;
            public float Schritt;
            public string Beschreibung = "";
        }

        /// <summary>Die Wunschwerte des Nutzers, je Cheat-Kennung.</summary>
        private static readonly Dictionary<string, float> _gewuenscht =
            new Dictionary<string, float>();

        public static readonly List<Eintrag> Alle = new List<Eintrag>
        {
            // --- Überleben
            N("wert.leben",      "Mehr Leben",            s => s.MaxHealth,        0f, 2000f, 10f,
              "Erhöht das Maximum. Das Auffüllen macht das Spiel selbst."),
            N("wert.regen",      "Lebensregeneration",    s => s.HealthRegen,      0f, 100f, 1f),
            N("wert.verteidigung", "Verteidigung",        s => s.Defense,          0f, 500f, 5f),
            N("wert.ausweichen", "Ausweichchance",        s => s.DodgeChance,      0f, 1f, 0.05f,
              "1 heißt: jeder Treffer geht daneben."),
            N("wert.reflekt",    "Schaden zurückwerfen",  s => s.DamageReflect,    0f, 10f, 0.5f),
            N("wert.lebensraub", "Lebensraub",            s => s.LifeStealPercentage, 0f, 1f, 0.05f),

            // --- Ausdauer und Mana
            N("wert.ausdauer",   "Mehr Ausdauer",         s => s.MaxStamina,       0f, 1000f, 10f),
            N("wert.mana",       "Mehr Mana",             s => s.MaxMana,          0f, 1000f, 10f),
            N("wert.manaregen",  "Manaregeneration",      s => s.ManaRegen,        0f, 100f, 1f),

            // --- Angriff
            N("wert.angriff",    "Angriffskraft",         s => s.AttackPower,      0f, 1000f, 10f),
            N("wert.magie",      "Magiekraft",            s => s.MagicPower,       0f, 1000f, 10f),
            N("wert.heilkraft",  "Heilkraft",             s => s.HealingPower,     0f, 500f, 5f),
            N("wert.nahkampf",   "Nahkampfschaden",       s => s.MeleeDamageMultiplier, 0f, 20f, 0.5f),
            N("wert.fernkampf",  "Fernkampfschaden",      s => s.RangedDamageMultiplier, 0f, 20f, 0.5f),
            N("wert.explosion",  "Explosionsschaden",     s => s.ExplosionDamageMultiplier, 0f, 20f, 0.5f),
            N("wert.elite",      "Schaden gegen Elite",   s => s.BonusDamageEliteBoss, 0f, 20f, 0.5f),
            N("wert.kritchance", "Kritische Trefferchance", s => s.CritChance,     0f, 1f, 0.05f),
            N("wert.kritschaden","Kritischer Schaden",    s => s.CritDamage,       0f, 20f, 0.5f),
            N("wert.rueckstoss", "Rückstoßchance",        s => s.ChanceOfKnockback, 0f, 1f, 0.05f),

            // --- Tempo
            N("wert.nahtempo",   "Nahkampftempo",         s => s.MeleeAttackSpeed, 0f, 10f, 0.25f),
            N("wert.ferntempo",  "Fernkampftempo",        s => s.RangedAttackSpeed, 0f, 10f, 0.25f),
            N("wert.magietempo", "Zaubertempo",           s => s.MagicSpeed,       0f, 10f, 0.25f),
            N("wert.tempo",      "Lauftempo",             s => s.SpeedMultiplier,  0f, 5f, 0.1f,
              "Vorsicht: sehr hohe Werte lassen dich durch Wände rutschen."),
            N("wert.sprint",     "Sprinttempo",           s => s.SprintSpeedMultiplier, 0f, 5f, 0.1f),
            N("wert.sprung",     "Sprunghöhe",            s => s.JumpHeightMultiplier, 0f, 5f, 0.1f),

            // --- Geschosse
            N("wert.geschosse",  "Zusätzliche Geschosse", s => s.ProjectileBonus,  0f, 20f, 1f),
            N("wert.geschosstempo", "Geschosstempo",      s => s.ProjectileSpeed,  0f, 100f, 5f),
            N("wert.abpraller",  "Abprallende Pfeile",    s => s.BounceArrowCount, 0f, 20f, 1f),

            // --- Graben und Beute
            N("wert.grabkraft",  "Grabkraft",             s => s.DigStrength,      0f, 100f, 1f,
              "Wie viel du mit einem Schlag aus dem Boden holst."),
            N("wert.schwergraben", "Schweres Graben",     s => s.HeavyDigMultiplier, 0f, 20f, 0.5f),
            N("wert.bonusrohstoff", "Bonus-Rohstoffe",    s => s.BonusResourceCount, 0f, 50f, 1f),
            N("wert.beutechance", "Beutechance",          s => s.DropItemChance,   0f, 1f, 0.05f),
            N("wert.stapel",     "Stapelgröße",           s => s.ItemMaxStackMultiplier, 0f, 20f, 0.5f),

            // --- Begleiter
            N("wert.begleiterschaden", "Begleiter: Schaden", s => s.CompanionDamageMultiplier, 0f, 20f, 0.5f),
            N("wert.begleiterleben",   "Begleiter: Leben",   s => s.CompanionMaxHealth, 0f, 1000f, 10f),
        };

        private static Eintrag N(string id, string label, Func<PlayerStats, CharacterStat> hole,
                                 float min, float max, float schritt, string beschreibung = "")
        {
            return new Eintrag
            {
                Id = id, Label = label, Hole = hole,
                Min = min, Max = max, Schritt = schritt, Beschreibung = beschreibung
            };
        }

        public static float Lies(string id)
        {
            float wert;
            return _gewuenscht.TryGetValue(id, out wert) ? wert : 0f;
        }

        public static void Setze(string id, float wert)
        {
            _gewuenscht[id] = wert;
            Anwenden(id);
        }

        /// <summary>Trägt einen einzelnen Zuschlag ein - oder nimmt ihn wieder heraus.</summary>
        private static void Anwenden(string id)
        {
            var stats = Game.Stats;
            if (stats == null) return;

            var eintrag = Alle.Find(e => e.Id == id);
            if (eintrag == null) return;

            CharacterStat stat;
            try { stat = eintrag.Hole(stats); }
            catch (Exception) { return; }

            if (stat == null) return;

            string quelle = Quelle + ":" + id;
            float wert = Lies(id);

            if (Mathf.Approximately(wert, 0f))
            {
                stat.RemoveAllModifiersFromSource(quelle);
                return;
            }

            stat.AddOrReplaceModifier(new StatModifier(wert, StatModType.FLAT, quelle));
        }

        /// <summary>Alle Zuschläge neu eintragen.
        ///
        /// Nötig, weil das Spiel die Werte bei jedem Klassenwechsel und zu Rundenbeginn
        /// frisch aufbaut - unsere Zuschläge wären sonst weg.</summary>
        public static void AlleAnwenden()
        {
            if (Game.Stats == null) return;

            foreach (var eintrag in Alle)
                if (!Mathf.Approximately(Lies(eintrag.Id), 0f))
                    Anwenden(eintrag.Id);
        }

        /// <summary>Was am Ende dabei herauskommt - der Wert, den auch das Spiel benutzt.</summary>
        public static string Endwert(string id)
        {
            var stats = Game.Stats;
            var eintrag = Alle.Find(e => e.Id == id);
            if (stats == null || eintrag == null) return "";

            try
            {
                var stat = eintrag.Hole(stats);
                return stat == null ? "" : stat.Value.ToString("0.##");
            }
            catch (Exception)
            {
                return "";
            }
        }
    }
}
