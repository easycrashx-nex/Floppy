using System.Text.Json;
using Floppy.Core;
using Floppy.Core.Api;
using Floppy.Unrailed2;

internal static class Program
{
    private const string TypeHash = "-486817492";
    private const string FieldHash = "953417568";
    private static readonly (string Name, Action Run)[] Cases =
    {
        ("runtime component IDs retain readable hash-based names", Mapping),
        ("hash and runtime ID read the same filtered fields", Fields),
        ("unknown component hashes never request an unfiltered world", Unknown),
        ("a new world ID reloads caches even in the same mode", WorldChange),
        ("disconnect invalidates components and names", Disconnect),
        ("writes require transport success and the target entity's readback", Writes),
        ("availability and info callbacks reuse one cheat snapshot without requests", Snapshot),
        ("a vanished selected field requires an explicit replacement choice", MissingField),
        ("a vanished selected component never selects another component automatically", MissingGroup),
        ("bulk writes share one target-specific readback", BulkTests.Run)
    };

    private static int Main()
    {
        int passed = 0;
        foreach (var test in Cases)
        {
            try { test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
            catch (Exception error) { Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine($"{passed}/{Cases.Length} Unrailed 2 regression groups passed");
        return passed == Cases.Length ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Start()
    {
        Debugger.Reset();
        Debugger.Responses["worldInfo"] = WorldInfo(0, "Menu");
        Debugger.Responses["typeNames"] = "{}";
        Welt.Aktualisiere();
        Debugger.Responses["worldInfo"] = WorldInfo(4, "Story");
        Debugger.Responses["componentMap"] = Map(46);
        Debugger.Responses["typeNames"] = Names("CheatComponent", "InfiniteDash");
        Debugger.Responses["listEntities?cTypes=46"] = Entities(46, false);
        Welt.Aktualisiere();
        Welt.Arten(erzwingen: true);
        Debugger.Requests.Clear();
    }

    private static void Mapping()
    {
        Start();
        var component = Welt.Arten().Single();
        Check(component.Schluessel == "46" && component.Anzeige == "CheatComponent" && component.Anzahl == 1,
            "componentMap.id is the runtime ID; componentMap.name must resolve the display name");
    }

    private static void Fields()
    {
        Start();
        var byHash = Welt.Felder(TypeHash);
        var byId = Welt.Felder("46");
        Check(byHash.Count == 1 && byId.Count == 1 && byHash[0].Entitaet == 42
            && byHash[0].Anzeige == "InfiniteDash" && byHash[0].Wert == "false"
            && byId[0].Anzeige == byHash[0].Anzeige && byId[0].Wert == byHash[0].Wert,
            "both identities must return entity 42's requested component and resolve its field hash");
        Check(Debugger.Requests.Where(p => p.StartsWith("listEntities", StringComparison.Ordinal))
            .SequenceEqual(new[] { "listEntities?cTypes=46", "listEntities?cTypes=46" }),
            "reads must send the runtime component ID rather than the type-name hash");
    }

    private static void Unknown()
    {
        Start();
        Check(Welt.Felder("-123456789").Count == 0, "unknown components must return no fields");
        Check(!Debugger.Requests.Any(p => p.StartsWith("listEntities", StringComparison.Ordinal)),
            "an unknown component must not issue any listEntities request");
    }

    private static void WorldChange()
    {
        Start();
        Debugger.Responses["worldInfo"] = WorldInfo(5, "Story");
        Debugger.Responses["componentMap"] = Map(73);
        Debugger.Responses["typeNames"] = Names("NextWorldCheatComponent", "NewWorldDash");
        Debugger.Responses["listEntities?cTypes=73"] = Entities(73, true);
        Welt.Aktualisiere();
        var component = Welt.Arten().Single();
        Check(component.Schluessel == "73" && component.Anzeige == "NextWorldCheatComponent"
            && Welt.Felder(TypeHash).Single().Anzeige == "NewWorldDash",
            "same-mode world replacement must invalidate both runtime ID and name caches immediately");
        Check(Debugger.Requests.Contains("componentMap") && Debugger.Requests.Contains("typeNames"),
            "the new world's mapping and type names must be fetched before cached data is exposed");
    }

    private static void Disconnect()
    {
        Start();
        Debugger.Responses.Clear();
        Welt.Aktualisiere();
        Check(!Welt.ImSpiel && Welt.Arten().Count == 0 && !Namen.Geladen,
            "disconnected worlds must not expose stale runtime components or names");
    }

    private static void Writes()
    {
        Start();
        Debugger.Responses["listEntities?cTypes=46"] = Entities(46, false, true);
        Debugger.OnRequest = path =>
        {
            if (path.StartsWith("changeValue?", StringComparison.Ordinal)) Debugger.Responses[path] = "";
        };
        Check(!Welt.Setze(42, TypeHash, FieldHash, "true"),
            "another entity already holding the requested value cannot prove this write succeeded");
        Debugger.OnRequest = path =>
        {
            if (!path.StartsWith("changeValue?", StringComparison.Ordinal)) return;
            if (path != "changeValue?command=" + Debugger.Verpacke("42." + TypeHash + "." + FieldHash + "=true"))
            {
                Debugger.Responses.Remove(path);
                return;
            }
            Debugger.Responses[path] = "";
            Debugger.Responses["listEntities?cTypes=46"] = Entities(46, true);
        };
        Check(Welt.Setze(42, "46", FieldHash, "true"),
            "a runtime ID write must use the type hash and confirm its target readback");
        Debugger.OnRequest = path =>
        {
            if (path.StartsWith("changeValue?", StringComparison.Ordinal)) Debugger.Responses.Remove(path);
        };
        Check(!Welt.Setze(42, TypeHash, FieldHash, "false"),
            "a failed later write must not reuse a previous success as proof");
    }

    private static void Snapshot()
    {
        Start();
        Cheats.VergissSnapshot();
        Cheats.AktualisiereSnapshot();
        Check(Cheats.Verfuegbar && Debugger.Requests.SequenceEqual(new[] { "listEntities?cTypes=46" }),
            "one snapshot refresh must read the singleton through its runtime ID exactly once");
        var options = new[] { Cheats.Zug(), Cheats.Bauen(), Cheats.Muttern(), Cheats.Automatik() }
            .SelectMany(category => category.Options).ToArray();
        int requests = Debugger.Requests.Count;
        bool available = true;
        for (int pass = 0; pass < 20; pass++)
            foreach (var option in options)
            {
                available &= option.Available;
                if (option.Kind == OptionKind.Info) option.OnChanged?.Invoke(option);
            }
        Check(available && options.Single(o => o.Id == "zug.zustand").TextValue.Contains("kaputt: false")
            && Debugger.Requests.Count == requests,
            "repeated UI availability and information reads must use the snapshot without requests");
        Debugger.Responses["listEntities?cTypes=46"] = "{\"entities\":[]}";
        Cheats.AktualisiereSnapshot();
        Check(!Cheats.Verfuegbar && !options.Single(o => o.Id == "zug.kaputt").Available,
            "a vanished singleton must immediately make its controls unavailable");
        requests = Debugger.Requests.Count;
        Check(Cheats.Zustand.Contains("Runde erkannt") && Debugger.Requests.Count == requests,
            "a running world without cheat support must explain its unavailable controls without requests");
    }

    private static void StartSelection()
    {
        Start();
        Debugger.Responses["componentMap"] = SelectionMap(includeFirst: true);
        Debugger.Responses["typeNames"] = "{\"-486817492\":{\"FullName\":\"Fixture.CheatComponent\",\"1\":\"Alpha\",\"2\":\"Beta\"},\"1234\":{\"FullName\":\"Fixture.OtherComponent\",\"3\":\"Gamma\"}}";
        Debugger.Responses["listEntities?cTypes=46"] = SelectionFields(includeFirst: true);
        Debugger.Responses["listEntities?cTypes=47"] = "{\"entities\":[{\"eid\":43,\"components\":[{\"id\":47,\"fields\":[{\"name\":\"3\",\"value\":33}]}]}]}";
        Debugger.Responses["listDumps"] = "[]";
        Namen.Vergiss();
        Namen.LadeFallsNoetig();
        Welt.Arten(erzwingen: true);
        var module = new Unrailed2Module();
        Registry.SetModule(module);
        module.Initialize();
        Unrailed2Module.ListenPflegen();
        Select("wert.gruppe", "CheatComponent");
        Select("wert.feld", "Alpha");
        Check(Registry.Find("wert.setzen").Available, "the initial explicit field choice must be writable");
    }

    private static void Select(string id, string label)
    {
        var option = Registry.Find(id);
        option.ChoiceIndex = Array.FindIndex(option.Choices, choice => choice.StartsWith(label, StringComparison.Ordinal));
        Check(option.ChoiceIndex >= 0, "fixture choice must exist: " + label);
        option.NotifyChanged();
    }

    private static void MustChooseAgain()
    {
        var apply = Registry.Find("wert.setzen");
        Check(!apply.Available, "a vanished selection must disable writing until an explicit new choice");
        Debugger.Requests.Clear();
        apply.Fire();
        Check(!Debugger.Requests.Any(path => path.StartsWith("changeValue?", StringComparison.Ordinal)),
            "even a queued invocation must not write an automatic replacement selection");
    }

    private static void MissingField()
    {
        StartSelection();
        Debugger.Responses["listEntities?cTypes=46"] = SelectionFields(includeFirst: false);
        Unrailed2Module.ListenPflegen();
        MustChooseAgain();
        Debugger.Responses["listEntities?cTypes=46"] = SelectionFields(includeFirst: true);
        Unrailed2Module.ListenPflegen();
        MustChooseAgain();
        Select("wert.feld", "Beta");
        Unrailed2Module.ListenPflegen();
        var info = Registry.Find("wert.jetzt");
        info.NotifyChanged();
        Check(Registry.Find("wert.setzen").Available && info.TextValue.StartsWith("17", StringComparison.Ordinal),
            "explicitly selecting a remaining field restores writing to that field and survives polling");
    }

    private static void MissingGroup()
    {
        StartSelection();
        Debugger.Responses["componentMap"] = SelectionMap(includeFirst: false);
        Welt.Arten(erzwingen: true);
        Unrailed2Module.ListenPflegen();
        MustChooseAgain();
        Debugger.Responses["componentMap"] = SelectionMap(includeFirst: true);
        Welt.Arten(erzwingen: true);
        Unrailed2Module.ListenPflegen();
        MustChooseAgain();
        Select("wert.gruppe", "OtherComponent");
        Select("wert.feld", "Gamma");
        Unrailed2Module.ListenPflegen();
        var info = Registry.Find("wert.jetzt");
        info.NotifyChanged();
        Check(Registry.Find("wert.setzen").Available && info.TextValue.StartsWith("33", StringComparison.Ordinal),
            "explicitly selecting another component and field restores the correct target");
    }

    private static string SelectionMap(bool includeFirst) => includeFirst
        ? "{\"46\":{\"id\":46,\"name\":\"-486817492\",\"count\":1},\"47\":{\"id\":47,\"name\":\"1234\",\"count\":1}}"
        : "{\"47\":{\"id\":47,\"name\":\"1234\",\"count\":1}}";
    private static string SelectionFields(bool includeFirst) => JsonSerializer.Serialize(new
    {
        entities = new[] { new { eid = 42, components = new[] { new { id = 46,
            fields = includeFirst ? new[] { new { name = "1", value = 11 }, new { name = "2", value = 17 } }
                : new[] { new { name = "2", value = 17 } } } } } }
    });

    private static string WorldInfo(int id, string mode) => JsonSerializer.Serialize(new { id, config = new object[] { 123, mode } });
    private static string Map(int id) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        [id.ToString()] = new { id, name = TypeHash, count = 1 }
    });
    private static string Names(string type, string field) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        [TypeHash] = new Dictionary<string, string> { ["FullName"] = "common.Unrailed2.Tests." + type, [FieldHash] = field }
    });
    private static string Entities(int type, bool target, bool? other = null)
    {
        object Component(int id, bool value) => new { id, fields = new[] { new { name = FieldHash, value } } };
        var entities = new List<object> { new { eid = 42, components = new[] { Component(type, target), Component(999, true) } } };
        if (other.HasValue) entities.Add(new { eid = 43, components = new[] { Component(type, other.Value) } });
        return JsonSerializer.Serialize(new { entities });
    }
}
