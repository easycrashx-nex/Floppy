using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;

namespace Floppy.Unreal
{
    /// <summary>Cheats für Mortal Shell II.
    ///
    /// Anders als bei unseren anderen Spielen läuft dieses Modul nicht im Spiel,
    /// sondern im Client - es liest und schreibt den Speicher des laufenden Spiels von
    /// außen. Nach außen sieht es trotzdem aus wie jedes andere Modul, deshalb
    /// funktionieren Client, Profile und alles Übrige unverändert.
    ///
    /// Die Cheatliste ist nicht abgetippt: Sie entsteht aus dem, was das Spiel selbst
    /// über seine Werte erzählt. Kommt in einem Update ein Attribut dazu, steht es
    /// beim nächsten Start von allein in der Liste.</summary>
    public sealed class MortalShellModule : IGameModule, IDisposable
    {
        public string ProductName => "MortalShell2";
        public string DisplayName => "Mortal Shell II";

        private readonly Spiel _spiel;
        private DateTime _letzteSuche = DateTime.MinValue;
        private string _letzteWelt = "";
        private ulong _letzteWeltKennung;
        private bool _wiederherstellungAusstehend;
        private string _attributSchema = "";

        public MortalShellModule() : this(new Spiel()) { }
        internal MortalShellModule(Spiel spiel) { _spiel = spiel; }

        /// <summary>Gemerkte Orte, wie bei Stonewards - nur solange dasselbe Gebiet lädt.</summary>
        private readonly List<(string Name, double X, double Y, double Z)> _orte = new();

        /// <summary>Welche Werte du gesetzt hast.
        ///
        /// Sie stehen im Speicher, nicht im Spielstand - nach jedem Ladebildschirm baut
        /// das Spiel seine Figur neu auf und alles ist wieder auf Anfang. Gemerkt wird
        /// nur die Kennung; den Wert hält der Cheat selbst, und ihn erneut zu melden
        /// schreibt ihn wieder ins Spiel.</summary>
        private readonly HashSet<string> _gesetzt = new(StringComparer.OrdinalIgnoreCase);

        public bool NachLadenWiederSetzen { get; set; } = true;

        public void Initialize()
        {
            _spiel.Verbinde();
        }

        public bool IsReady(out string status)
        {
            if (!_spiel.Verbunden)
            {
                status = "Mortal Shell II läuft nicht";
                return false;
            }

            if (!_spiel.Bereit)
            {
                status = "Warte auf geladenes Spiel";
                return false;
            }

            status = "Bereit - " + _spiel.AnzahlWerte + " Werte in " + _spiel.Welt;
            return true;
        }

        public void Update()
        {
            // Nach einem Ladebildschirm ist die Figur eine neue - alle Zeiger von
            // vorher zeigen dann ins Nichts.
            if ((DateTime.UtcNow - _letzteSuche).TotalSeconds < 2) return;
            _letzteSuche = DateTime.UtcNow;
            Aktualisiere();
        }

        internal void Aktualisiere()
        {
            if (!_spiel.Verbunden)
            {
                _spiel.Trenne();
                _wiederherstellungAusstehend = true;
                _spiel.Verbinde();
                if (!_spiel.Verbunden) return;
            }

            string welt = _spiel.Welt;
            bool gewechselt = welt != _letzteWelt || _spiel.WeltKennung != _letzteWeltKennung;
            if (gewechselt)
            {
                _letzteWelt = welt;
                _letzteWeltKennung = _spiel.WeltKennung;
                _orte.Clear();
                var orte = Registry.Find("ort.liste");
                if (orte != null) { orte.Choices = Array.Empty<string>(); orte.ChoiceIndex = 0; }
                _wiederherstellungAusstehend = true;
            }

            if (!_spiel.Bereit) _wiederherstellungAusstehend = true;
            if (gewechselt || _wiederherstellungAusstehend)
            {
                if (!SucheUndAktualisiere()) return;
                if (_wiederherstellungAusstehend && NachLadenWiederSetzen) WendeWiederAn();
                _wiederherstellungAusstehend = false;
            }

            Anzeigen();
        }

        private string AttributSchema() => string.Join("\n",
            _spiel.Attribute.Select(a => "attr." + a.Satz + "." + a.Name)
                .Concat(_spiel.Speicherstand.Select(a => "stand." + a.Name))
                .OrderBy(id => id, StringComparer.Ordinal));

        private bool SucheUndAktualisiere()
        {
            if (!_spiel.Suche()) return false;
            if (AttributSchema() != _attributSchema && ReferenceEquals(Registry.Module, this))
                Registry.RefreshCategories();
            return true;
        }

        public void Dispose() => _spiel.Trenne();

        public void SetMenuOpen(bool open) { }

