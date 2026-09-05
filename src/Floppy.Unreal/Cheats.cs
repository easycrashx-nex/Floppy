using System;
using System.Collections.Generic;

namespace Floppy.Unreal
{
    /// <summary>Die Cheats, die das Spiel selbst mitbringt.
    ///
    /// Mortal Shell II hat ein vollständiges Entwicklermenü an Bord: 287 Funktionen am
    /// PlayerController, davon rund sechzig zum Schummeln - Währungen, Freischaltungen,
    /// Karte aufdecken, Heilen, Stufen. Wir müssen also nichts nachbauen, sondern nur
    /// aufrufen, was ohnehin da ist.
    ///
    /// Der Vorteil: Das Spiel macht die Buchführung selbst. Gold über S_AddGold landet
    /// im Inventar, in der Anzeige und im Speicherstand - Zahlen von Hand zu setzen
    /// hätte nur eine der drei Stellen getroffen.</summary>
    public static class Cheats
    {
        public enum Art
        {
            Ohne,       // einfacher Knopf
            Ganzzahl,   // int-Parameter
            Komma,      // double-Parameter
            Fliess,     // float-Parameter
            Schalter    // bool-Parameter
        }

        public sealed class Eintrag
        {
            public string Funktion;
            public string Label;
            public string Rubrik;
            public Art Parameter = Art.Ohne;
            public double Vorgabe = 1;
            public string Hinweis = "";
        }

        private static Eintrag K(string funktion, string label, string rubrik,
                                Art art = Art.Ohne, double vorgabe = 1, string hinweis = "")
        {
            return new Eintrag
            {
                Funktion = funktion, Label = label, Rubrik = rubrik,
                Parameter = art, Vorgabe = vorgabe, Hinweis = hinweis
            };
        }

