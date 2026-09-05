using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Floppy.Unrailed2
{
    /// <summary>Eine Komponentenart in der Welt.
    ///
    /// Schluessel ist, womit das Spiel angesprochen wird (eine Zahl). Anzeige ist, was
    /// wir dem Menschen zeigen. Die beiden auseinanderzuhalten ist der ganze Trick:
    /// Wer den lesbaren Namen in den Befehl schreibt, bekommt "Component not found".</summary>
    internal sealed class Komponentenart
    {
        public string Schluessel;
        public int Anzahl;

        public string Anzeige => Namen.Typ(Schluessel);

        public override string ToString() => Anzeige + " (" + Anzahl + ")";
    }

    /// <summary>Ein Feld einer Komponente an einer bestimmten Entitaet.</summary>
    internal sealed class Feld
    {
        public long Entitaet;
        public string TypSchluessel;
        public string Schluessel;
        public string Wert;

        public string Anzeige => Namen.Feld(TypSchluessel, Schluessel);

        public double? Zahl =>
            double.TryParse(Wert, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d : (double?)null;
    }

    /// <summary>Der Blick in die laufende Welt.
    ///
    /// Unrailed 2 ist ein Entity-Component-System: Alles im Spiel ist eine Entitaet mit
    /// angehefteten Komponenten, und jede Komponente ist ein Buendel benannter Felder.
    /// Das Entwicklerwerkzeug legt genau diese Struktur offen - lesend ueber
    /// listEntities, schreibend ueber changeValue.
    ///
    /// Deshalb stehen hier keine fest verdrahteten Feldnamen. Wir fragen das Spiel,
    /// was es gerade gibt. Das ueberlebt auch ein Spielupdate, das etwas umbenennt:
    /// Dann verschwindet ein Eintrag, statt dass blind in etwas Falsches geschrieben
    /// wird.</summary>
    internal static class Welt
    {
        /// <summary>Der rohe Weltname aus worldInfo - "Menu" oder der Spielmodus.</summary>
        public static string Lage { get; private set; } = "unbekannt";

        public static bool ImSpiel => Lage != "unbekannt" && Lage != "Menu" && Lage != "?";

        private static List<Komponentenart> _arten = new List<Komponentenart>();
        private static DateTime _artenGeholt = DateTime.MinValue;

        public static void Aktualisiere()
        {
            string vorher = Lage;

            using (var doc = Debugger.Frage("worldInfo"))
            {
                if (doc == null) { Lage = "unbekannt"; return; }

                try
                {
                    var cfg = doc.RootElement.GetProperty("config");
                    Lage = cfg.GetArrayLength() > 1 ? cfg[1].GetString() : "?";
                }
                catch { Lage = "?"; }
            }

            // Beim Weltwechsel ist alles Gemerkte hinfaellig: andere Entitaeten, andere
            // Zahlen, unter Umstaenden auch eine andere Namenstabelle.
            if (Lage != vorher)
            {
                _arten = new List<Komponentenart>();
                _artenGeholt = DateTime.MinValue;
                Namen.Vergiss();
            }

            Namen.LadeFallsNoetig();
        }

        /// <summary>Welche Komponentenarten es in der laufenden Welt gibt.
        ///
        /// Hoechstens alle drei Sekunden neu. Jede Anfrage beantwortet das Spiel in
        /// seinem eigenen Bildtakt - haeufiger nachzufragen kostet dem Spielenden
        /// Bilder, ohne dass die Anzeige spuerbar frischer waere.</summary>
        public static List<Komponentenart> Arten(bool erzwingen = false)
        {
            if (!erzwingen && (DateTime.UtcNow - _artenGeholt).TotalSeconds < 3)
                return _arten;

            _artenGeholt = DateTime.UtcNow;

            using (var doc = Debugger.Frage("componentMap"))
            {
                if (doc == null) return _arten;

                var neu = new List<Komponentenart>();

                try
                {
                    foreach (var eintrag in doc.RootElement.EnumerateObject())
                    {
                        var k = eintrag.Value;

                        neu.Add(new Komponentenart
                        {
                            // Der Schluessel des Eintrags ist das, womit das Spiel die
                            // Komponente selbst benennt. Ein "id"-Feld darin hat Vorrang,
                            // falls es eins gibt.
                            Schluessel = Text(k, "id") ?? eintrag.Name,
                            Anzahl = Ganzzahl(k, "count")
                        });
                    }
                }
                catch (Exception ex)
                {
                    Floppy.Core.Log.Warning("Unrailed2: componentMap unlesbar - " + ex.Message);
                    return _arten;
                }

                _arten = neu.Where(a => a.Anzahl > 0)
                            .OrderBy(a => a.Anzeige, StringComparer.OrdinalIgnoreCase)
                            .ToList();
            }

            return _arten;
        }

        public static Komponentenart FindeArt(string schluessel)
        {
            return Arten().FirstOrDefault(a => a.Schluessel == schluessel);
        }

        /// <summary>Die Entitaet, die eine bestimmte Komponente traegt. Bei den
        /// Singleton-Komponenten - Spielzustand, Cheats, Teams - gibt es genau eine.</summary>
        public static long Traeger(string typSchluessel)
        {
            var felder = Felder(typSchluessel);
            return felder.Count > 0 ? felder[0].Entitaet : -1;
        }

        /// <summary>Alle Felder aller Entitaeten, die diese Komponente tragen.</summary>
        public static List<Feld> Felder(string typSchluessel)
        {
            var raus = new List<Feld>();
            if (string.IsNullOrEmpty(typSchluessel)) return raus;

            using (var doc = Debugger.Frage("listEntities?cTypes=" + Debugger.Verpacke(typSchluessel)))
            {
                if (doc == null) return raus;

                try
                {
                    foreach (var ent in Liste(doc.RootElement))
                    {
                        long eid = Ganzzahl64(ent, "eid");

                        if (!ent.TryGetProperty("components", out var komps)) continue;
                        if (komps.ValueKind != JsonValueKind.Array) continue;

                        foreach (var komp in komps.EnumerateArray())
                        {
                            string kschl = Text(komp, "id") ?? Text(komp, "name") ?? typSchluessel;

                            // listEntities liefert die ganze Entitaet, also auch ihre
                            // uebrigen Komponenten. Uns interessiert nur die gefragte.
                            if (kschl != typSchluessel) continue;

                            if (!komp.TryGetProperty("fields", out var felder)) continue;
                            if (felder.ValueKind != JsonValueKind.Array) continue;

                            foreach (var f in felder.EnumerateArray())
                            {
                                string fschl = Text(f, "id") ?? Text(f, "name");
                                if (fschl == null) continue;

                                raus.Add(new Feld
                                {
                                    Entitaet = eid,
                                    TypSchluessel = typSchluessel,
                                    Schluessel = fschl,
                                    Wert = Rohwert(f)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Floppy.Core.Log.Warning("Unrailed2: listEntities unlesbar - " + ex.Message);
                }
            }

            return raus;
        }

        public static string Lies(string typSchluessel, string feldSchluessel)
        {
            return Felder(typSchluessel)
                   .FirstOrDefault(f => f.Schluessel == feldSchluessel)?.Wert;
        }

        // ---------------------------------------------------------------- Schreiben

        /// <summary>Schreibt ein Feld - und prueft nach, ob es angekommen ist.
        ///
        /// Der Server antwortet auf changeValue auch dann mit 200, wenn er den Befehl
        /// gar nicht verwerten konnte; die Fehlermeldungen ("Component not found or not
        /// registered", "Field '{0}.{1}' not found") landen im Spielprotokoll, nicht in
        /// der Antwort. Ein blindes "hat geklappt" waere hier also gelogen. Deshalb
        /// lesen wir den Wert danach zurueck und vergleichen.
        ///
        /// Der Rueckvergleich hat einen zweiten Zweck: Er sagt uns, welche Schreibweise
        /// das Spiel ueberhaupt annimmt. Die Weboberflaeche des Spiels baut den Befehl
        /// aus Werten, die sie selbst vom Server bekommen hat - ob darin Zahlen oder
        /// Namen stehen, haengt an der Fassung. Wir probieren die Zahlenform zuerst,
        /// merken uns bei Misserfolg die Namensform und nehmen ab dann die, die geht.</summary>
        public static bool Setze(long entitaet, string typSchluessel, string feldSchluessel, string wertAlsJson)
        {
            if (entitaet < 0) return false;

            // Steht die Schreibweise fest, wird nur noch geschrieben. Die Rueckprobe
            // kostet eine zweite Anfrage, und die beantwortet das Spiel in seinem
            // Bildtakt - bei einem Bereich mit zweihundert Entitaeten waere das
            // zweihundertmal Lesen der gesamten Liste. Das merkt der Spielende sofort.
            // Ob es angekommen ist, prueft die Oberflaeche stattdessen einmal am Ende.
            if (_formGeklaert)
            {
                Schicke(entitaet, typSchluessel, feldSchluessel, wertAlsJson, _namensform);
                return true;
            }

            if (Versuche(entitaet, typSchluessel, feldSchluessel, wertAlsJson, _namensform))
            {
                _formGeklaert = true;
                return true;
            }

            bool andere = !_namensform;

            if (Versuche(entitaet, typSchluessel, feldSchluessel, wertAlsJson, andere))
            {
                _namensform = andere;
                _formGeklaert = true;

                Floppy.Core.Log.Info("Unrailed2: Schreibweise geklärt - " +
                                     (andere ? "Namen" : "Zahlen"));
                return true;
            }

            return false;
        }

        /// <summary>Liest ein Feld zurueck und sagt, ob dort der erwartete Wert steht.
        /// Damit prueft die Oberflaeche einen ganzen Schwung Schreibvorgaenge mit einer
        /// einzigen Anfrage nach, statt mit einer je Entitaet.</summary>
        public static int ZaehleTreffer(string typSchluessel, string feldSchluessel, string erwartet)
        {
            return Felder(typSchluessel)
                   .Where(f => f.Schluessel == feldSchluessel)
                   .Count(f => Gleich(f.Wert, erwartet));
        }

        private static bool _namensform;
        private static bool _formGeklaert;

        private static void Schicke(long eid, string typ, string feld, string wert, bool namensform)
        {
            string t = namensform ? Namen.Typ(typ) : typ;
            string f = namensform ? Namen.Feld(typ, feld) : feld;

            Debugger.Loese("changeValue?command=" +
                           Debugger.Verpacke(eid + "." + t + "." + f + "=" + wert));
        }

        private static bool Versuche(long eid, string typ, string feld, string wert, bool namensform)
        {
            Schicke(eid, typ, feld, wert, namensform);

            string jetzt = Lies(typ, feld);
            return jetzt != null && Gleich(jetzt, wert);
        }

        /// <summary>Vergleicht zwei Werte grosszuegig: Das Spiel gibt eine gesetzte 10
        /// unter Umstaenden als "10.0" zurueck, und ein true als "True".</summary>
        private static bool Gleich(string a, string b)
        {
            if (string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

            if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                return Math.Abs(x - y) < 0.0001;

            return false;
        }

        public static bool SetzeZahl(long entitaet, string typ, string feld, double wert)
        {
            // Immer mit Punkt als Trennzeichen. Das Spiel erwartet JSON, nicht das
            // Zahlenformat des Rechners - auf einem deutschen System stuende hier sonst
            // ein Komma, und das Spiel wuerde die Zahl verwerfen.
            return Setze(entitaet, typ, feld, wert.ToString("R", CultureInfo.InvariantCulture));
        }

        public static bool SetzeSchalter(long entitaet, string typ, string feld, bool an)
        {
            return Setze(entitaet, typ, feld, an ? "true" : "false");
        }

        // ------------------------------------------------------------------ Kleinkram

        private static IEnumerable<JsonElement> Liste(JsonElement wurzel)
        {
            if (wurzel.ValueKind == JsonValueKind.Array)
                return wurzel.EnumerateArray();

            if (wurzel.ValueKind == JsonValueKind.Object &&
                wurzel.TryGetProperty("entities", out var e) &&
                e.ValueKind == JsonValueKind.Array)
                return e.EnumerateArray();

            return Enumerable.Empty<JsonElement>();
        }

        private static string Text(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object) return null;
            if (!e.TryGetProperty(name, out var w)) return null;

            if (w.ValueKind == JsonValueKind.String) return w.GetString();
            if (w.ValueKind == JsonValueKind.Number) return w.GetRawText();

            return null;
        }

        private static string Rohwert(JsonElement f)
        {
            if (!f.TryGetProperty("value", out var w)) return "";

            return w.ValueKind == JsonValueKind.String ? w.GetString() : w.GetRawText();
        }

        private static int Ganzzahl(JsonElement e, string name)
        {
            return e.ValueKind == JsonValueKind.Object &&
                   e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Number
                ? w.GetInt32() : 0;
        }

        private static long Ganzzahl64(JsonElement e, string name)
        {
            return e.ValueKind == JsonValueKind.Object &&
                   e.TryGetProperty(name, out var w) && w.ValueKind == JsonValueKind.Number
                ? w.GetInt64() : 0;
        }
    }
}
