using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Floppy.Unreal
{
    /// <summary>Ruft Funktionen des Spiels auf - von außen.
    ///
    /// Werte zu verbiegen reicht nicht für alles. Ein Teleport ist keine Zahl, sondern
    /// eine Handlung: Die Bewegungssteuerung würde eine von Hand gesetzte Position im
    /// nächsten Frame wieder einkassieren. Also lassen wir das Spiel es selbst tun.
    ///
    /// Unreal schickt jeden Aufruf durch eine einzige Stelle: UObject::ProcessEvent.
    /// Wir legen ein paar Bytes Maschinencode im Spielprozess ab, die genau die mit
    /// unseren drei Werten aufrufen, und stoßen sie mit einem eigenen Faden an.
    ///
    /// Es bleibt nichts zurück: kein Loader im Spielordner, keine veränderte Datei.
    /// Beim Spielende ist alles weg.</summary>
    public sealed class Aufruf : IDisposable
    {
        private const uint MEM_COMMIT_RESERVE = 0x1000 | 0x2000;
        private const uint MEM_RELEASE = 0x8000;
        private const uint PAGE_READWRITE = 0x04;
        private const uint PAGE_EXECUTE_READ = 0x20;

        private const int PROCESS_CREATE_THREAD = 0x0002;
        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_READ = 0x0010;
        private const int PROCESS_VM_WRITE = 0x0020;
        private const int PROCESS_VM_OPERATION = 0x0008;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int zugriff, bool erben, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr griff, IntPtr wunsch,
            uint groesse, uint typ, uint schutz);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr griff, IntPtr adresse, uint groesse, uint typ);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr griff, IntPtr sicherheit,
            uint stapel, IntPtr start, IntPtr parameter, uint flags, IntPtr id);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr griff, uint millisekunden);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr griff);

        /// <summary>Die Stelle, durch die in Unreal jeder Funktionsaufruf läuft.
        /// Aus der Symboldatei, die das Spiel mitliefert.</summary>
        public const ulong RVA_PROCESSEVENT = 0x1619570;

        private const int PufferGroesse = 0x800;

        private readonly Speicher _s;
        private readonly Reflexion _r;
        private readonly Dictionary<string, ulong> _funktionen = new();
        private readonly object _schloss = new();

        private IntPtr _griff;
        private IntPtr _faden;
        private ulong _parameter;
        private ulong _code;
        private bool _entsorgt;
        private WaitHandle _warte;
        private RegisteredWaitHandle _aufraeumen;

        public string LetzterFehler { get; private set; } = "";

        public Aufruf(Speicher s, Reflexion r)
        {
            _s = s;
            _r = r;
        }

        public bool Bereit => _griff != IntPtr.Zero && _parameter != 0 && _code != 0;

        /// <summary>Richtet den Arbeitsplatz im Spielprozess ein.</summary>
        public bool Oeffne()
        {
            lock (_schloss) return OeffneIntern();
        }

        private bool OeffneIntern()
        {
            if (_entsorgt) { LetzterFehler = "Verbindung wurde beendet"; return false; }
            if (!VorherigerAufrufFertig()) return false;
            if (Bereit) return true;
            if (!_s.Offen) { LetzterFehler = "Kein Zugriff auf den Spielprozess"; return false; }

            _griff = OpenProcess(
                PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION | PROCESS_VM_READ |
                PROCESS_VM_WRITE | PROCESS_VM_OPERATION, false, _s.Pid);

            if (_griff == IntPtr.Zero) { LetzterFehler = "Spielprozess konnte nicht geöffnet werden"; return false; }

            _parameter = (ulong)VirtualAllocEx(_griff, IntPtr.Zero, PufferGroesse,
                MEM_COMMIT_RESERVE, PAGE_READWRITE).ToInt64();

            _code = (ulong)VirtualAllocEx(_griff, IntPtr.Zero, 0x100,
                MEM_COMMIT_RESERVE, PAGE_EXECUTE_READ).ToInt64();

            if (Bereit) return true;
            LetzterFehler = "Arbeitsplatz im Spielprozess konnte nicht angelegt werden";
            GibFrei();
            return false;
        }

        public void Dispose()
        {
            lock (_schloss)
            {
                if (_entsorgt) return;
                _entsorgt = true;
                if (VorherigerAufrufFertig()) { GibFrei(); return; }

                // Ein Timeout beendet den fremden Faden nicht. Die Puffer gehören ihm,
                // bis Windows sein Ende meldet; der Client muss darauf nicht blockieren.
                _warte = new FadenWartegriff(_faden);
                _aufraeumen = ThreadPool.RegisterWaitForSingleObject(_warte, (_, _) =>
                {
                    lock (_schloss)
                    {
                        GibFrei();
                        _warte.Dispose();
                        _warte = null;
                        _aufraeumen = null;
                    }
                }, null, Timeout.Infinite, true);
            }
        }

        private sealed class FadenWartegriff : WaitHandle
        {
            public FadenWartegriff(IntPtr faden) { SafeWaitHandle = new SafeWaitHandle(faden, false); }
        }

        private bool VorherigerAufrufFertig()
        {
            if (_faden == IntPtr.Zero) return true;
            if (WaitForSingleObject(_faden, 0) != 0)
            {
                LetzterFehler = "Der vorige Spielaufruf läuft noch; bitte warten";
                return false;
            }
            CloseHandle(_faden);
            _faden = IntPtr.Zero;
            return true;
        }

        // Nur ohne laufenden Faden oder nach einem bestätigten Endsignal aufrufen.
        private void GibFrei()
        {
            if (_faden != IntPtr.Zero) CloseHandle(_faden);
            _faden = IntPtr.Zero;
            if (_griff != IntPtr.Zero)
            {
                if (_parameter != 0)
                    VirtualFreeEx(_griff, (IntPtr)(long)_parameter, 0, MEM_RELEASE);

                if (_code != 0)
                    VirtualFreeEx(_griff, (IntPtr)(long)_code, 0, MEM_RELEASE);

                CloseHandle(_griff);
            }

            _griff = IntPtr.Zero;
            _parameter = 0;
            _code = 0;
        }

        // ---------------------------------------------------------------- Funktionen

        /// <summary>Sucht eine Funktion des Spiels über ihren Namen.
        ///
        /// Funktionen sind in Unreal selbst Objekte - wir finden sie also im selben
        /// Verzeichnis wie alles andere. Der Besitzer grenzt ein, welche gemeint ist:
        /// K2_SetActorLocation gibt es einmal bei Actor und je einmal geerbt.</summary>
        public ulong Finde(string name, string besitzer = null)
        {
            string schluessel = name + "@" + (besitzer ?? "");
            if (_funktionen.TryGetValue(schluessel, out ulong fertig) && fertig != 0) return fertig;

            int n = _r.Anzahl;
            for (int i = 0; i < n; i++)
            {
                ulong p = _r.Objekt(i);
                if (p == 0 || _r.KlassenName(p) != "Function") continue;
                if (_r.ObjektName(p) != name) continue;

                if (besitzer != null && _r.ObjektName(_r.Besitzer(p)) != besitzer) continue;

                _funktionen[schluessel] = p;
                return p;
            }

            return 0;
        }

        /// <summary>Beschreibt sich die Funktion selbst - Name, Typ und Platz je Parameter.</summary>
        public List<Reflexion.Eigenschaft> Parameter(ulong funktion)
        {
            return _r.Felder(funktion);
        }

        // ---------------------------------------------------------------- Ausführen

        /// <summary>Ruft die Funktion an dem Objekt auf und gibt den Parameterblock zurück.</summary>
        public byte[] Rufe(ulong objekt, ulong funktion, byte[] parameter, int rueckgabeGroesse = 0)
        {
            lock (_schloss) return RufeIntern(objekt, funktion, parameter, rueckgabeGroesse);
        }

        private byte[] RufeIntern(ulong objekt, ulong funktion, byte[] parameter, int rueckgabeGroesse)
        {
            LetzterFehler = "";
            if (!Oeffne() || objekt == 0 || funktion == 0) return null;
            if (rueckgabeGroesse < 0 || rueckgabeGroesse > PufferGroesse)
            {
                LetzterFehler = "Ungültige Rückgabegröße";
                return null;
            }

            var block = new byte[PufferGroesse];
            if (parameter != null)
                Array.Copy(parameter, block, Math.Min(parameter.Length, PufferGroesse));

            if (!_s.Schreibe(_parameter, block)) { LetzterFehler = "Parameter konnten nicht geschrieben werden"; return null; }

            if (!_s.Schreibe(_code, Maschinencode(objekt, funktion))) { LetzterFehler = "Aufruf konnte nicht vorbereitet werden"; return null; }

            _faden = CreateRemoteThread(_griff, IntPtr.Zero, 0,
                (IntPtr)(long)_code, IntPtr.Zero, 0, IntPtr.Zero);

            if (_faden == IntPtr.Zero) { LetzterFehler = "Spielaufruf konnte nicht gestartet werden"; return null; }

            if (!WarteAufAufruf(2000)) return null;

            return rueckgabeGroesse > 0 ? _s.Lies(_parameter, rueckgabeGroesse) : Array.Empty<byte>();
        }

        internal bool WarteAufAufruf(uint zeitgrenze)
        {
            lock (_schloss) return WarteAufAufrufIntern(zeitgrenze);
        }

        private bool WarteAufAufrufIntern(uint zeitgrenze)
        {
            if (_faden == IntPtr.Zero) return true;
            uint ergebnis = WaitForSingleObject(_faden, zeitgrenze);
            if (ergebnis != 0)
            {
                LetzterFehler = ergebnis == 258
                    ? "Zeitüberschreitung: Spielaufruf läuft möglicherweise noch; nicht erneut auslösen"
                    : "Das Ende des Spielaufrufs konnte nicht bestätigt werden";
                return false;
            }
            CloseHandle(_faden);
            _faden = IntPtr.Zero;

            return true;
        }

        /// <summary>Die paar Befehle, die ProcessEvent mit unseren Werten aufrufen.
        ///
        ///   rcx = das Objekt, rdx = die Funktion, r8 = der Parameterblock.
        /// Die 0x28 Bytes davor sind der Platz, den der Aufrufer auf Windows
        /// bereitstellen muss - ohne den räumt die gerufene Funktion im Nichts.</summary>
        private byte[] Maschinencode(ulong objekt, ulong funktion)
        {
            var code = new List<byte>(64);

            void Konstante(byte[] befehl, ulong wert)
            {
                code.AddRange(befehl);
                code.AddRange(BitConverter.GetBytes(wert));
            }

            Konstante(new byte[] { 0x48, 0xB9 }, objekt);                       // mov rcx, objekt
            Konstante(new byte[] { 0x48, 0xBA }, funktion);                     // mov rdx, funktion
            Konstante(new byte[] { 0x49, 0xB8 }, _parameter);                   // mov r8, parameter
            Konstante(new byte[] { 0x48, 0xB8 }, _s.Basis + RVA_PROCESSEVENT);  // mov rax, ProcessEvent

            code.AddRange(new byte[] { 0x48, 0x83, 0xEC, 0x28 });               // sub rsp, 0x28
            code.AddRange(new byte[] { 0xFF, 0xD0 });                           // call rax
            code.AddRange(new byte[] { 0x48, 0x83, 0xC4, 0x28 });               // add rsp, 0x28
            code.AddRange(new byte[] { 0x31, 0xC0 });                           // xor eax, eax
            code.Add(0xC3);                                                     // ret

            return code.ToArray();
        }
    }
}
