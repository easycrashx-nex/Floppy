using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Floppy.App;

/// <summary>Installs only a game's declared files and keeps the previous files for restoration.</summary>
public static class Installer
{
    private static string RuntimeDir => Path.Combine(AppContext.BaseDirectory, "runtime");
    private const string StateFolder = ".floppy";
    private const string StateFile = "installation.json";
    private const string CurrentVersion = "1.3.0";
    public enum Ausfuehrung { Mono, IL2CPP, Fremd }
    private sealed record Game(string Id, string Runtime, string? Module);
    public sealed class SavedFile
    {
        public string Path { get; set; } = "";
        public bool Existed { get; set; }
        public string Hash { get; set; } = "";
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public bool Removed { get; set; }
    }
    public sealed class InstallState
    {
        public string Game { get; set; } = "";
        public string Version { get; set; } = CurrentVersion;
        public List<SavedFile> Files { get; set; } = new();
    }

    public static Ausfuehrung ErkenneAusfuehrung(string installDir)
    {
        if (File.Exists(Path.Combine(installDir, "GameAssembly.dll"))) return Ausfuehrung.IL2CPP;
        return File.Exists(Path.Combine(installDir, "UnityPlayer.dll")) ||
            (Directory.Exists(installDir) && Directory.EnumerateDirectories(installDir, "*_Data").Any())
            ? Ausfuehrung.Mono : Ausfuehrung.Fremd;
    }

    private static Game Identify(string directory, string? productName)
    {
        string name = productName ?? "";
        if (name.Length != 0)
            return name switch
            {
                "How to Fish" => new(name, "mono", "Floppy.HowToFish.dll"),
                "Stonewards" => new(name, "mono", "Floppy.Stonewards.dll"),
                "ODDCORE" => new(name, "il2cpp", "Floppy.Oddcore.dll"),
                "Project P.I.T.T." => new(name, "pitt", null),
                "MortalShell2" or "Unrailed2" => new(name, "external", null),
                _ => throw new InvalidOperationException("Für dieses Spiel ist keine Installation definiert.")
            };
        if (name == "How to Fish" || Directory.Exists(Path.Combine(directory, "How to Fish_Data")))
            return new("How to Fish", "mono", "Floppy.HowToFish.dll");
        if (name == "Stonewards" || Directory.Exists(Path.Combine(directory, "Stonewards_Data")))
            return new("Stonewards", "mono", "Floppy.Stonewards.dll");
        if (name == "ODDCORE" || Directory.Exists(Path.Combine(directory, "ODDCORE_Data")))
            return new("ODDCORE", "il2cpp", "Floppy.Oddcore.dll");
        if (name == "Project P.I.T.T." || File.Exists(Path.Combine(directory, "projectpitt.pck")))
            return new("Project P.I.T.T.", "pitt", null);
        if (name is "MortalShell2" or "Unrailed2") return new(name, "external", null);
        if (File.Exists(Path.Combine(directory, "Unrailed2.exe"))) return new("Unrailed2", "external", null);
        if (Directory.EnumerateFiles(directory, "*MortalShell*.exe", SearchOption.TopDirectoryOnly).Any())
            return new("MortalShell2", "external", null);
        throw new InvalidOperationException("Spiel nicht eindeutig erkannt. Bitte das Spiel und seinen Ordner auswählen.");
    }

    public static bool LoaderVorhanden(string installDir) => File.Exists(Path.Combine(installDir, "winhttp.dll"));

    private static string[] PluginFiles(Game game) => new[]
    {
        "Floppy.Model.dll", game.Runtime == "mono" ? "Floppy.Unity.Mono.dll" : "Floppy.Unity.IL2CPP.dll", game.Module!
    };
    private static string[] LoaderFiles(Game game) => new[]
    {
        "winhttp.dll", "doorstop_config.ini", "BepInEx/core/BepInEx.Core.dll",
        "BepInEx/core/BepInEx.Unity." + (game.Runtime == "mono" ? "Mono" : "IL2CPP") + ".dll"
    };

    public static bool Bereit(string installDir, string? productName = null)
    {
        try
        {
            if (!Directory.Exists(installDir)) return false;
            var game = Identify(installDir, productName);
            if (game.Runtime == "external") return true;
            var state = LoadState(installDir);
            if (state == null || state.Game != game.Id || state.Version != CurrentVersion) return false;
            // PCK files can be large; hash them only during installation/restoration/diagnosis.
            if (game.Runtime == "pitt")
            {
                var saved = state.Files.Find(f => f.Path == "projectpitt.pck");
                var package = new FileInfo(Path.Combine(installDir, "projectpitt.pck"));
                return saved != null && package.Exists && package.Length == saved.Length &&
                    package.LastWriteTimeUtc.Ticks == saved.LastWriteUtcTicks &&
                    state.Files.Where(f => IsLegacyPittOverlay(f.Path)).All(f => f.Removed && !File.Exists(Inside(installDir, f.Path)));
            }
            return LoaderFiles(game).All(f => File.Exists(Inside(installDir, f))) &&
                PluginFiles(game).All(f =>
                {
                    string relative = "BepInEx/plugins/Floppy/" + f;
                    var saved = state.Files.Find(s => s.Path == relative);
                    return saved != null && File.Exists(Inside(installDir, relative)) && Hash(Inside(installDir, relative)) == saved.Hash &&
                        File.Exists(Path.Combine(RuntimeDir, game.Runtime, "plugins", f)) &&
                        Hash(Path.Combine(RuntimeDir, game.Runtime, "plugins", f)) == saved.Hash;
                });
        }
        catch (Exception) { return false; }
    }

