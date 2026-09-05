using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Floppy.Unrailed2
{
    /// <summary>Uebersetzt die Zahlenschluessel des Spiels in lesbare Namen.
    ///
    /// Unrailed 2 spricht intern nur in Zahlen: Jeder Typ und jedes Feld hat einen
    /// Hashwert, und der Schreibbefehl des Entwicklerwerkzeugs sieht deshalb so aus wie
    ///
    ///     14.-486817492.-70839394=10
    ///
    /// Damit kann kein Mensch arbeiten. Das Spiel bringt aber selbst eine Namenstabelle
    /// mit - sie liegt als res://Src/TypeNames.json im Paket und der Entwicklerserver
    /// gibt sie unter /typeNames heraus, sobald eine Partie laeuft. Aufbau:
    ///
    ///     { "&lt;TypHash&gt;": { "FullName": "common.Unrailed2....", "&lt;FeldHash&gt;": "Name" } }
    ///
    /// Wir holen sie vom Server statt aus dem Paket - dann stimmt sie auch nach einem
    /// Spielupdate, ohne dass jemand eine Datei nachziehen muss.</summary>
    internal static class Namen
    {
        private static readonly Dictionary<string, string> Typen = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> Felder = new Dictionary<string, string>();

        private static bool _geladen;

        public static bool Geladen => _geladen;

        public static void Vergiss()
        {
            Typen.Clear();
            Felder.Clear();
            _geladen = false;
        }

        /// <summary>Holt die Tabelle, falls noch nicht geschehen. Im Menue gibt das Spiel
        /// eine leere Tabelle heraus - dann bleiben wir unbeschriftet und versuchen es
        /// beim naechsten Mal wieder.</summary>
        public static void LadeFallsNoetig()
        {
            if (_geladen) return;

            using (var doc = Debugger.Frage("typeNames"))
            {
                if (doc == null) return;

                try
                {
                    int gezaehlt = 0;

                    foreach (var typ in doc.RootElement.EnumerateObject())
                    {
                        if (typ.Value.ValueKind != JsonValueKind.Object) continue;

                        foreach (var e in typ.Value.EnumerateObject())
                        {
                            if (e.Value.ValueKind != JsonValueKind.String) continue;

                            if (e.Name == "FullName")
                            {
                                Typen[typ.Name] = e.Value.GetString();
                                gezaehlt++;
                            }
                            else
                            {
                                // Feldschluessel sind nur innerhalb ihres Typs eindeutig.
                                Felder[typ.Name + "/" + e.Name] = e.Value.GetString();
                            }
                        }
                    }

                    if (gezaehlt > 0) _geladen = true;
                }
                catch (Exception ex)
                {
                    Floppy.Core.Log.Warning("Unrailed2: typeNames unlesbar - " + ex.Message);
                }
            }
        }

        /// <summary>Der kurze, lesbare Name eines Typs - ohne den langen Namensraum
        /// davor. Aus "common.Unrailed2.Modules.Base.TrainComponent" wird
        /// "TrainComponent".</summary>
        public static string Typ(string schluessel)
        {
            if (schluessel == null) return "?";

            if (!Typen.TryGetValue(schluessel, out string voll)) return schluessel;

            int punkt = voll.LastIndexOf('.');
            return punkt >= 0 && punkt < voll.Length - 1 ? voll.Substring(punkt + 1) : voll;
        }

        public static string VollerTyp(string schluessel)
        {
            return schluessel != null && Typen.TryGetValue(schluessel, out string voll) ? voll : schluessel;
        }

        public static string Feld(string typSchluessel, string feldSchluessel)
        {
            if (feldSchluessel == null) return "?";

            return Felder.TryGetValue(typSchluessel + "/" + feldSchluessel, out string name)
                ? name
                : feldSchluessel;
        }
    }
}
