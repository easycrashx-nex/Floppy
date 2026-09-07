using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Floppy.App;

internal static class Program
{
    private static int _count;
    private static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); _count++; Console.WriteLine("PASS " + message); }

    private static async Task<int> Main()
    {
        try
        {
            foreach (string text in new[] { "1,5", "1.5", " 1,5 " })
                Check(NumberInput.TryParse(text, out double n) && n == 1.5, "decimal " + text);
            foreach (string text in new[] { "1,234.5", "1.234,5", "NaN", "Infinity", "1e999", "" })
                Check(!NumberInput.TryParse(text, out _), "reject " + text);
            string folder = Path.Combine(Path.GetTempPath(), "floppy-app-tests-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(folder, "settings.json");
            var settings = new UserSettings { Width = 1320, LastGame = "Testspiel" };
            settings.Favorites["Testspiel"] = new List<string> { "player.god" };
            settings.Shortcuts["Testspiel"] = new Dictionary<string, string> { ["player.god"] = "Ctrl+G" };
            Check(settings.Save(out _, path), "settings atomic save");
            var loaded = UserSettings.Load(path);
            Check(loaded.Width == 1320 && loaded.LastGame == "Testspiel" && loaded.Favorites["Testspiel"][0] == "player.god" && loaded.Shortcuts["Testspiel"]["player.god"] == "Ctrl+G", "settings roundtrip");
            File.WriteAllText(path, "broken");
            Check(UserSettings.Load(path).Favorites.Count == 0, "broken settings fallback");
            await TimeoutTest();
            await SessionTest();
            await OverlayQueueTest();
            ItemCatalogTests();
            ItemMetadataTests();
            Console.WriteLine($"{_count} checks passed");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static async Task TimeoutTest()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var client = new IpcClient(((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromMilliseconds(120));
        var accepted = server.AcceptTcpClientAsync();
        Check(await client.ConnectAsync(), "client connects to fixture");
        using var peer = await accepted;
        var reply = await client.GetStateAsync().WaitAsync(TimeSpan.FromSeconds(3));
        Check(reply == null && !client.Connected && client.LastError.Contains("Antwortzeit"), "silent peer times out and disconnects");
    }

    private static async Task SessionTest()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var client = new IpcClient(((IPEndPoint)server.LocalEndpoint).Port);
        var accepted = server.AcceptTcpClientAsync();
        Check(await client.ConnectAsync(), "first session connects");
        using var peer = await accepted;
        using var reader = new StreamReader(peer.GetStream(), Encoding.UTF8);
        var first = client.GetStateAsync();
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var queued = client.SetBoolAsync("old.command", true);
        client.Disconnect();
        var nextPeer = server.AcceptTcpClientAsync();
        Check(await client.ConnectAsync(), "new session connects");
        using var next = await nextPeer;
        Check(await first == null && await queued == null, "old pending commands are not replayed");
        var response = Task.Run(async () =>
        {
            using var input = new StreamReader(next.GetStream(), Encoding.UTF8);
            using var output = new StreamWriter(next.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
            string? line = await input.ReadLineAsync();
            Check(JsonNode.Parse(line! )!["cmd"]!.GetValue<string>() == "schema", "new session receives only its own command");
            await output.WriteLineAsync("{\"ok\":true}");
        });
        Check((await client.GetSchemaAsync())?["ok"]?.GetValue<bool>() == true, "new session response is usable");
        await response;
    }

    private static async Task OverlayQueueTest()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var client = new IpcClient(((IPEndPoint)server.LocalEndpoint).Port);
        var accepted = server.AcceptTcpClientAsync();
        Check(await client.ConnectAsync(), "overlay queue fixture connects");
        using var peer = await accepted;
        using var reader = new StreamReader(peer.GetStream(), Encoding.UTF8);
        using var writer = new StreamWriter(peer.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        const string session = "fixture-session-ü-\"quoted\"";

        foreach (bool cancelInsteadOfClose in new[] { false, true })
        {
            string reason = cancelInsteadOfClose ? "cancel" : "close";
            var state = client.GetStateAsync();
            string? stateLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            Check(JsonNode.Parse(stateLine!)?["cmd"]?.GetValue<string>() == "state", reason + " waits behind outstanding state request");

            int epoch = 1;
            int requestEpoch = epoch;
            int guardCalls = 0;
            using var cancelled = new CancellationTokenSource();
            var queuedOpen = client.SetOverlayAsync(true, session, () =>
            {
                Interlocked.Increment(ref guardCalls);
                return !cancelled.IsCancellationRequested && Volatile.Read(ref epoch) == requestEpoch;
            });
            Check(guardCalls == 0 && !queuedOpen.IsCompleted, reason + " open guard waits for IPC semaphore");

            if (cancelInsteadOfClose) cancelled.Cancel();
            else Interlocked.Increment(ref epoch);
            var close = client.SetOverlayAsync(false, session);
            await writer.WriteLineAsync("{\"ok\":true}");
            Check((await state.WaitAsync(TimeSpan.FromSeconds(2)))?["ok"]?.GetValue<bool>() == true, reason + " original request completes");
            Check(await queuedOpen.WaitAsync(TimeSpan.FromSeconds(2)) == null && guardCalls == 1, reason + " rejects obsolete open after semaphore wait");

            string? nextLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
            var sent = JsonNode.Parse(nextLine!);
            Check(sent?["cmd"]?.GetValue<string>() == "overlay" && sent?["open"]?.GetValue<bool>() == false,
                reason + " sends only close, never the cancelled queued open");
            Check(sent?["sessionId"]?.GetValue<string>() == session, reason + " close preserves session identity in JSON");
            await writer.WriteLineAsync("{\"ok\":true,\"overlayOpen\":false}");
            Check((await close.WaitAsync(TimeSpan.FromSeconds(2)))?["ok"]?.GetValue<bool>() == true && client.Connected && client.LastError == "",
                reason + " close succeeds without disconnecting healthy IPC");
        }

        var validOpen = client.SetOverlayAsync(true, session, () => true);
        string? validLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(2));
        var valid = JsonNode.Parse(validLine!);
        Check(valid?["cmd"]?.GetValue<string>() == "overlay" && valid?["open"]?.GetValue<bool>() == true && valid?["sessionId"]?.GetValue<string>() == session,
            "current overlay open sends boolean and exact session identity");
        await writer.WriteLineAsync("{\"ok\":true,\"overlayOpen\":true}");
        Check((await validOpen.WaitAsync(TimeSpan.FromSeconds(2)))?["overlayOpen"]?.GetValue<bool>() == true, "current overlay open receives acknowledgement");
    }

    private static void ItemCatalogTests()
    {
        string emptyAssets = Path.Combine(Path.GetTempPath(), "floppy-no-item-assets-" + Guid.NewGuid().ToString("N"));
        string[] choices = { "ThoraciumPrime", "Coin", "MushroomVillageKey", "MapLocationHeavyHammer", "UnknownID7", "Coin", "" };
        var items = ItemCatalog.FromChoices(choices, emptyAssets);
        Check(items.Count == choices.Length && items.Select(e => e.Id).SequenceEqual(choices)
            && items.Select(e => e.Index).SequenceEqual(Enumerable.Range(0, choices.Length)), "catalog preserves every raw ID and source index, including duplicates");
        Check(items[0].Name == "Thoracium Prime" && items[3].Name == "Map Location Heavy Hammer"
            && items[4].Name == "Unknown ID 7", "catalog humanizes words and numbers without translating or rewriting IDs");
        Check(items[0].Category == "Materialien" && items[1].Category == "Währungen" && items[2].Category == "Schlüssel & Quests"
            && items[3].Category == "Karten & Freischaltungen" && items[4].Category == "Sonstiges", "catalog groups known IDs conservatively and keeps unknowns");
        var reordered = items.OrderByDescending(item => item.Name).ToArray();
        var filtered = ItemCatalog.Filter(reordered, ItemCatalog.AllCategories, "cOiN");
        Check(filtered.Count == 2 && filtered.Select(e => e.Index).SequenceEqual(new[] { 1, 5 })
            && filtered.All(e => choices[e.Index] == e.Id), "sorting and filtering preserve backend selection identity");
        Check(ItemCatalog.Filter(items, "materialien", "PRIME").Single().Id == "ThoraciumPrime", "catalog category and query match case-insensitively");
        Check(ItemCatalog.Filter(items, ItemCatalog.AllCategories, "schlüssel").Single().Index == 2, "catalog query searches category as well as ID and name");
        Check(ItemCatalog.Filter(items, ItemCatalog.AllCategories, "heavy hammer").Single().Index == 3, "catalog query searches humanized names");
        Check(ItemCatalog.Filter(items, "Artefakte", "").Count == 0, "category filtering never returns unrelated items");
        var definitions = ItemCatalog.FromChoices(new[] { "HealingAmount", "Bloodseed", "FoundryStone", "MuradeanActuator", "VartkoFeetPic1" }, emptyAssets);
        Check(definitions.Take(2).All(item => item.Category == "Verbesserungen")
            && definitions.Skip(2).Take(2).All(item => item.Category == "Karten & Freischaltungen")
            && definitions[4].Category == "Sammlerstücke", "observed progression, forge unlock and sketch definitions have distinct categories");
        Check(ItemCatalog.FromChoices(Array.Empty<string>(), emptyAssets).Count == 0 && ItemCatalog.FromChoices(null, emptyAssets).Count == 0
            && items[6].Id == "" && items[6].Index == 6 && items[6].IconPath == null, "empty input and empty IDs remain safe without shifting choice indices");
    }

    private static void ItemMetadataTests()
    {
        string folder = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "floppy-item-metadata-" + Guid.NewGuid().ToString("N")));
        string assets = Path.Combine(folder, "Assets", "MortalShell2");
        string icons = Path.Combine(assets, "icons");
        Directory.CreateDirectory(icons);
        try
        {
            string png = Path.Combine(icons, "Coin.png");
            File.WriteAllBytes(png, new byte[] { 137, 80, 78, 71 });
            string metadataPath = Path.Combine(assets, "items.json");
            File.WriteAllText(metadataPath, """
                {
                  "Coin": { "name": " Münze aus Metadaten ", "category": "Kataloggruppe", "icon": "icons/Coin.png" },
                  "ThoraciumPrime": { "name": " ", "category": "", "icon": "Coin.png" },
                  "Unsafe": { "name": 7, "category": false, "icon": "../Coin.png" },
                  "Url": { "icon": "https://example.invalid/Coin.png" },
                  "Nested": { "icon": "icons/../Coin.png" },
                  "Drive": { "icon": "C:\\elsewhere\\Coin.png" },
                  "Missing": { "icon": "icons/missing.png" },
                  "OtherType": { "icon": "icons/Coin.svg" },
                  "BadRecord": false
                }
                """);
            var items = ItemCatalog.FromChoices(new[] { "Coin", "ThoraciumPrime", "Unsafe", "Url", "Nested", "Drive", "Missing", "OtherType", "BadRecord" }, folder);
            Check(items[0].Name == "Münze aus Metadaten" && items[0].Category == "Kataloggruppe" && items[0].Index == 0 && items[0].Id == "Coin",
                "bundled display metadata takes precedence without changing ID or index");
            Check(items[0].IconPath == png && items[1].IconPath == png && Path.IsPathFullyQualified(items[0].IconPath!), "catalog resolves bundled PNG names only to absolute local icon paths");
            Check(items[1].Name == "Thoracium Prime" && items[1].Category == "Materialien" && items[2].Name == "Unsafe" && items[2].Category == "Sonstiges",
                "missing, blank and mistyped metadata fields fall back independently");
            Check(items.Skip(2).All(e => e.IconPath == null), "catalog rejects URL, drive, traversal, absent and unsupported icon paths");
            File.WriteAllText(metadataPath, "broken json");
            Check(ItemCatalog.FromChoices(new[] { "Coin" }, folder).Single().Name == "Coin", "corrupt optional metadata cannot hide game items");
            File.WriteAllText(metadataPath, "[]");
            Check(ItemCatalog.FromChoices(new[] { "Coin" }, folder).Single().Category == "Währungen", "unexpected metadata root retains safe fallback");
        }
        finally
        {
            string expectedRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!folder.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(folder).StartsWith("floppy-item-metadata-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test fixture path");
            Directory.Delete(folder, true);
        }
    }
}
