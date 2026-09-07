using Floppy.Unreal;
using System.Reflection.PortableExecutable;

string game = null, source = null, error;
for (int i = 0; i < args.Length; i += 2)
{
    if (i + 1 >= args.Length) throw new ArgumentException("Options require a path");
    switch (args[i])
    {
        case "--game-path": game = Path.GetFullPath(args[i + 1]); break;
        case "--fixture-path": source = Path.GetFullPath(args[i + 1]); break;
        default: throw new ArgumentException("Unknown option: " + args[i]);
    }
}
if (SpielSymbole.TryResolve(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), out var missing, out error) || missing != null || error.Length == 0)
    throw new Exception("Missing build unexpectedly accepted");
Console.WriteLine("PASS missing EXE fails closed without fallback");

string scratch = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Floppy.Symbol.Tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(scratch);
try
{
    string invalid = Path.Combine(scratch, "invalid.exe");
    File.WriteAllBytes(invalid, new byte[128]);
    Reject(invalid, "missing adjacent PDB fails closed");
    File.WriteAllBytes(Path.ChangeExtension(invalid, ".pdb"), new byte[128]);
    Reject(invalid, "invalid PE with adjacent PDB fails closed");
    if (game != null)
    {
        Require(SpielSymbole.TryResolve(game, out var actual, out error), "opt-in local game symbols resolve: " + error);
        Console.WriteLine($"objects=0x{actual.Objekte:X}, names=0x{actual.Namen:X}, world=0x{actual.Welt:X}, event=0x{actual.ProcessEvent:X}");
        Require(SpielSymbole.TryResolve(game, out var cached, out error) && ReferenceEquals(actual, cached),
            "success-only cache for unchanged EXE/PDB identity");
    }
    if (source == null)
    {
        Console.WriteLine("SKIP native symbol fixtures: run build-fixture.ps1 and supply --fixture-path (no game required)");
        return;
    }
    string valid = Pair("valid");
    Require(SpielSymbole.TryResolve(valid, out var first, out error), "native fixture resolves all four symbols: " + error);
    string validPdb = Path.ChangeExtension(valid, ".pdb");
    var before = File.GetLastWriteTimeUtc(validPdb);
    File.Move(validPdb, validPdb + ".old");
    File.Copy(validPdb + ".old", validPdb);
    File.SetLastWriteTimeUtc(validPdb, before);
    Require(SpielSymbole.TryResolve(valid, out var replaced, out error) && !ReferenceEquals(first, replaced),
        "replaced PDB invalidates cache despite identical length and timestamp: " + error);

    string noPdb = Pair("missing-pdb");
    File.Delete(Path.ChangeExtension(noPdb, ".pdb"));
    Reject(noPdb, "missing local PDB is rejected");

    string mismatch = Pair("wrong-identity");
    var bytes = File.ReadAllBytes(mismatch);
    using (var image = new PEReader(new MemoryStream(bytes)))
    {
        var debug = image.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
        bytes[debug.DataPointer + 4] ^= 0x5A; // Only this synthetic EXE's RSDS GUID.
    }
    File.WriteAllBytes(mismatch, bytes);
    Reject(mismatch, "mismatching PDB GUID is rejected", "passt nicht");

    string age = Pair("wrong-age");
    bytes = File.ReadAllBytes(age);
    using (var image = new PEReader(new MemoryStream(bytes)))
    {
        var debug = image.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
        bytes[debug.DataPointer + 20] ^= 0x5A;
    }
    File.WriteAllBytes(age, bytes);
    Reject(age, "mismatching PDB age is rejected", "passt nicht");

    string section = Pair("wrong-section");
    bytes = File.ReadAllBytes(section);
    using (var image = new PEReader(new MemoryStream(bytes)))
    {
        int table = BitConverter.ToInt32(bytes, 0x3C) + 4 + 20 + image.PEHeaders.CoffHeader.SizeOfOptionalHeader;
        for (int i = 0; i < image.PEHeaders.SectionHeaders.Length; i++)
        {
            var s = image.PEHeaders.SectionHeaders[i];
            if ((s.SectionCharacteristics & SectionCharacteristics.MemWrite) == 0) continue;
            int offset = table + i * 40 + 36;
            BitConverter.GetBytes(BitConverter.ToUInt32(bytes, offset) & ~0x80000000u).CopyTo(bytes, offset);
        }
    }
    File.WriteAllBytes(section, bytes);
    Reject(section, "data symbols in nonwritable EXE sections are rejected", "EXE-Bereich");

    string broken = Pair("broken-pdb");
    File.WriteAllBytes(Path.ChangeExtension(broken, ".pdb"), new byte[64]);
    Reject(broken, "corrupt PDB fails closed");
    File.Copy(Path.ChangeExtension(source, ".pdb"), Path.ChangeExtension(broken, ".pdb"), true);
    Require(SpielSymbole.TryResolve(broken, out _, out error), "failed resolution is not cached after PDB repair: " + error);
}
finally
{
    string prefix = Path.GetFullPath(Path.GetTempPath()) + "Floppy.Symbol.Tests-";
    if (!scratch.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected cleanup path");
    Directory.Delete(scratch, true);
}

string Pair(string name)
{
    string target = Path.Combine(scratch, name, "fixture.exe");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.Copy(source, target);
    File.Copy(Path.ChangeExtension(source, ".pdb"), Path.ChangeExtension(target, ".pdb"));
    return target;
}
void Reject(string path, string description, string contains = "")
{
    Require(!SpielSymbole.TryResolve(path, out var value, out var why) && value == null &&
        why.Length > 0 && why.Contains(contains, StringComparison.Ordinal), description + ": " + why);
}
static void Require(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("PASS " + description);
}
