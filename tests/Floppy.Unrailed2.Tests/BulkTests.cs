using System.Text.Json;
using Floppy.Unrailed2;

internal static class BulkTests
{
    public static void Run()
    {
        const string type = "-486817492", field = "-70839394";
        Debugger.Reset();
        Debugger.Responses["worldInfo"] = "{\"id\":901,\"config\":[0,\"Menu\"]}";
        Welt.Aktualisiere();
        Debugger.Responses["worldInfo"] = "{\"id\":902,\"config\":[0,\"Story\"]}";
        Debugger.Responses["typeNames"] = "{}";
        Debugger.Responses["componentMap"] = "{\"46\":{\"id\":46,\"name\":\"-486817492\",\"count\":101}}";
        var ids = Enumerable.Range(1, 100).Select(i => (long)i).ToArray();
        // One target rejects the value; entity 101 was not targeted and must not count.
        Debugger.Responses["listEntities?cTypes=46"] = JsonSerializer.Serialize(new
        {
            entities = Enumerable.Range(1, 101).Select(i => new
            {
                eid = i,
                components = new[] { new { id = 46, fields = new[] { new { name = field, value = i == 50 ? 0 : 5 } } } }
            })
        });
        Welt.Aktualisiere();
        Welt.Arten();
        Debugger.Requests.Clear();
        Debugger.OnRequest = path =>
        {
            if (path.StartsWith("changeValue?")) Debugger.Responses[path] = "";
        };
        int confirmed = Welt.SetzeZahlMehrere(ids.Concat(new[] { 1L }), type, field, 5);
        if (confirmed != 99 || Debugger.Requests.Count(p => p.StartsWith("listEntities?")) != 1
            || Debugger.Requests.Count(p => p.StartsWith("changeValue?")) != 100)
            throw new Exception("bulk writes must deduplicate targets and verify only them with one shared readback");

        Debugger.Requests.Clear();
        Debugger.OnRequest = path =>
        {
            if (path.StartsWith("changeValue?")) Debugger.Responses.Remove(path);
        };
        if (Welt.SetzeZahlMehrere(ids, type, field, 5) != 0
            || Debugger.Requests.Count(p => p.StartsWith("changeValue?")) != 1)
            throw new Exception("bulk transport failure must stop further writes and cannot report existing matching values as success");
    }
}
