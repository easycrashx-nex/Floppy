using System;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace Floppy.Core.Menu
{
    /// <summary>Ausgleich zwischen Mono und IL2CPP.
    ///
    /// Die erzeugten Zwischen-Assemblies bilden nicht jede Überladung ab: RectOffset hat
    /// hier keinen Konstruktor mit vier Werten, und GUI.Window nimmt einen Text statt
    /// eines GUIContent. Beides wird hier geradegebogen.</summary>
    internal static class Compat
    {
        public static RectOffset Offset(int links, int rechts, int oben, int unten)
        {
            var o = new RectOffset();
            o.left = links;
            o.right = rechts;
            o.top = oben;
            o.bottom = unten;
            return o;
        }

        public static Rect Window(int id, Rect rect, Action<int> zeichnen, GUIStyle stil)
        {
            // IL2CPP kann mit einem .NET-Delegaten nichts anfangen - er muss erst in
            // einen von der Laufzeit verstandenen umgewandelt werden.
            var fn = DelegateSupport.ConvertDelegate<GUI.WindowFunction>(zeichnen);
            return GUI.Window(id, rect, fn, "", stil);
        }
    }
}