    public static bool SpielLaeuft(string installDir)
    {
        // Match full paths: another game's generic launcher.exe must not block this game.
        string prefix = Path.GetFullPath(installDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var names = Directory.EnumerateFiles(installDir, "*.exe", SearchOption.AllDirectories)
            .Where(p => !p.Contains(Path.DirectorySeparatorChar + StateFolder + Path.DirectorySeparatorChar))
            .Select(Path.GetFileNameWithoutExtension).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string? name in names)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("UnityCrashHandler", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is string path && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (System.ComponentModel.Win32Exception) { return true; }
                    catch (InvalidOperationException) { /* Process already exited. */ }
                }
            }
        }
        return false;
    }

    public static string Diagnose(string installDir, string? productName = null)
    {
        try
        {
            if (!Directory.Exists(installDir)) return "Spielordner nicht gefunden: " + installDir;
            var game = Identify(installDir, productName);
            var lines = new List<string> { "Spiel: " + game.Id, "Ordner: " + Path.GetFullPath(installDir), "Floppy: " + CurrentVersion };
            if (game.Runtime == "external") { lines.Add("Externer Adapter: keine Plugin-Installation nötig."); return string.Join(Environment.NewLine, lines); }
            var state = LoadState(installDir);
            if (state == null) lines.Add("Keine von Floppy verwaltete Installation. Einrichten/Reparieren legt Sicherungen an.");
            else
            {
                lines.Add("Gesicherte Installation: " + state.Game + " " + state.Version);
                foreach (var file in state.Files)
                    if (file.Removed)
                    {
                        if (File.Exists(Inside(installDir, file.Path))) lines.Add("Entfernte Altdatei erneut vorhanden: " + file.Path);
                    }
                    else if (!File.Exists(Inside(installDir, file.Path))) lines.Add("Fehlt: " + file.Path);
                    else if (Hash(Inside(installDir, file.Path)) != file.Hash) lines.Add("Seit Installation geändert: " + file.Path);
            }
            if (game.Runtime != "pitt")
                foreach (string file in LoaderFiles(game))
                    if (!File.Exists(Inside(installDir, file))) lines.Add("Loader-Datei fehlt: " + file);
            lines.Add(Bereit(installDir, game.Id) ? "Installation vollständig." : "Einrichten/Reparieren erforderlich.");
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) { return "Diagnose fehlgeschlagen: " + ex.Message; }
    }

    public static bool Einrichten(string installDir, out string meldung, string? productName = null)
    {
        string staging = Path.Combine(Path.GetTempPath(), "floppy-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!Directory.Exists(installDir)) throw new IOException("Spielordner nicht gefunden");
            installDir = Path.GetFullPath(installDir);
            if (SpielLaeuft(installDir)) throw new IOException("Spiel läuft – bitte erst beenden");
            var game = Identify(installDir, productName);
            if (game.Runtime == "external") { meldung = "Externer Adapter bereit"; return true; }
            if (game.Runtime is "mono" or "il2cpp" && !Directory.Exists(Path.Combine(installDir, game.Id + "_Data")))
                throw new IOException("Der ausgewählte Ordner gehört nicht zu " + game.Id + ": " + game.Id + "_Data fehlt.");
            Directory.CreateDirectory(staging);
            var plan = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var state = LoadState(installDir) ?? new InstallState { Game = game.Id };
            if (state.Game != game.Id) throw new IOException("In diesem Ordner ist ein anderes Floppy-Spiel registriert.");
            if (game.Runtime == "pitt")
            {
                string original = Inside(installDir, "projectpitt.pck");
                string patched = Path.Combine(staging, "projectpitt.pck");
                var previous = state.Files.Find(f => f.Path == "projectpitt.pck");
                if (previous != null && Hash(original) != previous.Hash)
                    throw new IOException("Spielpaket wurde seit der Installation geändert. Erst Wiederherstellen wählen (geänderte Dateien bleiben erhalten), danach neu einrichten.");
                GodotPck.Patch(original, patched, Path.Combine(RuntimeDir, "pitt"));
                plan.Add("projectpitt.pck", patched);
                // Only remove loose files owned by an earlier Floppy installation.
                // A manually changed file remains untouched and is reported first.
                foreach (var legacy in state.Files.Where(f => IsLegacyPittOverlay(f.Path)))
                {
                    string path = Inside(installDir, legacy.Path);
                    if (File.Exists(path) && (legacy.Removed || Hash(path) != legacy.Hash))
                        throw new IOException("Altes Menüskript wurde inzwischen geändert: " + legacy.Path + ". Erst Wiederherstellen wählen; geänderte Dateien bleiben erhalten.");
                    plan.Add(legacy.Path, null);
                }
            }
            else
            {
                foreach (string file in PluginFiles(game))
                {
                    string source = Path.Combine(RuntimeDir, game.Runtime, "plugins", file);
                    if (!File.Exists(source)) throw new IOException("Laufzeitdatei fehlt: " + source);
                    plan.Add("BepInEx/plugins/Floppy/" + file, source);
                }
                bool completeLoader = LoaderFiles(game).All(f => File.Exists(Inside(installDir, f)));
                if (!completeLoader)
                {
                    if (LoaderVorhanden(installDir) && !state.Files.Any(f => f.Path.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("Vorhandener Loader ist nicht vollständig oder gehört zu einer anderen Mod. Bitte dessen Installation prüfen; er wird nicht überschrieben.");
                    string archive = Directory.EnumerateFiles(Path.Combine(RuntimeDir, game.Runtime), "BepInEx-Unity.*-win-x64-*.zip").SingleOrDefault()
                        ?? throw new IOException("Genau ein passendes BepInEx-Archiv wird benötigt.");
                    string extracted = Path.Combine(staging, "loader");
                    ZipFile.ExtractToDirectory(archive, extracted);
                    foreach (string file in LoaderFiles(game))
                        if (!File.Exists(Inside(extracted, file))) throw new InvalidDataException("BepInEx-Archiv unvollständig: " + file);
                    foreach (string source in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
                        plan.Add(Path.GetRelativePath(extracted, source).Replace('\\', '/'), source);
                }
            }
            Apply(installDir, plan, state, staging);
            meldung = game.Id + ": Floppy eingerichtet; vorherige Dateien sind gesichert.";
            return true;
        }
        catch (Exception ex) { meldung = "Einrichten fehlgeschlagen: " + ex.Message; return false; }
        finally { CleanTemp(staging); }
    }

    private static void Apply(string directory, Dictionary<string, string?> plan, InstallState state, string staging)
    {
        // Snapshot everything before the first replacement; an I/O failure rolls back this attempt.
        string stateDir = Inside(directory, StateFolder);
        var previous = new Dictionary<string, string?>();
        int index = 0;
        foreach (var pair in plan)
        {
            string target = Inside(directory, pair.Key);
            string? snapshot = null;
            if (File.Exists(target)) { snapshot = Path.Combine(staging, "before-" + index); File.Copy(target, snapshot); }
            previous.Add(pair.Key, snapshot);
            index++;
        }
        string manifest = Inside(stateDir, StateFile);
        string? oldManifest = File.Exists(manifest) ? File.ReadAllText(manifest) : null;
        var touched = new List<string>();
        try
        {
            Directory.CreateDirectory(stateDir);
            foreach (var pair in plan)
            {
                var file = state.Files.Find(f => f.Path.Equals(pair.Key, StringComparison.OrdinalIgnoreCase));
                if (file == null)
                {
                    file = new SavedFile { Path = pair.Key, Existed = previous[pair.Key] != null };
                    state.Files.Add(file);
                    if (file.Existed)
                    {
                        string backup = Inside(stateDir, "original/" + pair.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(previous[pair.Key]!, backup, overwrite: true);
                    }
                }
                file.Removed = pair.Value == null;
                file.Hash = pair.Value == null ? "" : Hash(pair.Value);
                var sourceInfo = pair.Value == null ? null : new FileInfo(pair.Value);
                file.Length = sourceInfo?.Length ?? 0;
                file.LastWriteUtcTicks = sourceInfo?.LastWriteTimeUtc.Ticks ?? 0;
            }
            // Persist recovery information first: an interrupted install can still be restored.
            state.Version = CurrentVersion;
            WriteState(manifest, state);
            foreach (var pair in plan)
            {
                string target = Inside(directory, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (pair.Value == null)
                {
                    if (File.Exists(target)) File.Delete(target);
                }
                else ReplaceFile(pair.Value, target);
                touched.Add(pair.Key);
            }
        }
        catch (Exception failure)
        {
            var rollbackErrors = RestoreSnapshots(directory, touched, previous);
            if (rollbackErrors.Count > 0)
                throw new IOException("Rücksetzen nicht vollständig möglich; Sicherungen unter .floppy/original behalten. " +
                    failure.Message, new AggregateException(rollbackErrors.Prepend(failure)));
            if (oldManifest != null) File.WriteAllText(manifest, oldManifest);
            else if (File.Exists(manifest)) File.Delete(manifest);
            throw;
        }
    }

    public static bool Wiederherstellen(string installDir, out string meldung)
    {
        string staging = Path.Combine(Path.GetTempPath(), "floppy-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (!Directory.Exists(installDir)) throw new IOException("Spielordner nicht gefunden");
            if (SpielLaeuft(installDir)) throw new IOException("Spiel läuft – bitte erst beenden");
            var state = LoadState(installDir);
            if (state == null) { meldung = "Keine gesicherte Installation vorhanden"; return false; }
            string stateDir = Inside(installDir, StateFolder);
            var restore = new List<SavedFile>();
            int preserved = 0;
            foreach (var file in state.Files)
            {
                string target = Inside(installDir, file.Path);
                if (File.Exists(target) && (file.Removed || Hash(target) != file.Hash)) { preserved++; continue; }
                if (file.Existed && !File.Exists(Inside(stateDir, "original/" + file.Path)))
                    throw new IOException("Sicherung fehlt: " + file.Path);
                restore.Add(file);
            }
            Directory.CreateDirectory(staging);
            var snapshots = new Dictionary<string, string?>();
            foreach (var file in restore)
            {
                string target = Inside(installDir, file.Path);
                string? snapshot = File.Exists(target) ? Path.Combine(staging, snapshots.Count.ToString()) : null;
                if (snapshot != null) File.Copy(target, snapshot);
                snapshots.Add(file.Path, snapshot);
            }
            var touched = new List<SavedFile>();
            try
            {
                foreach (var file in restore)
                {
                    string target = Inside(installDir, file.Path);
                    if (file.Existed)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        ReplaceFile(Inside(stateDir, "original/" + file.Path), target);
                    }
                    else if (File.Exists(target)) File.Delete(target);
                    touched.Add(file);
                }
                File.Delete(Inside(stateDir, StateFile));
            }
            catch (Exception failure)
            {
                var rollbackErrors = RestoreSnapshots(installDir, touched.Select(f => f.Path), snapshots);
                if (rollbackErrors.Count > 0)
                    throw new IOException("Rücksetzen nicht vollständig möglich; Sicherungen unter .floppy/original behalten. " +
                        failure.Message, new AggregateException(rollbackErrors.Prepend(failure)));
                throw;
            }
            // Keep original backups for manual recovery; do not delete user-created files or directories.
            meldung = "Vorherige Dateien wiederhergestellt; Floppy entfernt." +
                (preserved > 0 ? $" {preserved} inzwischen geänderte Dateien wurden unverändert gelassen." : "");
            return true;
        }
        catch (Exception ex) { meldung = "Wiederherstellen fehlgeschlagen: " + ex.Message; return false; }
        finally { CleanTemp(staging); }
    }

    private static InstallState? LoadState(string directory)
    {
        string path = Inside(directory, StateFolder + "/" + StateFile);
        if (!File.Exists(path)) return null;
        var state = JsonSerializer.Deserialize<InstallState>(File.ReadAllText(path)) ?? throw new InvalidDataException("Installationsdaten sind beschädigt.");
        foreach (var file in state.Files) Inside(directory, file.Path);
        if (state.Files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Files.Count)
            throw new InvalidDataException("Doppelte Einträge in Installationsdaten.");
        return state;
    }
    private static bool IsLegacyPittOverlay(string path) => Path.GetFileName(path) is
        "floppy_overlay.gd" or "floppy_overlay.gdc" or "floppy_overlay.gd.remap" or "floppy_overlay.gd.uid";
    private static void WriteState(string path, InstallState state)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, overwrite: true);
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static List<Exception> RestoreSnapshots(string directory, IEnumerable<string> touched, Dictionary<string, string?> snapshots)
    {
        var errors = new List<Exception>();
        foreach (string relative in touched.Reverse())
        {
            try
            {
                string target = Inside(directory, relative);
                if (snapshots[relative] is string snapshot) ReplaceFile(snapshot, target);
                else if (File.Exists(target)) File.Delete(target);
            }
            catch (Exception ex) { errors.Add(ex); }
        }
        return errors;
    }
    private static void ReplaceFile(string source, string target)
    {
        // Prepare on the destination volume so a failed copy cannot truncate a working file.
        string temporary = target + ".floppy-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, target, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Inside(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (Path.IsPathRooted(relative) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Ungültiger relativer Installationspfad.");
        // A junction could escape the checked path despite lexical containment.
        for (string? part = path; part != null && part.Length >= prefix.Length; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Installationspfade werden nicht überschrieben: " + part);
        return path;
    }
    private static void CleanTemp(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
