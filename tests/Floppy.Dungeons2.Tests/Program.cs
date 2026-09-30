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
        Console.WriteLine(response);
        using var json = JsonDocument.Parse(response!);
        return json.RootElement.GetProperty("ready").GetBoolean() ? 0 : 1;
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
    internal ulong Names, Objects, Chunk, Controller, Pawn, Ability, Health, Enemy, Attributes;
    internal Fixture()
    {
        Names = Allocate(128); _nameBlock = Allocate(65536); U64(Names + 16, _nameBlock); Name("None");
        Objects = Allocate(128); Chunk = Allocate(24 * 16);
        var table = Allocate(16); U64(table, Chunk); U64(Objects + 16, table); I32(Objects + 36, 6);
        var local = Object(0, "Local", Class("DungeonsLocalPlayer", ("PlayerController", "ObjectProperty", 0x40)));
        Controller = Object(1, "PC", Class("PlayerController", ("Pawn", "ObjectProperty", 0x50), ("AcknowledgedPawn", "ObjectProperty", 0x58)));
        Pawn = Object(2, "Player", Class("PlayerCharacter", ("Controller", "ObjectProperty", 0x60), ("AbilitySystemComponent", "ObjectProperty", 0x68)));
        Ability = Object(3, "Ability", Class("SWAbilitySystemComponent", ("OwnerActor", "ObjectProperty", 0x60),
            ("AvatarActor", "ObjectProperty", 0x68), ("SpawnedAttributes", "ArrayProperty", 0x80)));
        var healthClass = Class("ATR_Health", ("Health", "StructProperty", 0x90), ("HealthMax", "StructProperty", 0xb0));
        Health = Object(4, "Health", healthClass); Enemy = Object(5, "EnemyHealth", healthClass);
        U64(local + 0x40, Controller); U64(Controller + 0x50, Pawn); U64(Controller + 0x58, Pawn);
        U64(Pawn + 0x60, Controller); U64(Pawn + 0x68, Ability);
        U64(Ability + 0x60, Pawn); U64(Ability + 0x68, Pawn);
        U64(Health + 32, Pawn); U64(Enemy + 32, Enemy);
        Attributes = Allocate(16); U64(Attributes, Health); U64(Ability + 0x80, Attributes);
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
    internal void ReplacePawn()
    {
        var replacement = Object(6, "ReplacementPlayer", (ulong)Marshal.ReadInt64((IntPtr)(Pawn + 16)));
        I32(Objects + 36, 7);
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