        /// <summary>Schreibt die Werte erneut ins Spiel, die du gesetzt hattest.
        ///
        /// Wir melden dazu einfach jedem betroffenen Cheat, dass sich sein Wert geändert
        /// hat - er schreibt ihn dann selbst wieder hin. So braucht es keine zweite
        /// Stelle, an der Werte gespeichert werden und mit der Zeit auseinanderlaufen.</summary>
        public int WendeWiederAn()
        {
            int gemacht = 0;

            foreach (string id in _gesetzt.ToArray())
            {
                var option = Registry.Find(id);
                if (option == null) continue;

                try { option.NotifyChanged(); gemacht++; }
                catch (Exception) { }
            }

            return gemacht;
        }

        /// <summary>Die Anzeigezeilen nachziehen - sie haben nichts zum Anklicken.</summary>
        private void Anzeigen()
        {
            foreach (var option in Registry.AllOptions)
                if (option.Kind == OptionKind.Info && option.OnChanged != null)
                    option.OnChanged(option);
        }

        // ------------------------------------------------------------------ Rubriken

        /// <summary>Deutsche Überschriften für die Attributsätze des Spiels.</summary>
        private static readonly Dictionary<string, string> Ueberschrift = new()
        {
            ["SpartaHealthSet"] = "Leben und Widerstand",
            ["SpartaCombatSet"] = "Kampf",
            ["PlayerAttributeSet"] = "Spieler",
            ["SidearmAttributeSet"] = "Seitenwaffe",
            ["DarkPowerAttributeSet"] = "Dunkle Kraft",
            ["ShellAbilityAttributeSet"] = "Hüllenfähigkeit",
        };

        public List<CheatCategory> BuildCategories()
        {
            var raus = new List<CheatCategory> { Uebersicht(), Gegenstaende(), Orte() };
            raus.AddRange(SpielEigene());

            foreach (string satz in _spiel.Saetze)
                raus.Add(AusSatz(satz));

            if (_spiel.Speicherstand.Count > 0) raus.Add(Fortschritt());

            _attributSchema = AttributSchema();
            return raus;
        }

        /// <summary>Die Cheats, die das Spiel selbst mitbringt - nach Rubriken sortiert.
        ///
        /// Wo ein Wert dazugehört, steht er als eigenes Feld oben in der Rubrik. So
        /// bleiben die Knöpfe Knöpfe, statt dass jeder Cheat zwei Zeilen braucht.</summary>
        private IEnumerable<CheatCategory> SpielEigene()
        {
            foreach (var gruppe in Cheats.Alle.GroupBy(e => e.Rubrik))
            {
                var k = new CheatCategory(gruppe.Key);

                bool braucht = gruppe.Any(e => e.Parameter == Cheats.Art.Ganzzahl ||
                                               e.Parameter == Cheats.Art.Komma ||
                                               e.Parameter == Cheats.Art.Fliess);

                CheatOption menge = null;

                if (braucht)
                {
                    double vorgabe = gruppe.First(e => e.Parameter != Cheats.Art.Ohne &&
                                                       e.Parameter != Cheats.Art.Schalter).Vorgabe;

                    menge = new CheatOption
                    {
                        Id = "menge." + gruppe.Key,
                        Label = "Menge",
                        Description = "Gilt für die Knöpfe darunter.",
                        Kind = OptionKind.Number,
                        Min = -1000000f,
                        Max = 100000000f,
                        NumberValue = (float)vorgabe
                    };

                    k.Add(menge);
                }

                foreach (var eintrag in gruppe)
                {
                    var e = eintrag;

                    if (e.Parameter == Cheats.Art.Schalter)
                    {
                        k.Add(new CheatOption
                        {
                            Id = "cheat." + e.Funktion,
                            Label = e.Label,
                            Description = e.Hinweis,
                            Kind = OptionKind.Toggle,
                            OnChanged = o => o.Message = _spiel.RufeCheat(e, o.BoolValue ? 1 : 0)
                        });

                        continue;
                    }

                    var feld = menge;

                    k.Add(new CheatOption
                    {
                        Id = "cheat." + e.Funktion,
                        Label = e.Label,
                        Description = e.Hinweis,
                        Kind = OptionKind.Button,
                        OnInvoke = o =>
                        {
                            double wert = e.Parameter == Cheats.Art.Ohne
                                ? 0
                                : (feld?.NumberValue ?? e.Vorgabe);

                            o.Message = _spiel.RufeCheat(e, wert);
                        }
                    });
                }

                yield return k;
            }
        }

