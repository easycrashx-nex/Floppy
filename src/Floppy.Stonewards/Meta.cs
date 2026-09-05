using System;
using System.Collections.Generic;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Der Fortschritt zwischen den Runden: Freischaltungen, Söldner, Aufträge.
    ///
    /// Das steht alles in deinem Speicherstand und nicht auf dem Server - es betrifft
    /// deshalb nur dich, egal ob du hostest oder Gast bist. Dafür wird es gespeichert:
    /// Was hier freigeschaltet wird, bleibt freigeschaltet.</summary>
    internal static class Meta
    {
        /// <summary>Alle freischaltbaren Dinge des Spiels einsammeln.
        ///
        /// Sie liegen als ScriptableObjects im Spiel und sind über eine gemeinsame
        /// Schnittstelle erreichbar - so bekommen wir sie alle, ohne eine Liste zu
        /// pflegen, die nach dem nächsten Spiel-Update veraltet wäre.</summary>
        private static List<IUnlockable> Freischaltbare()
        {
            var liste = new List<IUnlockable>();

            foreach (var objekt in Resources.FindObjectsOfTypeAll<ScriptableObject>())
            {
                var freischaltbar = objekt as IUnlockable;
                if (freischaltbar == null || !freischaltbar.Unlockable) continue;
                liste.Add(freischaltbar);
            }

            return liste;
        }

        public static string FreischaltenText()
        {
            var verwalter = UnlockableManager.Instance;
            if (verwalter == null) return "noch nicht geladen";

            var alle = Freischaltbare();
            int offen = 0;

            foreach (var eintrag in alle)
            {
                try { if (verwalter.IsItemAcquired(eintrag)) offen++; }
                catch (Exception) { }
            }

            return offen + " von " + alle.Count + " freigeschaltet";
        }

        /// <summary>Alles freischalten, ohne dafür zu bezahlen.
        ///
        /// Das Spiel bringt dafür selbst einen Schalter mit: AcquireObject nimmt einen
        /// Parameter, der die Kosten überspringt. Wir müssen also nichts umgehen -
        /// nur den Weg nehmen, den der Entwickler ohnehin vorgesehen hat.</summary>
        public static string SchalteAllesFrei()
        {
            var verwalter = UnlockableManager.Instance;
            if (verwalter == null) return "Noch nicht geladen";

            int gemacht = 0;

            foreach (var eintrag in Freischaltbare())
            {
                try
                {
                    if (verwalter.IsItemAcquired(eintrag)) continue;

                    // Erst kaufbar machen, sonst gibt es keinen Eintrag zum Freischalten
                    verwalter.SetObjectPurchasable(eintrag);
                    verwalter.AcquireObject(eintrag, ignoreMetaCurrencyCost: true);
                    gemacht++;
                }
                catch (Exception)
                {
                    // Einzelne Sonderfälle sollen den Rest nicht aufhalten
                }
            }

            return gemacht == 0 ? "War schon alles frei" : gemacht + " freigeschaltet";
        }

        // ---------------------------------------------------------------- Söldner

        public static string HeuereSoeldnerAn()
        {
            var verwalter = MercenaryRecruitmentManager.Instance;
            if (verwalter == null) return "Noch nicht geladen";

            int gemacht = 0;

            foreach (var soeldner in Resources.FindObjectsOfTypeAll<MercenarySO>())
            {
                if (soeldner == null) continue;

                try
                {
                    // RecruitNPC trägt nur ein - bezahlt wird in einer eigenen Methode,
                    // die wir schlicht nicht aufrufen.
                    verwalter.RecruitNPC(soeldner.ID, 1);
                    gemacht++;
                }
                catch (Exception)
                {
                }
            }

            return gemacht == 0 ? "Keine Söldner gefunden" : gemacht + " angeheuert";
        }

        // ---------------------------------------------------------------- Aufträge

        public static string HoleBelohnungen()
        {
            var verwalter = QuestManager.Instance;
            if (verwalter == null) return "Noch nicht geladen";

            int gemacht = 0;

            foreach (var auftrag in Resources.FindObjectsOfTypeAll<QuestSO>())
            {
                if (auftrag == null) continue;

                try
                {
                    if (!verwalter.IsQuestClaimable(auftrag)) continue;
                    verwalter.TryClaimQuestRewards(auftrag);
                    gemacht++;
                }
                catch (Exception)
                {
                }
            }

            return gemacht == 0 ? "Gerade nichts abzuholen" : gemacht + " eingesammelt";
        }
    }
}