        public static readonly List<Eintrag> Alle = new()
        {
            // ---------------------------------------------------------- Währung
            K("S_AddGold", "Gold", "Währung", Art.Ganzzahl, 10000),
            K("S_AddGloom", "Gloom", "Währung", Art.Ganzzahl, 10000),
            K("S_AddGlimpses", "Glimpses", "Währung", Art.Ganzzahl, 100),
            K("S_AddOvums", "Ovums", "Währung", Art.Ganzzahl, 100),
            K("S_AddDorsalite", "Dorsalit", "Währung", Art.Ganzzahl, 100),
            K("S_AddLaterite", "Laterit", "Währung", Art.Ganzzahl, 100),
            K("S_AddTarcores", "Tarkerne", "Währung", Art.Ganzzahl, 100),
            K("S_AddThoracium", "Thoracium", "Währung", Art.Ganzzahl, 100),
            K("S_AddThoraciumPrime", "Thoracium Prime", "Währung", Art.Ganzzahl, 100),
            K("S_AddVentrium", "Ventrium", "Währung", Art.Ganzzahl, 100),

            // ---------------------------------------------------------- Figur
            K("S_Heal", "Heilen", "Figur", Art.Komma, 1000),
            K("S_GainResolve", "Resolve auffüllen", "Figur", Art.Komma, 100),
            K("S_ReviveShell", "Hülle wiederbeleben", "Figur"),
            K("S_RechargePPItem", "Gegenstand aufladen", "Figur"),
            K("S_ModifyHealingCharge", "Heilladungen dazu", "Figur", Art.Ganzzahl, 5),
            K("S_HealingChargeCapacityUpgrade", "Mehr Heilladungen möglich", "Figur", Art.Ganzzahl, 3),
            K("S_HealingChargeEfficiencyUpgrade", "Heilung wirksamer", "Figur", Art.Ganzzahl, 3),
            K("S_DamageReduction", "Schadensminderung", "Figur", Art.Komma, 90,
              "In Prozent, so wie das Spiel es selbst rechnet."),
            K("S_ParryResistanceDamageBonus", "Parier-Schadensbonus", "Figur", Art.Komma, 5),
            K("S_BlockExperience", "Keine Erfahrung sammeln", "Figur", Art.Schalter),
            K("S_DealDamage", "Sich selbst Schaden zufügen", "Figur", Art.Fliess, 10,
              "Zum Ausprobieren - nicht aus Versehen drücken."),
            K("S_KillMe", "Sterben", "Figur", Art.Ohne, 1,
              "Genau das, wonach es klingt."),

            // ---------------------------------------------------------- Stufen
            K("S_AddShellPoints", "Hüllenpunkte", "Stufen", Art.Ganzzahl, 10),
            K("S_AddWeaponLevel", "Waffenstufe", "Stufen", Art.Ganzzahl, 1),
            K("S_AddSidearmLevel", "Seitenwaffenstufe", "Stufen", Art.Ganzzahl, 1),
            K("S_SetDarkBroLevel", "Dunkle Stufe setzen", "Stufen", Art.Ganzzahl, 5),
            K("S_UpdateBondingPoints", "Bindungspunkte auffrischen", "Stufen"),
            K("S_TarstoneLevelIncrementall", "Alle Tarsteine eine Stufe höher", "Stufen"),

            // ---------------------------------------------------------- Freischalten
            K("S_UnlockAllWeapons", "Alle Waffen", "Freischalten"),
            K("S_UnlockAllShells", "Alle Hüllen", "Freischalten"),
            K("S_UnlockAllSeals", "Alle Siegel", "Freischalten"),
            K("S_UnlockAllSidearms", "Alle Seitenwaffen", "Freischalten"),
            K("S_UnlockAllMasks", "Alle Masken", "Freischalten"),
            K("S_UnlockAllClothing", "Alle Kleidung", "Freischalten"),
            K("S_UnlockAllGates", "Alle Tore", "Freischalten"),
            K("S_UnlockAllLandingAreas", "Alle Landeplätze", "Freischalten"),
            K("S_UnlockFastTravel", "Schnellreise", "Freischalten"),
            K("S_UnlockMap", "Karte", "Freischalten"),
            K("S_UnlockTarforgeModes", "Tarforge-Modi", "Freischalten"),
            K("S_UnlockDarkFormShades", "Dunkelform-Färbungen", "Freischalten"),
            K("S_UnlockShellShades", "Hüllen-Färbungen", "Freischalten"),
            K("S_UnlockCosmicHarbinger", "Kosmischer Vorbote", "Freischalten"),
            K("S_UnlockRedHarbinger", "Roter Vorbote", "Freischalten"),
            K("S_AddAllItems", "Alle Gegenstände", "Freischalten",
              Art.Ohne, 1, "Legt jeden Gegenstand des Spiels ins Inventar."),
            K("S_AddAllTarstonesMelee", "Alle Tarsteine (Nahkampf)", "Freischalten"),
            K("S_AddAllTarstonesSidearm", "Alle Tarsteine (Seitenwaffe)", "Freischalten"),
            K("S_AddAllTarstonesSupport", "Alle Tarsteine (Unterstützung)", "Freischalten"),
            K("S_GiveTarforgeUnlockItems", "Tarforge-Gegenstände", "Freischalten", Art.Schalter),

            // ---------------------------------------------------------- Karte
            K("S_MapReveal_All", "Alles aufdecken", "Karte"),
            K("S_MapReveal_Beacons", "Leuchtfeuer", "Karte"),
            K("S_MapReveal_Dungeons", "Verliese", "Karte"),
            K("S_MapReveal_Gates", "Tore", "Karte"),
            K("S_MapReveal_HUB", "Zentrale", "Karte"),
            K("S_MapReveal_Traversal", "Wege", "Karte"),
            K("S_MapReveal_EvilStatues", "Statuen", "Karte"),
            K("S_MapReveal_ShellObjectives", "Hüllen-Ziele", "Karte"),
            K("S_MapReveal_WeaponObjectives", "Waffen-Ziele", "Karte"),
            K("S_MapReveal_SidearmObjectives", "Seitenwaffen-Ziele", "Karte"),

            // ---------------------------------------------------------- Fortschritt
            K("S_CompletePrologue", "Prolog abschließen", "Spielverlauf"),
            K("S_PostGolemFightSettings", "Zustand nach dem Golem", "Spielverlauf"),
            K("S_IncrementMemoryProgress", "Erinnerung voranbringen", "Spielverlauf"),
            K("S_SendCleanseEvent", "Reinigung auslösen", "Spielverlauf"),
            K("S_EndActiveMemory", "Laufende Erinnerung beenden", "Spielverlauf"),
            K("S_ResetForNewGamePlus", "Für New Game+ zurücksetzen", "Spielverlauf",
              Art.Ohne, 1, "Setzt den Speicherstand für einen neuen Durchlauf zurück."),

            // ---------------------------------------------------------- Anzeige
            K("S_FreeAiming", "Freies Zielen", "Anzeige", Art.Schalter),
            K("S_HideAllMapIcons", "Kartensymbole ausblenden", "Anzeige", Art.Schalter),
            K("S_HideEquipmentMenu", "Ausrüstungsmenü ausblenden", "Anzeige", Art.Schalter),
            K("S_ResetHUDState", "Anzeige zurücksetzen", "Anzeige"),
            K("S_ClearScreenTransition", "Bildübergang abbrechen", "Anzeige"),
            K("S_ShowWeaponsCollider", "Waffen-Trefferzonen zeigen", "Anzeige", Art.Schalter),
            K("S_VisualizeEnemies", "Gegner hervorheben", "Anzeige"),
            K("S_DebugActiveEnemies", "Gegner zählen", "Anzeige"),
        };

        /// <summary>Baut den Parameterblock für einen Aufruf.
        ///
        /// Wo der Wert hingehört, sagt die Funktion selbst - wir lesen ihre erste
        /// Eigenschaft ab, statt einen Abstand einzutragen.</summary>
        public static byte[] Block(Aufruf ruf, ulong funktion, Eintrag eintrag, double wert)
        {
            var block = new byte[0x200];
            if (eintrag.Parameter == Art.Ohne) return block;

            var parameter = ruf.Parameter(funktion);
            if (parameter.Count == 0) return block;

            int abstand = parameter[0].Abstand;
            if (abstand < 0 || abstand + 8 > block.Length) return block;

            switch (eintrag.Parameter)
            {
                case Art.Ganzzahl:
                    BitConverter.GetBytes((int)wert).CopyTo(block, abstand);
                    break;

                case Art.Komma:
                    BitConverter.GetBytes(wert).CopyTo(block, abstand);
                    break;

                case Art.Fliess:
                    BitConverter.GetBytes((float)wert).CopyTo(block, abstand);
                    break;

                case Art.Schalter:
                    block[abstand] = (byte)(wert != 0 ? 1 : 0);
                    break;
            }

            return block;
        }
    }
}
