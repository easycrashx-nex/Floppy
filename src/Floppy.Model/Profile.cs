using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Floppy.Core.Api;

namespace Floppy.Core
{
    /// <summary>Gespeicherte Cheat-Zusammenstellungen.
    ///
    /// Ein Profil ist eine Datei mit allen Schaltern, Reglern und Auswahlen, so wie sie
    /// gerade stehen. Beim Laden werden sie wieder gesetzt - inklusive der Nebenwirkung,
    /// die ein Cheat beim Umschalten auslöst.
    ///
    /// Absichtlich ausgelassen: Knöpfe und Anzeigen. Ein Knopf hat keinen Zustand, den
    /// man aufheben könnte, und eine Anzeige kommt ohnehin laufend aus dem Spiel.
    ///
    /// Die Profile liegen unter %AppData%\Floppy\Profile\&lt;Spiel&gt; - also außerhalb
    /// des Spielordners. So überleben sie ein Spiel-Update oder eine Neuinstallation.</summary>
    public static class Profile
    {
        /// <summary>Diese Kennungen gehören zur Profilverwaltung selbst und werden
        /// nicht mitgespeichert - sonst würde ein Profil beim Laden die Auswahlliste
        /// verstellen, aus der es gerade geladen wurde.</summary>
        private const string EigenePrefix = "profil.";
        // Der Regressionstest legt Profile ausschließlich in seinem temporären Verzeichnis ab.
        internal static string StorageRoot;