        private CheatCategory Uebersicht()
        {
            var k = new CheatCategory("Übersicht");

            k.Add(new CheatOption
            {
                Id = "info.stand",
                Label = "Zustand",
                Kind = OptionKind.Info,
                OnChanged = o =>
                {
                    var leben = _spiel.Finde("SpartaHealthSet", "Health");
                    var max = _spiel.Finde("SpartaHealthSet", "MaxHealth");

                    o.TextValue = leben == null
                        ? "keine Figur geladen"
                        : string.Format("Leben {0:0}/{1:0}   |   {2} Werte   |   {3}",
                            leben.Wert(_spiel.Sp), max?.Wert(_spiel.Sp) ?? 0,
                            _spiel.AnzahlWerte, _spiel.Welt);
                }
            });

            k.Add(new CheatOption
            {
                Id = "info.position",
                Label = "Position",
                Kind = OptionKind.Info,
                OnChanged = o =>
                {
                    var (x, y, z) = _spiel.Position();
                    o.TextValue = string.Format("{0:0} / {1:0} / {2:0}", x, y, z);
                }
            });

            k.Add(new CheatOption
            {
                Id = "info.neusuchen",
                Label = "Werte neu einlesen",
                Description = "Nach einem Ladebildschirm passiert das von selbst - " +
                              "hier nur, falls doch mal etwas nicht stimmt.",
                Kind = OptionKind.Button,
                OnInvoke = o => o.Message = SucheUndAktualisiere()
                    ? _spiel.AnzahlWerte + " Werte gefunden"
                    : "Keine Figur gefunden"
            });

            k.Add(new CheatOption
            {
                Id = "info.wiedersetzen",
                Label = "Werte nach dem Laden wieder setzen",
                Description = "Deine Werte stehen im Speicher, nicht im Spielstand - nach einem " +
                              "Ladebildschirm wären sie sonst weg.",
                Kind = OptionKind.Toggle,
                BoolValue = true,
                Ruhebool = true,
                OnChanged = o => NachLadenWiederSetzen = o.BoolValue
            });

            k.Add(new CheatOption
            {
                Id = "info.jetztsetzen",
                Label = "Werte jetzt wieder setzen",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    int n = WendeWiederAn();
                    o.Message = n == 0 ? "Du hast noch nichts gesetzt" : n + " Werte wieder gesetzt";
                }
            });

            k.Add(new CheatOption
            {
                Id = "info.vollheilen",
                Label = "Voll heilen",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    var leben = _spiel.Finde("SpartaHealthSet", "Health");
                    var max = _spiel.Finde("SpartaHealthSet", "MaxHealth");

                    if (leben == null || max == null) { o.Message = "Keine Figur"; return; }

                    leben.Setze(_spiel.Sp, max.Wert(_spiel.Sp));
                    o.Message = "Aufgefüllt";
                }
            });

