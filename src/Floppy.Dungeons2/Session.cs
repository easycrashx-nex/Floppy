#nullable enable
using System;
using Floppy.Unreal;

namespace Floppy.Dungeons2;

/// <summary>Only the local controller's pawn and its registered health attribute set.
/// Every operation resolves this ownership chain again; no enemy or archetype fallback.</summary>
internal sealed class Session
{
    private readonly Speicher _memory;
    private readonly Reflexion _reflection;
    private ulong _localPlayer;
    private long _nextSearch;
    private ulong _health;
    private int _healthOffset;
    internal ulong Pawn { get; private set; }
    internal float Health { get; private set; }
    internal float Maximum { get; private set; }
    internal bool Ready { get; private set; }
    internal string Status { get; private set; } = "Suche lokale Spielfigur…";

    internal Session(Speicher memory, ulong objectsRva, ulong namesRva)
    {
        _memory = memory;
        _reflection = new Reflexion(memory, objectsRva, namesRva, 0x48);
    }

    private bool Live(ulong obj)
    {
        var bytes = _memory.Lies(obj, 40);
        if (obj < 0x10000 || bytes == null) return false;
        int index = BitConverter.ToInt32(bytes, 12);
        uint flags = BitConverter.ToUInt32(bytes, 8);
        return (flags & 0x30) == 0 && index >= 0 && index < _reflection.Anzahl &&
            _reflection.Objekt(index) == obj && _reflection.Echt(obj);
    }

    private ulong Pointer(ulong obj, string name)
    {
        if (!_reflection.Finde(obj, name, out var field) || field.Typ != "ObjectProperty" || field.Abstand < 40)
            return 0;
        var bytes = _memory.Lies(obj + (ulong)field.Abstand, 8);
        return bytes == null ? 0 : BitConverter.ToUInt64(bytes, 0);
    }

    internal bool Refresh()
    {
        Ready = false;
        Pawn = 0;
        _health = 0;
        Status = "Lade einen Spielstand mit aktiver Spielfigur.";
        if (!_memory.Lebt() || _reflection.Name(0) != "None") return false;
        if (!Live(_localPlayer))
        {
            _localPlayer = 0;
            if (Environment.TickCount64 < _nextSearch) return false;
            _nextSearch = Environment.TickCount64 + 2000;
            _localPlayer = _reflection.FindeErstes("DungeonsLocalPlayer", Live);
        }
        if (!Live(_localPlayer)) return false;
        ulong controller = Pointer(_localPlayer, "PlayerController");
        if (!Live(controller)) return false;
        ulong pawn = Pointer(controller, "Pawn");
        if (!Live(pawn) || Pointer(controller, "AcknowledgedPawn") != pawn ||
            Pointer(pawn, "Controller") != controller) return false;
        ulong ability = Pointer(pawn, "AbilitySystemComponent");
        if (!Live(ability) || Pointer(ability, "OwnerActor") != pawn || Pointer(ability, "AvatarActor") != pawn)
            return false;
        if (!_reflection.Finde(ability, "SpawnedAttributes", out var attributes) || attributes.Typ != "ArrayProperty") return false;
        var array = _memory.Lies(ability + (ulong)attributes.Abstand, 16);
        if (array == null) return false;
        int count = BitConverter.ToInt32(array, 8), capacity = BitConverter.ToInt32(array, 12);
        ulong data = BitConverter.ToUInt64(array, 0);
        if (data < 0x10000 || count <= 0 || count > 128 || capacity < count || capacity > 4096) return false;
        var entries = _memory.Lies(data, count * 8);
        if (entries == null) return false;
        ulong health = 0;
        for (int i = 0; i < count; i++)
        {
            ulong candidate = BitConverter.ToUInt64(entries, i * 8);
            if (!Live(candidate) || _reflection.KlassenName(candidate) != "ATR_Health" || _reflection.Besitzer(candidate) != pawn) continue;
            // Ambiguous attribute sets are not a writable target.
            if (health != 0) return false;
            health = candidate;
        }
        if (health == 0 || !Attribute(health, "Health", out _healthOffset, out float current) ||
            !Attribute(health, "HealthMax", out _, out float maximum) ||
            !ValidHealth(current, maximum)) return false;
        _health = health;
        Pawn = pawn;
        Health = current;
        Maximum = maximum;
        Ready = true;
        Status = "Lokale Spielfigur verbunden";
        return true;
    }

    internal static bool ValidHealth(float current, float maximum) => float.IsFinite(current) &&
        float.IsFinite(maximum) && maximum > 0 && maximum <= 10_000_000 && current > 0 && current <= maximum * 1.01f;

    private bool Attribute(ulong obj, string name, out int offset, out float current)
    {
        offset = 0;
        current = 0;
        if (!_reflection.Finde(obj, name, out var field) || field.Typ != "StructProperty" || field.Abstand < 40) return false;
        var bytes = _memory.Lies(obj + (ulong)field.Abstand, 16);
        if (bytes == null || BitConverter.ToUInt64(bytes, 0) < _memory.Basis ||
            BitConverter.ToUInt64(bytes, 0) >= _memory.Basis + 0x10000000 || !float.IsFinite(BitConverter.ToSingle(bytes, 8))) return false;
        offset = field.Abstand;
        current = BitConverter.ToSingle(bytes, 12);
        return float.IsFinite(current);
    }

    internal bool Heal(out string message)
    {
        if (!Refresh()) { message = Status; return false; }
        // Leave the base value and all modifiers untouched. Only the current value
        // is replenished; switching this feature off requires no save-file rollback.
        if (!_memory.Schreibe(_health + (ulong)_healthOffset + 12, BitConverter.GetBytes(Maximum)))
        { message = "Lebenspunkte konnten nicht geschrieben werden."; return false; }
        if (!Refresh() || Math.Abs(Health - Maximum) > Math.Max(0.01f, Maximum * 0.001f))
        { message = "Das Spiel hat die Änderung nicht bestätigt."; return false; }
        message = "Lebenspunkte aufgefüllt";
        return true;
    }
}