        public static string Ordner
        {
            get
            {
                string spiel = Saeubere(Registry.Module?.ProductName ?? "Allgemein");

                return Path.Combine(
                    StorageRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Floppy", "Profile", spiel);
            }
        }

        private static string Saeubere(string name)
        {
            foreach (char verboten in Path.GetInvalidFileNameChars())
                name = name.Replace(verboten, '_');

            return name.Trim();
        }

        public static List<string> Namen()
        {
            try
            {
                if (!Directory.Exists(Ordner)) return new List<string>();

                return Directory.GetFiles(Ordner, "*.json")
                    .Select(Path.GetFileNameWithoutExtension)
                    .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static bool Speicherbar(CheatOption option)
        {
            if (option == null || string.IsNullOrEmpty(option.Id)) return false;
            if (option.Id.StartsWith(EigenePrefix, StringComparison.OrdinalIgnoreCase)) return false;

            return option.Kind != OptionKind.Button && option.Kind != OptionKind.Info;
        }

        public static string Speichern(string name) { TrySave(name, out string message); return message; }
        public static string Laden(string name) { TryLoad(name, out string message); return message; }
        public static string Loeschen(string name) { TryDelete(name, out string message); return message; }
        public static string Zuruecksetzen() { TryReset(out string message); return message; }

        internal static bool TrySave(string name, out string message)
        {
            string temporary = null;
            try
            {
                name = Saeubere(name ?? "");
                if (name.Length == 0) { message = "Bitte einen Namen eintragen"; return false; }
                var entries = new List<string>();
                foreach (var option in Registry.AllOptions.Where(Speicherbar).OrderBy(o => o.Id, StringComparer.Ordinal))
                {
                    var entry = new Json.Writer().Set("id", option.Id).Set("kind", option.Kind.ToString());
                    switch (option.Kind)
                    {
                        case OptionKind.Toggle: entry.Set("bool", option.BoolValue); break;
                        case OptionKind.Slider:
                        case OptionKind.Number: entry.Set("number", option.NumberValue); break;
                        case OptionKind.Text: entry.Set("text", option.TextValue ?? ""); break;
                        case OptionKind.Choice:
                            entry.Set("choice", option.ChoiceIndex);
                            string selected = option.ChoiceIndex >= 0 && option.ChoiceIndex < (option.Choices?.Length ?? 0)
                                ? option.Choices[option.ChoiceIndex] : "";
                            entry.Set("choiceText", selected);
                            break;
                    }
                    entry.Set("share", option.Scope == CheatScope.Selectable && option.ShareWithOthers);
                    string jsonEntry = entry.ToString();
                    OptionValues.Read(option, Json.Parse(jsonEntry), profile: true);
                    entries.Add(jsonEntry);
                }
                string json = new Json.Writer().Set("version", 1)
                    .Set("spiel", Registry.Module?.ProductName ?? "")
                    .Set("gespeichert", DateTime.Now.ToString("s", CultureInfo.InvariantCulture))
                    .Raw("werte", Json.Array(entries)).ToString();
                Directory.CreateDirectory(Ordner);
                string target = Path.Combine(Ordner, name + ".json");
                temporary = Path.Combine(Ordner, "." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(temporary, json);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                message = "Profil \"" + name + "\" gespeichert (" + entries.Count + " Werte)";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Profil speichern: " + ex);
                message = "Speichern fehlgeschlagen: " + ex.Message;
                return false;
            }
            finally
            {
                if (temporary != null)
                    try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            }
        }

        internal static bool TryLoad(string name, out string message)
        {
            try
            {
                name = Saeubere(name ?? "");
                string path = Path.Combine(Ordner, name + ".json");
                if (name.Length == 0 || !File.Exists(path)) { message = "Profil nicht gefunden"; return false; }
                var data = Json.Parse(File.ReadAllText(path));
                if (data.TryGetValue("spiel", out object game) &&
                    (!(game is string product) || product != (Registry.Module?.ProductName ?? "")))
                    throw new FormatException("Profil gehört zu einem anderen Spiel");
                if (data.TryGetValue("version", out object version) && (!(version is double v) || v != 1))
                    throw new FormatException("Unbekannte Profilversion");
                if (!(data.TryGetValue("werte", out object raw) && raw is List<object> entries))
                    throw new FormatException("Profil enthält keine Werteliste");
                var pending = new List<Tuple<CheatOption, Dictionary<string, object>>>();
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var failures = new List<string>();
                int unknown = 0;
                foreach (object item in entries)
                {
                    if (!(item is Dictionary<string, object> entry)) throw new FormatException("Ungültiger Profileintrag");
                    string id = Json.AlsText(entry, "id");
                    if (string.IsNullOrEmpty(id) || !ids.Add(id)) throw new FormatException("Fehlende oder doppelte Optionskennung");
                    var option = Registry.Find(id);
                    if (!Speicherbar(option)) { unknown++; continue; }
                    // Abhängige Listen werden nach ihrem Kategorie-Callback neu geprüft.
                    OptionValues.Read(option, entry, profile: true, deferChoiceValidation: true);
                    pending.Add(Tuple.Create(option, entry));
                }
                // Typen und Zahlenbereiche sind geprüft. Listen können sich beim Anwenden ändern.
                int applied = 0;
                foreach (var change in pending.OrderBy(p => p.Item1.Id, StringComparer.Ordinal))
                {
                    try
                    {
                        if (!change.Item1.Available) { failures.Add(change.Item1.Label + " nicht verfügbar"); continue; }
                        OptionValues.Read(change.Item1, change.Item2, profile: true).Apply(change.Item1);
                        applied++;
                    }
                    catch (Exception ex)
                    {
                        failures.Add(change.Item1.Label + ": " + ex.Message);
                        Log.Warning("Profil, Option " + change.Item1.Id + ": " + ex.Message);
                    }
                }
                message = "Profil \"" + name + "\": " + applied + " Werte geladen";
                if (unknown > 0) message += ", " + unknown + " nicht mehr vorhanden";
                if (failures.Count > 0) message += "; nicht angewendet: " + string.Join("; ", failures);
                if (applied == 0 && failures.Count == 0) message += " (keine passenden Werte)";
                return applied > 0 && failures.Count == 0;
            }
            catch (Exception ex)
            {
                Log.Error("Profil laden: " + ex);
                message = "Laden fehlgeschlagen: " + ex.Message;
                return false;
            }
        }

        internal static bool TryDelete(string name, out string message)
        {
            try
            {
                name = Saeubere(name ?? "");
                string path = Path.Combine(Ordner, name + ".json");
                if (name.Length == 0 || !File.Exists(path)) { message = "Profil nicht gefunden"; return false; }
                File.Delete(path);
                message = "Profil \"" + name + "\" gelöscht";
                return true;
            }
            catch (Exception ex) { message = "Löschen fehlgeschlagen: " + ex.Message; return false; }
        }

        internal static bool TryReset(out string message)
        {
            int applied = 0;
            var failures = new List<string>();
            foreach (var option in Registry.AllOptions.Where(Speicherbar))
            {
                if (!option.Active && !option.ShareWithOthers) continue;
                var input = new Dictionary<string, object>();
                if (option.Kind == OptionKind.Toggle) input["bool"] = option.Ruhebool;
                if ((option.Kind == OptionKind.Number || option.Kind == OptionKind.Slider) && option.Ruhewert.HasValue)
                    input["number"] = (double)option.Ruhewert.Value;
                if (option.Kind == OptionKind.Choice && option.Ruheauswahl.HasValue)
                    input["choice"] = (double)option.Ruheauswahl.Value;
                if (option.Scope == CheatScope.Selectable) input["share"] = false;
                try { OptionValues.Read(option, input).Apply(option); applied++; }
                catch (Exception ex)
                {
                    failures.Add(option.Label + ": " + ex.Message);
                    Log.Warning("Zurücksetzen, Option " + option.Id + ": " + ex.Message);
                }
            }
            message = applied == 0 ? "Keine aktiven Änderungen zurückgesetzt" : applied + " Änderungen zurückgesetzt";
            if (failures.Count > 0) message += "; fehlgeschlagen: " + string.Join("; ", failures);
            return failures.Count == 0;
        }

        private static void Report(CheatOption option, bool success, string message)
        {
            if (success) option.Message = message;
            else option.Fail(message);
        }
        // ---------------------------------------------------------------- Oberfläche

        /// <summary>Die Rubrik, die jedes Spiel automatisch dazubekommt.
        ///
        /// Sie wird aus denselben Bausteinen gebaut wie die Cheats selbst - dadurch
        /// erscheint sie ohne weiteres Zutun sowohl im Ingame-Menü als auch im Client.</summary>
        public static CheatCategory BaueRubrik()
        {
            var kategorie = new CheatCategory("Profile");

            var auswahl = new CheatOption
            {
                Id = EigenePrefix + "liste",
                Label = "Gespeicherte Profile",
                Kind = OptionKind.Choice,
                Choices = Namen().ToArray()
            };

            var name = new CheatOption
            {
                Id = EigenePrefix + "name",
                Label = "Name",
                Description = "Für ein neues Profil. Leer lassen, um das oben gewählte zu überschreiben.",
                Kind = OptionKind.Text
            };

            kategorie.Add(auswahl);
            kategorie.Add(name);

            kategorie.Add(new CheatOption
            {
                Id = EigenePrefix + "speichern",
                Label = "Speichern",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    string ziel = (name.TextValue ?? "").Trim();

                    if (ziel.Length == 0 && auswahl.Choices.Length > 0)
                        ziel = auswahl.Choices[Math.Max(0,
                            Math.Min(auswahl.ChoiceIndex, auswahl.Choices.Length - 1))];

                    bool success = TrySave(ziel, out string message);
                    Report(o, success, message);
                    Auffrischen(auswahl, ziel);
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = EigenePrefix + "laden",
                Label = "Laden",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    if (auswahl.Choices.Length == 0) { o.Fail("Noch kein Profil da"); return; }

                    int index = Math.Max(0, Math.Min(auswahl.ChoiceIndex, auswahl.Choices.Length - 1));
                    bool success = TryLoad(auswahl.Choices[index], out string message);
                    Report(o, success, message);
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = EigenePrefix + "loeschen",
                Label = "Löschen",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    if (auswahl.Choices.Length == 0) { o.Fail("Noch kein Profil da"); return; }

                    int index = Math.Max(0, Math.Min(auswahl.ChoiceIndex, auswahl.Choices.Length - 1));
                    bool success = TryDelete(auswahl.Choices[index], out string message);
                    Report(o, success, message);
                    Auffrischen(auswahl, "");
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = EigenePrefix + "zuruecksetzen",
                Label = "Alle Cheats aus",
                Description = "Setzt aktive Änderungen auf die vom Spielmodul festgelegten Normalwerte zurück.",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    bool success = TryReset(out string message);
                    Report(o, success, message);
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = EigenePrefix + "ordner",
                Label = "Abgelegt unter",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Ordner
            });

            return kategorie;
        }

        /// <summary>Liste neu einlesen und möglichst wieder auf denselben Eintrag zeigen.</summary>
        private static void Auffrischen(CheatOption auswahl, string bevorzugt)
        {
            auswahl.Choices = Namen().ToArray();

            int index = Array.FindIndex(auswahl.Choices,
                n => string.Equals(n, bevorzugt, StringComparison.OrdinalIgnoreCase));

            auswahl.ChoiceIndex = index < 0 ? 0 : index;
        }
    }
}

