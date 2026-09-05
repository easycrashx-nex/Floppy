using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
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
}
