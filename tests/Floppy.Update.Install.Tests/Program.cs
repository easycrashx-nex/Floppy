using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Floppy.App;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); _checks++; Console.WriteLine("PASS " + message); }
    private static void Reject(Action action, string message)
    { try { action(); } catch (IOException) { Check(true, message); return; } throw new Exception(message); }

    private static int Main(string[] args)
    {
        string? fixtureRoot = Environment.GetEnvironmentVariable("FLOPPY_UPDATE_SMOKE_ROOT");
        if (fixtureRoot != null) return ProtocolFixture(args, fixtureRoot);
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP update file transaction checks require Windows."); return 0; }
        try
        {
            if (args.Length == 2 && args[0] == "--protocol-smoke")
            { ProtocolSmoke(args[1]); Console.WriteLine($"{_checks} real updater protocol checks passed."); return 0; }
            PreparationAndArguments();
            SuccessfulReplacement();
            InvalidPlansAndIdentity();
            ParentTimeoutAndMissingApproval();
            LockedAndChangedTargets();
            Rollback();
            Console.WriteLine($"{_checks} update installer checks passed; temp fixtures only, no network, processes, or elevation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void PreparationAndArguments()
    {
        using var f = new Fixture();
        var p = f.Prepare();
        Check(File.ReadAllBytes(p.SourceExe).SequenceEqual(Fixture.NewBytes), "verified candidate is copied completely");
        Check(p.SourceExe == Path.Combine(p.Folder, "Floppy.Update.exe") && Path.GetDirectoryName(p.Folder) == f.Updates, "source stays in unique update folder");
        Check(p.PlanSha == Hash(File.ReadAllBytes(p.PlanPath)), "plan digest covers exact written bytes");
        var plan = JsonSerializer.Deserialize<UpdateInstaller.UpdatePlan>(File.ReadAllBytes(p.PlanPath))!;
        Check(plan.TargetExe == f.Target && plan.ParentPid == 42 && plan.ParentStartedUtcTicks == Fixture.Started, "plan pins original path and process creation time");
        Check(plan.OldSha256 == Hash(Fixture.OldBytes) && plan.NewSha256 == Hash(Fixture.NewBytes), "plan pins old and new content");
        foreach (bool elevated in new[] { false, true })
        {
            var start = UpdateInstaller.HelperStartInfo(p, elevated);
            Check(start.FileName == p.SourceExe && start.WorkingDirectory == p.Folder && start.Arguments == "", "helper path is explicit, independent of shell strings");
            Check(start.ArgumentList.SequenceEqual(new[] { "--apply-update", p.PlanPath, p.PlanSha }), "Unicode and punctuation paths stay separate literal arguments");
            Check(start.UseShellExecute == elevated && start.Verb == (elevated ? "runas" : "") && start.WindowStyle == ProcessWindowStyle.Hidden, "elevation uses only the fixed runas verb when needed");
        }
        Check(File.ReadAllBytes(f.Target).SequenceEqual(Fixture.OldBytes), "preparation leaves original EXE untouched");
        Reject(() => f.Prepare(new string('0', 64)), "wrong source hash rejects preparation");
        Reject(() => f.Prepare(version: "1.4.4 & run.exe"), "version cannot contain a command fragment");
        Reject(() => UpdateInstaller.Prepare(f.Target, Hash(Fixture.OldBytes), "1.4.4", f.Target, 42, Fixture.Started, f.Updates), "identical source and target rejected");
        foreach (string name in new[] { "dotnet.exe", "devenv.exe", "testhost.exe", "vstest.console.exe", "settings.json" })
            Reject(() => UpdateInstaller.Prepare(f.Download, Hash(Fixture.NewBytes), "1.4.4", Path.Combine(f.Root, name), 42, Fixture.Started, f.Updates), "unsafe target rejected: " + name);
        Reject(() => UpdateInstaller.Prepare(f.Download, Hash(Fixture.NewBytes), "1.4.4", Path.Combine(f.Updates, "Floppy.exe"), 42, Fixture.Started, f.Updates), "target cannot be inside update cache");
    }

    private static void SuccessfulReplacement()
    {
        using var f = new Fixture();
        var p = f.Prepare(); f.Approve(p);
        string oldWorkingDirectory = Environment.CurrentDirectory;
        Directory.CreateDirectory(Path.Combine(f.Root, "unrelated cwd"));
        try
        {
            Environment.CurrentDirectory = Path.Combine(f.Root, "unrelated cwd");
            f.Parent.OnWait = () =>
            {
                Reject(() => File.WriteAllText(p.SourceExe, "tampered"), "candidate stays write-locked through waiting and copying");
                Reject(() => File.Move(p.SourceExe, p.SourceExe + ".moved"), "candidate cannot be renamed during application");
                Reject(() => File.WriteAllText(p.PlanPath, "tampered"), "plan stays write-locked through application");
            };
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 0, "atomic replacement succeeds");
        }
        finally { Environment.CurrentDirectory = oldWorkingDirectory; }
        Check(File.ReadAllBytes(f.Target).SequenceEqual(Fixture.NewBytes), "renamed original EXE contains verified new bytes");
        Check(f.Backups().Single() is string backup && File.ReadAllBytes(backup).SequenceEqual(Fixture.OldBytes), "old EXE is preserved byte for byte in backup");
        Check(f.Starts.SequenceEqual(new[] { f.Target }) && f.Parent.Disposed && f.Parent.WaitMilliseconds == 45000, "exact original path restarts after bounded parent wait");
        Check(File.ReadAllText(f.Sentinel) == "user preferences" && File.ReadAllText(f.Unrelated) == "neighbor", "settings and unrelated installation files stay untouched");
        var result = f.Result(p);
        Check(result.Success && result.Version == "1.4.4" && result.TimestampUtc.Kind == DateTimeKind.Utc, "success report contains version and UTC timestamp");
        Check(File.ReadAllBytes(Path.Combine(p.Folder, "result.json")).SequenceEqual(File.ReadAllBytes(Path.Combine(f.Updates, "last-result.json"))), "local and last-result reports agree");
    }

    private static void InvalidPlansAndIdentity()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            File.AppendAllText(p.PlanPath, " ");
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1, "changed exact plan bytes rejected");
            Check(f.Starts.Count == 0 && f.Parent.WaitMilliseconds == 0 && f.OldIntact, "tampered plan never waits, replaces, or starts anything");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            File.WriteAllBytes(p.SourceExe, Encoding.UTF8.GetBytes("tampered payload"));
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact && f.Starts.Count == 0, "candidate altered after preparation is rejected");
        }
        foreach (bool changePath in new[] { false, true })
        {
            using var f = new Fixture();
            var p = f.Prepare(); f.Approve(p);
            if (changePath) f.Parent.ExecutablePath = Path.Combine(f.Root, "other.exe");
            else f.Parent.StartedUtcTicks++;
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact && f.Starts.Count == 0, "parent identity mismatch rejected: " + (changePath ? "path" : "PID reuse/start time"));
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p, currentExe: f.Target)) == 1 && f.OldIntact && f.Starts.Count == 0, "helper must execute from exact candidate path");
            Check(UpdateInstaller.RunCore(Path.Combine(f.Root, "plan.json"), p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact, "plan outside unique transaction folder rejected");
        }
    }

    private static void ParentTimeoutAndMissingApproval()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p); f.Parent.ExitOnWait = false;
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact, "parent timeout leaves old EXE intact");
            Check(f.Parent.WaitMilliseconds == 45000 && f.Starts.Count == 0 && !f.Backups().Any(), "timeout does not replace, restart, or kill parent");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare();
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact, "late unapproved helper cannot apply update");
            Check(f.Parent.WaitMilliseconds == 0 && f.Starts.Count == 0, "missing approval fails before parent shutdown wait");
        }
    }

    private static void LockedAndChangedTargets()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            using var locked = new FileStream(f.Target, FileMode.Open, FileAccess.Read, FileShare.Read);
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact, "locked destination preserves original bytes");
            Check(f.Starts.SequenceEqual(new[] { f.Target }) && !f.Backups().Any(), "failure before replacement restarts unchanged old EXE after parent exit");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            f.Parent.OnWait = () => File.WriteAllText(f.Target, "external change");
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && File.ReadAllText(f.Target) == "external change", "changed target hash blocks replacement after parent wait");
            Check(f.Starts.Count == 0 && !f.Backups().Any(), "unknown changed EXE is neither overwritten nor restarted");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            var plan = JsonSerializer.Deserialize<UpdateInstaller.UpdatePlan>(File.ReadAllBytes(p.PlanPath))!;
            string backup = Path.Combine(f.Root, ".Floppy-backup-" + plan.Nonce + ".exe");
            File.WriteAllText(backup, "existing backup");
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact && File.ReadAllText(backup) == "existing backup", "pre-existing backup is never overwritten");
        }
    }

    private static void Rollback()
    {
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            f.OnStart = _ => { if (f.Starts.Count == 1) throw new IOException("new EXE start failure"); };
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && f.OldIntact, "failed new process start rolls back original EXE atomically");
            Check(f.Starts.SequenceEqual(new[] { f.Target, f.Target }), "rollback attempts old EXE restart");
            Check(f.Backups().Count() == 1 && File.ReadAllBytes(f.Backups().Single()).SequenceEqual(Fixture.OldBytes), "rollback retains original backup");
            Check(Directory.GetFiles(f.Root, ".Floppy-rejected-*.exe").Single() is string rejected && File.ReadAllBytes(rejected).SequenceEqual(Fixture.NewBytes), "failed candidate remains available after rollback");
            Check(!f.Result(p).Success && f.Result(p).Message.Contains("wiederhergestellt"), "rollback is reported as failed update with recovery outcome");
        }
        using (var f = new Fixture())
        {
            var p = f.Prepare(); f.Approve(p);
            f.OnStart = _ => { File.WriteAllText(f.Target, "new external change"); throw new IOException("start failed after replacement changed"); };
            Check(UpdateInstaller.RunCore(p.PlanPath, p.PlanSha, f.Runtime(p)) == 1 && File.ReadAllText(f.Target) == "new external change", "rollback never overwrites an independently changed target");
            Check(File.ReadAllBytes(f.Backups().Single()).SequenceEqual(Fixture.OldBytes) && f.Starts.Count == 1, "failed rollback retains original backup and does not launch unknown bytes");
        }
    }

    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private static int ProtocolFixture(string[] args, string root)
    {
        try
        {
            AssertFixtureRoot(root, "Floppy protocol QA ");
            if (!File.Exists(Path.Combine(root, "fixture.marker"))) throw new Exception("Not an owned protocol fixture");
            if (args.Length == 3 && args[0] == "--apply-update")
            {
                File.WriteAllText(Path.Combine(root, "helper.pid"), Environment.ProcessId.ToString());
                return UpdateInstaller.RunCore(args[1], args[2], new UpdateInstaller.ApplyRuntime { Root = Path.Combine(root, "Updates") });
            }
            if (args.Length == 1 && args[0] == "--fixture-parent")
            {
                string download = Path.Combine(root, "download.exe");
                UpdateInstaller.StartCore(download, Hash(File.ReadAllBytes(download)), "9.9.9", Path.Combine(root, "Updates"));
                File.WriteAllText(Path.Combine(root, "parent-ready.txt"), "helper ready; normal parent exit follows");
                return 0;
            }
            if (args.Length == 0)
            {
                File.WriteAllText(Path.Combine(root, "restarted.json"), JsonSerializer.Serialize(new
                { Pid = Environment.ProcessId, Exe = Environment.ProcessPath, Sha256 = Hash(File.ReadAllBytes(Environment.ProcessPath!)) }));
                return 0;
            }
            throw new Exception("Unsupported fixture command");
        }
        catch (Exception error)
        { File.WriteAllText(Path.Combine(root, "fixture-error-" + Environment.ProcessId + ".txt"), error.ToString()); return 1; }
    }

    private static void ProtocolSmoke(string standaloneFixture)
    {
        string root = Path.Combine(Path.GetTempPath(), "Floppy protocol QA ä & ' " + Guid.NewGuid().ToString("N"));
        Process? parent = null;
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "foreign cwd"));
            File.WriteAllText(Path.Combine(root, "fixture.marker"), "owned process fixture");
            string target = Path.Combine(root, "Mein Floppy ä & '.exe");
            string download = Path.Combine(root, "download.exe");
            File.Copy(standaloneFixture, target);
            File.Copy(standaloneFixture, download);
            // Trailing bytes make the two valid standalone PE bundles distinct.
            using (var old = new FileStream(target, FileMode.Append, FileAccess.Write)) old.Write("old fixture"u8);
            using (var candidate = new FileStream(download, FileMode.Append, FileAccess.Write)) candidate.Write("new fixture"u8);
            string oldHash = Hash(File.ReadAllBytes(target));
            string newHash = Hash(File.ReadAllBytes(download));
            var info = new ProcessStartInfo(target)
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.Combine(root, "foreign cwd")
            };
            info.ArgumentList.Add("--fixture-parent");
            info.Environment["FLOPPY_UPDATE_SMOKE_ROOT"] = root;
            info.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(root, "bundle-cache");
            parent = Process.Start(info) ?? throw new Exception("Own protocol parent did not start");
            Check(parent.WaitForExit(60000) && parent.ExitCode == 0, "real parent completes Start handshake and exits normally");
            var timer = Stopwatch.StartNew();
            string resultPath = Path.Combine(root, "Updates", "last-result.json");
            while ((!File.Exists(resultPath) || !File.Exists(Path.Combine(root, "restarted.json"))) && timer.ElapsedMilliseconds < 60000)
                Thread.Sleep(50);
            if (!File.Exists(resultPath)) throw new Exception("No real helper result; fixture evidence: " + root);
            var result = JsonSerializer.Deserialize<UpdateInstaller.UpdateResult>(File.ReadAllBytes(resultPath))!;
            Check(result.Success, "real helper reports successful replacement: " + result.Message);
            Check(Hash(File.ReadAllBytes(target)) == newHash, "real original EXE path now has candidate hash");
            Check(Hash(File.ReadAllBytes(Directory.GetFiles(root, ".Floppy-backup-*.exe").Single())) == oldHash, "real replacement keeps old executable backup");
            using var restarted = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "restarted.json")));
            Check(restarted.RootElement.GetProperty("Exe").GetString() == target && restarted.RootElement.GetProperty("Sha256").GetString() == newHash,
                "new executable restarts at the original Unicode path with verified bytes");
            Check(File.Exists(Path.Combine(root, "parent-ready.txt")) && result.Version == "9.9.9", "ready acknowledgement precedes normal parent shutdown");
            // Child helper/new target usually exit immediately; wait before removing
            // their own files so the smoke also checks process cleanup.
            foreach (int pid in new[] { int.Parse(File.ReadAllText(Path.Combine(root, "helper.pid"))), restarted.RootElement.GetProperty("Pid").GetInt32() })
            {
                try { using var process = Process.GetProcessById(pid); Check(process.WaitForExit(5000), "owned helper/restarted process exits"); }
                catch (ArgumentException) { Check(true, "owned helper/restarted process already exited"); }
            }
        }
        finally
        {
            if (parent != null) { if (!parent.HasExited) { parent.Kill(); parent.WaitForExit(5000); } parent.Dispose(); }
            foreach (string pidFile in new[] { "helper.pid", "restarted.json" })
            {
                string path = Path.Combine(root, pidFile);
                if (!File.Exists(path)) continue;
                int pid = pidFile.EndsWith(".json", StringComparison.Ordinal)
                    ? JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.GetProperty("Pid").GetInt32()
                    : int.Parse(File.ReadAllText(path));
                try
                {
                    using var process = Process.GetProcessById(pid);
                    string? executable = process.MainModule?.FileName;
                    if (executable != null && executable.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !process.HasExited)
                    { process.Kill(); process.WaitForExit(5000); }
                }
                catch (ArgumentException) { }
            }
            AssertFixtureRoot(root, "Floppy protocol QA ");
            Directory.Delete(root, true);
        }
    }

    private static void AssertFixtureRoot(string path, string namePrefix)
    {
        string absolute = Path.GetFullPath(path);
        string allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(absolute).StartsWith(namePrefix, StringComparison.Ordinal))
            throw new Exception("Refusing non-fixture directory");
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly byte[] OldBytes = Encoding.UTF8.GetBytes("original executable fixture, not runnable");
        internal static readonly byte[] NewBytes = Encoding.UTF8.GetBytes("new executable fixture, not runnable");
        internal const long Started = 638930000000000000;
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Floppy update QA ä & ' " + Guid.NewGuid().ToString("N"));
        public string Updates => Path.Combine(Root, "Updates");
        public string Target => Path.Combine(Root, "Mein Floppy ä & '.exe");
        public string Download => Path.Combine(Root, "download.exe");
        public string Sentinel => Path.Combine(Root, "AppData", "app.json");
        public string Unrelated => Path.Combine(Root, "games.json");
        public FakeParent Parent { get; }
        public List<string> Starts { get; } = new();
        public Action<string>? OnStart { get; set; }
        public bool OldIntact => File.ReadAllBytes(Target).SequenceEqual(OldBytes);
        public Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Sentinel)!);
            File.WriteAllBytes(Target, OldBytes); File.WriteAllBytes(Download, NewBytes);
            File.WriteAllText(Sentinel, "user preferences"); File.WriteAllText(Unrelated, "neighbor");
            Parent = new FakeParent { ExecutablePath = Target, StartedUtcTicks = Started };
        }
        public UpdateInstaller.PreparedUpdate Prepare(string? hash = null, string version = "1.4.4") =>
            UpdateInstaller.Prepare(Download, hash ?? Hash(NewBytes), version, Target, 42, Started, Updates);
        public void Approve(UpdateInstaller.PreparedUpdate p) => File.WriteAllText(Path.Combine(p.Folder, "continue.txt"), p.Proof);
        public IEnumerable<string> Backups() => Directory.GetFiles(Root, ".Floppy-backup-*.exe");
        public UpdateInstaller.UpdateResult Result(UpdateInstaller.PreparedUpdate p) => JsonSerializer.Deserialize<UpdateInstaller.UpdateResult>(File.ReadAllBytes(Path.Combine(p.Folder, "result.json")))!;
        public UpdateInstaller.ApplyRuntime Runtime(UpdateInstaller.PreparedUpdate p, string? currentExe = null) => new()
        {
            Root = Updates, CurrentExe = currentExe ?? p.SourceExe, ApprovalTimeoutMs = 0,
            OpenParent = pid => { if (pid != 42) throw new Exception("Wrong fixture PID"); return Parent; },
            StartTarget = path => { Starts.Add(path); OnStart?.Invoke(path); }
        };
        public void Dispose()
        {
            AssertFixtureRoot(Root, "Floppy update QA ");
            Directory.Delete(Root, true);
        }
    }

    private sealed class FakeParent : UpdateInstaller.IUpdateParent
    {
        public string ExecutablePath { get; set; } = "";
        public long StartedUtcTicks { get; set; }
        public bool HasExited { get; private set; }
        public bool ExitOnWait { get; set; } = true;
        public int WaitMilliseconds { get; private set; }
        public bool Disposed { get; private set; }
        public Action? OnWait { get; set; }
        public bool WaitForExit(int milliseconds)
        { WaitMilliseconds = milliseconds; OnWait?.Invoke(); HasExited = ExitOnWait; return ExitOnWait; }
        public void Dispose() => Disposed = true;
    }
}
