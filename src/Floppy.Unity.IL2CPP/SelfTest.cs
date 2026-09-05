using System;
using Floppy.Core;
using UnityEngine;

namespace Floppy.Core.Menu
{
    /// <summary>Kurzprobe: Welche Teile von Unitys Oberflächen-System funktionieren
    /// unter IL2CPP überhaupt? Wird nach der Fehlersuche wieder entfernt.</summary>
    internal static class SelfTest
    {
        public static bool Aktiv = false;
        private static bool _gemeldet;

        public static void Draw()
        {
            if (!Aktiv) return;

            // 1. Einfaches Zeichnen mit festen Rechtecken
            bool einfachOk = false;
            try
            {
                GUI.Label(new Rect(30f, 30f, 500f, 30f), "Floppy: einfaches Zeichnen funktioniert");
                einfachOk = true;
            }
            catch (Exception ex)
            {
                if (!_gemeldet) Log.Error("Einfaches Zeichnen fehlgeschlagen: " + ex.Message);
            }

            // 2. Dasselbe mit einem eigenen Stil
            bool stilOk = false;
            try
            {
                var stil = new GUIStyle();
                stil.normal.textColor = Color.cyan;
                GUI.Label(new Rect(30f, 60f, 500f, 30f), "Floppy: eigener Stil funktioniert", stil);
                stilOk = true;
            }
            catch (Exception ex)
            {
                if (!_gemeldet) Log.Error("Eigener Stil fehlgeschlagen: " + ex.Message);
            }

            // 3. Das Layout-System
            bool layoutOk = false;
            try
            {
                GUILayout.BeginArea(new Rect(30f, 90f, 500f, 60f));
                GUILayout.Label("Floppy: Layout funktioniert");
                GUILayout.EndArea();
                layoutOk = true;
            }
            catch (Exception ex)
            {
                if (!_gemeldet) Log.Error("Layout fehlgeschlagen: " + ex.GetType().Name + " - " + ex.Message);
            }

            if (!_gemeldet && Event.current.type == EventType.Repaint)
            {
                _gemeldet = true;
                Log.Info($"Kurzprobe - einfach: {einfachOk}, Stil: {stilOk}, Layout: {layoutOk}");
            }
        }
    }
}

