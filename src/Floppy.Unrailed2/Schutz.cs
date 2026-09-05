using System;
using System.Linq;

namespace Floppy.Unrailed2
{
    /// <summary>Der Riegel vor der oeffentlichen Bestenliste.
    ///
    /// Unrailed 2 betreibt einen eigenen Ranglisten-Server (u2.unrailed-online.com) mit
    /// Wochen-, Monats-, Jahres- und Ewigkeitswertung, dazu eine Wochenaufgabe mit fest
    /// vorgegebenem Startwert, Replay-Ablage und eine Sperrliste. Die Spielstandklasse
    /// hat eine Methode UploadToHighscore.
    ///
    /// Und sie hat kein einziges Feld, das eine Cheat-Nutzung vermerken wuerde. Eine
    /// gecheatete Runde landete dort also ununterscheidbar neben ehrlich gespielten -
    /// das trifft nicht mehr nur den, der cheatet, sondern jeden, der auf derselben
    /// Liste steht.
    ///
    /// Genau da verlaeuft die Grenze, die fuer Floppy von Anfang an gilt: Was nur die
    /// eigene Runde betrifft, ist Sache des Spielenden. Was Fremden etwas wegnimmt,
    /// nicht. Deshalb sperrt Floppy in gewerteten Laeufen von sich aus, statt es dem
    /// Zufall zu ueberlassen.
    ///
    /// Wo genau die Grenze liegt, konnte ich nicht restlos klaeren: Welche Modi
    /// tatsaechlich hochladen, steht nirgends im Klartext. Der Riegel greift deshalb
    /// beim Zeitmodus - dem Modus mit fester Wertung - und laesst die Geschichte, den
    /// eigenen Startwert und alles Private in Ruhe. Lieber einmal zu viel gesperrt als
    /// einmal zu wenig.</summary>
    internal static class Schutz
    {
        /// <summary>Modi, in denen Floppy nichts schreibt. Klein geschrieben, weil der
        /// Vergleich die Gross-/Kleinschreibung ignoriert.</summary>
        private static readonly string[] Gewertet =
        {
            "time",          // Zeitlauf - die Wertung, die auf der Rangliste landet
            "weekly",
            "challenge",
            "daily",
            "ranked",
            "highscore"
        };

        /// <summary>Modi, in denen es zwar keine Rangliste gibt, aber einen Gegner.
        /// Die sperren wir nicht - mit einem Freund abgesprochen ist das dessen Sache -
        /// aber wir sagen es deutlich.</summary>
        private static readonly string[] Gegeneinander =
        {
            "versus",
            "dynamitedeathmatch",
            "deathmatch"
        };

        /// <summary>Kann der Spielende das abschalten? Ja - es ist sein Spiel. Aber es
        /// steht von sich aus an, und es steht sichtbar da.</summary>
        public static bool An = true;

        private static bool Enthaelt(string[] liste, string lage)
        {
            if (string.IsNullOrWhiteSpace(lage)) return false;

            return liste.Any(w => lage.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>Ein Zeitlauf, der auf der oeffentlichen Rangliste landen kann.
        ///
        /// Der eigene Startwert ist ausgenommen: Wer den Startwert selbst waehlt, laeuft
        /// nicht gegen die Wochenaufgabe, sondern gegen sich selbst.</summary>
        public static bool Gewerteter_Lauf
        {
            get
            {
                string lage = Welt.Lage;

                if (lage != null &&
                    lage.IndexOf("customseed", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;

                return Enthaelt(Gewertet, lage);
            }
        }

        public static bool GegenMitspieler => Enthaelt(Gegeneinander, Welt.Lage);

        /// <summary>Darf jetzt geschrieben werden? Wenn nicht, steht in "grund", warum.</summary>
        public static bool Erlaubt(out string grund)
        {
            if (An && Gewerteter_Lauf)
            {
                grund = "Gesperrt: Dieser Lauf kann auf der öffentlichen Bestenliste landen";
                return false;
            }

            grund = null;
            return true;
        }

        /// <summary>Kurzfassung fuer die Anzeige.</summary>
        public static string Lagebericht()
        {
            if (!Welt.ImSpiel) return "keine Partie";

            if (Gewerteter_Lauf)
                return An
                    ? Welt.Lage + "   |   gewertet - Floppy schreibt hier nichts"
                    : Welt.Lage + "   |   gewertet - Schutz ist AUS";

            if (GegenMitspieler)
                return Welt.Lage + "   |   gegeneinander - trifft deinen Gegner";

            return Welt.Lage + "   |   frei";
        }
    }
}
