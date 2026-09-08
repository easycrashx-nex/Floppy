using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Floppy.Unrailed2
{
    /// <summary>Eine Komponentenart in der Welt.
    ///
    /// Schluessel ist die laufende Nummer für Abfragen. NameHash identifiziert den Typ
    /// für Namenstabelle und Schreibbefehle; die beiden Werte sind nicht austauschbar.</summary>
    internal sealed class Komponentenart
    {
        public string Schluessel;
        public string NameHash;
        public int Anzahl;

        public string Anzeige => Namen.Typ(NameHash ?? Schluessel);

        public override string ToString() => Anzeige + " (" + Anzahl + ")";
    }

    /// <summary>Ein Feld einer Komponente an einer bestimmten Entitaet.</summary>
    internal sealed class Feld
    {
        public long Entitaet;
        public string TypSchluessel;
        public string TypNameHash;
        public string Schluessel;
        public string Wert;
        public bool IsNumber;

        public string Anzeige => Namen.Feld(TypNameHash ?? TypSchluessel, Schluessel);

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

        public static bool ImSpiel => !string.IsNullOrWhiteSpace(Lage) && Lage != "unbekannt"
            && !string.Equals(Lage, "Menu", StringComparison.OrdinalIgnoreCase) && Lage != "?";

        private static List<Komponentenart> _arten = new List<Komponentenart>();
        private static List<Komponentenart> _registriert = new List<Komponentenart>();
        private static DateTime _artenGeholt = DateTime.MinValue;
        private static string _weltId;
        public static string Kennung => _weltId;

        private static void VergissWelt()
        {
            _arten = new List<Komponentenart>();
            _registriert = new List<Komponentenart>();
            _artenGeholt = DateTime.MinValue;
            Namen.Vergiss();
        }

        public static void Aktualisiere()
        {
            string vorher = Lage;
            string weltId = null;

            using (var doc = Debugger.Frage("worldInfo"))
            {
                if (doc == null)
                {
                    Lage = "unbekannt";
                    _weltId = null;
                    VergissWelt();
                    return;
                }

                try
                {
                    var cfg = doc.RootElement.GetProperty("config");
                    Lage = cfg.GetArrayLength() > 1 ? cfg[1].GetString() : "?";
                    weltId = Text(doc.RootElement, "id");
                }
                catch { Lage = "?"; }
            }

            // Beim Weltwechsel ist alles Gemerkte hinfaellig: andere Entitaeten, andere
            // Zahlen, unter Umstaenden auch eine andere Namenstabelle.
            if (Lage != vorher || weltId != _weltId)
            {
                VergissWelt();
            }
            _weltId = weltId;

            if (ImSpiel) Namen.LadeFallsNoetig();
        }

        /// <summary>Welche Komponentenarten es in der laufenden Welt gibt.
        ///
        /// Hoechstens alle drei Sekunden neu. Jede Anfrage beantwortet das Spiel in
        /// seinem eigenen Bildtakt - haeufiger nachzufragen kostet dem Spielenden
        /// Bilder, ohne dass die Anzeige spuerbar frischer waere.</summary>
        public static List<Komponentenart> Arten(bool erzwingen = false)
        {
            if (!ImSpiel || !Debugger.Erreichbar) return new List<Komponentenart>();
            if (!erzwingen && (DateTime.UtcNow - _artenGeholt).TotalSeconds < 3)
                return _arten;

            _artenGeholt = DateTime.UtcNow;

            using (var doc = Debugger.Frage("componentMap"))
            {
                if (doc == null)
                {
                    VergissWelt();
                    return _arten;
                }

                var neu = new List<Komponentenart>();

                try
                {
                    foreach (var eintrag in doc.RootElement.EnumerateObject())
                    {
                        var k = eintrag.Value;

                        neu.Add(new Komponentenart
                        {
                            // Queries use the runtime ID; labels and changeValue use the type hash.
                            Schluessel = Text(k, "id") ?? eintrag.Name,
                            NameHash = Text(k, "name"),
                            Anzahl = Ganzzahl(k, "count")
                        });
                    }
                }
                catch (Exception ex)
                {
                    Floppy.Core.Log.Warning("Unrailed2: componentMap unlesbar - " + ex.Message);
                    VergissWelt();
                    return _arten;
                }

                _registriert = neu;
                _arten = neu.Where(a => a.Anzahl > 0)
                            .OrderBy(a => a.Anzeige, StringComparer.OrdinalIgnoreCase)
                            .ToList();
            }

            return _arten;
        }

        public static Komponentenart FindeArt(string schluessel)
        {
            Arten();
            if (!ImSpiel || !Debugger.Erreichbar) return null;
            return _registriert.FirstOrDefault(a => a.Schluessel == schluessel)
                ?? _registriert.FirstOrDefault(a => a.NameHash == schluessel);
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
            var art = FindeArt(typSchluessel);
            // An unknown cTypes filter makes the game return the entire world.
            if (art == null) return raus;

            using (var doc = Debugger.Frage("listEntities?cTypes=" + Debugger.Verpacke(art.Schluessel)))
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
                            string kschl = Text(komp, "id");

                            // listEntities liefert die ganze Entitaet, also auch ihre
                            // uebrigen Komponenten. Uns interessiert nur die gefragte.
                            if (kschl != art.Schluessel) continue;

                            if (!komp.TryGetProperty("fields", out var felder)) continue;
                            if (felder.ValueKind != JsonValueKind.Array) continue;

                            foreach (var f in felder.EnumerateArray())
                            {
                                string fschl = Text(f, "id") ?? Text(f, "name");
                                if (fschl == null) continue;

                                raus.Add(new Feld
                                {
                                    Entitaet = eid,
                                    TypSchluessel = art.Schluessel,
                                    TypNameHash = art.NameHash,
                                    Schluessel = fschl,
                                    IsNumber = f.TryGetProperty("value", out var wert) && wert.ValueKind == JsonValueKind.Number,
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

        /// <summary>Writes the exact type hash used by the game's own web UI and verifies this entity.</summary>
        public static bool Setze(long entitaet, string typSchluessel, string feldSchluessel, string wertAlsJson, int leseVersuche = 1)
        {
            var art = FindeArt(typSchluessel);
            if (!Schreibe(art, entitaet, feldSchluessel, wertAlsJson)) return false;
            for (int versuch = 0; versuch < Math.Max(1, Math.Min(4, leseVersuche)); versuch++)
            {
                if (versuch > 0) System.Threading.Thread.Sleep(50);
                var field = Felder(art.Schluessel).FirstOrDefault(f => f.Entitaet == entitaet && f.Schluessel == feldSchluessel);
                if (field != null && Gleich(field.Wert, wertAlsJson)) return true;
                if (!Debugger.Erreichbar) break;
            }
            return false;
        }

        private static bool Schreibe(Komponentenart art, long entitaet, string feld, string wertAlsJson)
        {
            if (art == null || string.IsNullOrEmpty(art.NameHash) || entitaet < 0 || string.IsNullOrEmpty(feld))
                return false;
            string command = entitaet + "." + art.NameHash + "." + feld + "=" + wertAlsJson;
            return Debugger.Loese("changeValue?command=" + Debugger.Verpacke(command));
        }

        /// <summary>Ein Feld bei mehreren Entitaeten setzen und gemeinsam zuruecklesen.</summary>
        public static int SetzeZahlMehrere(IEnumerable<long> entitaeten, string typ, string feld, double wert)
        {
            var art = FindeArt(typ);
            if (art == null) return 0;
            string json = wert.ToString("R", CultureInfo.InvariantCulture);
            var gesendet = new HashSet<long>();
            foreach (long eid in entitaeten.Distinct())
            {
                if (!Schreibe(art, eid, feld, json)) break;
                gesendet.Add(eid);
            }
            if (gesendet.Count == 0) return 0;
            return Felder(art.Schluessel)
                .Where(f => gesendet.Contains(f.Entitaet) && f.Schluessel == feld && Gleich(f.Wert, json))
                .Select(f => f.Entitaet).Distinct().Count();
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

        /// <summary>Vergleicht zwei Werte grosszuegig: Das Spiel gibt eine gesetzte 10
        /// unter Umstaenden als "10.0" zurueck, und ein true als "True".</summary>
        private static bool Gleich(string a, string b)
        {
            a = OhneJsonAnfuehrung(a);
            b = OhneJsonAnfuehrung(b);
            if (string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

            if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                return Math.Abs(x - y) < 0.0001;

            return false;
        }

        private static string OhneJsonAnfuehrung(string wert)
        {
            if (wert == null || !wert.TrimStart().StartsWith("\"", StringComparison.Ordinal)) return wert;
            try
            {
                using var doc = JsonDocument.Parse(wert);
                return doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : wert;
            }
            catch (JsonException) { return wert; }
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
