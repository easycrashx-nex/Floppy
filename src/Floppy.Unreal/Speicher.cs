using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Floppy.Unreal
{
    /// <summary>Lesen und Schreiben im Speicher eines fremden Prozesses.
    ///
    /// Anders als bei unseren anderen Spielen läuft hier nichts im Spiel selbst: Wir
    /// hängen uns von außen an den laufenden Prozess. Kein Loader im Spielordner, keine
    /// veränderte Datei - und es funktioniert am bereits laufenden Spiel.</summary>
    public sealed class Speicher : IDisposable
    {
        private const int PROCESS_QUERY_INFORMATION = 0x0400;
        private const int PROCESS_VM_READ = 0x0010;
        private const int PROCESS_VM_WRITE = 0x0020;
        private const int PROCESS_VM_OPERATION = 0x0008;
        private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int zugriff, bool erben, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr griff);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetExitCodeProcess(IntPtr griff, out uint code);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr griff, uint flags, StringBuilder pfad, ref int laenge);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(IntPtr griff, IntPtr adresse,
            byte[] puffer, int groesse, out IntPtr gelesen);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteProcessMemory(IntPtr griff, IntPtr adresse,
            byte[] puffer, int groesse, out IntPtr geschrieben);

        private IntPtr _griff;
        private readonly Func<int, int, IntPtr> _oeffne;
        private readonly Func<int> _windowsFehler;

        public Speicher() : this((rechte, pid) => OpenProcess(rechte, false, pid), Marshal.GetLastWin32Error) { }

        // Allows deterministic denied-access tests without altering process privileges.
        internal Speicher(Func<int, int, IntPtr> oeffne, Func<int> windowsFehler)
        {
            _oeffne = oeffne ?? throw new ArgumentNullException(nameof(oeffne));
            _windowsFehler = windowsFehler ?? throw new ArgumentNullException(nameof(windowsFehler));
        }

        public int Pid { get; private set; }
        public ulong Basis { get; private set; }
        public bool Offen => _griff != IntPtr.Zero;
        public string LetzterFehler { get; private set; } = "";
        public string Programmpfad { get; private set; } = "";

        /// <summary>Hängt sich an den Prozess. Gibt false zurück, wenn er nicht läuft.</summary>
        public bool Verbinde(string prozessname, string modulname)
        {
            Trenne();
            LetzterFehler = "";
            if (string.IsNullOrWhiteSpace(prozessname) || string.IsNullOrWhiteSpace(modulname))
            {
                LetzterFehler = "Prozessname und EXE-Modul müssen angegeben werden";
                return false;
            }
            Process[] treffer = Array.Empty<Process>();
            string verweigert = null;
            try
            {
                treffer = Process.GetProcessesByName(prozessname);
                if (treffer.Length == 0)
                {
                    LetzterFehler = "Spielprozess " + prozessname + " läuft nicht";
                    return false;
                }
                foreach (var prozess in treffer)
                {
                    IntPtr griff = IntPtr.Zero;
                    try
                    {
                        int pid = prozess.Id;
                        // The executable path needs fewer rights and remains useful for diagnostics.
                        string pfad = LiesProgrammpfad(pid);
                        if (pfad.Length > 0) Programmpfad = pfad;
                        griff = _oeffne(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION, pid);
                        if (griff == IntPtr.Zero)
                        {
                            int fehler = _windowsFehler();
                            LetzterFehler = ProzessFehler("Zugriff auf Spielprozess " + pid, fehler);
                            if (fehler == 5) verweigert = LetzterFehler;
                            continue;
                        }
                        ulong basis = 0;
                        foreach (ProcessModule modul in prozess.Modules)
                            if (modul.ModuleName.Equals(modulname, StringComparison.OrdinalIgnoreCase))
                            {
                                basis = (ulong)modul.BaseAddress.ToInt64();
                                if (pfad.Length == 0) pfad = modul.FileName;
                                break;
                            }
                        if (basis == 0)
                        {
                            LetzterFehler = "EXE-Modul " + modulname + " im Spielprozess " + pid + " nicht gefunden";
                            continue;
                        }
                        if (!GetExitCodeProcess(griff, out uint code))
                        {
                            LetzterFehler = ProzessFehler("Spielprozess konnte nicht geprüft werden", Marshal.GetLastWin32Error());
                            continue;
                        }
                        if (code != 259)
                        {
                            LetzterFehler = "Spielprozess wurde während des Verbindens beendet";
                            continue;
                        }
                        // Publish attachment only after every required step has succeeded.
                        _griff = griff;
                        griff = IntPtr.Zero;
                        Pid = pid;
                        Basis = basis;
                        Programmpfad = pfad;
                        LetzterFehler = "";
                        return true;
                    }
                    catch (Exception ex) when (ProzessNichtVerfuegbar(ex))
                    {
                        LetzterFehler = ex is Win32Exception win32
                            ? ProzessFehler("Spielprozess nicht erreichbar", win32.NativeErrorCode)
                            : "Spielprozess während des Verbindens nicht erreichbar: " + ex.Message;
                        if (ex is Win32Exception denied && denied.NativeErrorCode == 5) verweigert = LetzterFehler;
                    }
                    finally { if (griff != IntPtr.Zero) CloseHandle(griff); }
                }
                if (verweigert != null) LetzterFehler = verweigert;
                return false;
            }
            catch (Exception ex) when (ProzessNichtVerfuegbar(ex))
            {
                LetzterFehler = "Spielprozess konnte nicht gesucht werden: " + ex.Message;
                return false;
            }
            finally { foreach (var prozess in treffer) prozess.Dispose(); }
        }

        private static bool ProzessNichtVerfuegbar(Exception ex) => ex is Win32Exception || ex is InvalidOperationException ||
            ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException;

        private static string ProzessFehler(string aktion, int code) => code == 5
            ? aktion + " verweigert (Windows-Fehler 5: Zugriff verweigert). Spiel und Floppy mit denselben Rechten starten; Spiel bevorzugt normal über Steam."
            : aktion + " fehlgeschlagen (Windows-Fehler " + code + ": " + new Win32Exception(code).Message + ")";

        private static string LiesProgrammpfad(int pid)
        {
            IntPtr griff = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (griff == IntPtr.Zero) return "";
            try
            {
                var pfad = new StringBuilder(32768);
                int laenge = pfad.Capacity;
                return QueryFullProcessImageName(griff, 0, pfad, ref laenge) ? pfad.ToString() : "";
            }
            finally { CloseHandle(griff); }
        }

        public void Trenne()
        {
            if (_griff != IntPtr.Zero) CloseHandle(_griff);
            _griff = IntPtr.Zero;
            Basis = 0;
            Pid = 0;
            Programmpfad = "";
        }

        public void Dispose() => Trenne();

        /// <summary>Läuft der Prozess noch? Beim Spielende wird sonst ins Leere gelesen.</summary>
        public bool Lebt()
        {
            if (!Offen) return false;

            // Test the attached handle, not a PID that Windows could already have reused.
            if (GetExitCodeProcess(_griff, out uint code) && code == 259) return true;
            LetzterFehler = "Der angebundene Spielprozess wurde beendet oder ist nicht mehr erreichbar";
            Trenne();
            return false;
        }

        // ---------------------------------------------------------------- Lesen

        public byte[] Lies(ulong adresse, int groesse)
        {
            if (!Offen || adresse < 0x10000 || groesse <= 0) return null;

            var puffer = new byte[groesse];
            if (!ReadProcessMemory(_griff, (IntPtr)(long)adresse, puffer, groesse, out IntPtr gelesen))
                return null;

            return gelesen.ToInt64() == groesse ? puffer : null;
        }

        public ulong U64(ulong a) { var d = Lies(a, 8); return d == null ? 0 : BitConverter.ToUInt64(d, 0); }
        public uint U32(ulong a) { var d = Lies(a, 4); return d == null ? 0 : BitConverter.ToUInt32(d, 0); }
        public int I32(ulong a) { var d = Lies(a, 4); return d == null ? 0 : BitConverter.ToInt32(d, 0); }
        public float F32(ulong a) { var d = Lies(a, 4); return d == null ? 0f : BitConverter.ToSingle(d, 0); }
        public double F64(ulong a) { var d = Lies(a, 8); return d == null ? 0d : BitConverter.ToDouble(d, 0); }
        public bool Flagge(ulong a) { var d = Lies(a, 1); return d != null && d[0] != 0; }

        // ---------------------------------------------------------------- Schreiben

        public bool Schreibe(ulong adresse, byte[] daten)
        {
            if (!Offen)
            {
                LetzterFehler = "Schreiben nicht möglich: keine Verbindung zum Spielprozess";
                return false;
            }
            if (adresse < 0x10000 || daten == null || daten.Length == 0)
            {
                LetzterFehler = "Schreiben nicht möglich: ungültige Speicheradresse oder leere Daten";
                return false;
            }
            if (!WriteProcessMemory(_griff, (IntPtr)(long)adresse, daten, daten.Length, out IntPtr n))
            {
                LetzterFehler = ProzessFehler("Schreiben in den Spielprozess", Marshal.GetLastWin32Error());
                return false;
            }
            if (n.ToInt64() != daten.Length)
            {
                LetzterFehler = "Schreiben in den Spielprozess unvollständig: " + n.ToInt64() + " von " + daten.Length + " Bytes";
                return false;
            }
            LetzterFehler = "";
            return true;
        }

        public bool SchreibeF32(ulong a, float wert) => Schreibe(a, BitConverter.GetBytes(wert));
        public bool SchreibeF64(ulong a, double wert) => Schreibe(a, BitConverter.GetBytes(wert));
        public bool SchreibeI32(ulong a, int wert) => Schreibe(a, BitConverter.GetBytes(wert));
        public bool SchreibeFlagge(ulong a, bool wert) => Schreibe(a, new[] { (byte)(wert ? 1 : 0) });
    }
}
