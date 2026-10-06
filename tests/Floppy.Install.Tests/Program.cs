using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
    Run("Dumb Ways installs only its module and restores existing files", () =>
    {
        string dir = GameDir("dumb-ways");
        Directory.CreateDirectory(Path.Combine(dir, "Dumb Ways to Build_Data"));
        File.WriteAllText(Path.Combine(dir, "Dumb Ways to Build.exe"), "fixture, never executed");
        string module = Path.Combine(dir, "BepInEx/plugins/Floppy/Floppy.DumbWays.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(module)!);
        File.WriteAllText(module, "previous plugin");
        Assert(Installer.Einrichten(dir, out string message), message);
        Assert(Installer.Bereit(dir, "Dumb Ways to Build"), "Dumb Ways not ready");
        Assert(File.Exists(module), "Dumb Ways module absent");
        Assert(!File.Exists(Path.Combine(dir, "BepInEx/plugins/Floppy/Floppy.Oddcore.dll")), "unrelated game module installed");
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllText(module) == "previous plugin" && !File.Exists(Path.Combine(dir, "winhttp.dll")), "Dumb Ways original files not restored");
        Assert(Directory.Exists(Path.Combine(dir, "Dumb Ways to Build_Data")), "game data removed");
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
        Assert(!Encoding.UTF8.GetString(once).Contains("floppy_overlay"), "obsolete menu script present");
        Assert(Installer.Einrichten(dir, out message, "Project P.I.T.T."), message);
        Assert(File.ReadAllBytes(pck).SequenceEqual(once), "second install differs");
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllBytes(pck).SequenceEqual(original), "PCK not exactly restored");
    });
    Run("Pitt upgrade removes old embedded and owned loose menus while retaining original backups", () =>
    {
        string dir = InstallDir("pitt-upgrade");
        var original = PrepareLegacyPitt(dir, originalLoose: "previous loose file");
        string pck = Path.Combine(dir, "projectpitt.pck");
        Assert(!Installer.Bereit(dir, "Project P.I.T.T."), "old backend accepted as current");
        Assert(Installer.Einrichten(dir, out string message, "Project P.I.T.T."), message);
        Assert(Installer.Bereit(dir, "Project P.I.T.T."), "new backend not ready");
        string package = Encoding.UTF8.GetString(File.ReadAllBytes(pck));
        Assert(!package.Contains("floppy_overlay") && !package.Contains("autoload/FloppyOverlay"), "old embedded menu remains");
        Assert(!File.Exists(Path.Combine(dir, "floppy_overlay.gd")), "owned loose menu remains");
        Assert(File.ReadAllBytes(Path.Combine(dir, ".floppy/original/projectpitt.pck")).SequenceEqual(original), "original package backup overwritten");
        Assert(!Installer.Diagnose(dir, "Project P.I.T.T.").Contains("Fehlt:"), "removed menu incorrectly diagnosed as missing");
        Assert(Installer.Einrichten(dir, out message, "Project P.I.T.T."), message);
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllBytes(pck).SequenceEqual(original), "upgrade lost original package");
        Assert(File.ReadAllText(Path.Combine(dir, "floppy_overlay.gd")) == "previous loose file", "upgrade lost original loose file");
    });
    Run("Locked legacy menu rolls back package and old manifest", () =>
    {
        string dir = InstallDir("pitt-locked-upgrade");
        PrepareLegacyPitt(dir);
        string pck = Path.Combine(dir, "projectpitt.pck");
        string manifest = Path.Combine(dir, ".floppy/installation.json");
        byte[] before = File.ReadAllBytes(pck);
        string state = File.ReadAllText(manifest);
        using var locked = new FileStream(Path.Combine(dir, "floppy_overlay.gd"), FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert(!Installer.Einrichten(dir, out _, "Project P.I.T.T."), "locked obsolete menu accepted");
        Assert(File.ReadAllBytes(pck).SequenceEqual(before), "package not rolled back");
        Assert(File.ReadAllText(manifest) == state, "old manifest not recovered");
    });
    Run("Modified legacy menu aborts upgrade before changing the package", () =>
    {
        string dir = InstallDir("pitt-modified-upgrade");
        PrepareLegacyPitt(dir);
        string pck = Path.Combine(dir, "projectpitt.pck");
        byte[] before = File.ReadAllBytes(pck);
        File.WriteAllText(Path.Combine(dir, "floppy_overlay.gd"), "modified independently");
        Assert(!Installer.Einrichten(dir, out _, "Project P.I.T.T."), "modified obsolete file deleted");
        Assert(File.ReadAllBytes(pck).SequenceEqual(before), "package changed before preflight failure");
        Assert(File.ReadAllText(Path.Combine(dir, "floppy_overlay.gd")) == "modified independently", "modified loose file lost");
    });
    Run("Restore preserves a new file at the removed menu path", () =>
    {
        string dir = InstallDir("pitt-recreated-menu");
        PrepareLegacyPitt(dir);
        Assert(Installer.Einrichten(dir, out string message, "Project P.I.T.T."), message);
        File.WriteAllText(Path.Combine(dir, "floppy_overlay.gd"), "new independent file");
        Assert(!Installer.Bereit(dir, "Project P.I.T.T."), "recreated legacy path accepted");
        Assert(Installer.Wiederherstellen(dir, out message), message);
        Assert(File.ReadAllText(Path.Combine(dir, "floppy_overlay.gd")) == "new independent file", "new file overwritten during restore");
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
                "runtime/il2cpp/plugins/Floppy.Unity.IL2CPP.dll", "runtime/il2cpp/plugins/Floppy.Oddcore.dll", "runtime/il2cpp/plugins/Floppy.DumbWays.dll", "runtime/pitt/floppy.gd" })
                Assert(File.Exists(Path.Combine(args[0], file)), "published file missing: " + file);
            Assert(!File.Exists(Path.Combine(args[0], "runtime/pitt/floppy_overlay.gd")), "obsolete Pitt menu is still published");
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
        foreach (string name in new[] { "Floppy.Model", "Floppy.Unity.Mono", "Floppy.HowToFish", "Floppy.Stonewards", "Floppy.Unity.IL2CPP", "Floppy.Oddcore", "Floppy.DumbWays" })
            File.WriteAllText(Path.Combine(dir, "plugins", name + ".dll"), "new-" + name);
        string loader = GameDir("loader-" + engine);
        MakeLoader(loader, engine == "mono" ? "Mono" : "IL2CPP");
        string archive = Path.Combine(dir, "BepInEx-Unity." + engine + "-win-x64-test.zip");
        if (File.Exists(archive)) File.Delete(archive);
        ZipFile.CreateFromDirectory(loader, archive);
    }
    Directory.CreateDirectory(Path.Combine(runtime, "pitt"));
    File.WriteAllText(Path.Combine(runtime, "pitt/floppy.gd"), "extends Node\n");
    string oldOverlay = Path.Combine(runtime, "pitt/floppy_overlay.gd");
    if (File.Exists(oldOverlay)) File.Delete(oldOverlay);
}
byte[] PrepareLegacyPitt(string dir, string? originalLoose = null)
{
    string pck = Path.Combine(dir, "projectpitt.pck");
    MakePck(pck);
    byte[] original = File.ReadAllBytes(pck);
    string backups = Path.Combine(dir, ".floppy/original");
    Directory.CreateDirectory(backups);
    File.Copy(pck, Path.Combine(backups, "projectpitt.pck"));
    MakePck(pck, legacy: true);
    string overlay = Path.Combine(dir, "floppy_overlay.gd");
    File.WriteAllText(overlay, "extends CanvasLayer\n# old installed menu");
    if (originalLoose != null) File.WriteAllText(Path.Combine(backups, "floppy_overlay.gd"), originalLoose);
    var state = new Installer.InstallState { Game = "Project P.I.T.T.", Version = "1.1.0" };
    state.Files.Add(Snapshot(pck, "projectpitt.pck", existed: true));
    state.Files.Add(Snapshot(overlay, "floppy_overlay.gd", existed: originalLoose != null));
    File.WriteAllText(Path.Combine(dir, ".floppy/installation.json"), JsonSerializer.Serialize(state));
    return original;
}
Installer.SavedFile Snapshot(string path, string relative, bool existed) => new()
{
    Path = relative, Existed = existed, Hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
    Length = new FileInfo(path).Length, LastWriteUtcTicks = File.GetLastWriteTimeUtc(path).Ticks
};
void MakePck(string path, bool legacy = false)
{
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write(Encoding.ASCII.GetBytes("GDPC"));
    writer.Write(4); writer.Write(4); writer.Write(7); writer.Write(2); writer.Write(2);
    writer.Write((ulong)112); writer.Write((ulong)0); writer.Write(new byte[72]);
    byte[] settings = Encoding.ASCII.GetBytes("ECFG\0\0\0\0");
    if (legacy)
    {
        using var config = new MemoryStream();
        using var configWriter = new BinaryWriter(config, Encoding.UTF8, leaveOpen: true);
        configWriter.Write(Encoding.ASCII.GetBytes("ECFG")); configWriter.Write(1);
        byte[] key = Encoding.UTF8.GetBytes("autoload/FloppyOverlay");
        byte[] value = Encoding.UTF8.GetBytes("*res://floppy_overlay.gd");
        configWriter.Write(key.Length); configWriter.Write(key);
        configWriter.Write(value.Length); configWriter.Write(value);
        settings = config.ToArray();
    }
    var files = new List<(string, byte[])> { ("res://project.binary", settings), ("res://keep.bin", Encoding.UTF8.GetBytes("original payload")) };
    if (legacy)
        foreach (string name in new[] { "res://floppy_overlay.gd", "floppy_overlay.gd", "res://floppy_overlay.gdc", "res://floppy_overlay.gd.remap", "res://floppy_overlay.gd.uid" })
            files.Add((name, Encoding.UTF8.GetBytes("old menu payload")));
    var offsets = new List<long>();
    foreach (var (_, content) in files) { offsets.Add(stream.Position - 112); writer.Write(content); while (stream.Position % 16 != 0) writer.Write((byte)0); }
    long index = stream.Position;
    writer.Write(files.Count);
    for (int i = 0; i < files.Count; i++)
    {
        byte[] name = Encoding.UTF8.GetBytes(files[i].Item1);
        int length = (name.Length + 3) / 4 * 4;
        writer.Write(length); writer.Write(name); writer.Write(new byte[length - name.Length]);
        writer.Write((ulong)offsets[i]); writer.Write((ulong)files[i].Item2.Length); writer.Write(MD5.HashData(files[i].Item2)); writer.Write(0);
    }
    stream.Position = 32; writer.Write((ulong)index);
}


