using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Floppy.Unreal;

internal static class Program
{
    private static int _checks;

    private static int Main(string[] args)
    {
        if (args.Length > 0) return Probe.Run(args);
        try
        {
            using var process = Process.GetCurrentProcess();
            string processName = process.ProcessName;
            string moduleName = process.MainModule!.ModuleName;
            OwnMemory(processName, moduleName);
            ConnectionFailures(processName, moduleName);
            PropertyLayout(processName, moduleName);
            Console.WriteLine($"PASS: {_checks} Unreal memory checks; own process and injected failures only.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
        Console.WriteLine("PASS " + message);
    }

    private static void OwnMemory(string processName, string moduleName)
    {
        using var memory = new Speicher();
        IntPtr buffer = Marshal.AllocHGlobal(32);
        try
        {
            var original = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
            Marshal.Copy(original, 0, buffer, original.Length);
            ulong address = (ulong)buffer.ToInt64();
            Check(memory.Verbinde(processName, moduleName), "own process attaches: " + memory.LetzterFehler);
            Check(memory.Offen && memory.Lebt() && memory.Pid == Environment.ProcessId && memory.Basis != 0,
                "successful attachment publishes live handle, PID and base");
            Check(string.Equals(Path.GetFullPath(memory.Programmpfad), Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase),
                "executable path identifies actual process image");
            Check(memory.Lies(address, 32)?.SequenceEqual(original) == true, "ReadProcessMemory reads real own-process buffer");
            Check(memory.SchreibeF32(address, 17.25f) && memory.SchreibeF64(address + 8, -42.5)
                && memory.SchreibeI32(address + 16, 123456) && memory.SchreibeFlagge(address + 20, true),
                "WriteProcessMemory writes typed own-process values");
            Check(memory.F32(address) == 17.25f && memory.F64(address + 8) == -42.5
                && Marshal.ReadInt32(buffer + 16) == 123456 && Marshal.ReadByte(buffer + 20) == 1,
                "native buffer independently confirms typed writes");
            Check(!memory.Schreibe(ulong.MaxValue, new byte[] { 1 }) && memory.LetzterFehler.Contains("Windows-Fehler"),
                "invalid native write reports Windows failure");
            Check(!memory.Schreibe(1, new byte[] { 1 }) && memory.LetzterFehler.Contains("ungültige Speicheradresse"),
                "invalid low address rejected before native write");
            Check(memory.Lies(address, -1) == null && memory.Lies(address, 0) == null, "invalid read sizes do not throw");
            memory.Trenne();
            Check(!memory.Offen && memory.Pid == 0 && memory.Basis == 0 && memory.Programmpfad == "", "disconnect clears published attachment");
            Check(!memory.Schreibe(address, new byte[] { 1 }) && memory.LetzterFehler.Contains("keine Verbindung"),
                "disconnected writes report missing attachment");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static void ConnectionFailures(string processName, string moduleName)
    {
        using var missing = new Speicher();
        Check(!missing.Verbinde("Floppy-NoSuchProcess-" + Guid.NewGuid().ToString("N"), moduleName)
            && missing.LetzterFehler.Contains("läuft nicht") && !missing.Offen && missing.Pid == 0 && missing.Basis == 0,
            "missing process returns bounded diagnostic and no attachment");
        Check(!missing.Verbinde(processName, "Floppy-Missing-Module.exe") && missing.LetzterFehler.Contains("nicht gefunden")
            && !missing.Offen && missing.Pid == 0 && missing.Basis == 0, "missing module releases candidate handle");

        int requestedRights = 0;
        using var denied = Inject((rights, pid) => { requestedRights = rights; return IntPtr.Zero; }, () => 5);
        Check(!denied.Verbinde(processName, moduleName), "injected access denial returns false");
        Check(requestedRights == 0x438 && denied.LetzterFehler.Contains("Windows-Fehler 5")
            && denied.LetzterFehler.Contains("denselben Rechten") && denied.LetzterFehler.Contains("normal über Steam"),
            "access denial explains matching rights without elevating privileges");
        Check(!denied.Offen && denied.Pid == 0 && denied.Basis == 0 && denied.Programmpfad.Length > 0,
            "denied attachment retains read-only executable diagnostic but no active PID/base");

        using var raced = Inject((_, _) => throw new InvalidOperationException("fixture process exited"), () => 0);
        Check(!raced.Verbinde(processName, moduleName) && raced.LetzterFehler.Contains("fixture process exited")
            && !raced.Offen && raced.Pid == 0 && raced.Basis == 0, "process-exit race does not escape connection method");
        using var nativeError = Inject((_, _) => throw new Win32Exception(5), () => 0);
        Check(!nativeError.Verbinde(processName, moduleName) && nativeError.LetzterFehler.Contains("Windows-Fehler 5"),
            "native process query failure keeps access-denied explanation");
    }

    private static Speicher Inject(Func<int, int, IntPtr> open, Func<int> error) =>
        (Speicher)Activator.CreateInstance(typeof(Speicher), BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, args: new object[] { open, error }, culture: null)!;

    private static void PropertyLayout(string processName, string moduleName)
    {
        using var memory = new Speicher();
        Check(memory.Verbinde(processName, moduleName), "reflection fixture attaches only to own process");
        var reflection = new Reflexion(memory, 0, 0);
        var readOffset = typeof(Reflexion).GetMethod("WertAbstand", BindingFlags.Instance | BindingFlags.NonPublic)!;
        IntPtr field = Marshal.AllocHGlobal(0x60);
        try
        {
            ulong address = (ulong)field.ToInt64();
            int Offset(ulong p) => (int)readOffset.Invoke(reflection, new object[] { p })!;
            Marshal.WriteInt32(field + 0x44, 0);
            Marshal.WriteInt32(field + 0x4c, 64);
            Check(Offset(address) == 0, "first function parameter legitimately has offset zero");
            Marshal.WriteInt32(field + 0x44, 0x148);
            Check(Offset(address) == 0x148, "property uses the PDB-confirmed Offset_Internal member");
            Marshal.WriteInt32(field + 0x44, -1);
            Check(Offset(address) == -1, "invalid property offset cannot fall back to unrelated data");
            Check(Offset(0) == -1, "unreadable property metadata is not accepted as offset zero");
        }
        finally { Marshal.FreeHGlobal(field); }
    }
}
