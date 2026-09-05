using System;
using System.Collections.Generic;

namespace Floppy.Unreal
{
    /// <summary>Unreals eigenes Auskunftssystem, von außen gelesen.
    ///
    /// Jedes Objekt im Spiel kennt seine Klasse, jede Klasse ihre Eigenschaften mit
    /// Namen, Typ und Abstand. Wir fragen also das Spiel, wo "Health" liegt, statt eine
    /// Adresse einzutragen, die nach dem nächsten Update falsch wäre.</summary>
    public sealed class Reflexion
    {
        // Die Wurzelzeiger stammen aus der Symboldatei, die das Spiel mitliefert.
        private readonly ulong _guObjectArray;
        private readonly ulong _namePool;

        private readonly Speicher _s;

        // Diese Abstände unterscheiden sich je nach Engine-Fassung. Sie werden beim
        // Verbinden gemessen, nicht aus einer Tabelle genommen - siehe Kalibriere().
        private int _ustSuper = 0x40;
        private int _ustChild = 0x50;
        private int _ffNext = 0x18;
        private int _ffName = 0x20;

        private readonly Dictionary<uint, string> _namen = new();
        private readonly Dictionary<ulong, List<Eigenschaft>> _felder = new();

        public Reflexion(Speicher s, ulong guObjectArrayRva, ulong namePoolRva)
        {
            _s = s;
            _guObjectArray = s.Basis + guObjectArrayRva;
            _namePool = s.Basis + namePoolRva;
        }

        public readonly struct Eigenschaft
        {
            public Eigenschaft(string name, string typ, int abstand)
            {
                Name = name; Typ = typ; Abstand = abstand;
            }

            public string Name { get; }
            public string Typ { get; }
            public int Abstand { get; }
        }

        // ---------------------------------------------------------------- Namen

        /// <summary>Löst eine FName-Kennzahl in Text auf.
        ///
        /// Die Namen liegen in Blöcken zu je 65536 Einträgen; jeder Eintrag beginnt mit
        /// zwei Bytes, in denen Länge und Textbreite stecken.</summary>
        public string Name(uint index)
        {
            if (_namen.TryGetValue(index, out string fertig)) return fertig;

            ulong block = _s.U64(_namePool + 0x10 + (ulong)(index >> 16) * 8);
            if (block == 0) return "?";

            ulong eintrag = block + (ulong)(index & 0xFFFF) * 2;
            var kopf = _s.Lies(eintrag, 2);
            if (kopf == null) return "?";

            int h = BitConverter.ToUInt16(kopf, 0);
            bool breit = (h & 1) != 0;
            int laenge = h >> 6;

            if (laenge <= 0 || laenge > 250) return "?";

            var roh = _s.Lies(eintrag + 2, laenge * (breit ? 2 : 1));
            if (roh == null) return "?";

            string text = breit
                ? System.Text.Encoding.Unicode.GetString(roh)
                : System.Text.Encoding.UTF8.GetString(roh);

            _namen[index] = text;
            return text;
        }

        public string ObjektName(ulong objekt) => objekt == 0 ? "?" : Name(_s.U32(objekt + 0x18));
        public ulong Klasse(ulong objekt) => objekt == 0 ? 0 : _s.U64(objekt + 0x10);
        public ulong Besitzer(ulong objekt) => objekt == 0 ? 0 : _s.U64(objekt + 0x20);
        public string KlassenName(ulong objekt) => ObjektName(Klasse(objekt));

        // ---------------------------------------------------------------- Objekte

        public int Anzahl => _s.I32(_guObjectArray + 0x10 + 0x14);

        public ulong Objekt(int i)
        {
            ulong tabelle = _s.U64(_guObjectArray + 0x10);
            ulong block = _s.U64(tabelle + (ulong)(i >> 16) * 8);
            return block == 0 ? 0 : _s.U64(block + (ulong)(i & 0xFFFF) * 0x18);
        }

        /// <summary>Ein echtes Objekt im Spiel - kein Prototyp.
        ///
        /// Unreal hält von jeder Klasse eine Vorlage im Speicher. Die sieht aus wie das
        /// Original, aber ihre Zeiger sind leer. Wer das übersieht, schreibt in die
        /// Vorlage und wundert sich, dass im Spiel nichts passiert.</summary>
        public bool Echt(ulong objekt)
        {
            if (objekt == 0) return false;
            if (ObjektName(objekt).StartsWith("Default__", StringComparison.Ordinal)) return false;

            return !ObjektName(Besitzer(objekt)).StartsWith("Default__", StringComparison.Ordinal);
        }

        /// <summary>Sucht das erste echte Objekt einer Klasse.</summary>
        public ulong FindeErstes(string klassenName, Func<ulong, bool> zusatz = null)
        {
            int n = Anzahl;
            for (int i = 0; i < n; i++)
            {
                ulong p = Objekt(i);
                if (p == 0 || !Echt(p)) continue;
                if (KlassenName(p) != klassenName) continue;
                if (zusatz != null && !zusatz(p)) continue;
                return p;
            }
            return 0;
        }

        public List<ulong> FindeAlle(string klassenName)
        {
            var raus = new List<ulong>();
            int n = Anzahl;

            for (int i = 0; i < n; i++)
            {
                ulong p = Objekt(i);
                if (p != 0 && Echt(p) && KlassenName(p) == klassenName) raus.Add(p);
            }

            return raus;
        }

