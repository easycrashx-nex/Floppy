using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Floppy.Dungeons2;
using Floppy.Unreal;
using Host = Floppy.Dungeons2.Host;

Console.OutputEncoding = new UTF8Encoding(false);
if (args.Contains("--probe-host"))
{
    if (!Host.Starte(out string message)) { Console.Error.WriteLine(message); return 1; }
    try
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync("127.0.0.1", Floppy.Core.IpcServer.Port);
        using var stream = socket.GetStream();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(stream, leaveOpen: true);
        await Task.Delay(1500);
        await writer.WriteLineAsync("{\"cmd\":\"schema\"}");
        var response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(8));
        using var json = JsonDocument.Parse(response!);
        var root = json.RootElement;
        var values = root.GetProperty("values");
        var info = new[] { "progress.level", "progress.xp.current", "progress.xp.next", "currency.emeralds", "ammo.loaded" }
            .ToDictionary(id => id, id => values.GetProperty(id).GetProperty("text").GetString());
        bool ready = root.GetProperty("ready").GetBoolean();
        Console.WriteLine(JsonSerializer.Serialize(new { ready, game = root.GetProperty("game").GetString(), info }));
        return ready && info.Values.All(text => !string.IsNullOrEmpty(text) && text != "–") ? 0 : 1;
    }
    finally { while (!await Host.StoppeAsync()) { } }
}

if (args.Contains("--probe-game"))
{
    using var module = new DungeonsModule();
    var options = module.BuildCategories().SelectMany(c => c.Options).ToArray();
    var watch = Stopwatch.StartNew();
    module.Initialize();
    bool ready = module.IsReady(out var status);
    string? action = null;
    bool actionOk = true;
    if (args.Contains("--heal") && ready)
    {
        var heal = options.Single(o => o.Id == "health.heal");
        heal.Fire();
        action = heal.Message;
        actionOk = !heal.MessageIsError;
        module.Update();
    }
    Console.WriteLine(JsonSerializer.Serialize(new { ready, status, pid = module.ProcessId, action, actionOk,
        health = options.Single(o => o.Id == "health.current").TextValue, elapsedMs = watch.ElapsedMilliseconds },
        new JsonSerializerOptions { WriteIndented = true }));
    return ready && actionOk ? 0 : 1;
}

if (args.Contains("--probe-features") || args.Contains("--probe-emerald"))
{
    using var memoryProbe = new Speicher();
    if (!memoryProbe.Verbinde(DungeonsModule.ProcessName, DungeonsModule.ProcessName + ".exe"))
    { Console.Error.WriteLine(memoryProbe.LetzterFehler); return 1; }
    using var image = File.OpenRead(memoryProbe.Programmpfad);
    if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)) != "7C83AFBF0AD34A40B853CDB25A22FFFB605D08E2A1E2D431974D7C7C1EE0BA54") return 2;
    var live = new Session(memoryProbe, 0xBEA8BF0, 0xBDC5040);
    if (!live.Refresh()) { Console.Error.WriteLine(live.Status); return 1; }
    try
    {
        if (args.Contains("--probe-emerald"))
        {
            float before = live.Values["ATR_Currency.Emeralds"].Current;
            bool given = live.Grant("ATR_Currency.Emeralds", "ATR_Currency.EmeraldsMax", 1, out string message);
            Console.WriteLine(JsonSerializer.Serialize(new { before, given, message }));
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(1000); live.Refresh();
                Console.WriteLine(JsonSerializer.Serialize(new { second = i + 1, ready = live.Ready,
                    value = live.Values.TryGetValue("ATR_Currency.Emeralds", out var value) ? value : null }));
            }
            return given ? 0 : 1;
        }
        var results = new List<object>();
        int passed = 0;
        foreach (var feature in FeatureCatalog.All)
        {
            live.Refresh();
            bool found = live.Values.TryGetValue(feature.Target, out var before);
            float setting = feature.Mode is Adjustment.Multiply or Adjustment.Divide ? Math.Min(feature.Max, 1.25f) : feature.Step;
            bool changed = found && live.ApplyFeature(feature, setting, out _);
            bool restored = live.ApplyFeature(feature, feature.Neutral, out string message);
            live.Refresh();
            bool afterFound = live.Values.TryGetValue(feature.Target, out var after);
            bool same = found && afterFound && before!.Object == after!.Object && Math.Abs(after.Current - before.Current) < .001f;
            if (changed && restored && same) passed++;
            results.Add(new { feature.Id, found, changed, restored, same, before = before?.Current, after = after?.Current, message });
        }
        Console.WriteLine(JsonSerializer.Serialize(new { passed, total = FeatureCatalog.All.Count, results }, new JsonSerializerOptions { WriteIndented = true }));
        return passed == FeatureCatalog.All.Count ? 0 : 1;
    }
    finally { live.RestoreAll(out _); }
}

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
using var fixture = new Fixture();
using var memory = new Speicher();
using var self = Process.GetCurrentProcess();
Check(memory.Verbinde(self.ProcessName, self.MainModule!.ModuleName), "attach to test process only");
var session = new Session(memory, unchecked(fixture.Objects - memory.Basis), unchecked(fixture.Names - memory.Basis));
Check(session.Refresh() && session.Health == 25 && session.Maximum == 100, "resolve owned live health");
Check(session.Heal(out _) && fixture.Float(fixture.Health + 0x9c) == 100, "heal current health");
Check(fixture.Float(fixture.Health + 0x98) == 25 && fixture.Float(fixture.Health + 0xb8) == 100 &&
    fixture.Float(fixture.Enemy + 0x9c) == 17, "base, maximum and enemy untouched");

