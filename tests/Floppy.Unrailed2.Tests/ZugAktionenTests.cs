using System.Text.Json;
using Floppy.Core.Api;
using Floppy.Unrailed2;

internal static class ZugAktionenTests
{
    public static void Run()
    {
        var f = new Fixture();
        Check(f.Stop.Available && !f.Resume.Available && f.Cool.Available, "one running train enables stop and cooling only");
        int before = Debugger.Requests.Count;
        for (int i = 0; i < 20; i++)
            foreach (var option in f.Options)
            {
                _ = option.Available;
                if (option.Kind == OptionKind.Info) option.NotifyChanged();
            }
        Check(Debugger.Requests.Count == before, "availability and information must not read HTTP");
        f.Stop.Fire();
        Check(!f.Stop.MessageIsError && f.Trains[42] == "Pause" && f.Resume.Available,
            "a confirmed own stop enables resume");
        f.Resume.Fire();
        Check(!f.Resume.MessageIsError && f.Trains[42] == "Running" && !f.Resume.Available,
            "resume must restore the saved running state and consume ownership");
        Check(f.Commands.SequenceEqual(new[] { "42.919923075.-762103952=\"Pause\"", "42.919923075.-762103952=\"Running\"" }),
            "state writes must use exact type/field hashes and quoted enum strings");

        f = new Fixture();
        f.Trains[42] = "Pause";
        f.Refresh();
        f.Resume.Fire();
        Check(f.Resume.MessageIsError && f.Commands.Count == 0, "an already paused train must never be claimed as our own stop");

        f = new Fixture();
        f.Stop.Fire();
        f.World++;
        f.Trains[42] = "Pause";
        f.Refresh();
        int sent = f.Commands.Count;
        f.Resume.Fire();
        Check(!f.Resume.Available && f.Resume.MessageIsError && f.Commands.Count == sent,
            "a new world with the same entity ID must invalidate the saved stop");

        f = new Fixture();
        f.Stop.Fire();
        f.Trains.Clear();
        f.Trains[43] = "Pause";
        f.Refresh();
        sent = f.Commands.Count;
        f.Resume.Fire();
        Check(f.Resume.MessageIsError && f.Commands.Count == sent, "another train in the same world cannot inherit stop ownership");

        f = new Fixture();
        f.Stop.Fire();
        f.Trains[42] = "Running";
        f.Refresh();
        f.Trains[42] = "Pause";
        f.Refresh();
        sent = f.Commands.Count;
        f.Resume.Fire();
        Check(f.Resume.MessageIsError && f.Commands.Count == sent, "an external resume and later pause must not revive ownership");

        f = new Fixture { ChangeWorldOnWrite = true };
        f.Stop.Fire();
        Check(f.Stop.MessageIsError && !f.Resume.Available && f.Commands.Count == 1,
            "a matching readback from a replacement world reusing the entity ID cannot confirm our stop");

        f = new Fixture();
        f.World++;
        f.Stop.Fire();
        Check(f.Stop.MessageIsError && f.Commands.Count == 0,
            "an old UI click must not stop a new world reusing the same train entity ID");
        f = new Fixture();
        f.Trains.Clear();
        f.Trains[43] = "Running";
        f.Stop.Fire();
        Check(f.Stop.MessageIsError && f.Commands.Count == 0,
            "an old UI click must not silently choose a replacement train in the same world");

        f = new Fixture();
        f.Trains[43] = "Running";
        f.Refresh();
        f.Stop.Fire();
        f.Cool.Fire();
        Check(!f.Stop.Available && !f.Cool.Available && f.Commands.Count == 0 &&
            f.Options.Single(o => o.Kind == OptionKind.Info).TapInfo().Contains("Mehrere Züge"),
            "multiple trains must abstain visibly without selecting the first entity");
        f.Trains.Clear();
        f.Refresh();
        f.Stop.Fire();
        Check(!f.Stop.Available && f.Commands.Count == 0, "missing trains must not invent entity zero");

        f = new Fixture { ConfirmWrites = false };
        f.Stop.Fire();
        Check(f.Stop.MessageIsError && f.Stop.Message.Contains("unklar") && !f.Resume.Available && f.Commands.Count == 1,
            "an unconfirmed write must report uncertainty, avoid repeated writes and not acquire resume ownership");

        f = new Fixture();
        f.Cool.Fire();
        Check(!f.Cool.MessageIsError && f.Wagons[101].Heat == 0 && f.Wagons[102].Heat == 0 &&
            f.Wagons[103].Heat == 800 && f.Wagons[104].Heat == 700 &&
            f.Commands.SequenceEqual(new[] { "101.1418987843.806557303=0" }),
            "cooling must write only hot wagons attached to the selected train, not cold, other-train or detached wagons");
        f = new Fixture { ConfirmWrites = false };
        f.Cool.Fire();
        Check(f.Cool.MessageIsError && f.Cool.Message.Contains("unklar"), "unconfirmed cooling cannot report success");
        f = new Fixture { ChangeWorldOnWagonsRead = true };
        f.Cool.Fire();
        Check(f.Cool.MessageIsError && f.Commands.Count == 0,
            "a world change while reading wagon targets must abort before the first cooling write");

        f = new Fixture();
        f.Mode = "Ranked";
        f.Refresh();
        f.Stop.Fire();
        f.Cool.Fire();
        Check(f.Commands.Count == 0 && f.Stop.MessageIsError && f.Cool.MessageIsError,
            "fresh world state must preserve the existing ranked-run protection");
        ZugAktionen.Vergiss();
    }

