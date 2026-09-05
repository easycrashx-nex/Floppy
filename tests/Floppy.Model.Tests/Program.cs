using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Floppy.Core;
using Floppy.Core.Api;

internal static class Program
{
    private static int _assertions;
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "Floppy.Model.Tests-" + Guid.NewGuid().ToString("N"));

    private static async Task<int> Main()
    {
        Profile.StorageRoot = Scratch;
        Directory.CreateDirectory(Scratch);
        try
        {
            JsonTests();
            ProfileTests();
            DependentProfileTests();
            ValidationAndResetTests();
            RegistryTests();
            await DispatcherTests();
            await IpcTests();
            OverlayLeaseTests();
            ExternalCursorTests();
            await OverlayIpcTests();
            Console.WriteLine("PASS: " + _assertions + " assertions; only fake modules, temporary profiles and an ephemeral loopback port.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            IpcServer.Stop();
            Dispatcher.Pump();
            Profile.StorageRoot = null;
            string resolved = Path.GetFullPath(Scratch);
            string expected = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Floppy.Model.Tests-");
            if (resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new Exception("FAIL: " + message);
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException)
        { _assertions++; return; }
        throw new Exception("FAIL: accepted " + message);
    }

    private static CheatOption Option(string id, OptionKind kind) => new CheatOption { Id = id, Label = id, Kind = kind };

    private static Fixture Setup()
    {
        var module = new Fixture();
        module.Options = new List<CheatOption>
        {
            Option("toggle", OptionKind.Toggle),
            new CheatOption { Id="normalTrue", Label="normalTrue", Kind=OptionKind.Toggle, BoolValue=true, Ruhebool=true },
            new CheatOption { Id="spread", Label="spread", Kind=OptionKind.Slider, Min=0, Max=2, NumberValue=1, Ruhewert=1 },
            new CheatOption { Id="damage", Label="damage", Kind=OptionKind.Slider, Min=1, Max=25, NumberValue=1, Ruhewert=1 },
            new CheatOption { Id="number", Label="number", Kind=OptionKind.Number, Min=1, Max=100, NumberValue=5 },
            new CheatOption { Id="choice", Label="choice", Kind=OptionKind.Choice, Choices=new[]{"normal", "rare", "valuable"}, Ruheauswahl=0 },
            new CheatOption { Id="text", Label="text", Kind=OptionKind.Text, Scope=CheatScope.Selectable },
            Option("button", OptionKind.Button), Option("info", OptionKind.Info)
        };
        Registry.SetModule(module);
        return module;
    }

    private static void JsonTests()
    {
        string special = "quoted \"line\"\nGrüße\t\\\b\f";
        string entry = new Json.Writer().Set("text", special).Set("number", -1.25e20).Set("bool", true).Raw("none", "null").ToString();
        var parsed = Json.Parse(new Json.Writer().Raw("values", Json.Array(new[] { entry, "[false,0,{}]" })).ToString());
        var values = (List<object>)parsed["values"];
        var first = (Dictionary<string, object>)values[0];
        Check((string)first["text"] == special && (double)first["number"] == -1.25e20 && (bool)first["bool"], "nested JSON roundtrip");
        Check(first["none"] == null && ((List<object>)values[1]).Count == 3, "arrays/null/objects preserved");
        Check((string)Json.Parse("{\"text\":\"\\u00fc\\b\\f\\/\"}")["text"] == "ü\b\f/", "JSON escapes");
        foreach (string invalid in new[] { "", "{", "{\"x\":1", "{\"x\":[1,]}", "{\"x\":1,}", "{} garbage", "[]", "{\"x\":01}",
            "{\"x\":+1}", "{\"x\":1.}", "{\"x\":1e}", "{\"x\":NaN}", "{\"x\":1e9999}", "{\"x\":true,\"X\":false}",
            "{\"x\":\"\\q\"}", "{\"x\":\"line\nline\"}", "{\"x\":tru}", "{\"x\":\"unterminated}" })
            Reject(() => Json.Parse(invalid), invalid);
        Reject(() => Json.Parse("{\"x\":" + new string('[', 70) + "0" + new string(']', 70) + "}"), "excessive nesting");
        Reject(() => Json.Number(double.PositiveInfinity), "infinite JSON number");
        Reject(() => Json.Number(double.NaN), "NaN JSON number");
    }

    private static void ProfileTests()
    {
        Setup();
        int callbacks = 0, buttons = 0;
        Registry.Find("toggle").BoolValue = true;
        Registry.Find("number").NumberValue = 72;
        Registry.Find("choice").ChoiceIndex = 2;
        Registry.Find("text").TextValue = "saved \"text\"\nGrüße";
        Registry.Find("text").ShareWithOthers = true;
        foreach (var option in Registry.AllOptions.Where(o => o.Kind != OptionKind.Info && o.Kind != OptionKind.Button))
            option.OnChanged = _ => callbacks++;
        Registry.Find("button").OnInvoke = _ => buttons++;
        Check(Profile.TrySave("roundtrip", out _), "profile save");
        string path = Path.Combine(Profile.Ordner, "roundtrip.json");
        string saved = File.ReadAllText(path);
        Check(!saved.Contains("profil.") && !saved.Contains("\"id\":\"button\"") && !saved.Contains("\"id\":\"info\""), "profile excludes controls/actions/info");
        Registry.Find("toggle").BoolValue = false;
        Registry.Find("number").NumberValue = 5;
        Registry.Find("choice").ChoiceIndex = 0;
        Registry.Find("text").TextValue = "changed";
        Registry.Find("text").ShareWithOthers = false;
        Check(Profile.TryLoad("roundtrip", out _), "saved profile loads");
        Check(Registry.Find("toggle").BoolValue && Registry.Find("number").NumberValue == 72 && Registry.Find("choice").ChoiceIndex == 2, "profile restores values");
        Check(Registry.Find("text").TextValue == "saved \"text\"\nGrüße" && Registry.Find("text").ShareWithOthers, "profile restores text/share");
        Check(callbacks == 7 && buttons == 0, "profile callbacks and no button execution");
        Registry.Find("number").NumberValue = 60;
        Check(Profile.TrySave("roundtrip", out _) && File.ReadAllText(path) != saved, "atomic overwrite updates existing profile");
        string beforeFailure = File.ReadAllText(path);
        Registry.Find("number").NumberValue = float.NaN;
        Check(!Profile.TrySave("roundtrip", out _) && File.ReadAllText(path) == beforeFailure, "failed save preserves previous profile");
        Check(Directory.GetFiles(Profile.Ordner, "*.tmp").Length == 0, "save leaves no temporary files");
        Registry.Find("number").NumberValue = 5;
        Registry.Find("toggle").BoolValue = false;
        File.WriteAllText(Path.Combine(Profile.Ordner, "invalid.json"), "{\"werte\":[{\"id\":\"toggle\",\"bool\":true},{\"id\":\"number\",\"number\":999}]}" );
        Check(!Profile.TryLoad("invalid", out _) && !Registry.Find("toggle").BoolValue, "invalid profile never partially applies before validation");
        File.WriteAllText(Path.Combine(Profile.Ordner, "legacy.json"), "{\"spiel\":\"fixture\",\"werte\":[{\"id\":\"toggle\",\"bool\":true,\"number\":0,\"text\":\"\",\"choice\":0,\"share\":false}]}" );
        Check(Profile.TryLoad("legacy", out _) && Registry.Find("toggle").BoolValue, "legacy all-fields profile supported");
        Registry.Find("toggle").BoolValue = false;
        Registry.Find("toggle").OnChanged = _ => throw new InvalidOperationException("fixture failure");
        Check(!Profile.TryLoad("legacy", out string failure) && failure.Contains("fixture failure") && !Registry.Find("toggle").BoolValue, "profile callback failure is honest and restores model value");
        Registry.Find("toggle").OnChanged = null;
        Registry.Find("toggle").IsAvailable = () => false;
        Check(!Profile.TryLoad("legacy", out failure) && failure.Contains("nicht verfügbar") && !Registry.Find("toggle").BoolValue, "profile respects per-option availability");
        File.WriteAllText(Path.Combine(Profile.Ordner, "wronggame.json"), "{\"spiel\":\"another\",\"werte\":[]}");
        Check(!Profile.TryLoad("wronggame", out _), "wrong-game profile rejected");
    }

    private static void ValidationAndResetTests()
    {
        Setup();
        var number = Registry.Find("number");
        foreach (string invalid in new[] { "{\"number\":0}", "{\"number\":999}", "{\"number\":1e100}", "{\"number\":true}", "{\"number\":\"8\"}", "{\"bool\":true}", "{}" })
            Reject(() => OptionValues.Read(number, Json.Parse(invalid)).Apply(number), invalid);
        Check(number.NumberValue == 5, "invalid numbers do not mutate state");
        foreach (string invalid in new[] { "{\"choice\":-1}", "{\"choice\":3}", "{\"choice\":0.5}", "{\"choice\":true}" })
            Reject(() => OptionValues.Read(Registry.Find("choice"), Json.Parse(invalid)), invalid);
        Reject(() => OptionValues.Read(Registry.Find("toggle"), Json.Parse("{\"bool\":1}")), "numeric boolean");
        Reject(() => OptionValues.Read(Registry.Find("toggle"), Json.Parse("{\"share\":true}")), "share on non-selectable option");
        Reject(() => OptionValues.Read(Registry.Find("button"), Json.Parse("{\"bool\":true}")), "button as editable value");
        Check(Profile.TryReset(out _) && Registry.Find("spread").NumberValue == 1 && Registry.Find("normalTrue").BoolValue, "reset cannot enable no-spread or disable a normal-on toggle");
        Registry.Find("spread").NumberValue = 0;
        Registry.Find("damage").NumberValue = 3;
        Registry.Find("choice").ChoiceIndex = 2;
        Registry.Find("normalTrue").BoolValue = false;
        Registry.Find("toggle").BoolValue = true;
        Registry.Find("text").ShareWithOthers = true;
        Check(Profile.TryReset(out _), "reset succeeds");
        Check(Registry.Find("spread").NumberValue == 1 && Registry.Find("damage").NumberValue == 1 && Registry.Find("choice").ChoiceIndex == 0, "numeric and choice neutral defaults restored");
        Check(Registry.Find("normalTrue").BoolValue && !Registry.Find("toggle").BoolValue && !Registry.Find("text").ShareWithOthers, "toggle and share defaults restored");
        Check(number.NumberValue == 5 && !number.Active && !Registry.AllOptions.Any(o => o.Active), "direct game number unchanged and no known active modification remains");
        Registry.Find("toggle").BoolValue = true;
        Registry.Find("toggle").OnChanged = _ => throw new InvalidOperationException("cannot reset");
        Check(!Profile.TryReset(out string message) && message.Contains("cannot reset") && Registry.Find("toggle").BoolValue, "failed reset cannot claim switched off");
    }

    private static void DependentProfileTests()
    {
        var module = Setup();
        var category = new CheatOption { Id="spawn.category", Label="category", Kind=OptionKind.Choice, Choices=new[]{"A", "B"}, ChoiceIndex=1 };
        var item = new CheatOption { Id="spawn.pick", Label="item", Kind=OptionKind.Choice, Choices=new[]{"B1", "B2"}, ChoiceIndex=1 };
        category.OnChanged = _ => item.ChoiceIndex = 0;
        item.IsAvailable = () => { item.Choices = category.ChoiceIndex == 0 ? new[]{"A1", "A2"} : new[]{"B1", "B2"}; return true; };
        module.Options.Add(category);
        module.Options.Add(item);
        Registry.SetModule(module);
        Check(Profile.TrySave("dependent", out _), "dependent choice profile saves");
        category.ChoiceIndex = 0;
        category.NotifyChanged();
        _ = item.Available;
        Check(Profile.TryLoad("dependent", out string message) && category.ChoiceIndex == 1 && item.ChoiceIndex == 1 && item.Choices[1] == "B2",
            "dependent choices restore against saved category: " + message);
    }

    private static void RegistryTests()
    {
        var module = Setup();
        Registry.Find("text").TextValue = "keep";
        Registry.Find("text").ShareWithOthers = true;
        Registry.Find("number").NumberValue = 42;
        Registry.Find("toggle").BoolValue = true;
        Registry.Find("choice").ChoiceIndex = 2;
        int version = Registry.SchemaVersion;
        int callback = 0;
        module.Options = module.Options.Select(old => new CheatOption
        {
            Id=old.Id, Label=old.Label, Kind=old.Kind, Scope=old.Scope, Min=old.Min, Max=old.Max,
            Choices=old.Id == "choice" ? new[]{"valuable","normal","rare"} : old.Choices,
            OnChanged = _ => callback++
        }).ToList();
        module.Options.Add(Option("new", OptionKind.Number));
        Registry.RefreshCategories();
        Check(Registry.SchemaVersion == version + 1 && Registry.Find("new") != null, "schema version advances on complete refresh");
        Check(Registry.Find("text").TextValue == "keep" && Registry.Find("text").ShareWithOthers && Registry.Find("number").NumberValue == 42 && Registry.Find("toggle").BoolValue, "refresh preserves matching state");
        Check(Registry.Find("choice").ChoiceIndex == 0, "refresh preserves choice identity after reorder");
        Registry.Find("number").NotifyChanged();
        Check(callback == 1, "refresh keeps new callback closures");
        version = Registry.SchemaVersion;
        module.Options.Add(Option("new", OptionKind.Number));
        Reject(Registry.RefreshCategories, "duplicate IDs");
        Check(Registry.SchemaVersion == version && Registry.Find("number").NumberValue == 42, "failed refresh leaves published registry intact");
    }

    private static async Task DispatcherTests()
    {
        int effects = 0;
        try { await Task.Run(() => Dispatcher.Run(() => ++effects, 20)); throw new Exception("FAIL: expected timeout"); }
        catch (TimeoutException) { _assertions++; }
        Dispatcher.Pump();
        Check(effects == 0, "expired dispatcher mutation never executes later");
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Dispatcher.Enqueue(() => { started.Set(); if (!release.Wait(3000)) throw new Exception("test release timeout"); });
        Dispatcher.Enqueue(() => effects++);
        var first = Task.Run(Dispatcher.Pump);
        Check(started.Wait(2000), "first pump started");
        await Task.Run(Dispatcher.Pump);
        Check(effects == 0, "second pump cannot execute concurrently");
        release.Set();
        await first;
        Check(effects == 1, "serialized pump drains each action once");
        var result = Task.Run(() => Dispatcher.Run(() => Dispatcher.Run(() => 42)));
        await Until(() => result.IsCompleted, Dispatcher.Pump);
        Check(await result == 42, "nested dispatcher call completes without deadlock");
    }

    private static async Task IpcTests()
    {
        Setup();
        IpcServer.Start(0);
        Check(IpcServer.ListeningPort != IpcServer.Port, "IPC tests use an ephemeral port");
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", IpcServer.ListeningPort);
        using var reader = new StreamReader(client.GetStream(), Encoding.UTF8);
        using var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        var schema = await Request(reader, writer, "{\"cmd\":\"schema\"}");
        string session = (string)schema["sessionId"];
        Check((string)schema["gameId"] == "fixture" && session.Length > 0 && (double)schema["schemaVersion"] == Registry.SchemaVersion, "schema identifies module/session/version");
        string schemaJson = System.Text.Json.JsonSerializer.Serialize(schema);
        Check(schemaJson.Contains("resetNumber") && schemaJson.Contains("resetBool") && schemaJson.Contains("resetChoice"), "schema includes reset metadata");
        var response = await Request(reader, writer, "{\"cmd\":\"set\",\"id\":\"number\",\"number\":1e100}");
        Check(!(bool)response["ok"] && Registry.Find("number").NumberValue == 5, "IPC rejects overflow without losing connection");
        response = await Request(reader, writer, "{\"cmd\":\"invoke\",\"id\":\"toggle\"}");
        Check(!(bool)response["ok"], "IPC rejects invoking a toggle");
        Registry.Find("profil.liste").Choices = new[] { "does-not-exist" };
        Registry.Find("profil.liste").ChoiceIndex = 0;
        response = await Request(reader, writer, "{\"cmd\":\"invoke\",\"id\":\"profil.laden\"}");
        Check(!(bool)response["ok"] && response.ContainsKey("error"), "profile failure is IPC error");
        Registry.Find("choice").Choices = new[] { "other", "same", "count" };
        Registry.Find("spread").NumberValue = 0;
        response = await Request(reader, writer, "{\"cmd\":\"state\"}");
        var state = (Dictionary<string, object>)response["values"];
        Check((bool)((Dictionary<string, object>)state["spread"])["active"], "state reports actual active modifications");
        Check((string)((List<object>)((Dictionary<string, object>)state["choice"])["choices"])[0] == "other", "state exposes same-length choice changes");
        await writer.WriteLineAsync("{\"cmd\":\"set\",\"id\":\"toggle\",\"bool\":true}");
        await Until(() => PendingCount() > 0);
        var closed = reader.ReadLineAsync();
        IpcServer.Stop();
        try { Check(await closed.WaitAsync(TimeSpan.FromSeconds(2)) == null, "stop closes accepted idle sockets"); }
        catch (IOException) { _assertions++; }
        Check(IpcServer.ConnectedClients == 0, "stop clears client lifecycle");
        IpcServer.Start(0);
        Dispatcher.Pump();
        Check(!Registry.Find("toggle").BoolValue, "old-session pending mutation cannot execute after restart");
        using var next = new TcpClient();
        await next.ConnectAsync("127.0.0.1", IpcServer.ListeningPort);
        using var nextReader = new StreamReader(next.GetStream(), Encoding.UTF8);
        using var nextWriter = new StreamWriter(next.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        schema = await Request(nextReader, nextWriter, "{\"cmd\":\"schema\"}");
        Check((string)schema["sessionId"] != session, "restart changes session identity");
        IpcServer.Stop();
    }

    private static int PendingCount()
    {
        var queue = (Queue<Action>)typeof(Dispatcher).GetField("_queue", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        lock (queue) return queue.Count;
    }

    private static void OverlayLeaseTests()
    {
        long now = 0;
        var transitions = new List<bool>();
        var lease = new OverlayLease(transitions.Add, () => now);
        var first = new object();
        var second = new object();
        lease.Apply(first, true);
        Check(lease.IsOpen && transitions.SequenceEqual(new[] { true }), "overlay acquires input lock once");
        now = 2900;
        lease.Apply(first, true);
        Check(transitions.Count == 1, "overlay heartbeat does not recapture cursor/input state");
        Reject(() => lease.Apply(second, true), "overlay takeover by another connection");
        Reject(() => lease.Apply(second, false), "overlay close by another connection");
        now = 5899;
        lease.Tick();
        Check(lease.IsOpen, "renewed overlay lease stays active before deadline");
        now = 5900;
        lease.Tick();
        Check(!lease.IsOpen && transitions.SequenceEqual(new[] { true, false }), "overlay lease releases exactly at deadline");
        lease.Apply(second, true);
        lease.Release(first);
        Check(lease.IsOpen, "stale disconnect cannot release new owner");
        lease.Release(second);
        lease.Apply(first, false);
        Check(!lease.IsOpen && transitions.Count == 4, "owner disconnect releases and idle close is idempotent");
        lease.Apply(first, true);
        lease.Close();
        lease.Close();
        Check(!lease.IsOpen && transitions.Count == 6, "stop releases overlay exactly once");
        Reject(() => lease.Apply(null, true), "missing overlay owner");
        bool restored = false;
        var failed = new OverlayLease(open => { if (open) throw new InvalidOperationException("fixture lock failure"); restored = true; }, () => now);
        Reject(() => failed.Apply(first, true), "failed input lock");
        Check(!failed.IsOpen && restored, "failed input lock attempts restoration and drops lease");
    }

    private static async Task OverlayIpcTests()
    {
        var module = Setup();
        var cursorTransitions = new List<bool>();
        IpcServer.Start(0, externalOverlay: true, overlayChanged: cursorTransitions.Add);
        using var owner = new TcpClient();
        await owner.ConnectAsync("127.0.0.1", IpcServer.ListeningPort);
        using var reader = new StreamReader(owner.GetStream(), Encoding.UTF8);
        using var writer = new StreamWriter(owner.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        var schema = await Request(reader, writer, "{\"cmd\":\"schema\"}");
        string session = (string)schema["sessionId"];
        Check((double)schema["processId"] == Environment.ProcessId && (bool)schema["externalOverlay"] && !(bool)schema["overlayOpen"],
            "overlay schema reports actual server process and capability");
        string Command(bool open, string identity) => new Json.Writer().Set("cmd", "overlay").Set("open", open).Set("sessionId", identity).ToString();
        var response = await Request(reader, writer, Command(true, "old-session"));
        Check(!(bool)response["ok"] && !module.MenuOpen, "overlay rejects stale session before input lock");
        response = await Request(reader, writer, "{\"cmd\":\"overlay\",\"open\":true}");
        Check(!(bool)response["ok"] && !module.MenuOpen, "overlay requires explicit session identity");
        response = await Request(reader, writer, new Json.Writer().Set("cmd", "overlay").Set("open", "true").Set("sessionId", session).ToString());
        Check(!(bool)response["ok"] && !module.MenuOpen, "overlay rejects non-boolean open");
        response = await Request(reader, writer, Command(true, session));
        Check((bool)response["ok"] && (bool)response["overlayOpen"] && (double)response["leaseMs"] == 3000 && module.MenuOpen,
            "overlay command acquires module input lock with lease acknowledgement");
        await Request(reader, writer, Command(true, session));
        Check(module.MenuChanges == 1 && cursorTransitions.SequenceEqual(new[] { true }), "IPC heartbeat preserves initial input and cursor snapshot");
        using var other = new TcpClient();
        await other.ConnectAsync("127.0.0.1", IpcServer.ListeningPort);
        using var otherReader = new StreamReader(other.GetStream(), Encoding.UTF8);
        using var otherWriter = new StreamWriter(other.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        response = await Request(otherReader, otherWriter, Command(false, session));
        Check(!(bool)response["ok"] && module.MenuOpen, "IPC non-owner cannot close overlay");
        response = await Request(otherReader, otherWriter, Command(true, session));
        Check(!(bool)response["ok"] && module.MenuOpen, "IPC non-owner cannot renew overlay");
        owner.Dispose();
        await Until(() => !module.MenuOpen, Dispatcher.Pump);
        Check(cursorTransitions.SequenceEqual(new[] { true, false }), "owner disconnect restores cursor and input on dispatcher");
        response = await Request(otherReader, otherWriter, Command(true, session));
        Check((bool)response["ok"] && module.MenuOpen, "new connection can acquire after owner disconnect");
        await Until(() => !module.MenuOpen, IpcServer.TickOverlay);
        Check(cursorTransitions.SequenceEqual(new[] { true, false, true, false }), "Unity frame lease tick restores lock after missing heartbeat");
        await Request(otherReader, otherWriter, Command(true, session));
        IpcServer.Stop();
        Dispatcher.Pump();
        Check(!module.MenuOpen && cursorTransitions.Count == 6 && !cursorTransitions.Last(), "server stop releases active input and cursor lease");
        IpcServer.Start(0);
        using var unsupported = new TcpClient();
        await unsupported.ConnectAsync("127.0.0.1", IpcServer.ListeningPort);
        using var unsupportedReader = new StreamReader(unsupported.GetStream(), Encoding.UTF8);
        using var unsupportedWriter = new StreamWriter(unsupported.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        schema = await Request(unsupportedReader, unsupportedWriter, "{\"cmd\":\"schema\"}");
        response = await Request(unsupportedReader, unsupportedWriter, Command(true, (string)schema["sessionId"]));
        Check(!(bool)schema["externalOverlay"] && !(bool)response["ok"], "non-Unity hosts do not advertise unsupported external overlay locks");
        IpcServer.Stop();
        Dispatcher.Pump();
    }

    private static void ExternalCursorTests()
    {
        UnityEngine.Application.runInBackground = false;
        UnityEngine.Cursor.visible = false;
        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Locked;
        var overlay = new ExternalOverlay();
        Check(UnityEngine.Application.runInBackground, "external overlay enables Unity IPC updates without focus");
        overlay.SetOpen(true);
        Check(UnityEngine.Cursor.visible && UnityEngine.Cursor.lockState == UnityEngine.CursorLockMode.None, "external overlay releases and shows cursor");
        overlay.SetOpen(true);
        UnityEngine.Cursor.visible = false;
        UnityEngine.Cursor.lockState = UnityEngine.CursorLockMode.Confined;
        UnityEngine.Application.runInBackground = false;
        overlay.Tick();
        Check(UnityEngine.Cursor.visible && UnityEngine.Cursor.lockState == UnityEngine.CursorLockMode.None && UnityEngine.Application.runInBackground,
            "external overlay maintains cursor and background updates against game changes");
        overlay.SetOpen(false);
        Check(!UnityEngine.Cursor.visible && UnityEngine.Cursor.lockState == UnityEngine.CursorLockMode.Locked, "closing overlay restores cursor snapshot taken before first open");
        overlay.SetOpen(true);
        overlay.Dispose();
        Check(!UnityEngine.Cursor.visible && UnityEngine.Cursor.lockState == UnityEngine.CursorLockMode.Locked && !UnityEngine.Application.runInBackground,
            "plugin disposal restores cursor and prior runInBackground setting");
        overlay.Tick();
        overlay.SetOpen(true);
        Check(!UnityEngine.Cursor.visible && !UnityEngine.Application.runInBackground, "disposed overlay cannot recapture cursor or background setting");
    }

    private static async Task<Dictionary<string, object>> Request(StreamReader reader, StreamWriter writer, string request)
    {
        await writer.WriteLineAsync(request);
        var response = reader.ReadLineAsync();
        await Until(() => response.IsCompleted, Dispatcher.Pump);
        string line = await response;
        using var validJson = System.Text.Json.JsonDocument.Parse(line);
        return Json.Parse(line);
    }

    private static async Task Until(Func<bool> completed, Action tick = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (!completed())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Test condition did not complete");
            tick?.Invoke();
            await Task.Delay(5);
        }
    }

    private sealed class Fixture : IGameModule
    {
        internal List<CheatOption> Options;
        internal bool MenuOpen;
        internal int MenuChanges;
        public string ProductName => "fixture";
        public string DisplayName => "Fixture";
        public void Initialize() { }
        public void Update() { }
        public void SetMenuOpen(bool open) { MenuOpen = open; MenuChanges++; }
        public bool IsReady(out string status) { status = "fixture"; return true; }
        public List<CheatCategory> BuildCategories() => new List<CheatCategory> { new CheatCategory("Fixture") { Options = Options } };
    }
}
