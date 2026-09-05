using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Floppy.Core.Api;

namespace Floppy.Core
{
    /// <summary>Kleiner Server auf 127.0.0.1, über den die externe App dieselben Cheats
    /// bedient wie das Overlay. Zeilenweise JSON, nur lokale Verbindungen.</summary>
    public static class IpcServer
    {
        public const int Port = 47821;

        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static volatile bool _running;
        private static readonly object _lifecycle = new object();
        private static readonly HashSet<TcpClient> _clients = new HashSet<TcpClient>();
        private static int _generation;
        private static string _sessionId = "";
        public static int ConnectedClients { get { lock (_lifecycle) return _clients.Count; } }
        public static int ListeningPort { get; private set; }

        public static void Start(int port = Port)
        {
            lock (_lifecycle)
            {
                if (_running) return;
                var listener = new TcpListener(IPAddress.Loopback, port);
                try { listener.Start(); }
                catch (SocketException ex)
                {
                    listener.Stop();
                    Log.Error("IPC-Port " + port + " nicht verfügbar (" + ex.SocketErrorCode + "). " +
                              "Läuft das Spiel doppelt? Die externe App bleibt ohne Verbindung.");
                    return;
                }
                _listener = listener;
                ListeningPort = ((IPEndPoint)listener.LocalEndpoint).Port;
                _sessionId = Guid.NewGuid().ToString("N");
                int generation = ++_generation;
                _running = true;
                _acceptThread = new Thread(() => AcceptLoop(listener, generation)) { IsBackground = true, Name = "Floppy-IPC" };
                _acceptThread.Start();
                Log.Info("IPC-Server läuft auf 127.0.0.1:" + ListeningPort);
            }
        }

        public static void Stop()
        {
            TcpListener listener;
            TcpClient[] clients;
            lock (_lifecycle)
            {
                _running = false;
                ++_generation;
                listener = _listener;
                _listener = null;
                ListeningPort = 0;
                clients = new TcpClient[_clients.Count];
                _clients.CopyTo(clients);
                _clients.Clear();
            }
            try { listener?.Stop(); } catch { }
            foreach (var client in clients) try { client.Dispose(); } catch { }
        }

        private static bool Current(int generation) => _running && generation == Volatile.Read(ref _generation);

        private static void AcceptLoop(TcpListener listener, int generation)
        {
            while (Current(generation))
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { return; }
                lock (_lifecycle)
                {
                    if (!Current(generation)) { client.Dispose(); return; }
                    _clients.Add(client);
                }
                var thread = new Thread(() => HandleClient(client, generation)) { IsBackground = true };
                thread.Start();
            }
        }

