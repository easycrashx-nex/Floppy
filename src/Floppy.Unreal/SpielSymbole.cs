using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Floppy.Unreal
{
    internal sealed record SpielAdressen(ulong Objekte, ulong Namen, ulong Welt, ulong ProcessEvent);

    /// <summary>Liest ausschließlich die passende lokale PDB. Unbekannte Fassungen bekommen keine Ersatzadressen.</summary>
    internal static class SpielSymbole
    {
        private static readonly object Schloss = new(); // DbgHelp ist pro Prozess nicht threadsicher.
        private static DateiKennung _kennung;
        private static SpielAdressen _adressen;
        private sealed record DateiKennung(string Pfad, DateiId Exe, DateiId Pdb, Guid Guid, int Alter);
        private sealed record DateiId(uint Laufwerk, ulong Index, ulong Laenge, ulong Schreibzeit);

        public static bool TryResolve(string exePath, out SpielAdressen adressen, out string error)
        {
            adressen = null;
            error = "";
            lock (Schloss)
            {
                try
                {
                    string exe = Path.GetFullPath(exePath);
                    string pdb = Path.ChangeExtension(exe, ".pdb");
                    // Die Handles verhindern Austausch/Schreiben während Prüfung und Symbolauflösung.
                    using var bild = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var symbole = new FileStream(pdb, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var pe = new PEReader(bild, PEStreamOptions.LeaveOpen);
                    var header = pe.PEHeaders.PEHeader;
                    if (IntPtr.Size != 8 || pe.PEHeaders.CoffHeader.Machine != Machine.Amd64 ||
                        header == null || header.SizeOfImage <= 0)
                        throw new InvalidDataException("Die Spiel-EXE ist keine unterstützte 64-Bit-Fassung.");
                    var debug = pe.ReadDebugDirectory().Where(e => e.Type == DebugDirectoryEntryType.CodeView).ToArray();
                    if (debug.Length != 1) throw new InvalidDataException("Die Spiel-EXE hat keine eindeutige PDB-Kennung.");
                    var id = pe.ReadCodeViewDebugDirectoryData(debug[0]);
                    var kennung = new DateiKennung(exe, Identitaet(bild), Identitaet(symbole), id.Guid, id.Age);
                    if (kennung == _kennung && _adressen != null) { adressen = _adressen; return true; }

                    // Ein eigener Bezeichner: kein Spielprozess und kein Eingriff in andere Symbol-Sitzungen.
                    IntPtr sitzung = Marshal.AllocHGlobal(1);
                    uint vorher = SymGetOptions();
                    bool gestartet = false;
                    try
                    {
                        // Kein Netzwerk-Suchpfad, kein Symbolserver, keine Dialoge oder unpassende Symbole.
                        SymSetOptions(0x2 | 0x4 | 0x400 | 0x1000 | 0x80000 | 0x200 | 0x02000000);
                        gestartet = SymInitializeW(sitzung, Path.GetDirectoryName(pdb), false);
                        if (!gestartet) throw NativeFehler("Symbol-Leser konnte nicht gestartet werden");
                        ulong basis = SymLoadModuleExW(sitzung, symbole.SafeFileHandle.DangerousGetHandle(),
                            pdb, "FloppySpiel", header.ImageBase, (uint)header.SizeOfImage, IntPtr.Zero, 0);
                        if (basis == 0) throw NativeFehler("Die lokale PDB konnte nicht geladen werden");

                        // Erzwingt auch bei verzögertem Laden die PDB-Prüfung vor der Rückgabe von Adressen.
                        ulong objekte = Finde(sitzung, basis, pe, "GUObjectArray", false);
                        var info = new ModulInfo { SizeOfStruct = (uint)Marshal.SizeOf<ModulInfo>() };
                        if (!SymGetModuleInfoW64(sitzung, basis, ref info)) throw NativeFehler("PDB-Kennung nicht lesbar");
                        // Direkt geladene PDBs melden PdbUnmatched auch bei gleicher Kennung;
                        // deshalb vergleichen wir ihre tatsächliche GUID/Alter selbst mit der EXE.
                        if (info.PdbSig70 != id.Guid || info.PdbAge != id.Age || info.SymType != 3 ||
                            !string.Equals(Path.GetFullPath(info.LoadedPdbName), pdb, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Die lokale PDB passt nicht zur gestarteten Spiel-EXE.");

                        adressen = new SpielAdressen(objekte,
                            Finde(sitzung, basis, pe, "NamePoolData", false),
                            Finde(sitzung, basis, pe, "GWorld", false),
                            Finde(sitzung, basis, pe, "UObject::ProcessEvent", true));
                        _kennung = kennung;
                        _adressen = adressen;
                        return true;
                    }
                    finally
                    {
                        if (gestartet) SymCleanup(sitzung);
                        SymSetOptions(vorher);
                        Marshal.FreeHGlobal(sitzung);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    ArgumentException or InvalidDataException or BadImageFormatException or Win32Exception or
                    DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
                {
                    error = "Spielsymbole nicht verfügbar: " + ex.Message;
                    return false;
                }
            }
        }

        private static ulong Finde(IntPtr sitzung, ulong basis, PEReader pe, string name, bool funktion)
        {
            IntPtr puffer = Marshal.AllocHGlobal(Marshal.SizeOf<SymbolInfo>() + 1024);
            try
            {
                var symbol = new SymbolInfo { SizeOfStruct = (uint)Marshal.SizeOf<SymbolInfo>(), MaxNameLen = 1024 };
                Marshal.StructureToPtr(symbol, puffer, false);
                if (!SymFromName(sitzung, "FloppySpiel!" + name, puffer)) throw NativeFehler("Symbol fehlt: " + name);
                symbol = Marshal.PtrToStructure<SymbolInfo>(puffer);
                if (symbol.Address < basis || symbol.ModBase != basis || symbol.Tag != (funktion ? 5u : 7u))
                    throw new InvalidDataException("Ungültiges Spielsymbol: " + name);
                ulong rva = symbol.Address - basis;
                bool passt = pe.PEHeaders.SectionHeaders.Any(s =>
                    rva >= (ulong)s.VirtualAddress && rva < (ulong)s.VirtualAddress + (uint)s.VirtualSize &&
                    Math.Max(1UL, symbol.Size) <= (ulong)s.VirtualAddress + (uint)s.VirtualSize - rva &&
                    (s.SectionCharacteristics & SectionCharacteristics.MemRead) != 0 &&
                    ((s.SectionCharacteristics & SectionCharacteristics.MemExecute) != 0) == funktion &&
                    (funktion || (s.SectionCharacteristics & SectionCharacteristics.MemWrite) != 0));
                if (!passt) throw new InvalidDataException("Spielsymbol liegt im falschen EXE-Bereich: " + name);
                return rva;
            }
            finally { Marshal.FreeHGlobal(puffer); }
        }

        private static Win32Exception NativeFehler(string text) => new(Marshal.GetLastWin32Error(), text);

        private static DateiId Identitaet(FileStream datei)
        {
            if (!GetFileInformationByHandle(datei.SafeFileHandle, out var info)) throw NativeFehler("Dateikennung nicht lesbar");
            return new DateiId(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow,
                ((ulong)info.FileSizeHigh << 32) | info.FileSizeLow,
                ((ulong)info.LastWriteHigh << 32) | info.LastWriteLow);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DateiInfo
        {
            public uint FileAttributes, CreationLow, CreationHigh, LastAccessLow, LastAccessHigh,
                LastWriteLow, LastWriteHigh, VolumeSerialNumber, FileSizeHigh, FileSizeLow,
                NumberOfLinks, FileIndexHigh, FileIndexLow;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SymbolInfo
        {
            public uint SizeOfStruct, TypeIndex;
            public ulong Reserved1, Reserved2;
            public uint Index, Size;
            public ulong ModBase;
            public uint Flags;
            public ulong Value, Address;
            public uint Register, Scope, Tag, NameLen, MaxNameLen;
            public byte Name;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ModulInfo
        {
            public uint SizeOfStruct;
            public ulong BaseOfImage;
            public uint ImageSize, TimeDateStamp, CheckSum, NumSyms, SymType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ModuleName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ImageName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string LoadedImageName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string LoadedPdbName;
            public uint CVSig;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 780)] public string CVData;
            public uint PdbSig;
            public Guid PdbSig70;
            public uint PdbAge;
            public int PdbUnmatched, DbgUnmatched, LineNumbers, GlobalSymbols, TypeInfo, SourceIndexed, Publics;
            public uint MachineType, Reserved;
        }

        // System32 verhindert, dass eine DLL aus dem Spielordner als Symbol-Leser geladen wird.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out DateiInfo info);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll")] private static extern uint SymGetOptions();
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll")] private static extern uint SymSetOptions(uint options);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool SymInitializeW(IntPtr process, string searchPath, bool invadeProcess);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern ulong SymLoadModuleExW(IntPtr process, IntPtr file, string imageName,
            string moduleName, ulong baseOfDll, uint dllSize, IntPtr data, uint flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern bool SymFromName(IntPtr process, string name, IntPtr symbol);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool SymGetModuleInfoW64(IntPtr process, ulong address, ref ModulInfo module);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("dbghelp.dll")] private static extern bool SymCleanup(IntPtr process);
    }
}
