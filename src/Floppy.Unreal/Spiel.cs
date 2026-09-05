using System;
using System.Collections.Generic;
using System.Linq;

namespace Floppy.Unreal
{
    /// <summary>Alles, was speziell Mortal Shell II betrifft.
    ///
    /// Die Adressen der Wurzelzeiger stammen aus der Symboldatei, die das Spiel selbst
    /// mitliefert. Alles andere wird zur Laufzeit erfragt - deshalb hält das hier auch
    /// ein Spiel-Update aus, solange die Namen gleich bleiben.</summary>
    public class Spiel
    {
        public const string Prozess = "MortalShell2-Win64-Shipping";
        public const string Modul = "MortalShell2-Win64-Shipping.exe";

        private const ulong RVA_OBJEKTE = 0xB03CF60;
        private const ulong RVA_NAMEN = 0xAF59400;
        private const ulong RVA_GWORLD = 0xB1CAE08;

        /// <summary>Ein Attribut des Fähigkeitensystems: Grundwert und aktueller Wert,
        /// beide als float direkt hintereinander.</summary>
        public sealed class Attribut
        {
            public string Satz;
            public string Name;
            public ulong Adresse;      // Anfang des Gebildes

            public float Wert(Speicher s) => s.F32(Adresse + 12);

            public void Setze(Speicher s, float wert)
            {
                s.SchreibeF32(Adresse + 8, wert);       // Grundwert
                s.SchreibeF32(Adresse + 12, wert);      // aktueller Wert
            }
        }

        public sealed class Zahl
        {
            public string Name;
            public ulong Adresse;
            public bool IstFliess;

            public double Wert(Speicher s) => IstFliess ? s.F64(Adresse) : s.I32(Adresse);

            public void Setze(Speicher s, double wert)
            {
                if (IstFliess) s.SchreibeF64(Adresse, wert);
                else s.SchreibeI32(Adresse, (int)wert);
            }
        }

        public Speicher Sp { get; } = new Speicher();
        public Reflexion Ref { get; private set; }
        public Aufruf Ruf { get; private set; }

        public ulong Figur { get; private set; }
        public ulong Steuerung { get; private set; }
        public ulong Wurzel { get; private set; }
        public int PositionAbstand { get; private set; } = -1;

        // Die Listen werden beim Ladebildschirm neu gefüllt, während der Client sie
        // liest. Ohne Absprache bekäme er dabei eine halbfertige Liste zu sehen.
        private readonly object _schloss = new();

        public List<Attribut> Attribute { get; } = new();
        public List<Zahl> Speicherstand { get; } = new();

        /// <summary>Die Gegenstaende des Spiels, aus seiner eigenen Tabelle.</summary>
        public List<(uint Kennung, string Name)> Gegenstaende { get; private set; } = new();

        /// <summary>Wie viele Werte gerade bekannt sind.
        ///
        /// Mit Absprache gelesen: Während eines Ladebildschirms wird die Liste geleert
        /// und neu gefüllt. Wer ohne Absprache hineinschaut, erwischt die Lücke
        /// dazwischen und meldet null - genau das stand vorhin in der Anzeige.</summary>
        public int AnzahlWerte { get { lock (_schloss) return Attribute.Count; } }

        public virtual bool Verbunden => Sp.Offen && Sp.Lebt();
        public virtual bool Bereit => Sp.Offen && Ref != null && AnzahlWerte > 0 &&
                                      Figur != 0 && Ref.KlassenName(Figur) == "BP_PlayerCharacter_C";

        public virtual ulong WeltKennung => Sp.Offen ? Sp.U64(Sp.Basis + RVA_GWORLD) : 0;
        public virtual string Welt => Ref == null ? "" : Ref.ObjektName(WeltKennung);

        // ---------------------------------------------------------------- Anbinden

        public virtual bool Verbinde()
        {
            Trenne();
            if (!Sp.Verbinde(Prozess, Modul)) return false;

            Ref = new Reflexion(Sp, RVA_OBJEKTE, RVA_NAMEN);
            Ruf = new Aufruf(Sp, Ref);
            return Suche();
        }

        public virtual void Trenne()
        {
            Ruf?.Dispose();
            Ruf = null;
            Sp.Trenne();
            Ref = null;
            Figur = 0;
            Steuerung = 0;
            Wurzel = 0;
            PositionAbstand = -1;
            Attribute.Clear();
            Speicherstand.Clear();
            Gegenstaende.Clear();
        }

