using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Floppy.App;

/// <summary>Verbindung zum Plugin im laufenden Spiel. Zeilenweise JSON über 127.0.0.1.</summary>
public sealed class IpcClient : IDisposable
{
    public const int Port = 47821;

    private Connection? _connection;
    private int _generation;
    private bool _disposed;
    private readonly int _port;
    private readonly TimeSpan _responseTimeout;

    // Eine Anfrage nach der anderen - das Protokoll ordnet Antworten nicht zu.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Connected => _connection != null;
    public int Generation => Volatile.Read(ref _generation);
    public string LastError { get; private set; } = "";
    public DateTime? LastResponse { get; private set; }

    public IpcClient(int port = Port, TimeSpan? responseTimeout = null)
    {
        _port = port;
        _responseTimeout = responseTimeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<bool> ConnectAsync()
    {
        Disconnect();
        int generation = _generation;
        await _gate.WaitAsync();
        TcpClient? client = null;
        try
        {
            if (_disposed || generation != _generation) return false;
            client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
            await client.ConnectAsync("127.0.0.1", _port, timeout.Token);
            if (_disposed || generation != _generation) return false;
            _connection = new Connection(client);
            client = null;
            LastError = "";
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex is OperationCanceledException ? "Verbindungsaufbau hat zu lange gedauert" : ex.Message;
            return false;
        }
        finally { client?.Dispose(); _gate.Release(); }
    }

    public void Disconnect()
    {
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _connection, null)?.Dispose();
    }

    /// <summary>Schickt eine Anfrage und gibt die Antwort zurück. Bei Verbindungsverlust null.</summary>
    public async Task<JsonObject?> SendAsync(JsonObject request, Func<bool>? stillCurrent = null)
    {
        var connection = _connection;
        if (connection == null) return null;

        await _gate.WaitAsync();
        try
        {
            // Eine vor einem Spielwechsel eingereihte Anfrage gehört niemals zur neuen Verbindung.
            if (!ReferenceEquals(connection, _connection) || stillCurrent?.Invoke() == false) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Stopped.Token);
            timeout.CancelAfter(_responseTimeout);
            await connection.Writer.WriteLineAsync(request.ToJsonString().AsMemory(), timeout.Token);
            string? line = await connection.Reader.ReadLineAsync(timeout.Token);
            if (line == null)
                throw new IOException("Das Spiel hat die Verbindung geschlossen");
            var response = JsonNode.Parse(line) as JsonObject
                ?? throw new JsonException("Ungültige Antwort vom Spiel");
            if (!ReferenceEquals(connection, _connection)) return null;
            LastResponse = DateTime.Now;
            return response;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(connection, _connection))
            {
                LastError = ex is OperationCanceledException
                    ? "Antwortzeit überschritten. Der Ausgang eines gestarteten Befehls ist unklar; bitte den Spielzustand prüfen."
                    : ex.Message;
                Disconnect();
            }
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<JsonObject?> GetSchemaAsync() => SendAsync(new JsonObject { ["cmd"] = "schema" });

    public Task<JsonObject?> GetStateAsync() => SendAsync(new JsonObject { ["cmd"] = "state" });

    public Task<JsonObject?> SetOverlayAsync(bool open, string sessionId, Func<bool>? stillCurrent = null) =>
        SendAsync(new JsonObject { ["cmd"] = "overlay", ["open"] = open, ["sessionId"] = sessionId }, stillCurrent);

    public Task<JsonObject?> InvokeAsync(string id) =>
        SendAsync(new JsonObject { ["cmd"] = "invoke", ["id"] = id });

    public Task<JsonObject?> SetBoolAsync(string id, bool value) =>
        SendAsync(new JsonObject { ["cmd"] = "set", ["id"] = id, ["bool"] = value });

    public Task<JsonObject?> SetNumberAsync(string id, double value) =>
        SendAsync(new JsonObject { ["cmd"] = "set", ["id"] = id, ["number"] = value });

    public Task<JsonObject?> SetTextAsync(string id, string value) =>
        SendAsync(new JsonObject { ["cmd"] = "set", ["id"] = id, ["text"] = value });

    public Task<JsonObject?> SetShareAsync(string id, bool share) =>
        SendAsync(new JsonObject { ["cmd"] = "set", ["id"] = id, ["share"] = share });

    public Task<JsonObject?> SetChoiceAsync(string id, int index) =>
        SendAsync(new JsonObject { ["cmd"] = "set", ["id"] = id, ["choice"] = index });

    public void Dispose() { _disposed = true; Disconnect(); }

    private sealed class Connection : IDisposable
    {
        private readonly TcpClient _client;
        public readonly StreamReader Reader;
        public readonly StreamWriter Writer;
        public readonly CancellationTokenSource Stopped = new();
        public Connection(TcpClient client)
        {
            _client = client;
            var stream = client.GetStream();
            Reader = new StreamReader(stream, Encoding.UTF8);
            Writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        }
        public void Dispose()
        {
            Stopped.Cancel();
            _client.Dispose();
            // Schließen des Sockets beendet auch ausstehende Lese-/Schreiboperationen.
        }
    }
}
