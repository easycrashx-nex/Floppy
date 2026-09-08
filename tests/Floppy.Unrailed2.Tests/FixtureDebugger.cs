using System.Net;
using System.Text.Json;

namespace Floppy.Unrailed2;

// This test assembly never includes the production HTTP debugger.
internal static class Debugger
{
    public static readonly Dictionary<string, string> Responses = new();
    public static readonly List<string> Requests = new();
    public static Action<string> OnRequest;
    public static bool Erreichbar { get; private set; }
    public static string Zustand { get; private set; } = "Fixture getrennt";

    public static void Reset()
    {
        NativeCheats.Reset();
        Responses.Clear();
        Requests.Clear();
        OnRequest = null;
        Erreichbar = false;
        Zustand = "Fixture getrennt";
    }

    public static string Hole(string pfad)
    {
        Requests.Add(pfad);
        OnRequest?.Invoke(pfad);
        Responses.TryGetValue(pfad, out string response);
        Erreichbar = response != null;
        Zustand = Erreichbar ? "Fixture verbunden" : "Fixture getrennt";
        return response;
    }

    public static JsonDocument Frage(string pfad)
    {
        string response = Hole(pfad);
        if (string.IsNullOrWhiteSpace(response)) return null;
        try { return JsonDocument.Parse(response); }
        catch (JsonException)
        {
            Erreichbar = false;
            Zustand = "Fixture-Antwort unlesbar";
            return null;
        }
    }

    public static bool Loese(string pfad) => Hole(pfad) != null;
    public static string Verpacke(string wert) => WebUtility.UrlEncode(wert);
}

// Native integration fixtures never open or write a game process.
internal static class NativeCheats
{
    internal static byte[] Values;
    internal static ulong Identity = 1;
    internal static bool Writable;
    internal static bool RejectWrite;
    internal static readonly List<(int Offset, byte[] Value)> Writes = new();
    public static bool CanWrite => Writable;
    public static void Reset() { Values = null; Writable = false; RejectWrite = false; Writes.Clear(); Identity = 1; }
    public static bool TryRead(out byte[] values, out ulong identity, out string reason)
    { values = Values?.ToArray(); identity = Identity; reason = "Native fixture unavailable"; return Values != null; }
    public static bool TrySet(int offset, byte[] value, ulong expectedIdentity, out string reason)
    {
        reason = "Native fixture rejected write";
        if (!Writable || RejectWrite || expectedIdentity != Identity || Values == null) return false;
        Writes.Add((offset, value.ToArray()));
        Array.Copy(value, 0, Values, offset, value.Length);
        return true;
    }
}

internal static class Spiel
{
    public static bool Angeschaltet => true;
    public static bool Laeuft => true;
    public static bool HatSicherung => false;
    public static string Anschalten() => throw new InvalidOperationException("Settings actions are outside this fixture");
    public static string Ausschalten() => throw new InvalidOperationException("Settings actions are outside this fixture");
    public static string Wiederherstellen() => throw new InvalidOperationException("Settings actions are outside this fixture");
}
