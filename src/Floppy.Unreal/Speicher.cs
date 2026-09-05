using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int zugriff, bool erben, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr griff);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ReadProcessMemory(IntPtr griff, IntPtr adresse,
            byte[] puffer, int groesse, out IntPtr gelesen);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool WriteProcessMemory(IntPtr griff, IntPtr adresse,
            byte[] puffer, int groesse, out IntPtr geschrieben);

        private IntPtr _griff;

        public int Pid { get; private set; }
        public ulong Basis { get; private set; }
        public bool Offen => _griff != IntPtr.Zero;

        /// <summary>Hängt sich an den Prozess. Gibt false zurück, wenn er nicht läuft.</summary>
        public bool Verbinde(string prozessname, string modulname)
        {
            Trenne();

            var treffer = Process.GetProcessesByName(prozessname);
            if (treffer.Length == 0) return false;

            var prozess = treffer[0];
            Pid = prozess.Id;

            foreach (ProcessModule modul in prozess.Modules)
            {
                if (!modul.ModuleName.Equals(modulname, StringComparison.OrdinalIgnoreCase))
                    continue;

                Basis = (ulong)modul.BaseAddress.ToInt64();
                break;
            }

            if (Basis == 0) return false;

            _griff = OpenProcess(
                PROCESS_QUERY_INFORMATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_VM_OPERATION,
                false, Pid);

            return Offen;
        }

        public void Trenne()
        {
            if (_griff != IntPtr.Zero) CloseHandle(_griff);
            _griff = IntPtr.Zero;
            Basis = 0;
            Pid = 0;
        }

        public void Dispose() => Trenne();

        /// <summary>Läuft der Prozess noch? Beim Spielende wird sonst ins Leere gelesen.</summary>
        public bool Lebt()
        {
            if (!Offen) return false;

            try
            {
                var p = Process.GetProcessById(Pid);
                return !p.HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- Lesen

        public byte[] Lies(ulong adresse, int groesse)
        {
            if (!Offen || adresse < 0x10000) return null;

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
            if (!Offen || adresse < 0x10000 || daten == null) return false;

            return WriteProcessMemory(_griff, (IntPtr)(long)adresse, daten, daten.Length, out IntPtr n)
                   && n.ToInt64() == daten.Length;
        }

        public bool SchreibeF32(ulong a, float wert) => Schreibe(a, BitConverter.GetBytes(wert));
        public bool SchreibeF64(ulong a, double wert) => Schreibe(a, BitConverter.GetBytes(wert));
        public bool SchreibeI32(ulong a, int wert) => Schreibe(a, BitConverter.GetBytes(wert));
        public bool SchreibeFlagge(ulong a, bool wert) => Schreibe(a, new[] { (byte)(wert ? 1 : 0) });
    }
}
