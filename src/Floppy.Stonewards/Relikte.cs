using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Die Aufwertungen einer Runde - die Dinger, die man sonst zwischen den
    /// Wellen aus drei Vorschlägen auswählt.
    ///
    /// Das Spiel hat dafür eine fertige Funktion: ApplyUpgrade nimmt eine Auswahl aus
    /// Aufwertung, Stufe und Seltenheit entgegen und trägt sie ein. Genau die rufen wir
    /// auf - dieselbe, die auch das Auswahlfenster benutzt.
    ///
    /// Es wirkt auf deine Figur. Die Weitergabe ans Netz macht das Spiel selbst, und
    /// zwar über einen Befehl ohne Rechteprüfung - das geht also auch als Gast.</summary>
    internal static class Relikte
    {
        private static List<RogueLikeUpgradeSO> _alle;

        /// <summary>Alle Aufwertungen des Spiels, alphabetisch.
        ///
        /// Wir holen sie aus den geladenen Spieldaten statt aus einer eigenen Liste -
        /// so sind Aufwertungen aus einem späteren Spiel-Update von selbst dabei.</summary>
        public static List<RogueLikeUpgradeSO> Alle
        {
            get
            {
                if (_alle != null && _alle.Count > 0) return _alle;

                _alle = Resources.FindObjectsOfTypeAll<RogueLikeUpgradeSO>()
                    .Where(u => u != null && !string.IsNullOrEmpty(u.ID))
                    .GroupBy(u => u.ID)
                    .Select(g => g.First())
                    .OrderBy(Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                return _alle;
            }
        }

        public static string Name(RogueLikeUpgradeSO aufwertung)
        {
            try
            {
                string titel = aufwertung.GetLocalizedTitle();
                if (!string.IsNullOrEmpty(titel) && !titel.Contains("_TITLE")) return titel;
            }
            catch (Exception)
            {
                // Übersetzungstabelle noch nicht geladen
            }

            return aufwertung.ID;
        }

        public static string[] Namen()
        {
            return Alle.Select(Name).ToArray();
        }

        public static string Gib(int index, int stufe, Rarity seltenheit)
        {
            var verwalter = RogueLikeUpgradeManager.Instance;
            if (verwalter == null) return "Keine Runde";
            if (Game.LocalPlayer == null) return "Keine Figur";

            var liste = Alle;
            if (liste.Count == 0) return "Nichts gefunden";

            var aufwertung = liste[Mathf.Clamp(index, 0, liste.Count - 1)];

            var auswahl = new RogueLikeUpgradeManager.UpgradeDraftChoice
            {
                Upgrade = aufwertung,
                Level = Mathf.Clamp(stufe, 1, Mathf.Max(1, aufwertung.MaxLevel)),
                Rarity = seltenheit
            };

            verwalter.ApplyUpgrade(auswahl);
            return Name(aufwertung) + " dazu";
        }

        /// <summary>Jede Aufwertung einmal, in der gewählten Seltenheit.</summary>
        public static string GibAlle(Rarity seltenheit)
        {
            var verwalter = RogueLikeUpgradeManager.Instance;
            if (verwalter == null) return "Keine Runde";

            int gemacht = 0;

            for (int i = 0; i < Alle.Count; i++)
            {
                try
                {
                    Gib(i, 1, seltenheit);
                    gemacht++;
                }
                catch (Exception)
                {
                    // Einzelne, die gerade nicht passen, überspringen
                }
            }

            return gemacht == 0 ? "Nichts gefunden" : gemacht + " Aufwertungen dazu";
        }

        public static string Text()
        {
            var verwalter = RogueLikeUpgradeManager.Instance;
            if (verwalter == null) return "keine Runde";

            return string.Format("{0} bekannt   |   Ladung {1}/{2}",
                Alle.Count, verwalter.GetCurrentCharge(), verwalter.GetCurrentChargeNeeded());
        }
    }
}
