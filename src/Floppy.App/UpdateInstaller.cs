using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Floppy.App;

public static class UpdateInstaller
{
    private const int ReadyTimeoutMs = 30000;
    private const int ParentTimeoutMs = 45000;
    internal static string UpdatesRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Floppy", "Updates");

    public static void Start(string verifiedDownload, string expectedSha256, string version) =>
        StartCore(verifiedDownload, expectedSha256, version, UpdatesRoot);

    internal static void StartCore(string verifiedDownload, string expectedSha256, string version, string updatesRoot)
    {
        using var current = Process.GetCurrentProcess();
        var prepared = Prepare(verifiedDownload, expectedSha256, version,
            Environment.ProcessPath ?? throw new InvalidOperationException("Der aktuelle EXE-Pfad fehlt."),
            current.Id, current.StartTime.ToUniversalTime().Ticks, updatesRoot);
        using var candidate = OpenPinned(prepared.SourceExe);
        VerifyHash(candidate, expectedSha256);
        var info = HelperStartInfo(prepared, NeedsElevation(Path.GetDirectoryName(prepared.TargetExe)!));
        using var helper = Process.Start(info) ?? throw new IOException("Der Update-Helfer konnte nicht gestartet werden.");
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < ReadyTimeoutMs)
        {
            if (helper.HasExited) throw new IOException(ReadFailure(prepared.Folder));
            if (ReadProof(Path.Combine(prepared.Folder, "ready.txt"), prepared.Proof))
            {
                // An explicit acknowledgement prevents a late helper from installing
                // after Start has already timed out and left the old UI open.
                WriteNew(Path.Combine(prepared.Folder, "continue.txt"), prepared.Proof);
                return;
            }
            Thread.Sleep(40);
        }
        throw new TimeoutException("Der Update-Helfer wurde nicht rechtzeitig bereit. Floppy bleibt geöffnet.");
    }

    public static int Run(string planPath, string planSha) => RunCore(planPath, planSha, new ApplyRuntime());

    internal static PreparedUpdate Prepare(string download, string expectedSha256, string version,
        string target, int parentPid, long parentStartedUtcTicks, string updatesRoot)
    {
        ValidateHash(expectedSha256);
        ValidateVersion(version);
        target = Path.GetFullPath(target);
        ValidateTarget(target, updatesRoot);
        if (SamePath(target, Path.GetFullPath(download))) throw new IOException("Download und aktuelle EXE sind identisch.");
        EnsureNoReparsePoint(updatesRoot);
        EnsureNoReparsePoint(target);
        string folder = Path.Combine(Path.GetFullPath(updatesRoot), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string source = Path.Combine(folder, "Floppy.Update.exe");
        using (var input = OpenPinned(Path.GetFullPath(download)))
        {
            VerifyHash(input, expectedSha256);
            CopyPinned(input, source);
        }
        var plan = new UpdatePlan
        {
            SourceExe = source, TargetExe = target, ParentPid = parentPid,
            ParentStartedUtcTicks = parentStartedUtcTicks, OldSha256 = HashFile(target),
            NewSha256 = expectedSha256, Version = version, Nonce = Guid.NewGuid().ToString("N")
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(plan);
        string planPath = Path.Combine(folder, "plan.json");
        using (var file = new FileStream(planPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { file.Write(bytes); file.Flush(true); }
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        return new PreparedUpdate(folder, source, target, planPath, digest, digest + "\n" + plan.Nonce);
    }

    internal static ProcessStartInfo HelperStartInfo(PreparedUpdate prepared, bool elevate)
    {
        var info = new ProcessStartInfo(prepared.SourceExe)
        {
            UseShellExecute = elevate, Verb = elevate ? "runas" : "",
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = prepared.Folder
        };
        info.ArgumentList.Add("--apply-update");
        info.ArgumentList.Add(prepared.PlanPath);
        info.ArgumentList.Add(prepared.PlanSha);
        return info;
    }

    internal static int RunCore(string planPath, string planSha, ApplyRuntime runtime)
    {
        string? folder = null;
        UpdatePlan? plan = null;
        IUpdateParent? parent = null;
        FileStream? transactionLock = null;
        bool replaced = false;
        bool parentValidated = false;
        string? backup = null;
        try
        {
            folder = ValidatePlanPath(planPath, runtime.Root);
            transactionLock = new FileStream(Path.Combine(folder, "apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            ValidateHash(planSha);
            using var planFile = OpenPinned(planPath);
            if (planFile.Length > 16384) throw new IOException("Der Update-Plan ist zu groß.");
            byte[] bytes = new byte[checked((int)planFile.Length)];
            planFile.ReadExactly(bytes);
            if (!HashEquals(Convert.ToHexString(SHA256.HashData(bytes)), planSha)) throw new IOException("Der Update-Plan wurde verändert.");
            plan = JsonSerializer.Deserialize<UpdatePlan>(bytes) ?? throw new IOException("Der Update-Plan fehlt.");
            ValidatePlan(plan, folder, runtime.Root, runtime.CurrentExe);
            using var source = OpenPinned(plan.SourceExe);
            VerifyHash(source, plan.NewSha256);
            parent = runtime.OpenParent(plan.ParentPid);
            if (parent.StartedUtcTicks != plan.ParentStartedUtcTicks || !SamePath(parent.ExecutablePath, plan.TargetExe))
                throw new IOException("Der ursprüngliche Floppy-Prozess stimmt nicht mit dem Update-Plan überein.");
            parentValidated = true;
            if (!HashEquals(HashFile(plan.TargetExe), plan.OldSha256)) throw new IOException("Die aktuelle EXE wurde zwischenzeitlich verändert.");
            string proof = planSha.ToUpperInvariant() + "\n" + plan.Nonce;
            WriteNew(Path.Combine(folder, "ready.txt"), proof);
            var timer = Stopwatch.StartNew();
            while (!ReadProof(Path.Combine(folder, "continue.txt"), proof))
            {
                if (timer.ElapsedMilliseconds >= runtime.ApprovalTimeoutMs)
                    throw new TimeoutException("Das Update wurde von der laufenden App nicht freigegeben.");
                Thread.Sleep(40);
            }
            if (!parent.WaitForExit(ParentTimeoutMs)) throw new TimeoutException("Floppy hat einen laufenden Vorgang noch nicht beendet. Die alte EXE bleibt erhalten.");

            EnsureNoReparsePoint(plan.TargetExe);
            if (!HashEquals(HashFile(plan.TargetExe), plan.OldSha256)) throw new IOException("Die aktuelle EXE wurde vor dem Austausch verändert.");
            string targetFolder = Path.GetDirectoryName(plan.TargetExe)!;
            string sibling = Path.Combine(targetFolder, ".Floppy-update-" + plan.Nonce + ".new");
            backup = Path.Combine(targetFolder, ".Floppy-backup-" + plan.Nonce + ".exe");
            if (File.Exists(backup)) throw new IOException("Eine Updatesicherung mit diesem Namen existiert bereits.");
            CopyPinned(source, sibling);
            using (var copied = OpenPinned(sibling)) VerifyHash(copied, plan.NewSha256);
            File.Replace(sibling, plan.TargetExe, backup);
            replaced = true;
            runtime.StartTarget(plan.TargetExe);
            WriteResult(runtime.Root, folder, true, "Floppy " + plan.Version + " wurde installiert. Die vorherige EXE bleibt als Sicherung erhalten.", plan.Version);
            return 0;
        }
        catch (Exception error)
        {
            string message = "Update nicht abgeschlossen: " + error.Message;
            if (plan != null && parentValidated)
            {
                try
                {
                    if (replaced && backup != null)
                    {
                        if (!HashEquals(HashFile(plan.TargetExe), plan.NewSha256))
                            throw new IOException("Die neue Zieldatei wurde verändert; sie wird nicht überschrieben.");
                        string rollback = Path.Combine(Path.GetDirectoryName(plan.TargetExe)!, ".Floppy-rollback-" + plan.Nonce + ".new");
                        using (var old = OpenPinned(backup)) { VerifyHash(old, plan.OldSha256); CopyPinned(old, rollback); }
                        string rejected = Path.Combine(Path.GetDirectoryName(plan.TargetExe)!, ".Floppy-rejected-" + plan.Nonce + ".exe");
                        if (File.Exists(rejected)) throw new IOException("Die Sicherung der abgewiesenen EXE existiert bereits.");
                        File.Replace(rollback, plan.TargetExe, rejected);
                        message += " Die vorherige EXE wurde wiederhergestellt.";
                    }
                    if (parent?.HasExited == true && HashEquals(HashFile(plan.TargetExe), plan.OldSha256))
                        runtime.StartTarget(plan.TargetExe);
                }
                catch (Exception recoveryError) { message += " Wiederherstellung/Neustart: " + recoveryError.Message; }
            }
            if (backup != null && File.Exists(backup)) message += " Sicherung: " + backup;
            WriteResult(runtime.Root, folder, false, message, plan?.Version ?? "");
            return 1;
        }
        finally { parent?.Dispose(); transactionLock?.Dispose(); }
    }

    private static void ValidatePlan(UpdatePlan plan, string folder, string root, string currentExe)
    {
        ValidateTarget(plan.TargetExe, root);
        ValidateHash(plan.NewSha256); ValidateHash(plan.OldSha256); ValidateVersion(plan.Version);
        if (plan.ParentPid <= 0 || plan.ParentStartedUtcTicks <= 0 || !Guid.TryParseExact(plan.Nonce, "N", out _))
            throw new IOException("Die Prozessidentität im Update-Plan ist ungültig.");
        string source = Path.Combine(folder, "Floppy.Update.exe");
        if (!SamePath(plan.SourceExe, source) || !SamePath(currentExe, source) || SamePath(source, plan.TargetExe))
            throw new IOException("Der Update-Helfer liegt nicht am erwarteten Ort.");
        EnsureNoReparsePoint(source); EnsureNoReparsePoint(plan.TargetExe);
    }

    private static string ValidatePlanPath(string path, string root)
    {
        RequireAbsolute(path);
        string folder = Path.GetDirectoryName(path)!;
        if (!string.Equals(Path.GetFileName(path), "plan.json", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(folder), "N", out _) || !SamePath(Path.GetDirectoryName(folder)!, Path.GetFullPath(root)))
            throw new IOException("Der Update-Plan liegt außerhalb des Updateordners.");
        EnsureNoReparsePoint(path);
        return folder;
    }

    private static void ValidateTarget(string target, string root)
    {
        RequireAbsolute(target);
        string name = Path.GetFileNameWithoutExtension(target);
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) || name.Equals("devenv", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) || name.StartsWith("vstest", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Diese EXE kann nicht durch das automatische Update ersetzt werden.");
    }

    private static void RequireAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.IndexOf(':', 2) >= 0 || !SamePath(path, Path.GetFullPath(path)))
            throw new IOException("Ein Updatepfad ist nicht eindeutig absolut.");
    }

    private static void EnsureNoReparsePoint(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Updatepfade werden nicht verändert.");
    }

    private static bool NeedsElevation(string directory)
    {
        try
        {
            using var probe = new FileStream(Path.Combine(directory, ".Floppy-write-probe-" + Guid.NewGuid().ToString("N")),
                FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return false;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException) { return true; }
    }

    private static FileStream OpenPinned(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static string HashFile(string path) { using var stream = OpenPinned(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static void VerifyHash(FileStream stream, string expected)
    { stream.Position = 0; if (!HashEquals(Convert.ToHexString(SHA256.HashData(stream)), expected)) throw new IOException("Die SHA-256-Prüfung ist fehlgeschlagen."); stream.Position = 0; }
    private static bool HashEquals(string actual, string expected) => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    private static bool SamePath(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static void ValidateHash(string hash) { if (hash == null || !Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z")) throw new IOException("Die SHA-256-Prüfsumme ist ungültig."); }
    private static void ValidateVersion(string version)
    { if (version == null || !Regex.IsMatch(version, @"\A\d+\.\d+\.\d+(?:\.\d+)?\z") || !Version.TryParse(version, out _)) throw new IOException("Die Updateversion ist ungültig."); }
    private static void CopyPinned(FileStream input, string destination)
    { input.Position = 0; using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None); input.CopyTo(output); output.Flush(true); }
    private static void WriteNew(string path, string value)
    { string temporary = path + "." + Guid.NewGuid().ToString("N"); File.WriteAllText(temporary, value); File.Move(temporary, path, false); }
    private static bool ReadProof(string path, string proof)
    { try { return File.Exists(path) && new FileInfo(path).Length < 1024 && File.ReadAllText(path) == proof; } catch (IOException) { return false; } }
    private static string ReadFailure(string folder)
    { try { return JsonSerializer.Deserialize<UpdateResult>(File.ReadAllText(Path.Combine(folder, "result.json")))?.Message ?? "Der Update-Helfer wurde beendet."; } catch (Exception) { return "Der Update-Helfer wurde beendet."; } }
    private static void WriteResult(string root, string? folder, bool success, string message, string version)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new UpdateResult(success, message, version, DateTime.UtcNow));
        foreach (string path in folder == null ? new[] { Path.Combine(root, "last-result.json") } : new[] { Path.Combine(folder, "result.json"), Path.Combine(root, "last-result.json") })
        {
            try { EnsureNoReparsePoint(path); Directory.CreateDirectory(Path.GetDirectoryName(path)!); string temp = path + "." + Guid.NewGuid().ToString("N"); File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); }
            catch (Exception) { /* A report failure must not invalidate an already completed replacement. */ }
        }
    }

    internal sealed record PreparedUpdate(string Folder, string SourceExe, string TargetExe, string PlanPath, string PlanSha, string Proof);
    internal sealed record UpdateResult(bool Success, string Message, string Version, DateTime TimestampUtc);
    internal sealed class UpdatePlan
    {
        public string SourceExe { get; set; } = "";
        public string TargetExe { get; set; } = "";
        public int ParentPid { get; set; }
        public long ParentStartedUtcTicks { get; set; }
        public string OldSha256 { get; set; } = "";
        public string NewSha256 { get; set; } = "";
        public string Version { get; set; } = "";
        public string Nonce { get; set; } = "";
    }

    internal interface IUpdateParent : IDisposable
    {
        string ExecutablePath { get; }
        long StartedUtcTicks { get; }
        bool HasExited { get; }
        bool WaitForExit(int milliseconds);
    }
    private sealed class UpdateParent : IUpdateParent
    {
        private readonly Process _process;
        public UpdateParent(int pid) { _process = Process.GetProcessById(pid); _ = _process.SafeHandle; }
        public string ExecutablePath => _process.MainModule?.FileName ?? throw new IOException("Der ursprüngliche EXE-Pfad ist nicht lesbar.");
        public long StartedUtcTicks => _process.StartTime.ToUniversalTime().Ticks;
        public bool HasExited => _process.HasExited;
        public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);
        public void Dispose() => _process.Dispose();
    }
    internal sealed class ApplyRuntime
    {
        public string Root { get; init; } = UpdatesRoot;
        public string CurrentExe { get; init; } = Environment.ProcessPath ?? "";
        public Func<int, IUpdateParent> OpenParent { get; init; } = pid => new UpdateParent(pid);
        public Action<string> StartTarget { get; init; } = path =>
        { using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! }); if (process == null) throw new IOException("Floppy konnte nicht neu gestartet werden."); };
        public int ApprovalTimeoutMs { get; init; } = ReadyTimeoutMs;
    }
}
