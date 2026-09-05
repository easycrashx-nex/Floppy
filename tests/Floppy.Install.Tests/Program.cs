using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Floppy.App;

string root = Path.Combine(Path.GetTempPath(), "floppy-install-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
string runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
int passed = 0;
try
{
    MakeRuntime();
    Run("Unknown non-Unity directory is not ready", () => Assert(!Installer.Bereit(GameDir("unknown")), "unknown engine ready"));
    Run("Missing module fails before changing game", () =>
    {
        string dir = InstallDir("preflight");
        string module = Path.Combine(runtime, "mono/plugins/Floppy.HowToFish.dll");
        File.Move(module, module + ".hold");
        try
        {
            Assert(!Installer.Einrichten(dir, out _, "How to Fish"), "missing module accepted");
            Assert(Directory.EnumerateFileSystemEntries(dir).Count() == 1, "preflight mutated game");
        }
        finally { File.Move(module + ".hold", module); }
    });
    Run("Mono install selects exact module, diagnoses missing file, repairs and restores", () =>
    {
        string dir = InstallDir("mono");
        Assert(Installer.Einrichten(dir, out string message, "How to Fish"), message);
        Assert(Installer.Bereit(dir, "How to Fish"), "installed mono not ready");
        Assert(!File.Exists(Path.Combine(dir, "BepInEx/plugins/Floppy/Floppy.Stonewards.dll")), "foreign module installed");
        string plugin = Path.Combine(dir, "BepInEx/plugins/Floppy/Floppy.Unity.Mono.dll");
        File.Delete(plugin);
        Assert(!Installer.Bereit(dir, "How to Fish"), "incomplete module ready");
        Assert(Installer.Diagnose(dir, "How to Fish").Contains("Fehlt:"), "missing file not diagnosed");
        Assert(Installer.Einrichten(dir, out message, "How to Fish"), message);
        File.WriteAllText(Path.Combine(dir, "my-mod.txt"), "keep me");
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(!File.Exists(Path.Combine(dir, "winhttp.dll")), "owned loader remains");
        Assert(File.ReadAllText(Path.Combine(dir, "my-mod.txt")) == "keep me", "other file changed");
    });
    Run("Foreign loader is never overwritten", () =>
    {
        string dir = InstallDir("foreign");
        File.WriteAllText(Path.Combine(dir, "winhttp.dll"), "another loader");
        Assert(!Installer.Einrichten(dir, out _, "How to Fish"), "foreign loader accepted");
        Assert(File.ReadAllText(Path.Combine(dir, "winhttp.dll")) == "another loader", "foreign loader overwritten");
    });
    Run("Copy failure rolls back replaced files", () =>
    {
        string dir = InstallDir("rollback");
        MakeLoader(dir, "Mono");
        string plugins = Path.Combine(dir, "BepInEx/plugins/Floppy");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "Floppy.Model.dll"), "previous model");
        Directory.CreateDirectory(Path.Combine(plugins, "Floppy.Unity.Mono.dll"));
        Assert(!Installer.Einrichten(dir, out _, "How to Fish"), "copy failure accepted");
        Assert(File.ReadAllText(Path.Combine(plugins, "Floppy.Model.dll")) == "previous model", "previous file not restored");
        Assert(!File.Exists(Path.Combine(dir, ".floppy/installation.json")), "failed install marked ready");
    });
    Run("Existing complete loader survives install and restore", () =>
    {
        string dir = InstallDir("existing");
        MakeLoader(dir, "Mono");
        string plugins = Path.Combine(dir, "BepInEx/plugins/Floppy");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "Floppy.Model.dll"), "previous model");
        Assert(Installer.Einrichten(dir, out string message, "How to Fish"), message);
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllText(Path.Combine(plugins, "Floppy.Model.dll")) == "previous model", "original plugin lost");
        Assert(File.Exists(Path.Combine(dir, "winhttp.dll")), "existing loader deleted");
    });
    Run("Locked second existing DLL does not prevent rollback of first DLL", () =>
    {
        string dir = InstallDir("locked-update");
        MakeLoader(dir, "Mono");
        string plugins = Path.Combine(dir, "BepInEx/plugins/Floppy");
        Directory.CreateDirectory(plugins);
        string model = Path.Combine(plugins, "Floppy.Model.dll");
        string engine = Path.Combine(plugins, "Floppy.Unity.Mono.dll");
        File.WriteAllText(model, "old model");
        File.WriteAllText(engine, "old engine");
        using var locked = new FileStream(engine, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert(!Installer.Einrichten(dir, out _, "How to Fish"), "locked update accepted");
        Assert(File.ReadAllText(model) == "old model", "first DLL was not rolled back");
        Assert(File.ReadAllText(engine) == "old engine", "locked DLL changed");
        Assert(!File.Exists(Path.Combine(dir, ".floppy/installation.json")), "failed install manifest retained");
    });
    Run("Locked second original during restore rolls back first restored DLL", () =>
    {
        string dir = InstallDir("locked-restore");
        MakeLoader(dir, "Mono");
        string plugins = Path.Combine(dir, "BepInEx/plugins/Floppy");
        Directory.CreateDirectory(plugins);
        string model = Path.Combine(plugins, "Floppy.Model.dll");
        string engine = Path.Combine(plugins, "Floppy.Unity.Mono.dll");
        File.WriteAllText(model, "old model");
        File.WriteAllText(engine, "old engine");
        Assert(Installer.Einrichten(dir, out string message, "How to Fish"), message);
        string installed = File.ReadAllText(model);
        using var locked = new FileStream(engine, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert(!Installer.Wiederherstellen(dir, out _), "locked restore accepted");
        Assert(File.ReadAllText(model) == installed, "first restored DLL was not rolled back");
        Assert(File.Exists(Path.Combine(dir, ".floppy/installation.json")), "recovery manifest lost");
    });
    Run("IL2CPP uses its loader and exact module", () =>
    {
        string dir = InstallDir("il2cpp");
        Assert(Installer.Einrichten(dir, out string message, "ODDCORE"), message);
        Assert(Installer.Bereit(dir, "ODDCORE"), "IL2CPP not ready");
        Assert(File.Exists(Path.Combine(dir, "BepInEx/plugins/Floppy/Floppy.Oddcore.dll")), "ODDCORE module absent");
        Assert(Installer.Wiederherstellen(dir, out message), message);
    });
    Run("Pitt package preserves data/version, installs idempotently and restores byte-for-byte", () =>
    {
        string dir = InstallDir("pitt");
        string pck = Path.Combine(dir, "projectpitt.pck");
        MakePck(pck);
        byte[] original = File.ReadAllBytes(pck);
        Assert(Installer.Einrichten(dir, out string message, "Project P.I.T.T."), message);
        Assert(Installer.Bereit(dir, "Project P.I.T.T."), "Pitt not ready");
        byte[] once = File.ReadAllBytes(pck);
        Assert(BitConverter.ToInt32(once, 16) == 2, "engine patch version changed");
        Assert(Encoding.UTF8.GetString(once).Contains("original payload"), "original contents lost");
        Assert(Encoding.UTF8.GetString(once).Contains("autoload/Floppy"), "autoload absent");
        Assert(Installer.Einrichten(dir, out message, "Project P.I.T.T."), message);
        Assert(File.ReadAllBytes(pck).SequenceEqual(once), "second install differs");
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllBytes(pck).SequenceEqual(original), "PCK not exactly restored");
    });
    Run("Unsupported PCK fails without mutation", () =>
    {
        string dir = InstallDir("invalid-pck");
        string pck = Path.Combine(dir, "projectpitt.pck");
        File.WriteAllText(pck, "unknown package");
        Assert(!Installer.Einrichten(dir, out _, "Project P.I.T.T."), "invalid package installed");
        Assert(File.ReadAllText(pck) == "unknown package", "invalid original changed");
        Assert(!Directory.Exists(Path.Combine(dir, ".floppy")), "invalid package got install records");
    });
    Run("Restore preserves files replaced by a game update", () =>
    {
        string dir = InstallDir("update");
        Assert(Installer.Einrichten(dir, out string message, "How to Fish"), message);
        string loader = Path.Combine(dir, "winhttp.dll");
        File.WriteAllText(loader, "new game update");
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllText(loader) == "new game update", "game update overwritten");
        Assert(message.Contains("geänderte Dateien"), "preserved change not reported");
    });
    Run("Wrong selected game folder is rejected", () =>
    {
        string dir = GameDir("wrong-folder");
        Directory.CreateDirectory(Path.Combine(dir, "OtherGame_Data"));
        Assert(!Installer.Einrichten(dir, out _, "How to Fish"), "wrong game folder accepted");
        Assert(!File.Exists(Path.Combine(dir, "winhttp.dll")), "loader copied into wrong game");
    });
    Run("Missing original backup aborts restoration before deleting anything", () =>
    {
        string dir = InstallDir("missing-backup");
        MakeLoader(dir, "Mono");
        string plugins = Path.Combine(dir, "BepInEx/plugins/Floppy");
        Directory.CreateDirectory(plugins);
        File.WriteAllText(Path.Combine(plugins, "Floppy.Model.dll"), "original");
        Assert(Installer.Einrichten(dir, out string message, "How to Fish"), message);
        File.Delete(Path.Combine(dir, ".floppy/original/BepInEx/plugins/Floppy/Floppy.Model.dll"));
        Assert(!Installer.Wiederherstellen(dir, out _), "restored without original");
        Assert(File.Exists(Path.Combine(plugins, "Floppy.HowToFish.dll")), "other owned files already deleted");
    });
    Run("Manifest cannot traverse outside installation", () =>
    {
        string dir = InstallDir("traversal");
        Directory.CreateDirectory(Path.Combine(dir, ".floppy"));
        File.WriteAllText(Path.Combine(dir, ".floppy/installation.json"), "{\"Game\":\"How to Fish\",\"Files\":[{\"Path\":\"../outside.txt\"}]}");
        Assert(!Installer.Wiederherstellen(dir, out _), "path traversal accepted");
    });
    if (args.Length == 2 && args[0] == "--pck")
        Run("Installed PCK can be prepared without changing its source", () =>
        {
            byte[] before;
            using (var input = File.OpenRead(args[1])) before = SHA256.HashData(input);
            GodotPck.Patch(args[1], Path.Combine(root, "compatibility-only.pck"), Path.Combine(runtime, "pitt"));
            using var after = File.OpenRead(args[1]);
            Assert(before.SequenceEqual(SHA256.HashData(after)), "source PCK changed");
        });
    if (args.Length == 1)
        Run("Published package contains all runtime files", () =>
        {
            foreach (string file in new[] { "Floppy.exe", "runtime/mono/plugins/Floppy.Model.dll", "runtime/mono/plugins/Floppy.Unity.Mono.dll",
                "runtime/mono/plugins/Floppy.HowToFish.dll", "runtime/mono/plugins/Floppy.Stonewards.dll", "runtime/il2cpp/plugins/Floppy.Model.dll",
                "runtime/il2cpp/plugins/Floppy.Unity.IL2CPP.dll", "runtime/il2cpp/plugins/Floppy.Oddcore.dll", "runtime/pitt/floppy.gd", "runtime/pitt/floppy_overlay.gd" })
                Assert(File.Exists(Path.Combine(args[0], file)), "published file missing: " + file);
            foreach (string engine in new[] { "mono", "il2cpp" })
                Assert(Directory.EnumerateFiles(Path.Combine(args[0], "runtime", engine), "BepInEx-*.zip").Count() == 1, "loader archive absent or ambiguous");
        });
    Console.WriteLine($"PASS: {passed} installer/package tests");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