fixture.F32(fixture.Health + 0x9c, 25);
fixture.U64(fixture.Controller + 0x58, 0);
Check(!session.Heal(out _) && fixture.Float(fixture.Health + 0x9c) == 25, "unacknowledged pawn cannot be written");
fixture.U64(fixture.Controller + 0x58, fixture.Pawn);
fixture.U64(fixture.Health + 32, fixture.Enemy);
Check(!session.Refresh(), "foreign ownership rejected");
fixture.U64(fixture.Health + 32, fixture.Pawn);
fixture.I32(fixture.Health + 8, 0x10);
Check(!session.Heal(out _), "class default object rejected");
fixture.I32(fixture.Health + 8, 0);
fixture.U64(fixture.Chunk + 4 * 24, fixture.Enemy);
Check(!session.Heal(out _), "stale object-table identity rejected");
fixture.U64(fixture.Chunk + 4 * 24, fixture.Health);
fixture.I32(fixture.Ability + 0x88, 129);
Check(!session.Refresh(), "corrupt attribute count bounded");
fixture.I32(fixture.Ability + 0x88, 1);
fixture.U64(fixture.Attributes + 8, fixture.Health);
fixture.I32(fixture.Ability + 0x88, 2);
Check(!session.Refresh(), "ambiguous attribute sets rejected");
fixture.I32(fixture.Ability + 0x88, 1);
foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, 200f })
{
    fixture.F32(fixture.Health + 0x9c, invalid);
    Check(!session.Heal(out _), "invalid or dead health rejected: " + invalid);
}
fixture.F32(fixture.Health + 0x9c, 25);
fixture.U64(fixture.Ability + 0x80, 0x10000);
Check(!session.Heal(out _), "unreadable attribute list rejected");
fixture.U64(fixture.Ability + 0x80, fixture.Attributes);
Check(session.Refresh(), "recovers when live chain becomes valid again");
var maximumFeature = FeatureCatalog.All.Single(f => f.Id == "health.maximum");
Check(session.ApplyFeature(maximumFeature, 2, out _) && fixture.Float(fixture.Health + 0xbc) == 200, "maximum multiplier applies");
Check(session.ApplyFeature(maximumFeature, 2, out _) && fixture.Float(fixture.Health + 0xbc) == 200, "repeated multiplier does not compound");
Check(session.Heal(out _) && fixture.Float(fixture.Health + 0x9c) == 200, "healing respects boosted maximum");
Check(session.ApplyFeature(maximumFeature, 1, out _) && fixture.Float(fixture.Health + 0xbc) == 100 && fixture.Float(fixture.Health + 0x9c) == 100,
    "maximum reset clamps current health safely");
