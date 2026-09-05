using UnityEngine;

namespace Floppy.Oddcore
{
    /// <summary>Solange das Overlay offen ist, gehört die Maus dem Menü.
    ///
    /// Vorerst nur der Mauszeiger - ob ODDCORE eine eigene Eingabesperre hat wie
    /// How to Fish, muss ich noch heraussuchen.</summary>
    internal static class InputLock
    {
        public static bool MenuOpen { get; private set; }

        private static CursorLockMode _vorher;
        private static bool _zeigerVorher;

        public static void SetMenuOpen(bool open)
        {
            MenuOpen = open;

            if (open)
            {
                _vorher = Cursor.lockState;
                _zeigerVorher = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = _vorher;
                Cursor.visible = _zeigerVorher;
            }
        }
    }
}