            return k;
        }

        /// <summary>Baut aus einem Attributsatz eine Rubrik - ein Feld je Wert.</summary>
        private CheatCategory AusSatz(string satz)
        {
            string titel = Ueberschrift.TryGetValue(satz, out string t) ? t : satz;
            var k = new CheatCategory(titel);

            foreach (var attribut in _spiel.Attribute.Where(a => a.Satz == satz))
            {
                // Nur Namen merken, nicht das Objekt: Nach einem Ladebildschirm wird die
                // Figur neu erzeugt und alle Adressen von vorher zeigen ins Leere. Wer
                // hier den Fund festhält, schreibt später in freigegebenen Speicher.
                string wo = satz;
                string was = attribut.Name;

                k.Add(new CheatOption
                {
                    Id = "attr." + satz + "." + attribut.Name,
                    Label = Lesbar(attribut.Name),
                    Kind = OptionKind.Number,
                    Scope = CheatScope.OnlyMe,
                    Min = -100000f,
                    Max = 1000000f,
                    NumberValue = attribut.Wert(_spiel.Sp),
                    OnChanged = o =>
                    {
                        _gesetzt.Add(o.Id);

                        var jetzt = _spiel.Finde(wo, was);
                        if (jetzt != null) jetzt.Setze(_spiel.Sp, o.NumberValue);
                    }
                });
            }

            return k;
        }

        private CheatCategory Fortschritt()
        {
            var k = new CheatCategory("Speicherstand");

            k.Add(new CheatOption
            {
                Id = "stand.hinweis",
                Label = "Achtung",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue =
                    "Diese Werte landen beim nächsten Speichern dauerhaft im Spielstand."
            });

            foreach (var zahl in _spiel.Speicherstand)
            {
                string was = zahl.Name;

                k.Add(new CheatOption
                {
                    Id = "stand." + zahl.Name,
                    Label = Lesbar(zahl.Name),
                    Kind = OptionKind.Number,
                    Scope = CheatScope.OnlyMe,
                    Min = 0f,
                    Max = 100000000f,
                    NumberValue = (float)zahl.Wert(_spiel.Sp),
                    OnChanged = o =>
                    {
                        _gesetzt.Add(o.Id);

                        var jetzt = _spiel.Speicherstand.FirstOrDefault(e => e.Name == was);
                        if (jetzt != null) jetzt.Setze(_spiel.Sp, o.NumberValue);
                    }
                });
            }

            return k;
        }

        /// <summary>Einzelne Gegenstaende - aus der Tabelle des Spiels, nicht abgetippt.</summary>
        private CheatCategory Gegenstaende()
        {
            var k = new CheatCategory("Gegenstände");

            var auswahl = new CheatOption
            {
                Id = "item.was",
                Label = "Gegenstand",
                Kind = OptionKind.Choice,
                Choices = _spiel.Gegenstaende.Select(g => g.Name).ToArray()
            };

            var menge = new CheatOption
            {
                Id = "item.menge",
                Label = "Anzahl",
                Kind = OptionKind.Number,
                Min = 1f, Max = 999f,
                NumberValue = 1f
            };

            k.Add(auswahl);
            k.Add(menge);

            k.Add(new CheatOption
            {
                Id = "item.geben",
                Label = "Ins Inventar legen",
                Description = "Ruft dieselbe Funktion auf, die das Spiel beim Aufheben benutzt.",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    var liste = _spiel.Gegenstaende;
                    if (liste.Count == 0) { o.Message = "Tabelle noch nicht geladen"; return; }

                    var ziel = liste[Math.Clamp(auswahl.ChoiceIndex, 0, liste.Count - 1)];
                    bool gut = _spiel.GibGegenstand(ziel.Kennung, (int)menge.NumberValue) == "Erledigt";

                    o.Message = gut ? ziel.Name + " dazu" : "Ging nicht";
                }
            });

            k.Add(new CheatOption
            {
                Id = "item.anzahl",
                Label = "Bekannte Gegenstände",
                Kind = OptionKind.Info,
                OnChanged = o =>
                {
                    // Die Tabelle kommt erst mit der geladenen Welt - dann nachtragen
                    if (auswahl.Choices.Length == 0 && _spiel.Gegenstaende.Count > 0)
                        auswahl.Choices = _spiel.Gegenstaende.Select(g => g.Name).ToArray();

                    o.TextValue = _spiel.Gegenstaende.Count.ToString();
                }
            });

            return k;
        }

        private CheatCategory Orte()
        {
            var k = new CheatCategory("Orte");

            var liste = new CheatOption
            {
                Id = "ort.liste",
                Label = "Ort",
                Kind = OptionKind.Choice
            };

            var name = new CheatOption
            {
                Id = "ort.name",
                Label = "Name",
                Description = "Leer lassen für eine fortlaufende Nummer.",
                Kind = OptionKind.Text
            };

            k.Add(liste);
            k.Add(name);

            k.Add(new CheatOption
            {
                Id = "ort.merken",
                Label = "Hier merken",
                Description = "Beim Gebietswechsel wird die Liste verworfen - Koordinaten " +
                              "aus einem anderen Gebiet wären dort wertlos.",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    var (x, y, z) = _spiel.Position();
                    if (x == 0 && y == 0 && z == 0) { o.Message = "Keine Figur"; return; }

                    string wie = (name.TextValue ?? "").Trim();
                    if (wie.Length == 0) wie = "Ort " + (_orte.Count + 1);

                    _orte.RemoveAll(e => string.Equals(e.Name, wie, StringComparison.OrdinalIgnoreCase));
                    _orte.Add((wie, x, y, z));

                    liste.Choices = _orte.Select(e => e.Name).ToArray();
                    name.TextValue = "";
                    o.Message = "\"" + wie + "\" gemerkt";
                }
            });

            k.Add(new CheatOption
            {
                Id = "ort.hin",
                Label = "Dorthin springen",
                Description = "Ruft die Sprungfunktion des Spiels auf - dieselbe, die auch " +
                              "ein Aufzug benutzt.",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    if (_orte.Count == 0) { o.Message = "Noch nichts gemerkt"; return; }

                    var ziel = _orte[Math.Clamp(liste.ChoiceIndex, 0, _orte.Count - 1)];
                    string ergebnis = _spiel.SetzePosition(ziel.X, ziel.Y, ziel.Z);

                    o.Message = ergebnis == "Umgesetzt" ? "Bei \"" + ziel.Name + "\"" : ergebnis;
                }
            });

            k.Add(new CheatOption
            {
                Id = "ort.weg",
                Label = "Alle löschen",
                Kind = OptionKind.Button,
                OnInvoke = o =>
                {
                    int n = _orte.Count;
                    _orte.Clear();
                    liste.Choices = Array.Empty<string>();
                    o.Message = n + " gelöscht";
                }
            });

            return k;
        }

        /// <summary>Aus BaseKnockbackStrength wird "Base Knockback Strength".</summary>
        private static string Lesbar(string name)
        {
            var raus = new System.Text.StringBuilder(name.Length + 8);

            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    raus.Append(' ');

                raus.Append(name[i]);
            }

            return raus.ToString();
        }
    }
}