fixture.F32(fixture.Health + 0x9c, 25);
Check(session.ApplyFeature(maximumFeature, 2, out _), "reapply maximum");
fixture.F32(fixture.Health + 0xb8, 120); fixture.F32(fixture.Health + 0xbc, 120);
Check(session.ApplyFeature(maximumFeature, 2, out _) && fixture.Float(fixture.Health + 0xbc) == 240, "equipment change becomes new baseline");
Check(session.RestoreAll(out _) && fixture.Float(fixture.Health + 0xbc) == 120, "restore keeps new equipment baseline");
fixture.F32(fixture.Health + 0xb8, 100); fixture.F32(fixture.Health + 0xbc, 100);
Check(session.ApplyFeature(maximumFeature, 2, out _), "maximum boost before serial change");
fixture.I32(fixture.Chunk + 4 * 24 + 16, 1);
Check(session.ApplyFeature(maximumFeature, 1, out _) && fixture.Float(fixture.Health + 0xbc) == 200, "reused object serial does not receive previous restore");
fixture.F32(fixture.Health + 0xbc, 100);
Check(!session.ApplyFeature(maximumFeature, float.NaN, out _) && !session.ApplyFeature(maximumFeature, 100, out _), "invalid feature settings rejected");
Check(FeatureCatalog.All.Select(f => f.Id).Distinct().Count() == FeatureCatalog.All.Count &&
    FeatureCatalog.All.Select(f => f.Target).Distinct().Count() == FeatureCatalog.All.Count, "catalog IDs and writable targets are unique");
foreach (var feature in FeatureCatalog.All)
    Check(float.IsFinite(feature.Value(1, feature.Min)) && float.IsFinite(feature.Value(1, feature.Max)) &&
        feature.Neutral >= feature.Min && feature.Neutral <= feature.Max, "bounded catalog setting: " + feature.Id);
const string emeralds = "ATR_Currency.Emeralds", emeraldMax = "ATR_Currency.EmeraldsMax";
fixture.EnableExtraAttributes();
Check(session.Refresh() && session.CanGrant(emeralds, emeraldMax), "owned currency available");
Check(session.Grant(emeralds, emeraldMax, 7, out _) && fixture.Float(fixture.Currency + 0x98) == 35 &&
    fixture.Float(fixture.Currency + 0x9c) == 35, "grant updates base and current together");
Check(session.RestoreAll(out _) && fixture.Float(fixture.Currency + 0x9c) == 35, "reset does not remove one-time currency grant");
foreach (float invalid in new[] { 0f, -1f, .5f, float.NaN, float.PositiveInfinity, 10000f })
    Check(!session.Grant(emeralds, emeraldMax, invalid, out _) && fixture.Float(fixture.Currency + 0x9c) == 35,
        "invalid or excessive grant rejected: " + invalid);
fixture.F32(fixture.Currency + 0x98, 34);
Check(!session.Grant(emeralds, emeraldMax, 1, out _), "currency modifiers reject ambiguous permanent count");
fixture.F32(fixture.Currency + 0x98, 35);
fixture.U64(fixture.Currency + 32, fixture.Enemy);
Check(!session.Grant(emeralds, emeraldMax, 1, out _), "foreign currency cannot be written");
fixture.U64(fixture.Currency + 32, fixture.Pawn);
var souls = FeatureCatalog.Resources.Single(r => r.Id == "soul");
Check(session.Refill(souls, out _) && fixture.Float(fixture.Soul + 0x9c) == 100 && fixture.Float(fixture.Soul + 0x98) == 10,
    "resource refill changes current only");
