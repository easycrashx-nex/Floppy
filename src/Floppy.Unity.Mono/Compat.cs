using System;
using UnityEngine;

namespace Floppy.Core.Menu
{
    /// <summary>Ausgleich zwischen Mono und IL2CPP.
    ///
    /// Das Overlay ist für beide derselbe Quellcode. An zwei Stellen unterscheiden sich
    /// die Unity-Schnittstellen aber doch - dafür gibt es diese Schicht, einmal je
    /// Ausführung. Hier, unter Mono, ist es reine Durchreiche.</summary>
    internal static class Compat
    {
        public static RectOffset Offset(int links, int rechts, int oben, int unten)
        {
            return new RectOffset(links, rechts, oben, unten);
        }

        public static Rect Window(int id, Rect rect, Action<int> zeichnen, GUIStyle stil)
        {
            return GUI.Window(id, rect, new GUI.WindowFunction(zeichnen), GUIContent.none, stil);
        }
    }
}