        // ---------------------------------------------------------------- Eigenschaften

        /// <summary>Die Eigenschaften einer Klasse samt Elternklassen - einmal ermittelt.</summary>
        public List<Eigenschaft> Felder(ulong klasse)
        {
            if (_felder.TryGetValue(klasse, out var fertig)) return fertig;

            var raus = new List<Eigenschaft>();
            ulong k = klasse;
            int tiefe = 0;

            while (k != 0 && tiefe++ < 24)
            {
                string kn = ObjektName(k);
                ulong feld = _s.U64(k + (ulong)_ustChild);
                int zaehler = 0;

                while (feld != 0 && zaehler++ < 4096)
                {
                    string fn = Name(_s.U32(feld + (ulong)_ffName));
                    ulong fk = _s.U64(feld + 0x08);
                    string typ = fk == 0 ? "?" : Name(_s.U32(fk));
                    int abstand = WertAbstand(feld);

                    if (fn != "?" && abstand >= 0) raus.Add(new Eigenschaft(fn, typ, abstand));

                    feld = _s.U64(feld + (ulong)_ffNext);
                }

                if (kn == "Object") break;
                k = _s.U64(k + (ulong)_ustSuper);
            }

            _felder[klasse] = raus;
            return raus;
        }

        /// <summary>Wo im Objekt der Wert liegt. Die Stelle wandert zwischen Fassungen,
        /// deshalb zwei Kandidaten und der plausible gewinnt.</summary>
        private int WertAbstand(ulong feld)
        {
            foreach (int kandidat in new[] { 0x44, 0x4C })
            {
                int o = _s.I32(feld + (ulong)kandidat);
                if (o >= 0 && o < 0x8000) return o;
            }
            return -1;
        }

        public bool Finde(ulong objekt, string feldname, out Eigenschaft treffer)
        {
            treffer = default;
            if (objekt == 0) return false;

            foreach (var e in Felder(Klasse(objekt)))
            {
                if (e.Name != feldname) continue;
                treffer = e;
                return true;
            }

            return false;
        }

        public ulong Zeiger(ulong objekt, string feldname)
        {
            return Finde(objekt, feldname, out var e) ? _s.U64(objekt + (ulong)e.Abstand) : 0;
        }

        // ---------------------------------------------------------------- Datentabellen

        /// <summary>Die Zeilennamen einer Datentabelle des Spiels.
        ///
        /// Eine Tabelle haelt ihre Zeilen in einer Abbildung von Name auf Daten. Fuer uns
        /// zaehlt nur der Name - und zwar als Kennzahl, weil genau die eine Funktion des
        /// Spiels als Parameter erwartet. So muessen wir keinen Text zurueckuebersetzen.</summary>
        public List<(uint Kennung, string Name)> Tabellenzeilen(ulong tabelle)
        {
            var raus = new List<(uint, string)>();
            if (tabelle == 0) return raus;

            const int RowMap = 0x30;      // gemessen, nicht geraten
            const int Schritt = 24;       // Name + Zeiger + zwei Verweise

            ulong daten = _s.U64(tabelle + RowMap);
            int anzahl = _s.I32(tabelle + RowMap + 8);

            if (daten < 0x10000 || anzahl <= 0 || anzahl > 20000) return raus;

            for (int i = 0; i < anzahl; i++)
            {
                uint kennung = _s.U32(daten + (ulong)(i * Schritt));
                string name = Name(kennung);

                // Leere Plaetze in der Abbildung liefern keinen Namen
                if (name != "?") raus.Add((kennung, name));
            }

            return raus;
        }

        public ulong Tabelle(string name)
        {
            return FindeErstes("DataTable", p => ObjektName(p) == name);
        }

        // ---------------------------------------------------------------- Kalibrierung

        /// <summary>Misst die Abstände am laufenden Spiel nach.
        ///
        /// Sie unterscheiden sich zwischen Engine-Fassungen um einige Bytes. Statt eine
        /// Tabelle zu pflegen, probieren wir die Kandidaten durch und nehmen die
        /// Kombination, bei der eine Kette lesbarer Namen herauskommt.</summary>
        public bool Kalibriere(ulong beispielObjekt)
        {
            ulong k = Klasse(beispielObjekt);
            if (k == 0) return false;

            foreach (int child in new[] { 0x50, 0x40, 0x48 })
            {
                ulong start = _s.U64(k + (ulong)child);
                if (start < 0x10000) continue;

                foreach (int nameOff in new[] { 0x20, 0x28 })
                {
                    foreach (int nextOff in new[] { 0x18, 0x20 })
                    {
                        if (nameOff == nextOff) continue;
                        if (Kette(start, nameOff, nextOff) < 3) continue;

                        _ustChild = child;
                        _ffName = nameOff;
                        _ffNext = nextOff;
                        _ustSuper = child == 0x50 ? 0x40 : 0x30;

                        _felder.Clear();
                        return true;
                    }
                }
            }

            return false;
        }

        private int Kette(ulong start, int nameOff, int nextOff)
        {
            ulong feld = start;
            int gut = 0;

            for (int i = 0; i < 6 && feld > 0x10000; i++)
            {
                if (Name(_s.U32(feld + (ulong)nameOff)) == "?") break;
                gut++;
                feld = _s.U64(feld + (ulong)nextOff);
            }

            return gut;
        }
    }
}