fixture.F32(fixture.Soul + 0x9c, -1);
Check(!session.Refill(souls, out _), "invalid resource count rejected");
fixture.F32(fixture.Soul + 0x9c, 10);
var speed = FeatureCatalog.All.Single(f => f.Id == "movement.speed");
Check(session.ApplyFeature(speed, 2, out _) && fixture.Float(fixture.Movement + 0x90) == 1400, "owned movement float applies");
Check(session.ApplyFeature(speed, 2, out _) && fixture.Float(fixture.Movement + 0x90) == 1400, "movement does not compound");
Check(session.ApplyFeature(speed, 1, out _) && fixture.Float(fixture.Movement + 0x90) == 700, "movement restores exact baseline");
Check(session.ApplyFeature(speed, 2, out _), "movement adjustment before external change");
fixture.F32(fixture.Movement + 0x90, 800);
Check(session.RestoreAll(out _) && fixture.Float(fixture.Movement + 0x90) == 800, "restore preserves external movement change");
fixture.U64(fixture.Movement + 32, fixture.Enemy);
Check(!session.ApplyFeature(speed, 2, out _) && fixture.Float(fixture.Movement + 0x90) == 800, "foreign movement component rejected");
fixture.U64(fixture.Movement + 32, fixture.Pawn);
Check(session.ApplyFeature(speed, 2, out _), "movement adjustment before unavailable chain");
fixture.U64(fixture.Controller + 0x58, 0);
Check(!session.RestoreAll(out _) && fixture.Float(fixture.Movement + 0x90) == 1600, "pending restore is reported while ownership chain unavailable");
fixture.U64(fixture.Controller + 0x58, fixture.Pawn);
Check(session.RestoreAll(out _) && fixture.Float(fixture.Movement + 0x90) == 800, "pending restore succeeds after ownership recovers");
using (var module = new DungeonsModule())
{
    var categories = module.BuildCategories();
    var options = categories.SelectMany(c => c.Options).ToArray();
    Check(options.Select(o => o.Id).Distinct().Count() == options.Length, "public option IDs unique");
    Check(options.Count(o => o.Kind == Floppy.Core.Api.OptionKind.Slider) == 58 &&
        options.Where(o => o.Kind == Floppy.Core.Api.OptionKind.Slider).All(o => o.Ruhewert == o.NumberValue), "all adjustments start at neutral reset value");
    Check(options.Single(o => o.Id == "emerald.give").Kind == Floppy.Core.Api.OptionKind.Button &&
        !options.Any(o => o.Id == "ammo.refill"), "currency grant exposed; reserve is not misrepresented as loaded-ammo refill");
    Check(options.Where(o => o.Kind != Floppy.Core.Api.OptionKind.Info && o.Id is not "emerald.amount" and not "springstone.amount")
        .All(o => !o.Available), "all game mutations unavailable before connection; amount inputs may be edited offline");
}
fixture.ReplacePawn();
Check(session.Refresh() && session.Pawn != fixture.Pawn && session.Health == 17, "respawn resolves replacement pawn and attribute set");
Check(session.Heal(out _) && fixture.Float(fixture.Enemy + 0x9c) == 100 && fixture.Float(fixture.Health + 0x9c) == 25,
    "respawn heal leaves previous pawn's attribute untouched");
var old = new Reflexion(memory, unchecked(fixture.Objects - memory.Basis), unchecked(fixture.Names - memory.Basis));
Check(old.Finde(fixture.Health, "Health", out var property) && property.Abstand == 0,
    "existing Unreal property layout remains default");
Console.WriteLine($"PASS Dungeons II: {checks} native fixture checks; no game accessed");
return 0;

