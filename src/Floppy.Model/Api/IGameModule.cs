using System.Collections.Generic;

namespace Floppy.Core.Api
{
    /// <summary>Ein Spielmodul. Pro unterstütztem Spiel eine Implementierung -
    /// der Core kennt das Spiel selbst nicht.</summary>
    public interface IGameModule
    {
        /// <summary>Muss Application.productName des Zielspiels entsprechen.</summary>
        string ProductName { get; }

        /// <summary>Anzeigename in der Oberfläche.</summary>
        string DisplayName { get; }

        /// <summary>Einmalig beim Laden - hier Harmony-Patches setzen.</summary>
        void Initialize();

        /// <summary>Die Cheats, gruppiert. Wird einmal nach Initialize() abgefragt.</summary>
        List<CheatCategory> BuildCategories();

        /// <summary>Jeden Frame (aus dem Unity-Mainthread).</summary>
        void Update();

        /// <summary>Das Overlay wurde geöffnet oder geschlossen.
        ///
        /// Damit kann das Modul die Spielsteuerung sperren, solange das Menü offen ist -
        /// wie das geht, weiß nur es selbst.</summary>
        void SetMenuOpen(bool open);

        /// <summary>Kurzer Statustext für die Kopfzeile, z.B. "Bereit" oder
        /// "Nur als Host verfügbar". Rückgabe false = Cheats gesperrt.</summary>
        bool IsReady(out string status);
    }
}
