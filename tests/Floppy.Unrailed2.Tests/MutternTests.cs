using System.Text.Json;
using Floppy.Core;
using Floppy.Core.Api;
using Floppy.Unrailed2;

internal static class MutternTests
{
    private const string TeamType = "1077059188";
    private const string TeamId = "1702275985";
    private const string Bolts = "437256907";
    private const string ReadTeams = "listEntities?cTypes=51";
    private sealed record Team(long Eid, int Id, int Stock);
    private static CheatOption Give => Registry.Find("mut.geben");
    private static string[] Writes => Debugger.Requests.Where(p => p.StartsWith("changeValue?", StringComparison.Ordinal)).ToArray();

    public static void Run()
    {
        FreshStock();
        ExplicitTeam();
        InvalidAmounts();
        Confirmation();
        WorldChanges();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Muttern: " + message);
    }

    private static void Start(params Team[] teams)
    {
        Debugger.Reset();
        Muttern.Vergiss();
        Cheats.VergissSnapshot();
        Debugger.Responses["worldInfo"] = World(0, "Menu");
        Welt.Aktualisiere();
        Debugger.Responses["worldInfo"] = World(4);
        Debugger.Responses["componentMap"] = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["51"] = new { id = 51, name = TeamType, count = teams.Length }
        });
        Debugger.Responses["typeNames"] = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [TeamType] = new Dictionary<string, string>
            {
                ["FullName"] = "Fixture.TeamComponent", [TeamId] = "TeamId", [Bolts] = "Bolts"
            }
        });
        Debugger.Responses[ReadTeams] = Entities(teams);
        Welt.Aktualisiere();
        Registry.SetModule(new FixtureModule());
        Muttern.Aktualisiere();
        Registry.Find("mut.menge").NumberValue = 10;
        Debugger.Requests.Clear();
    }

    private static void FreshStock()
    {
        Start(new Team(42, 7, 5));
        Check(!Cheats.Verfuegbar && Give.Available && Registry.Find("mut.team").ChoiceIndex == 1,
            "one real team must be usable automatically without CheatSingleton");
        var live = new[] { new Team(42, 7, 12) };
        Debugger.Responses[ReadTeams] = Entities(live);
        AcceptWrite(live, 42, 22);
        Give.Fire();
        Check(!Give.MessageIsError && Writes.Length == 1 && Registry.Find("mut.jetzt").TextValue == "22",
            "adding ten must use live stock twelve and send one absolute target of twenty-two");
    }

    private static void ExplicitTeam()
    {
        var teams = new[] { new Team(42, 2, 5), new Team(43, 7, 100) };
        Start(teams);
        Check(!Give.Available && Registry.Find("mut.team").ChoiceIndex == 0,
            "multiple teams must require a deliberate team choice");
        Give.Fire();
        Check(Give.MessageIsError && Writes.Length == 0, "an unselected multi-team action must fail without writing");
        var choice = Registry.Find("mut.team");
        choice.ChoiceIndex = 2;
        choice.NotifyChanged();
        AcceptWrite(teams, 43, 110);
        Give.Fire();
        Check(!Give.MessageIsError && Writes.Length == 1,
            "the explicit second team must receive the addition while the first stays untouched");

        Start(new Team(42, 7, 5), new Team(43, 7, 100));
        Give.Fire();
        Check(!Give.Available && Give.MessageIsError && Writes.Length == 0,
            "duplicate team identities must not silently choose a target");
    }

    private static void InvalidAmounts()
    {
        foreach (float amount in new[] { 0f, -1f, 100001f, 1.5f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Start(new Team(42, 7, 5));
            Registry.Find("mut.menge").NumberValue = amount;
            Give.Fire();
            Check(Give.MessageIsError && Writes.Length == 0,
                "invalid amount " + amount + " must fail before any write");
        }
        var nearLimit = new[] { new Team(42, 7, int.MaxValue - 2) };
        Start(nearLimit);
        Give.Fire();
        Check(Give.MessageIsError && Writes.Length == 0,
            "an overflowing sum must be rejected instead of wrapping or silently reducing the addition");
        Start(new Team(42, 7, -1));
        Give.Fire();
        Check(!Give.Available && Give.MessageIsError && Writes.Length == 0,
            "malformed negative stock must not become a writable team");
    }

    private static void Confirmation()
    {
        var teams = new[] { new Team(42, 7, 5) };
        Start(teams);
        AcceptWrite(teams, 42, 15, oldReadbacks: 1);
        Give.Fire();
        Check(!Give.MessageIsError && Writes.Length == 1,
            "delayed readback must be retried without repeating the addition");

        Start(teams);
        AcceptWrite(teams, 42, 15, oldReadbacks: int.MaxValue);
        Give.Fire();
        Check(Give.MessageIsError && Writes.Length == 1
            && (Give.Message.Contains("bestätig", StringComparison.OrdinalIgnoreCase)
                || Give.Message.Contains("unbekannt", StringComparison.OrdinalIgnoreCase)),
            "an unconfirmed write must report uncertainty and never send the addition twice");

        Start(teams);
        Give.Fire(); // The fake has no response for changeValue: transport fails.
        Check(Give.MessageIsError && Writes.Length == 1, "transport failure must reach CheatOption.Fail");
    }

    private static void WorldChanges()
    {
        var teams = new[] { new Team(42, 7, 5) };
        Start(teams);
        Debugger.Responses["worldInfo"] = World(5);
        Give.Fire();
        Check(Give.MessageIsError && Writes.Length == 0, "a stale selection from another Story world must not be written");

        Start(teams);
        Debugger.OnRequest = path =>
        {
            if (path == ReadTeams) Debugger.Responses["worldInfo"] = World(5);
        };
        Give.Fire();
        Check(Give.MessageIsError && Writes.Length == 0,
            "a world change while reading fresh stock must cancel the write");

        Start(teams);
        AcceptWrite(teams, 42, 15);
        var apply = Debugger.OnRequest;
        Debugger.OnRequest = path =>
        {
            apply(path);
            if (path.StartsWith("changeValue?", StringComparison.Ordinal)) Debugger.Responses["worldInfo"] = World(5);
        };
        Give.Fire();
        Check(Give.MessageIsError && Writes.Length == 1,
            "matching stock on a reused entity in a new world must not confirm the old transaction");
    }

    private static void AcceptWrite(Team[] teams, long entity, int target, int oldReadbacks = 0)
    {
        bool sent = false;
        int reads = 0;
        string expected = "changeValue?command=" + Debugger.Verpacke(entity + "." + TeamType + "." + Bolts + "=" + target);
        Debugger.OnRequest = path =>
        {
            if (path.StartsWith("changeValue?", StringComparison.Ordinal))
            {
                Check(path == expected, "unexpected team, field or target in command: " + path);
                Debugger.Responses[path] = "";
                sent = true;
            }
            if (sent && path == ReadTeams && reads++ >= oldReadbacks)
                Debugger.Responses[ReadTeams] = Entities(teams.Select(t => t.Eid == entity ? t with { Stock = target } : t).ToArray());
        };
    }

    private static string World(int id, string mode = "Story") => JsonSerializer.Serialize(new { id, config = new object[] { 123, mode } });
    private static string Entities(Team[] teams) => JsonSerializer.Serialize(new
    {
        entities = teams.Select(team => new { eid = team.Eid, components = new[] { new { id = 51,
            fields = new[] { new { name = TeamId, value = team.Id }, new { name = Bolts, value = team.Stock } }
        } } }).ToArray()
    });

    private sealed class FixtureModule : IGameModule
    {
        public string ProductName => "Unrailed2-Muttern-Fixture";
        public string DisplayName => "Isolated bolt tests";
        public void Initialize() { }
        public void Update() { }
        public void SetMenuOpen(bool open) { }
        public bool IsReady(out string status) { status = "Fixture"; return true; }
        public List<CheatCategory> BuildCategories() => new() { Muttern.Kategorie() };
    }
}