sealed class Fixture : IDisposable
{
    private readonly List<IntPtr> _blocks = new();
    private readonly Dictionary<string, uint> _names = new();
    private readonly ulong _nameBlock;
    private int _nameCursor;
    internal ulong Names, Objects, Chunk, Controller, Pawn, Ability, Health, Enemy, Attributes, Currency, Soul, Movement;
    internal Fixture()
    {
        Names = Allocate(128); _nameBlock = Allocate(65536); U64(Names + 16, _nameBlock); Name("None");
        Objects = Allocate(128); Chunk = Allocate(24 * 16);
        var table = Allocate(16); U64(table, Chunk); U64(Objects + 16, table); I32(Objects + 36, 6);
        var local = Object(0, "Local", Class("DungeonsLocalPlayer", ("PlayerController", "ObjectProperty", 0x40)));
        Controller = Object(1, "PC", Class("PlayerController", ("Pawn", "ObjectProperty", 0x50), ("AcknowledgedPawn", "ObjectProperty", 0x58)));
        Pawn = Object(2, "Player", Class("PlayerCharacter", ("Controller", "ObjectProperty", 0x60),
            ("AbilitySystemComponent", "ObjectProperty", 0x68), ("CharacterMovement", "ObjectProperty", 0x70)));
        Ability = Object(3, "Ability", Class("SWAbilitySystemComponent", ("OwnerActor", "ObjectProperty", 0x60),
            ("AvatarActor", "ObjectProperty", 0x68), ("SpawnedAttributes", "ArrayProperty", 0x80)));
        var healthClass = Class("ATR_Health", ("Health", "StructProperty", 0x90), ("HealthMax", "StructProperty", 0xb0));
        Health = Object(4, "Health", healthClass); Enemy = Object(5, "EnemyHealth", healthClass);
        U64(local + 0x40, Controller); U64(Controller + 0x50, Pawn); U64(Controller + 0x58, Pawn);
        U64(Pawn + 0x60, Controller); U64(Pawn + 0x68, Ability);
        U64(Ability + 0x60, Pawn); U64(Ability + 0x68, Pawn);
        U64(Health + 32, Pawn); U64(Enemy + 32, Enemy);
        Attributes = Allocate(32); U64(Attributes, Health); U64(Ability + 0x80, Attributes);
        I32(Ability + 0x88, 1); I32(Ability + 0x8c, 2);
        foreach (var obj in new[] { Health, Enemy })
        {
            ulong vtable = (ulong)Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64() + 0x100;
            U64(obj + 0x90, vtable); U64(obj + 0xb0, vtable);
            F32(obj + 0x98, 25); F32(obj + 0x9c, obj == Health ? 25 : 17);
            F32(obj + 0xb8, 100); F32(obj + 0xbc, 100);
        }
    }
    private ulong Allocate(int length)
    {
        var p = Marshal.AllocHGlobal(length); _blocks.Add(p); Marshal.Copy(new byte[length], 0, p, length);
        return unchecked((ulong)p.ToInt64());
    }
    private uint Name(string text)
    {
        if (_names.TryGetValue(text, out var index)) return index;
        index = (uint)_nameCursor / 2;
        var raw = Encoding.UTF8.GetBytes(text);
        Marshal.WriteInt16((IntPtr)(_nameBlock + (ulong)_nameCursor), (short)(raw.Length << 6));
        Marshal.Copy(raw, 0, (IntPtr)(_nameBlock + (ulong)_nameCursor + 2), raw.Length);
        _nameCursor += (raw.Length + 3) & ~1; _names[text] = index; return index;
    }
    private ulong Object(int index, string name, ulong cls)
    {
        var obj = Allocate(512); I32(obj + 12, index); U64(obj + 16, cls); I32(obj + 24, (int)Name(name));
        U64(Chunk + (ulong)index * 24, obj); return obj;
    }
    private ulong Class(string name, params (string Name, string Type, int Offset)[] fields)
    {
        var cls = Allocate(128); I32(cls + 24, (int)Name(name));
        ulong last = 0;
        foreach (var spec in fields.AsEnumerable().Reverse())
        {
            var f = Allocate(128); var type = Allocate(16); I32(type, (int)Name(spec.Type));
            U64(f + 8, type); U64(f + 24, last); I32(f + 32, (int)Name(spec.Name)); I32(f + 72, spec.Offset); last = f;
        }
        U64(cls + 0x50, last); return cls;
    }
    internal void U64(ulong p, ulong v) => Marshal.WriteInt64((IntPtr)p, unchecked((long)v));
    internal void EnableExtraAttributes()
    {
        Currency = Object(6, "Currency", Class("ATR_Currency", ("Emeralds", "StructProperty", 0x90), ("EmeraldsMax", "StructProperty", 0xb0)));
        Soul = Object(7, "Soul", Class("ATR_Soul", ("Souls", "StructProperty", 0x90), ("SoulsMax", "StructProperty", 0xb0)));
        Movement = Object(8, "Movement", Class("CharacterMovementComponent", ("MaxWalkSpeed", "FloatProperty", 0x90)));
        I32(Objects + 36, 9); U64(Pawn + 0x70, Movement); U64(Movement + 32, Pawn); F32(Movement + 0x90, 700);
        U64(Attributes + 8, Currency); U64(Attributes + 16, Soul); I32(Ability + 0x88, 3); I32(Ability + 0x8c, 4);
        foreach (var obj in new[] { Currency, Soul })
        {
            U64(obj + 32, Pawn);
            ulong vtable = (ulong)Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64() + 0x100;
            U64(obj + 0x90, vtable); U64(obj + 0xb0, vtable);
            F32(obj + 0x98, obj == Currency ? 28 : 10); F32(obj + 0x9c, obj == Currency ? 28 : 10);
            F32(obj + 0xb8, obj == Currency ? 9999 : 100); F32(obj + 0xbc, obj == Currency ? 9999 : 100);
        }
    }
    internal void ReplacePawn()
    {
        var replacement = Object(9, "ReplacementPlayer", (ulong)Marshal.ReadInt64((IntPtr)(Pawn + 16)));
        I32(Objects + 36, 10);
        U64(Controller + 0x50, replacement); U64(Controller + 0x58, replacement);
        U64(replacement + 0x60, Controller); U64(replacement + 0x68, Ability);
        U64(Ability + 0x60, replacement); U64(Ability + 0x68, replacement);
        U64(Enemy + 32, replacement); U64(Attributes, Enemy);
    }
    internal void I32(ulong p, int v) => Marshal.WriteInt32((IntPtr)p, v);
    internal void F32(ulong p, float v) => I32(p, BitConverter.SingleToInt32Bits(v));
    internal float Float(ulong p) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32((IntPtr)p));
    public void Dispose() { foreach (var p in _blocks) Marshal.FreeHGlobal(p); }
}