        /// <summary>Sucht Figur, Attributsätze und Speicherstand neu.
        ///
        /// Muss nach jedem Ladebildschirm laufen: Beim Wechsel des Gebiets wird die
        /// Figur neu erzeugt, und alle alten Zeiger zeigen ins Leere.</summary>
        public virtual bool Suche()
        {
            lock (_schloss)
                return SucheIntern();
        }

        private bool SucheIntern()
        {
            Attribute.Clear();
            Speicherstand.Clear();
            Figur = 0;
            Steuerung = 0;
            Wurzel = 0;
            PositionAbstand = -1;

            if (Ref == null) return false;

            Figur = Ref.FindeErstes("BP_PlayerCharacter_C");
            if (Figur == 0) return false;

            // Das Entwicklermenü des Spiels hängt an der Steuerung, nicht an der Figur
            Steuerung = Ref.FindeErstes("BP_PlayerController_C");

            Ref.Kalibriere(Figur);

            // Position
            Wurzel = Ref.Zeiger(Figur, "RootComponent");
            PositionAbstand = Ref.Finde(Wurzel, "RelativeLocation", out var ort) ? ort.Abstand : -1;

            SucheAttribute();
            SucheSpeicherstand();

            if (Gegenstaende.Count == 0)
                Gegenstaende = Ref.Tabellenzeilen(Ref.Tabelle("DT_PickUpItems"));

            return Attribute.Count > 0;
        }

        /// <summary>Alle Attributsätze der Figur einsammeln.
        ///
        /// Sie hängen als eigene Objekte an der Figur - wir nehmen alles, dessen Besitzer
        /// die Figur ist und dessen Klassenname auf einen Attributsatz hindeutet.</summary>
        private void SucheAttribute()
        {
            int n = Ref.Anzahl;

            for (int i = 0; i < n; i++)
            {
                ulong p = Ref.Objekt(i);
                if (p == 0 || Ref.Besitzer(p) != Figur) continue;

                string satz = Ref.KlassenName(p);
                if (!satz.EndsWith("Set", StringComparison.Ordinal)) continue;
                if (satz == "CSAnimSet") continue;                 // nur Animationsdaten

                foreach (var e in Ref.Felder(Ref.Klasse(p)))
                {
                    if (e.Typ != "StructProperty") continue;

                    // Ein Attribut erkennt man daran, dass hinter dem Zeiger zwei
                    // gleiche Fließkommazahlen stehen - Grundwert und aktueller Wert.
                    ulong adresse = p + (ulong)e.Abstand;
                    var roh = Sp.Lies(adresse, 16);
                    if (roh == null) continue;

                    float grund = BitConverter.ToSingle(roh, 8);
                    float jetzt = BitConverter.ToSingle(roh, 12);

                    if (float.IsNaN(grund) || float.IsNaN(jetzt)) continue;
                    if (Math.Abs(grund) > 1e9f || Math.Abs(jetzt) > 1e9f) continue;

                    Attribute.Add(new Attribut { Satz = satz, Name = e.Name, Adresse = adresse });
                }
            }
        }

        /// <summary>Die Zahlen aus dem Speicherstand: Währungen, Tode, Bosse, NG+.</summary>
        private void SucheSpeicherstand()
        {
            ulong stand = Ref.FindeErstes("BP_SpartaPlayerSaveObject_C");
            if (stand == 0) return;

            foreach (var e in Ref.Felder(Ref.Klasse(stand)))
            {
                if (e.Typ != "IntProperty" && e.Typ != "DoubleProperty") continue;

                Speicherstand.Add(new Zahl
                {
                    Name = e.Name,
                    Adresse = stand + (ulong)e.Abstand,
                    IstFliess = e.Typ == "DoubleProperty"
                });
            }
        }

        // ---------------------------------------------------------------- Position

        public (double X, double Y, double Z) Position()
        {
            if (Wurzel == 0 || PositionAbstand < 0) return (0, 0, 0);

            ulong a = Wurzel + (ulong)PositionAbstand;
            return (Sp.F64(a), Sp.F64(a + 8), Sp.F64(a + 16));
        }

