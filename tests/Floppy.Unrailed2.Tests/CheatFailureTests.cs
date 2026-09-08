using System.Reflection;
using System.Text.Json;
using Floppy.Core;
using Floppy.Core.Api;
using Floppy.Unrailed2;

internal static class CheatFailureTests
{
    private const string ReadCheats = "listEntities?cTypes=46";
    private sealed record Case(string Id, string Field, object Initial, object Requested);
    private static readonly Case[] Cases =
    {
        new("zug.kaputt", "953417568", false, true),
        new("zug.brennt", "-1773927608", false, true),
        new("zug.haltan", "1439690848", false, true),
        new("bau.physik", "-973793006", false, true),
        new("bau.beacons", "-1677779445", false, true),
        new("auto.an", "693253938", false, true),
        new("auto.stopp", "-812172858", false, true),
        new("auto.unten", "-557360116", false, true),
        new("auto.mapgen", "-162511644", false, true),
        new("auto.bosse", "338793709", 0, 7)
    };
    private static readonly MethodInfo ApplySet = typeof(IpcServer).GetMethod("ApplySet", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo StorageRoot = typeof(Profile).GetField("StorageRoot", BindingFlags.Static | BindingFlags.NonPublic);
    private static string[] Writes => Debugger.Requests.Where(p => p.StartsWith("changeValue?", StringComparison.Ordinal)).ToArray();

    public static void Run()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "Floppy.CheatFailure.Tests-" + Guid.NewGuid().ToString("N"));
        object previousRoot = StorageRoot.GetValue(null);
        bool previousProtection = Schutz.An;
        StorageRoot.SetValue(null, scratch);
        try
        {
            foreach (var test in Cases)
            {
                var option = Start(test);
                Check(!Apply(test) && option.MessageIsError && IsValue(option, test.Initial) && !option.Active,
                    test.Id + ": transport failure must reject the UI change and restore its prior value");
                Check(Writes.Length == 1, test.Id + ": transport failure must not repeat the write");

                option = Start(test);
                var unconfirmed = AcceptWrite(test, int.MaxValue);
                Check(!Apply(test) && option.MessageIsError && IsValue(option, test.Initial) && !option.Active,
                    test.Id + ": unconfirmed changes must roll back the UI value");
                Check(Writes.Length == 1 && unconfirmed() == 4
                    && option.Message.Contains("bestätig", StringComparison.OrdinalIgnoreCase),
                    test.Id + ": four readbacks must report uncertainty without repeating the write");

                option = Start(test);
                var delayed = AcceptWrite(test, 3);
                Check(Apply(test) && !option.MessageIsError && IsValue(option, test.Requested) && option.Active,
                    test.Id + ": a fourth-read confirmation must retain the requested value");
                Check(Writes.Length == 1 && delayed() == 4,
                    test.Id + ": delayed confirmation must retry reads only");
            }

            foreach (var test in new[] { Cases[0], Cases[^1] })
            {
                Guards(test);
                Profiles(test);
            }
        }
        finally
        {
            StorageRoot.SetValue(null, previousRoot);
            Schutz.An = previousProtection;
            Debugger.Reset();
            Cheats.VergissSnapshot();
            string resolved = Path.GetFullPath(scratch);
            string expected = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Floppy.CheatFailure.Tests-");
            if (resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
                Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Cheat failure: " + message);
    }

    private static CheatOption Start(Case test, bool active = false)
    {
        Debugger.Reset();
        Schutz.An = true;
        Cheats.VergissSnapshot();
        Debugger.Responses["worldInfo"] = "{\"id\":0,\"config\":[123,\"Menu\"]}";
        Welt.Aktualisiere();
        Debugger.Responses["worldInfo"] = "{\"id\":4,\"config\":[123,\"Story\"]}";
        Debugger.Responses["componentMap"] = "{\"46\":{\"id\":46,\"name\":\"-486817492\",\"count\":1}}";
        Debugger.Responses["typeNames"] = "{}";
        Debugger.Responses[ReadCheats] = Entities(test, active ? test.Requested : test.Initial);
        Welt.Aktualisiere();
        var option = new[] { Cheats.Zug(), Cheats.Bauen(), Cheats.Automatik() }
            .SelectMany(c => c.Options).Single(o => o.Id == test.Id);
        Registry.SetModule(new FixtureModule(option));
        SetLocal(option, active ? test.Requested : test.Initial);
        Cheats.AktualisiereSnapshot();
        Check(option.Available, test.Id + ": fixture option must start available");
        Debugger.Requests.Clear();
        return option;
    }

    // Invoke the same synchronous application path as IPC, without starting a server or socket.
    private static bool Apply(Case test)
    {
        var input = new Dictionary<string, object> { ["id"] = test.Id };
        input[test.Requested is bool ? "bool" : "number"] = test.Requested is bool flag ? flag : Convert.ToDouble(test.Requested);
        try
        {
            string response = (string)ApplySet.Invoke(null, new object[] { input });
            using var json = JsonDocument.Parse(response);
            return json.RootElement.GetProperty("ok").GetBoolean();
        }
        catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
        {
            return false;
        }
    }

    private static Func<int> AcceptWrite(Case test, int oldReadbacks)
    {
        bool sent = false;
        int reads = 0;
        string expected = "changeValue?command=" + Debugger.Verpacke("42." + Cheats.Singleton + "." + test.Field + "=" + JsonSerializer.Serialize(test.Requested));
        Debugger.OnRequest = path =>
        {
            if (path.StartsWith("changeValue?", StringComparison.Ordinal))
            {
                Check(path == expected, test.Id + ": command must retain its field and JSON value type");
                Debugger.Responses[path] = "";
                sent = true;
            }
            if (sent && path == ReadCheats && reads++ >= oldReadbacks)
                Debugger.Responses[ReadCheats] = Entities(test, test.Requested);
        };
        return () => reads;
    }

    private static void Guards(Case test)
    {
        var option = Start(test);
        Debugger.Responses[ReadCheats] = "{\"entities\":[]}";
        Check(!Apply(test) && option.MessageIsError && IsValue(option, test.Initial) && Writes.Length == 0,
            test.Id + ": a carrier disappearing after availability must fail without writing");

        option = Start(test);
        Debugger.Responses["worldInfo"] = "{\"id\":5,\"config\":[123,\"Time\"]}";
        Welt.Aktualisiere();
        Check(!Apply(test) && option.MessageIsError && IsValue(option, test.Initial) && Writes.Length == 0,
            test.Id + ": a rejected write must fail and roll back before sending a command");
    }

    private static void Profiles(Case test)
    {
        var option = Start(test);
        SetLocal(option, test.Requested);
        Registry.Find("profil.name").TextValue = test.Id;
        var save = Registry.Find("profil.speichern");
        save.Fire();
        Check(!save.MessageIsError, test.Id + ": isolated profile fixture must save");
        SetLocal(option, test.Initial);
        var reads = AcceptWrite(test, int.MaxValue);
        var load = Registry.Find("profil.laden");
        load.Fire();
        Check(load.MessageIsError && IsValue(option, test.Initial) && !option.Active && Writes.Length == 1 && reads() == 4,
            test.Id + ": loading an unconfirmed profile must report failure and preserve the old model value");

        option = Start(test, active: true);
        var reset = Registry.Find("profil.zuruecksetzen");
        reset.Fire();
        Check(reset.MessageIsError && IsValue(option, test.Requested) && option.Active && Writes.Length == 1,
            test.Id + ": a failed all-off reset must not claim the cheat is inactive");
    }

    private static bool IsValue(CheatOption option, object expected) => expected is bool flag
        ? option.BoolValue == flag : option.NumberValue == Convert.ToSingle(expected);

    private static void SetLocal(CheatOption option, object value)
    {
        if (value is bool flag) option.BoolValue = flag;
        else option.NumberValue = Convert.ToSingle(value);
    }

    private static string Entities(Case test, object value) => JsonSerializer.Serialize(new
    {
        entities = new[] { new { eid = 42, components = new[] { new { id = 46,
            fields = new[] { new { name = test.Field, value } } } } } }
    });

    private sealed class FixtureModule(CheatOption option) : IGameModule
    {
        public string ProductName => "Unrailed2-CheatFailure-Fixture";
        public string DisplayName => "Isolated cheat failure tests";
        public void Initialize() { }
        public void Update() { }
        public void SetMenuOpen(bool open) { }
        public bool IsReady(out string status) { status = "Fixture"; return true; }
        public List<CheatCategory> BuildCategories() => new() { new CheatCategory("Fixture").Add(option) };
    }
}