    private static string TapInfo(this CheatOption option) { option.NotifyChanged(); return option.TextValue; }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private sealed class Fixture
    {
        public int World = 700;
        public string Mode = "Story";
        public bool ConfirmWrites = true;
        public bool ChangeWorldOnWrite;
        public bool ChangeWorldOnWagonsRead;
        public readonly Dictionary<long, string> Trains = new() { [42] = "Running" };
        public readonly Dictionary<long, (long Attached, double Heat)> Wagons = new()
        {
            [101] = (42, 900), [102] = (42, 0), [103] = (99, 800), [104] = (-1, 700)
        };
        public readonly List<string> Commands = new();
        public CheatOption[] Options { get; }
        public CheatOption Stop => Options.Single(o => o.Id == "zug.anhalten");
        public CheatOption Resume => Options.Single(o => o.Id == "zug.weiterfahren");
        public CheatOption Cool => Options.Single(o => o.Id == "zug.abkuehlen");

        public Fixture()
        {
            Debugger.Reset();
            Debugger.Responses["worldInfo"] = "{\"id\":0,\"config\":[0,\"Menu\"]}";
            Debugger.Responses["typeNames"] = "{}";
            Welt.Aktualisiere();
            ZugAktionen.Vergiss();
            Schutz.An = true;
            Debugger.OnRequest = Request;
            Refresh();
            Options = ZugAktionen.Kategorie().Options.ToArray();
        }

        public void Refresh()
        {
            Welt.Aktualisiere();
            Welt.Arten(erzwingen: true);
            ZugAktionen.Aktualisiere();
        }

        private void Request(string path)
        {
            if (path == "worldInfo")
                Debugger.Responses[path] = JsonSerializer.Serialize(new { id = World, config = new object[] { 0, Mode } });
            else if (path == "componentMap")
                Debugger.Responses[path] = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["13"] = new { id = 13, name = "919923075", count = Trains.Count },
                    ["15"] = new { id = 15, name = "1418987843", count = Wagons.Count }
                });
            else if (path == "listEntities?cTypes=13")
                Debugger.Responses[path] = JsonSerializer.Serialize(new { entities = Trains.Select(t => new
                {
                    eid = t.Key, components = new[] { new { id = 13, fields = new[] { new { name = "-762103952", value = t.Value } } } }
                }) });
            else if (path == "listEntities?cTypes=15")
            {
                if (ChangeWorldOnWagonsRead) World++;
                Debugger.Responses[path] = JsonSerializer.Serialize(new { entities = Wagons.Select(w => new
                {
                    eid = w.Key, components = new[] { new { id = 15, fields = new object[]
                    {
                        new { name = "-2134332611", value = w.Value.Attached.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        new { name = "806557303", value = w.Value.Heat }
                    } } }
                }) });
            }
            else if (path.StartsWith("changeValue?command=", StringComparison.Ordinal))
            {
                Debugger.Responses[path] = "";
                string command = System.Net.WebUtility.UrlDecode(path.Substring("changeValue?command=".Length));
                Commands.Add(command);
                if (ChangeWorldOnWrite) World++;
                if (!ConfirmWrites) return;
                var parts = command.Split('=', 2);
                var key = parts[0].Split('.');
                long eid = long.Parse(key[0]);
                if (key[1] == "919923075" && key[2] == "-762103952" && Trains.ContainsKey(eid))
                    Trains[eid] = JsonSerializer.Deserialize<string>(parts[1]);
                else if (key[1] == "1418987843" && key[2] == "806557303" && Wagons.TryGetValue(eid, out var wagon))
                    Wagons[eid] = (wagon.Attached, JsonSerializer.Deserialize<double>(parts[1]));
            }
        }
    }
}