finally { Directory.Delete(root, recursive: true); }

void Run(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
string GameDir(string name) { string dir = Path.Combine(root, name); Directory.CreateDirectory(dir); return dir; }
string InstallDir(string name) { string dir = GameDir(name); Directory.CreateDirectory(Path.Combine(dir, name == "il2cpp" ? "ODDCORE_Data" : "How to Fish_Data")); return dir; }
void MakeLoader(string dir, string engine)
{
    foreach (string name in new[] { "winhttp.dll", "doorstop_config.ini", "BepInEx/core/BepInEx.Core.dll", "BepInEx/core/BepInEx.Unity." + engine + ".dll" })
    { string path = Path.Combine(dir, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "loader-" + name); }
}
void MakeRuntime()
{
    foreach (string engine in new[] { "mono", "il2cpp" })
    {
        string dir = Path.Combine(runtime, engine);
        Directory.CreateDirectory(Path.Combine(dir, "plugins"));
        foreach (string name in new[] { "Floppy.Model", "Floppy.Unity.Mono", "Floppy.HowToFish", "Floppy.Stonewards", "Floppy.Unity.IL2CPP", "Floppy.Oddcore" })
            File.WriteAllText(Path.Combine(dir, "plugins", name + ".dll"), "new-" + name);
        string loader = GameDir("loader-" + engine);
        MakeLoader(loader, engine == "mono" ? "Mono" : "IL2CPP");
        string archive = Path.Combine(dir, "BepInEx-Unity." + engine + "-win-x64-test.zip");
        if (File.Exists(archive)) File.Delete(archive);
        ZipFile.CreateFromDirectory(loader, archive);
    }
    Directory.CreateDirectory(Path.Combine(runtime, "pitt"));
    File.WriteAllText(Path.Combine(runtime, "pitt/floppy.gd"), "extends Node\n");
    File.WriteAllText(Path.Combine(runtime, "pitt/floppy_overlay.gd"), "extends CanvasLayer\n");
}
void MakePck(string path)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(Encoding.ASCII.GetBytes("GDPC"));
    writer.Write(4); writer.Write(4); writer.Write(7); writer.Write(2); writer.Write(2);
    writer.Write((ulong)112); writer.Write((ulong)0); writer.Write(new byte[72]);
    var files = new[] { ("res://project.binary", Encoding.ASCII.GetBytes("ECFG\0\0\0\0")), ("res://keep.bin", Encoding.UTF8.GetBytes("original payload")) };
    var offsets = new List<long>();
    foreach (var (_, content) in files) { offsets.Add(stream.Position - 112); writer.Write(content); while (stream.Position % 16 != 0) writer.Write((byte)0); }
    long index = stream.Position;
    writer.Write(files.Length);
    for (int i = 0; i < files.Length; i++)
    {
        byte[] name = Encoding.UTF8.GetBytes(files[i].Item1);
        int length = (name.Length + 3) / 4 * 4;
        writer.Write(length); writer.Write(name); writer.Write(new byte[length - name.Length]);
        writer.Write((ulong)offsets[i]); writer.Write((ulong)files[i].Item2.Length); writer.Write(MD5.HashData(files[i].Item2)); writer.Write(0);
    }
    stream.Position = 32; writer.Write((ulong)index);
}


