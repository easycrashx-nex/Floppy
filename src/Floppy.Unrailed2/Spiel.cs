using System;
using System.IO;
using System.Linq;

namespace Floppy.Unrailed2
{
    /// <summary>Wo das Spiel liegt und wie man seine Entwicklerfunktionen anschaltet.
    ///
    /// Unrailed 2 ist ein Sonderfall unter allen Spielen, die Floppy kennt: Es bringt
    /// sein eigenes Entwicklerwerkzeug mit. Zwei Zeilen in der Einstellungsdatei, und
    /// das Spiel oeffnet beim naechsten Start selbst einen kleinen Webserver, ueber den
    /// sich die laufende Welt lesen und veraendern laesst.
    ///
    /// Deshalb schleusen wir hier nichts ein. Keine veraenderte .pck, keine fremde DLL,
    /// keine Speichermanipulation - wir setzen einen Schalter, den die Entwickler selbst
    /// eingebaut haben, und reden danach ueber HTTP mit dem Spiel.</summary>
    internal static class Spiel
    {
        public const string Prozess = "Unrailed2";
        public const string AppId = "2211170";

        /// <summary>Der Ordner, in dem das Spiel seine Nutzerdaten ablegt. Godot legt
        /// ihn unter dem Projektnamen an.</summary>
        public static string Nutzerordner =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Godot", "app_userdata", "Unrailed2");

        public static string Einstellungsdatei =>
            Path.Combine(Nutzerordner, "settingsFNA.txt");

        public static bool Laeuft =>
            System.Diagnostics.Process.GetProcessesByName(Prozess).Length > 0;

        public static bool Angeschaltet
        {
            get
            {
                try { return Einstellungen.Angeschaltet(Einstellungsdatei); }
                catch { return false; }
            }
        }

        public static bool HatSicherung => File.Exists(Einstellungen.Sicherung(Einstellungsdatei));

        /// <summary>Schaltet die Entwicklerfunktionen ein.
        ///
        /// Das Spiel liest seine Einstellungen beim Start und schreibt sie beim Beenden
        /// komplett neu. Wir aendern die Datei deshalb nur, solange das Spiel nicht
        /// laeuft - sonst ueberschreibt es unsere Zeilen beim Beenden wieder.</summary>
        public static string Anschalten()
        {
            if (Laeuft)
                return "Bitte Unrailed 2 erst beenden - sonst überschreibt es die Datei wieder";

            try
            {
                Einstellungen.Setze(Einstellungsdatei, true);

                return "Angeschaltet - beim nächsten Start von Unrailed 2 ist Floppy dabei";
            }
            catch (Exception ex)
            {
                return "Ging nicht: " + ex.Message;
            }
        }

        public static string Ausschalten()
        {
            if (Laeuft)
                return "Bitte Unrailed 2 erst beenden";

            try
            {
                Einstellungen.Setze(Einstellungsdatei, false);
                return "Wieder ausgeschaltet";
            }
            catch (Exception ex)
            {
                return "Ging nicht: " + ex.Message;
            }
        }

        public static string Wiederherstellen()
        {
            if (Laeuft) return "Bitte Unrailed 2 erst beenden";
            try
            {
                Einstellungen.StelleWiederHer(Einstellungsdatei);
                return "Ursprüngliche Einstellungen wiederhergestellt";
            }
            catch (Exception ex) { return "Ging nicht: " + ex.Message; }
        }
    }
}
