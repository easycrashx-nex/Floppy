using System.Net;
using System.Text.Json;
using Floppy.Core;
using Floppy.Core.Api;
using Floppy.Unrailed2;

internal static class ModuleReliabilityTests
{
    private const string ReadFields = "listEntities?cTypes=46";
    private const string Dump = "B & Ä";
    private static Dictionary<long, Dictionary<string, object>> _entities;
    private static CheatOption Apply => Registry.Find("wert.setzen");
    private static string[] Writes => Debugger.Requests.Where(p => p.StartsWith("changeValue?", StringComparison.Ordinal)).ToArray();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Module reliability: " + message);
    }

    private static void Start()
    {
        Debugger.Reset();
        Schutz.An = true;
        Debugger.Responses["worldInfo"] = World(0, "Menu");
        Welt.Aktualisiere();
        Debugger.Responses["worldInfo"] = World(4);
        Debugger.Responses["componentMap"] = "{\"46\":{\"id\":46,\"name\":\"1234\",\"count\":3}}";
        Debugger.Responses["typeNames"] = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["1234"] = new Dictionary<string, string>
            {
                ["FullName"] = "Fixture.NumericComponent", ["1"] = "Health", ["2"] = "Spare",
                ["3"] = "Flag", ["4"] = "State", ["5"] = "Object", ["6"] = "Array",
                ["7"] = "NaN", ["8"] = "Infinity", ["9"] = "Null", ["10"] = "NumericText"
            }
        });
        _entities = new()
        {
            [42] = new() { ["1"] = 5, ["2"] = 9, ["3"] = true, ["4"] = "Running", ["5"] = new { x = 1 },
                ["6"] = new[] { 1, 2 }, ["7"] = "NaN", ["8"] = "Infinity", ["9"] = null, ["10"] = "123" },
            [43] = new() { ["1"] = 6 },
            [44] = new() { ["2"] = 7 }
        };
        PublishFields();
        Dumps("A", Dump, "C");
        Welt.Aktualisiere();
        var module = new Unrailed2Module();
        Registry.SetModule(module);
        module.Initialize();
        Unrailed2Module.ListenPflegen();
        Select("wert.gruppe", "NumericComponent  (3)");
        Select("wert.feld", "Health");
        Registry.Find("wert.neu").NumberValue = 12;
        Debugger.Requests.Clear();
    }

    private static void Select(string id, string value)
    {
        var option = Registry.Find(id);
        option.ChoiceIndex = Array.IndexOf(option.Choices, value);
        Check(option.ChoiceIndex >= 0, "fixture choice must exist: " + value);
        option.NotifyChanged();
    }

    public static void NumericFields()
    {
        Start();
        Check(Registry.Find("wert.feld").Choices.SequenceEqual(new[] { "(Wert wählen)", "Health", "Spare" }),
            "the numeric editor must exclude booleans, enum/text, numeric strings, objects, arrays and non-finite values");
        AcceptWrites();
        Apply.Fire();
        Check(!Apply.MessageIsError && Writes.Length == 2 && _entities[42]["1"].Equals(12)
            && _entities[43]["1"].Equals(12) && !_entities[44].ContainsKey("1"),
            "bulk writes must target only numeric carriers of the chosen field");

        Start();
        _entities[42]["1"] = "12";
        _entities[43]["1"] = "12";
        PublishFields();
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length == 0 && Registry.Find("wert.feld").ChoiceIndex == 0,
            "a selected field becoming text must invalidate the write instead of coercing it");

        Start();
        Registry.Find("wert.neu").NumberValue = float.NaN;
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length == 0, "a queued malformed number must fail before writing");

        Start();
        AcceptWrites(rejectSecond: true);
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length == 2 && Apply.Message.Contains("1 von 2"),
            "partial confirmation must be an error with the confirmed target count");

        Start();
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length == 1, "bulk transport failure must fail and stop subsequent writes");
    }

    public static void WorldSelection()
    {
        Start();
        Debugger.Responses["worldInfo"] = World(5);
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length == 0 && !Apply.Available,
            "a click from the previous world must not target reused entity IDs");

        Start();
        Debugger.Responses["worldInfo"] = World(5);
        Welt.Aktualisiere();
        Unrailed2Module.ListenPflegen();
        Check(!Apply.Available && Registry.Find("wert.gruppe").ChoiceIndex == 0
            && Registry.Find("wert.feld").ChoiceIndex == 0,
            "polling a new world with identical components must clear both selections");
        Unrailed2Module.ListenPflegen();
        Check(!Apply.Available, "polling must not silently restore a selection from an old world");
        Select("wert.gruppe", "NumericComponent  (3)");
        Select("wert.feld", "Health");
        Check(Apply.Available, "an explicit fresh selection may restore editing");

        Start();
        Debugger.OnRequest = path => { if (path == ReadFields) Debugger.Responses["worldInfo"] = World(5); };
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length == 0,
            "a world change during the fresh target read must cancel the operation");

        Start();
        AcceptWrites();
        var accept = Debugger.OnRequest;
        Debugger.OnRequest = path =>
        {
            accept(path);
            if (path.StartsWith("changeValue?", StringComparison.Ordinal)) Debugger.Responses["worldInfo"] = World(5);
        };
        Apply.Fire();
        Check(Apply.MessageIsError && Writes.Length > 0 && !Apply.Available,
            "matching readback in a new world must not be reported as a confirmed old-world operation");
    }

    public static void DumpSelection()
    {
        Start();
        var choice = Registry.Find("zust.welcher");
        Check(choice.Choices[0] == "-" && choice.ChoiceIndex == 0 && !Registry.Find("zust.loeschen").Available,
            "available files must not implicitly select a load/delete target");
        Select("zust.welcher", Dump);
        Dumps(Dump, "A", "C");
        Unrailed2Module.ListenPflegen();
        Check(choice.Choices[choice.ChoiceIndex] == Dump, "reordering files must preserve selection by exact name");
        var remove = Registry.Find("zust.loeschen");
        string expected = "deleteDump?name=" + Debugger.Verpacke(Dump);
        Debugger.Responses[expected] = "";
        remove.Fire();
        Check(!remove.MessageIsError && Debugger.Requests.Count(p => p.StartsWith("deleteDump?", StringComparison.Ordinal)) == 1
            && Debugger.Requests.Contains(expected), "deletion must target only the selected, URL-encoded filename");

        Start();
        Select("zust.welcher", Dump);
        Dumps("A", "C");
        Unrailed2Module.ListenPflegen();
        var load = Registry.Find("zust.laden");
        remove = Registry.Find("zust.loeschen");
        Check(Registry.Find("zust.welcher").ChoiceIndex == 0 && !load.Available && !remove.Available,
            "a vanished file must clear selection without selecting another file");
        load.Fire();
        remove.Fire();
        Check(load.MessageIsError && remove.MessageIsError && !Debugger.Requests.Any(IsDumpMutation),
            "queued actions after selection loss must fail without opening or deleting a replacement");

        Start();
        Select("zust.welcher", Dump);
        Dumps("A", "C"); // No intervening UI poll.
        load = Registry.Find("zust.laden");
        load.Fire();
        Check(load.MessageIsError && !Debugger.Requests.Any(IsDumpMutation),
            "the chosen name must be checked again immediately before the action");
    }

    public static void ActionFailures()
    {
        var actions = new (string Id, string Endpoint)[]
        {
            ("zust.sichern", "createDump?name="), ("zust.laden", "openDump?name=" + Debugger.Verpacke(Dump)),
            ("zust.loeschen", "deleteDump?name=" + Debugger.Verpacke(Dump)),
            ("fort.freischalten", "unlockAll"), ("fort.tutorialfertig", "finishTutorials"),
            ("fort.tutorialzurueck", "clearTutorial"), ("fort.loeschen", "clearProgress")
        };
        foreach (var (id, endpoint) in actions)
        {
            Start();
            Select("zust.welcher", Dump);
            Registry.Find("fort.riegel").BoolValue = true;
            var action = Registry.Find(id);
            Check(action.Available, id + ": the valid fixture must permit the action");
            action.Fire();
            Check(action.MessageIsError && Debugger.Requests.Count(p => p == endpoint) == 1,
                id + ": transport failure must propagate through Fail without retries");

            Start();
            Select("zust.welcher", Dump);
            Registry.Find("fort.riegel").BoolValue = true;
            action = Registry.Find(id);
            Debugger.Responses[endpoint] = "";
            action.Fire();
            Check(!action.MessageIsError && Debugger.Requests.Count(p => p == endpoint) == 1,
                id + ": an acknowledged request must preserve the successful action path");

            Debugger.Reset();
            Registry.Find("fort.riegel").BoolValue = true;
            Check(!action.Available, id + ": disconnected actions must be unavailable");
            action.Fire();
            Check(action.MessageIsError && !Debugger.Requests.Contains(endpoint),
                id + ": a queued disconnected action must fail without sending a request");
        }
    }

    private static void AcceptWrites(bool rejectSecond = false)
    {
        Debugger.OnRequest = path =>
        {
            if (!path.StartsWith("changeValue?", StringComparison.Ordinal)) return;
            string command = WebUtility.UrlDecode(path.Substring("changeValue?command=".Length));
            long eid = long.Parse(command.Split('.')[0]);
            Check((eid == 42 || eid == 43) && command == eid + ".1234.1=12", "unexpected numeric write target: " + command);
            Debugger.Responses[path] = "";
            if (!rejectSecond || eid != 43) _entities[eid]["1"] = 12;
            PublishFields();
        };
    }

    private static bool IsDumpMutation(string path) => path.StartsWith("openDump?", StringComparison.Ordinal)
        || path.StartsWith("deleteDump?", StringComparison.Ordinal);
    private static string World(int id, string mode = "Story") => JsonSerializer.Serialize(new { id, config = new object[] { 123, mode } });
    private static void Dumps(params string[] names) => Debugger.Responses["listDumps"] = JsonSerializer.Serialize(new
        { files = names.Select(name => new { name }).ToArray() });
    private static void PublishFields() => Debugger.Responses[ReadFields] = JsonSerializer.Serialize(new
    {
        entities = _entities.Select(e => new { eid = e.Key, components = new[] { new { id = 46,
            fields = e.Value.Select(f => new { name = f.Key, value = f.Value }).ToArray() } } }).ToArray()
    });
}