        private static void HandleClient(TcpClient client, int generation)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
                {
                    string line;
                    while (Current(generation) && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        string response;
                        try { response = Handle(line, generation); }
                        catch (Exception ex) { response = Error(ex.Message); }
                        writer.WriteLine(response);
                    }
                }
            }
            catch (IOException) { /* Verbindung weg - normal */ }
            catch (Exception ex) { Log.Warning("IPC-Client abgebrochen: " + ex.Message); }
            finally { lock (_lifecycle) _clients.Remove(client); }
        }

        private static string Handle(string json, int generation)
        {
            var request = Json.Parse(json);
            string cmd = Json.AlsText(request, "cmd") ?? "";

            switch (cmd.ToLowerInvariant())
            {
                case "ping":
                    return new Json.Writer().Set("ok", true).Set("pong", true).ToString();

                case "schema":
                    return Dispatch(BuildSchema, generation);

                case "state":
                    return Dispatch(BuildState, generation);

                case "set":
                    return Dispatch(() => ApplySet(request), generation);

                case "invoke":
                    return Dispatch(() => ApplyInvoke(Json.AlsText(request, "id")), generation);

                default:
                    return Error("Unbekannter Befehl: " + cmd);
            }
        }

        private static string Dispatch(Func<string> action, int generation)
        {
            return Dispatcher.Run(() =>
            {
                if (!Current(generation)) throw new InvalidOperationException("Verbindung gehört zu einer beendeten Sitzung");
                return action();
            });
        }

        // --- Alles ab hier läuft im Mainthread des Spiels ---

        private static string BuildSchema()
        {
            var kategorien = new List<string>();

            foreach (var category in Registry.Categories)
            {
                var optionen = new List<string>();

                foreach (var option in category.Options)
                {
                    SafeAvailable(option); // dynamische Listen vor ihrer Beschreibung aktualisieren
                    var description = new Json.Writer()
                        .Set("id", option.Id)
                        .Set("label", option.Label)
                        .Set("description", option.Description ?? "")
                        .Set("kind", option.Kind.ToString())
                        .Set("scope", option.Scope.ToString())
                        .Set("min", option.Min)
                        .Set("max", option.Max)
                        .Set("step", option.Step)
                        .Raw("choices", Json.TextArray(option.Choices ?? new string[0]));
                    if (option.Kind == OptionKind.Toggle) description.Set("resetBool", option.Ruhebool);
                    if (option.Ruhewert.HasValue) description.Set("resetNumber", option.Ruhewert.Value);
                    if (option.Ruheauswahl.HasValue) description.Set("resetChoice", option.Ruheauswahl.Value);
                    optionen.Add(description.ToString());
                }

                kategorien.Add(new Json.Writer()
                    .Set("name", category.Name)
                    .Raw("options", Json.Array(optionen))
                    .ToString());
            }

            var w = new Json.Writer().Set("ok", true).Set("game", Registry.GameName);
            w.Raw("categories", Json.Array(kategorien));
            WriteStatus(w);
            w.Raw("values", ValueMap());
            return w.ToString();
        }

        private static string BuildState()
        {
            var w = new Json.Writer().Set("ok", true);
            WriteStatus(w);
            w.Raw("values", ValueMap());
            return w.ToString();
        }

        private static string ValueMap()
        {
            var w = new Json.Writer();

            foreach (var option in Registry.AllOptions)
            {
                bool available = SafeAvailable(option);
                var value = new Json.Writer()
                    .Set("bool", option.BoolValue)
                    .Set("number", option.NumberValue)
                    .Set("text", option.TextValue ?? "")
                    .Set("choice", option.ChoiceIndex)

                    // Damit die App merkt, wenn sich eine Auswahlliste geändert hat -
                    // etwa weil eine andere Spawn-Rubrik gewählt wurde.
                    .Set("choiceCount", option.Choices == null ? 0 : option.Choices.Length)

                    .Set("share", option.ShareWithOthers)
                    .Set("active", option.Active)
                    .Set("available", available);
                if (option.Kind == OptionKind.Choice)
                    value.Raw("choices", Json.TextArray(option.Choices ?? Array.Empty<string>()));
                w.Raw(option.Id, value.ToString());
            }

            return w.ToString();
        }

        private static bool SafeAvailable(CheatOption option)
        {
            try { return option.Available; }
            catch { return false; }
        }

        private static void WriteStatus(Json.Writer w)
        {
            string status;
            bool ready = Registry.Ready(out status);
            w.Set("ready", ready).Set("status", status)
                .Set("gameId", Registry.Module?.ProductName ?? "")
                .Set("sessionId", _sessionId).Set("schemaVersion", Registry.SchemaVersion);
        }

        private static string ApplySet(Dictionary<string, object> request)
        {
            string id = Json.AlsText(request, "id");
            var option = Registry.Find(id);
            if (option == null) return Error("Diesen Cheat gibt es nicht: " + id);
            if (!SafeAvailable(option)) return Error(option.Label + " ist gerade nicht verfügbar");

            OptionValues.Read(option, request).Apply(option);
            var response = new Json.Writer().Set("ok", true);
            string message = option.TakeMessage();
            if (!string.IsNullOrEmpty(message)) response.Set("message", message);
            return response.Raw("values", ValueMap()).ToString();
        }

        private static string ApplyInvoke(string id)
        {
            var option = Registry.Find(id);
            if (option == null) return Error("Diesen Cheat gibt es nicht: " + id);
            if (!SafeAvailable(option)) return Error(option.Label + " ist gerade nicht verfügbar");
            if (option.Kind != OptionKind.Button && option.Kind != OptionKind.Text)
                return Error("Diese Option kann nicht ausgeführt werden: " + option.Label);

            option.Fire();

            var w = new Json.Writer().Set("ok", true);

            string message = option.TakeMessage();
            if (option.MessageIsError) return Error(message);
            if (!string.IsNullOrEmpty(message)) w.Set("message", message);

            w.Raw("values", ValueMap());
            return w.ToString();
        }

        private static string Error(string message)
        {
            return new Json.Writer().Set("ok", false).Set("error", message).ToString();
        }
    }
}