        /// <summary>Setzt die Figur an einen anderen Ort.
        ///
        /// Nicht durch Schreiben der Position: Die Bewegungssteuerung würde sie im
        /// nächsten Frame zurückholen. Stattdessen rufen wir die Sprungfunktion des
        /// Spiels auf - dieselbe, die auch ein Aufzug oder ein Portal benutzt.</summary>
        public string SetzePosition(double x, double y, double z)
        {
            if (Figur == 0) return "Keine Figur";
            if (Ruf == null || !Ruf.Oeffne()) return Ruf?.LetzterFehler ?? "Kein Zugriff auf den Spielprozess";

            ulong funktion = Ruf.Finde("K2_SetActorLocation", "Actor");
            if (funktion == 0) return "Sprungfunktion nicht gefunden";

            // Die Funktion beschreibt ihre Parameter selbst - nichts abgetippt.
            var block = new byte[0x200];
            BitConverter.GetBytes(x).CopyTo(block, 0);
            BitConverter.GetBytes(y).CopyTo(block, 8);
            BitConverter.GetBytes(z).CopyTo(block, 16);

            foreach (var e in Ruf.Parameter(funktion))
            {
                // Als echtes Umsetzen, nicht als Bewegung durch die Wand
                if (e.Name == "bTeleport" && e.Abstand < block.Length) block[e.Abstand] = 1;
            }

            return Ruf.Rufe(Figur, funktion, block) == null ? Ruf.LetzterFehler : "Umgesetzt";
        }

        // ---------------------------------------------------------------- Spieleigene Cheats

        /// <summary>Ruft einen der mitgelieferten Entwickler-Cheats auf.
        ///
        /// Das Spiel übernimmt damit die ganze Buchführung selbst: Gold landet im
        /// Inventar, in der Anzeige und im Speicherstand. Eine Zahl von Hand zu setzen
        /// hätte nur eine der drei Stellen getroffen.</summary>
        public string RufeCheat(Cheats.Eintrag eintrag, double wert)
        {
            if (Steuerung == 0) Steuerung = Ref?.FindeErstes("BP_PlayerController_C") ?? 0;
            if (Steuerung == 0) return "Keine Spielsteuerung gefunden";
            if (Ruf == null || !Ruf.Oeffne()) return Ruf?.LetzterFehler ?? "Kein Zugriff auf den Spielprozess";

            ulong funktion = Ruf.Finde(eintrag.Funktion, "BP_PlayerController_C");
            if (funktion == 0) return "Diesen Cheat gibt es in dieser Fassung nicht";

            var block = Cheats.Block(Ruf, funktion, eintrag, wert);

            return Ruf.Rufe(Steuerung, funktion, block) == null
                ? Ruf.LetzterFehler
                : "Erledigt";
        }

        /// <summary>Legt einen Gegenstand ins Inventar.
        ///
        /// Der Name wird als Kennzahl uebergeben - genau so, wie das Spiel ihn intern
        /// fuehrt. Einen Text muesste man erst nachschlagen; die Kennzahl haben wir beim
        /// Lesen der Tabelle ohnehin schon.</summary>
        public string GibGegenstand(uint kennung, int menge)
        {
            if (Steuerung == 0) Steuerung = Ref?.FindeErstes("BP_PlayerController_C") ?? 0;
            if (Steuerung == 0) return "Keine Spielsteuerung gefunden";
            if (Ruf == null || !Ruf.Oeffne()) return Ruf?.LetzterFehler ?? "Kein Zugriff auf den Spielprozess";

            ulong funktion = Ruf.Finde("S_AddItemQuantity", "BP_PlayerController_C");
            if (funktion == 0) return "Funktion nicht gefunden";

            var parameter = Ruf.Parameter(funktion);
            if (parameter.Count < 2) return "Parameter nicht lesbar";

            var block = new byte[0x200];
            BitConverter.GetBytes(kennung).CopyTo(block, parameter[0].Abstand);
            BitConverter.GetBytes(0).CopyTo(block, parameter[0].Abstand + 4);
            BitConverter.GetBytes(menge).CopyTo(block, parameter[1].Abstand);

            return Ruf.Rufe(Steuerung, funktion, block) == null ? Ruf.LetzterFehler : "Erledigt";
        }

        // ---------------------------------------------------------------- Hilfen

        public IEnumerable<string> Saetze =>
            Attribute.Select(a => a.Satz).Distinct().OrderBy(s => s, StringComparer.Ordinal);

        public Attribut Finde(string satz, string name)
        {
            lock (_schloss)
                return Attribute.FirstOrDefault(a => a.Satz == satz && a.Name == name);
        }
    }
}
