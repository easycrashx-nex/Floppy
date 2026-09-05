using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core;
using Floppy.Core.Api;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Cheats für "Stonewards".
    ///
    /// Das Spiel läuft über Mirror: einer hostet, die anderen sind Gäste. Deshalb ist
    /// jeder Cheat hier danach sortiert, wen er trifft:
    ///
    ///   "nur ich"      - Werte deiner Figur, Ausdauer, Mana. Liegt auf deinem Rechner.
    ///   "betrifft alle"- alles, was in der Welt landet oder am Server hängt.
    ///
    /// Was nur der Gastgeber darf, ist als solches gekennzeichnet und bei Gästen
    /// ausgegraut - statt still nichts zu tun.</summary>
    public class StonewardsModule : IGameModule
    {
        public string ProductName => "Stonewards";
        public string DisplayName => "Stonewards";

        public void Initialize()
        {
            Patches.Apply(Plugin.Harmony);
            Sicht.Spawn();
        }

        public bool IsReady(out string status)
        {
            status = Game.Rolle;
            return Game.ImSpiel;
        }

        public void Update()
        {
            // Vor allem anderen: Der Kartenwechsel passiert genau dann, wenn wir
            // gerade NICHT in einer Runde sind - die Prüfung darf deshalb nicht hinter
            // der "läuft eine Runde"-Abfrage stehen.
            Sprung.PruefeSzene();

            Loops.Tick();
            Flug.Tick();                 // jeden Frame - sonst ruckelt das Steigen
            AktualisiereGegenstandsListe();
        }

        public void SetMenuOpen(bool open)
        {
            // Stonewards hat kein eigenes Eingabeschloss, das wir umlegen könnten.
            // Das Overlay fängt die Maus bereits selbst ab.
        }

        private static Func<bool> NurHost => () => Game.IstHost;

        // ------------------------------------------------------------------ Aufbau

        /// <summary>Sagt der Profilverwaltung, was bei diesen Cheats "aus" heißt.
        ///
        /// Bei einem Regler, der bei null anfängt, ist die Null der Ruhezustand -
        /// "Alles aus" darf ihn dorthin zurückdrehen. Ohne diese Angabe fasst die
        /// Profilverwaltung Zahlen gar nicht an, damit sie nirgends eine Null in einen
        /// Wert schreibt, der keine verträgt.</summary>
        private static List<CheatCategory> MitRuhewerten(List<CheatCategory> kategorien)
        {
            foreach (var kategorie in kategorien)
                foreach (var option in kategorie.Options)
                    if (option.Kind == OptionKind.Slider && option.Min == 0f)
                        option.Ruhewert = 0f;

            return kategorien;
        }

        public List<CheatCategory> BuildCategories()
        {
            return MitRuhewerten(new List<CheatCategory>
            {
                Ich(),
                Kampfwerte(),
                Bewegung(),
                Orte(),
                SichtRubrik(),
                Graben(),
                Waehrung(),
                Fortschritt(),
                Aufwertungen(),
                Gegner(),
                Runde(),
                Herbeirufen()
            });
        }

        private static CheatCategory Ich()
        {
            var kategorie = new CheatCategory("Ich");

            kategorie.Add(new CheatOption
            {
                Id = "ich.info",
                Label = "Aktueller Stand",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = string.Format(
                    "Leben {0:0}/{1:0}   |   Ausdauer {2:0}/{3:0}   |   Mana {4:0}/{5:0}",
                    Game.Leben, Game.MaxLeben,
                    Game.Ausdauer, Game.MaxAusdauer,
                    Game.Mana, Game.MaxMana)
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.ausdauer",
                Label = "Unbegrenzte Ausdauer",
                Description = "Der Verbrauch findet gar nicht erst statt. Rein bei dir.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Patches.UnbegrenzteAusdauer = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.mana",
                Label = "Unbegrenztes Mana",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Patches.UnbegrenztesMana = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.lebenhalten",
                Label = "Leben halten",
                Description = "Füllt dein Leben nach, sobald es unter die Schwelle fällt. " +
                              "Funktioniert auch als Gast und wirkt nur auf deine Figur.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Loops.LebenHalten = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.schwelle",
                Label = "Nachfüllen ab",
                Description = "Prozent des Maximums.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 10f, Max = 99f, Step = 5f,
                NumberValue = 90f,
                OnChanged = o => Loops.LebenSchwelle = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.unverwundbar",
                Label = "Unverwundbar",
                Description = "Fängt den Schaden ab, bevor er verrechnet wird - nur für deine Figur. " +
                              "Das passiert beim Gastgeber, deshalb geht es nur, wenn du selbst hostest. " +
                              "Als Gast nimm \"Leben halten\".",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                IsAvailable = NurHost,
                OnChanged = o => Patches.Unverwundbar = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.status",
                Label = "Keine Statuseffekte",
                Description = "Gift, Feuer, Frost und Verlangsamung greifen bei dir nicht mehr. " +
                              "Wird beim Gastgeber vergeben - geht deshalb nur, wenn du hostest.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                IsAvailable = NurHost,
                OnChanged = o => Patches.KeineStatuseffekte = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.heilen",
                Label = "Voll heilen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => { Game.VollHeilen(); o.Message = "Aufgefüllt"; }
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.ausdauerhalten",
                Label = "Ausdauer nachfüllen",
                Description = "Falls dir \"unbegrenzt\" zu weit geht: füllt nur laufend auf.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Loops.AusdauerHalten = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "ich.manahalten",
                Label = "Mana nachfüllen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Loops.ManaHalten = o.BoolValue
            });

            return kategorie;
        }

        /// <summary>Baut aus der Werte-Tabelle eine Rubrik - alle mit denselben Regeln.</summary>
        private static void FuegeWerteEin(CheatCategory kategorie, params string[] ids)
        {
            foreach (string id in ids)
            {
                var eintrag = Werte.Alle.Find(e => e.Id == id);
                if (eintrag == null) continue;

                string kennung = eintrag.Id;

                kategorie.Add(new CheatOption
                {
                    Id = kennung,
                    Label = eintrag.Label,
                    Description = eintrag.Beschreibung,
                    Kind = OptionKind.Slider,
                    Scope = CheatScope.OnlyMe,
                    Min = eintrag.Min,
                    Max = eintrag.Max,
                    Step = eintrag.Schritt,
                    OnChanged = o => Werte.Setze(kennung, o.NumberValue)
                });
            }
        }

        private static CheatCategory Kampfwerte()
        {
            var kategorie = new CheatCategory("Kampf");

            kategorie.Add(new CheatOption
            {
                Id = "kampf.info",
                Label = "Angriff / Verteidigung",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = string.Format(
                    "Angriffskraft {0}   |   Verteidigung {1}   |   Kritchance {2}",
                    Werte.Endwert("wert.angriff"),
                    Werte.Endwert("wert.verteidigung"),
                    Werte.Endwert("wert.kritchance"))
            });

            FuegeWerteEin(kategorie,
                "wert.angriff", "wert.magie", "wert.heilkraft",
                "wert.nahkampf", "wert.fernkampf", "wert.explosion", "wert.elite",
                "wert.kritchance", "wert.kritschaden", "wert.rueckstoss",
                "wert.nahtempo", "wert.ferntempo", "wert.magietempo",
                "wert.geschosse", "wert.geschosstempo", "wert.abpraller",
                "wert.leben", "wert.regen", "wert.verteidigung", "wert.ausweichen",
                "wert.reflekt", "wert.lebensraub",
                "wert.ausdauer", "wert.mana", "wert.manaregen",
                "wert.begleiterschaden", "wert.begleiterleben");

            return kategorie;
        }

        private static CheatCategory Bewegung()
        {
            var kategorie = new CheatCategory("Bewegung");
            FuegeWerteEin(kategorie, "wert.tempo", "wert.sprint", "wert.sprung");

            kategorie.Add(new CheatOption
            {
                Id = "flug.an",
                Label = "Fliegen",
                Description = "Schaltet nur den Zug nach unten ab. Laufen bleibt wie gewohnt, " +
                              "hoch geht mit Leertaste, runter mit Strg links.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Flug.Aktiv = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sprung.koenig",
                Label = "Zum König",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Sprung.ZumKoenig()
            });

            kategorie.Add(new CheatOption
            {
                Id = "sprung.truhe",
                Label = "Zur nächsten Truhe",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Sprung.ZurNaechstenTruhe()
            });

            kategorie.Add(new CheatOption
            {
                Id = "sprung.beute",
                Label = "Zur nächsten Beute",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Sprung.ZurNaechstenBeute()
            });

            kategorie.Add(new CheatOption
            {
                Id = "flug.tempo",
                Label = "Steiggeschwindigkeit",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 1f, Max = 40f, Step = 1f,
                NumberValue = 8f,
                OnChanged = o => Flug.Tempo = o.NumberValue
            });

            return kategorie;
        }

        /// <summary>Gemerkte Orte. Die Liste hält nur, solange die Karte hält.</summary>
        private static CheatCategory Orte()
        {
            var kategorie = new CheatCategory("Orte");

            kategorie.Add(new CheatOption
            {
                Id = "ort.info",
                Label = "Gemerkte Orte",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Sprung.Text()
            });

            var liste = new CheatOption
            {
                Id = "ort.liste",
                Label = "Ort",
                Kind = OptionKind.Choice,
                Scope = CheatScope.OnlyMe
            };

            var name = new CheatOption
            {
                Id = "ort.name",
                Label = "Name",
                Description = "Leer lassen für eine fortlaufende Nummer.",
                Kind = OptionKind.Text,
                Scope = CheatScope.OnlyMe
            };

            kategorie.Add(liste);
            kategorie.Add(name);

            kategorie.Add(new CheatOption
            {
                Id = "ort.merken",
                Label = "Hier merken",
                Description = "Speichert, wo du gerade stehst. Beim Kartenwechsel wird alles " +
                              "verworfen - ein Punkt aus der letzten Karte wäre dort wertlos.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o =>
                {
                    o.Message = Sprung.Merke(name.TextValue);
                    name.TextValue = "";
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = "ort.hin",
                Label = "Dorthin springen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Sprung.SpringeZu(liste.ChoiceIndex)
            });

            kategorie.Add(new CheatOption
            {
                Id = "ort.weg",
                Label = "Ort löschen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Sprung.Loesche(liste.ChoiceIndex)
            });

            kategorie.Add(new CheatOption
            {
                Id = "ort.alleweg",
                Label = "Alle löschen",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Sprung.LoescheAlle()
            });

            return kategorie;
        }

        private static CheatCategory SichtRubrik()
        {
            var kategorie = new CheatCategory("Sicht");

            var eintraege = new (string, string, Action<bool>)[]
            {
                ("sicht.gegner",      "Gegner markieren",      an => Sicht.Gegner = an),
                ("sicht.truhen",      "Truhen markieren",      an => Sicht.Truhen = an),
                ("sicht.beute",       "Beute markieren",       an => Sicht.Beute = an),
                ("sicht.haustiere",   "Haustiere markieren",   an => Sicht.Haustiere = an),
                ("sicht.fundstellen", "Fundstellen markieren", an => Sicht.Fundstellen = an),
                ("sicht.mitspieler",  "Mitspieler markieren",  an => Sicht.Mitspieler = an),
            };

            foreach (var (id, label, setzen) in eintraege)
            {
                var schalter = setzen;
                kategorie.Add(new CheatOption
                {
                    Id = id,
                    Label = label,
                    Kind = OptionKind.Toggle,
                    Scope = CheatScope.OnlyMe,
                    OnChanged = o => schalter(o.BoolValue)
                });
            }

            kategorie.Add(new CheatOption
            {
                Id = "sicht.umrisse",
                Label = "Umrisse statt Punkte",
                Description = "Zeichnet einen Kasten um das ganze Objekt statt eines Punkts - " +
                              "durch Wände hindurch, weil die Markierung über dem fertigen Bild liegt.",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Sicht.Umrisse = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.schrift",
                Label = "Schriftgröße",
                Description = "Wie groß die Beschriftungen der Markierungen sind.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 8f, Max = 32f, Step = 1f,
                NumberValue = 11f,
                OnChanged = o => Sicht.Schriftgroesse = (int)o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.punkt",
                Label = "Punktgröße",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 2f, Max = 16f, Step = 1f,
                NumberValue = 4f,
                OnChanged = o => Sicht.Punktgroesse = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.entfernung",
                Label = "Entfernung anzeigen",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                BoolValue = true,
                Ruhebool = true,
                OnChanged = o => Sicht.MitEntfernung = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.ansicht",
                Label = "Darstellung",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Ansicht.Text()
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.fov",
                Label = "Sichtfeld",
                Description = "0 lässt die Einstellung des Spiels in Ruhe.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 0f, Max = 140f, Step = 5f,
                OnChanged = o => Ansicht.Sichtfeld = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.nebel",
                Label = "Nebel aus",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.OnlyMe,
                OnChanged = o => Ansicht.NebelAus = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.helligkeit",
                Label = "Grundhelligkeit",
                Description = "Hellt dunkle Ecken auf - eine Art Nachtsicht. 0 lässt alles wie es war.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 0f, Max = 8f, Step = 0.25f,
                OnChanged = o => Ansicht.Helligkeit = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "sicht.reichweite",
                Label = "Reichweite",
                Description = "In Metern. Weiter entfernte Markierungen werden blasser.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.OnlyMe,
                Min = 20f, Max = 400f, Step = 10f,
                NumberValue = 120f,
                OnChanged = o => Sicht.Reichweite = o.NumberValue
            });

            return kategorie;
        }

        private static CheatCategory Graben()
        {
            var kategorie = new CheatCategory("Graben");

            kategorie.Add(new CheatOption
            {
                Id = "graben.info",
                Label = "Abbaugröße",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Stonewards.Graben.Text()
            });

            kategorie.Add(new CheatOption
            {
                Id = "graben.radius",
                Label = "Abbau-Radius",
                Description = "Wie viel ein Schlag wegnimmt. 0 lässt das Spiel entscheiden. " +
                              "Große Werte reißen entsprechend große Löcher - das kostet Rechenzeit, " +
                              "und rückgängig machen kann man es nicht.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                Min = 0f, Max = 3f, Step = 0.05f,
                OnChanged = o => Stonewards.Graben.Radius = o.NumberValue
            });

            FuegeWerteEin(kategorie,
                "wert.grabkraft", "wert.schwergraben", "wert.bonusrohstoff",
                "wert.beutechance", "wert.stapel");
            return kategorie;
        }

        private static CheatCategory Waehrung()
        {
            var kategorie = new CheatCategory("Währung");

            kategorie.Add(new CheatOption
            {
                Id = "geld.info",
                Label = "Guthaben",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Game.MetaWaehrung.ToString()
            });

            foreach (int betrag in new[] { 100, 1000, 10000 })
            {
                int menge = betrag;
                kategorie.Add(new CheatOption
                {
                    Id = "geld.plus" + menge,
                    Label = "+ " + menge.ToString("#,0"),
                    Kind = OptionKind.Button,
                    Scope = CheatScope.OnlyMe,
                    OnInvoke = o =>
                    {
                        Game.GibWaehrung(menge);
                        o.Message = "Jetzt " + Game.MetaWaehrung;
                    }
                });
            }

            return kategorie;
        }

        private static CheatCategory Gegner()
        {
            var kategorie = new CheatCategory("Gegner und Welt");

            kategorie.Add(new CheatOption
            {
                Id = "welt.info",
                Label = "Lage",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = string.Format(
                    "{0} Gegner   |   Mauern: {1}   |   König: {2}",
                    Welt.LebendeGegner, Welt.MauerText(), Game.KoenigText())
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.toeten",
                Label = "Alle Gegner töten",
                Description = "Nutzt denselben Weg wie eine echte Waffe - geht deshalb auch als Gast, " +
                              "und die Abschüsse zählen für deine Aufträge mit.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Welt.ToeteAlle()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.einfrieren",
                Label = "Gegner einfrieren",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                OnChanged = o => Welt.GegnerEinfrieren = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.bremse",
                Label = "Gegner bremsen",
                Description = "Anteil, um den sie langsamer werden. 1 wäre Stillstand.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                Min = 0f, Max = 1f, Step = 0.05f,
                OnChanged = o => Welt.GegnerBremse = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.mauern",
                Label = "Mauern auffüllen",
                Description = "Setzt stehende Mauern auf volles Leben. Zerstörte bleiben zerstört - " +
                              "die baut man im Spiel selbst wieder auf.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                OnInvoke = o => o.Message = Welt.FuelleMauern()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.mauernhalten",
                Label = "Mauern heil halten",
                Kind = OptionKind.Toggle,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                OnChanged = o => Welt.MauernAuffuellen = o.BoolValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.welle",
                Label = "Nächste Welle sofort",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                OnInvoke = o => o.Message = Welt.NaechsteWelle()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.countdown",
                Label = "Countdown setzen",
                Description = "Sekunden bis zur nächsten Welle - auch nach oben, wenn du Ruhe brauchst.",
                Kind = OptionKind.Number,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                Min = 0f, Max = 3600f, Step = 10f,
                OnChanged = o => o.Message = Welt.SetzeCountdown(o.NumberValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.haustiere",
                Label = "Alle Haustiere retten",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Welt.RetteHaustiere()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.truhen",
                Label = "Alle Truhen öffnen",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o => o.Message = Welt.OeffneTruhen()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.bautempo",
                Label = "Bautempo der Schmiede",
                Description = "Zuschlag auf das Tempo. Die Bauzeit ist Rezeptzeit geteilt durch " +
                              "das Tempo - das Spiel rechnet dann von selbst richtig.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                Min = 0f, Max = 100f, Step = 1f,
                OnChanged = o => Welt.Bautempo = o.NumberValue
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.zeitinfo",
                Label = "Tag und Nacht",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Welt.ZeitText()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.tag",
                Label = "Taglänge",
                Description = "In Sekunden.",
                Kind = OptionKind.Number,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                Min = 5f, Max = 3600f, Step = 5f,
                OnChanged = o => Welt.SetzeTagLaenge(o.NumberValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.nacht",
                Label = "Nachtlänge",
                Kind = OptionKind.Number,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                Min = 5f, Max = 3600f, Step = 5f,
                OnChanged = o => Welt.SetzeNachtLaenge(o.NumberValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.tempo",
                Label = "Spieltempo",
                Description = "Beschleunigt oder bremst die ganze Runde. 1 ist normal.",
                Kind = OptionKind.Slider,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                Min = 0.1f, Max = 5f, Step = 0.1f,
                NumberValue = 1f,
                Ruhewert = 1f,
                OnChanged = o => Welt.SetzeTempo(o.NumberValue)
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.aufwertung",
                Label = "Aufwertung anbieten",
                Description = "Ruft die Auswahl sofort auf, ohne dass die Ladung voll ist.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                OnInvoke = o => o.Message = Welt.BieteAufwertung()
            });

            kategorie.Add(new CheatOption
            {
                Id = "welt.koenig",
                Label = "König stärken",
                Description = "Hebt sein Maximum an - das Spiel hebt dabei auch sein aktuelles Leben mit.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                IsAvailable = NurHost,
                OnInvoke = o => o.Message = Welt.StaerkeKoenig(5000f)
            });

            return kategorie;
        }

        private static CheatCategory Fortschritt()
        {
            var kategorie = new CheatCategory("Fortschritt");

            kategorie.Add(new CheatOption
            {
                Id = "meta.info",
                Label = "Freischaltungen",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Meta.FreischaltenText()
            });

            kategorie.Add(new CheatOption
            {
                Id = "meta.alles",
                Label = "Alles freischalten",
                Description = "Nutzt den Schalter, den das Spiel selbst dafür vorsieht: " +
                              "Freischalten ohne die Währung abzuziehen. Wird gespeichert.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Meta.SchalteAllesFrei()
            });

            kategorie.Add(new CheatOption
            {
                Id = "meta.soeldner",
                Label = "Alle Söldner anheuern",
                Description = "Anheuern und Bezahlen sind im Spiel zwei getrennte Schritte - " +
                              "wir rufen nur den ersten auf.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Meta.HeuereSoeldnerAn()
            });

            kategorie.Add(new CheatOption
            {
                Id = "meta.belohnungen",
                Label = "Belohnungen abholen",
                Description = "Sammelt alles ein, was fertig ist. Unfertige Aufträge bleiben unfertig.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o => o.Message = Meta.HoleBelohnungen()
            });

            return kategorie;
        }

        private static CheatCategory Aufwertungen()
        {
            var kategorie = new CheatCategory("Aufwertungen");

            kategorie.Add(new CheatOption
            {
                Id = "relikt.info",
                Label = "Stand",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Relikte.Text()
            });

            kategorie.Add(new CheatOption
            {
                Id = "relikt.was",
                Label = "Aufwertung",
                Kind = OptionKind.Choice,
                Scope = CheatScope.OnlyMe
            });

            kategorie.Add(new CheatOption
            {
                Id = "relikt.seltenheit",
                Label = "Seltenheit",
                Description = "Je seltener, desto stärker wirkt sie.",
                Kind = OptionKind.Choice,
                Scope = CheatScope.OnlyMe,
                Choices = SeltenheitNamen,
                ChoiceIndex = 4
            });

            kategorie.Add(new CheatOption
            {
                Id = "relikt.stufe",
                Label = "Stufe",
                Kind = OptionKind.Number,
                Scope = CheatScope.OnlyMe,
                Min = 1f, Max = 10f, Step = 1f,
                NumberValue = 1f
            });

            kategorie.Add(new CheatOption
            {
                Id = "relikt.geben",
                Label = "Aufwertung geben",
                Description = "Ruft dieselbe Funktion auf wie das Auswahlfenster zwischen den Wellen.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o =>
                {
                    var was = Registry.Find("relikt.was");
                    var stufe = Registry.Find("relikt.stufe");
                    var seltenheit = Registry.Find("relikt.seltenheit");

                    o.Message = Relikte.Gib(
                        was == null ? 0 : was.ChoiceIndex,
                        stufe == null ? 1 : (int)stufe.NumberValue,
                        Seltenheiten[Mathf.Clamp(seltenheit == null ? 4 : seltenheit.ChoiceIndex,
                                                 0, Seltenheiten.Length - 1)]);
                }
            });

            kategorie.Add(new CheatOption
            {
                Id = "relikt.alle",
                Label = "Alle Aufwertungen geben",
                Description = "Jede einmal, in der gewählten Seltenheit. Das ist viel auf einmal - " +
                              "die Runde ist danach eine andere.",
                Kind = OptionKind.Button,
                Scope = CheatScope.OnlyMe,
                OnInvoke = o =>
                {
                    var seltenheit = Registry.Find("relikt.seltenheit");
                    o.Message = Relikte.GibAlle(
                        Seltenheiten[Mathf.Clamp(seltenheit == null ? 4 : seltenheit.ChoiceIndex,
                                                 0, Seltenheiten.Length - 1)]);
                }
            });

            return kategorie;
        }

        private static CheatCategory Runde()
        {
            var kategorie = new CheatCategory("Runde");

            kategorie.Add(new CheatOption
            {
                Id = "runde.info",
                Label = "Welle",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Game.WellenText()
            });

            kategorie.Add(new CheatOption
            {
                Id = "runde.koenig",
                Label = "König",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Game.KoenigText()
            });

            kategorie.Add(new CheatOption
            {
                Id = "runde.rolle",
                Label = "Deine Rolle",
                Kind = OptionKind.Info,
                OnChanged = o => o.TextValue = Game.IstHost
                    ? "Gastgeber - serverseitige Cheats gehen"
                    : "Gast - serverseitige Cheats sind aus"
            });

            return kategorie;
        }

        // ------------------------------------------------------------------ Gegenstände

        private static readonly Rarity[] Seltenheiten =
        {
            Rarity.COMMON, Rarity.UNCOMMON, Rarity.RARE, Rarity.EPIC, Rarity.LEGENDARY
        };

        private static readonly string[] SeltenheitNamen =
        {
            "Gewöhnlich", "Ungewöhnlich", "Selten", "Episch", "Legendär"
        };

        /// <summary>Die Rubriken zum Herbeirufen. Null steht für "alles auf einmal",
        /// der letzte Eintrag für die tragbaren Sachen mit ihrem eigenen Befehl.</summary>
        private static readonly (string Name, Rarity? Seltenheit, bool Tragbar)[] SpawnRubriken =
        {
            ("Alles", null, false),
            ("Gewöhnlich", Rarity.COMMON, false),
            ("Ungewöhnlich", Rarity.UNCOMMON, false),
            ("Selten", Rarity.RARE, false),
            ("Episch", Rarity.EPIC, false),
            ("Legendär", Rarity.LEGENDARY, false),
            ("Kisten und Tragbares", null, true),
        };

        /// <summary>Kennungen zur zuletzt aufgebauten Liste - die Anzeige zeigt Namen,
        /// verschickt werden muss aber die Kennung.</summary>
        private static List<string> _kennungen = new List<string>();
        private static int _letzteRubrik = -1;

        private static void AktualisiereGegenstandsListe()
        {
            if (!Game.ImSpiel) return;

            // Auswahlliste der gemerkten Orte nachziehen
            var orte = Registry.Find("ort.liste");
            if (orte != null && orte.Choices.Length != Sprung.Anzahl)
            {
                orte.Choices = Sprung.Namen();
                orte.ChoiceIndex = Mathf.Clamp(orte.ChoiceIndex, 0,
                    Mathf.Max(0, orte.Choices.Length - 1));
            }

            // Die Aufwertungsliste einmal füllen, sobald die Spieldaten geladen sind
            var relikte = Registry.Find("relikt.was");
            if (relikte != null && relikte.Choices.Length == 0)
                relikte.Choices = Relikte.Namen();

            var auswahl = Registry.Find("spawn.was");
            var rubrik = Registry.Find("spawn.rubrik");
            if (auswahl == null || rubrik == null) return;

            int index = Mathf.Clamp(rubrik.ChoiceIndex, 0, SpawnRubriken.Length - 1);
            if (index == _letzteRubrik && auswahl.Choices.Length > 0) return;

            var gewaehlt = SpawnRubriken[index];
            var liste = gewaehlt.Tragbar
                ? Game.Tragbares()
                : Game.GegenstaendeMitSeltenheit(gewaehlt.Seltenheit);
            _kennungen = liste.Select(e => e.Key).ToList();

            auswahl.Choices = liste.Select(e => e.Value).ToArray();
            auswahl.ChoiceIndex = Mathf.Clamp(auswahl.ChoiceIndex, 0,
                Mathf.Max(0, auswahl.Choices.Length - 1));

            _letzteRubrik = index;
        }

        private static CheatCategory Herbeirufen()
        {
            var kategorie = new CheatCategory("Herbeirufen");

            kategorie.Add(new CheatOption
            {
                Id = "spawn.rubrik",
                Label = "Rubrik",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone,
                Choices = SpawnRubriken.Select(r => r.Name).ToArray()
            });

            kategorie.Add(new CheatOption
            {
                Id = "spawn.was",
                Label = "Gegenstand",
                Kind = OptionKind.Choice,
                Scope = CheatScope.Everyone
            });

            kategorie.Add(new CheatOption
            {
                Id = "spawn.anzahl",
                Label = "Anzahl",
                Kind = OptionKind.Number,
                Scope = CheatScope.Everyone,
                Min = 1f, Max = 50f, Step = 1f,
                NumberValue = 1f
            });

            kategorie.Add(new CheatOption
            {
                Id = "spawn.los",
                Label = "Herbeirufen",
                Description = "Legt den Gegenstand vor dich. Er liegt für alle sichtbar in der " +
                              "Welt - deshalb ist das als \"betrifft alle\" markiert.",
                Kind = OptionKind.Button,
                Scope = CheatScope.Everyone,
                OnInvoke = o =>
                {
                    var auswahl = Registry.Find("spawn.was");
                    var anzahl = Registry.Find("spawn.anzahl");

                    if (auswahl == null || _kennungen.Count == 0)
                    {
                        o.Message = "Noch keine Liste";
                        return;
                    }

                    var rubrik = Registry.Find("spawn.rubrik");
                    bool tragbar = rubrik != null &&
                        SpawnRubriken[Mathf.Clamp(rubrik.ChoiceIndex, 0, SpawnRubriken.Length - 1)].Tragbar;

                    int index = Mathf.Clamp(auswahl.ChoiceIndex, 0, _kennungen.Count - 1);
                    int menge = anzahl == null ? 1 : Mathf.Clamp((int)anzahl.NumberValue, 1, 50);

                    o.Message = Game.Spawne(_kennungen[index], menge, tragbar);
                }
            });

            return kategorie;
        }
    }
}
