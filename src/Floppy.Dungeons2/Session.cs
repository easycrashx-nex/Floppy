#nullable enable
using System;
using System.Collections.Generic;
using Floppy.Unreal;

namespace Floppy.Dungeons2;

/// <summary>Resolves the local player's owned objects before every group of writes.</summary>
internal sealed class Session
{
    internal sealed record Value(ulong Pawn, ulong Object, ulong Class, int Index, int Serial, int Offset,
        bool Attribute, float Base, float Current)
    {
        internal ulong Address => Object + (ulong)Offset + (Attribute ? 12UL : 0);
    }
    private sealed record Patch(Value Original, float Last);
    private readonly Speicher _memory;
    private readonly Reflexion _reflection;
    private readonly ulong _objects;
    private ulong _localPlayer;
    private long _nextSearch;
    private bool _owned;
    private readonly Dictionary<string, Patch> _patches = new();
    private readonly Dictionary<string, Value> _values = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, Value> Values => _values;
    internal ulong Pawn { get; private set; }
    internal float Health { get; private set; }
    internal float Maximum { get; private set; }
    internal bool Ready { get; private set; }
    internal bool HasAuthority { get; private set; }
    private int _role = -1;
    internal bool CanModify => Ready && HasAuthority;
    internal string Mode => HasAuthority ? "Spielwerte lokal verwaltet" : _role is 1 or 2
        ? "Server-Spiel · nur Anzeigen unterstützt" : "Spielrolle unbekannt · nur Anzeigen unterstützt";
    internal string Status { get; private set; } = "Suche lokale Spielfigur…";

    internal Session(Speicher memory, ulong objectsRva, ulong namesRva)
    {
        _memory = memory;
        _objects = memory.Basis + objectsRva;
        _reflection = new Reflexion(memory, objectsRva, namesRva, 0x48);
    }

    private bool Live(ulong obj)
    {
        var bytes = _memory.Lies(obj, 40);
        if (obj < 0x10000 || bytes == null) return false;
        int index = BitConverter.ToInt32(bytes, 12);
        uint flags = BitConverter.ToUInt32(bytes, 8);
        return (flags & 0x18030) == 0 && index >= 0 && index < _reflection.Anzahl &&
            _reflection.Objekt(index) == obj && _reflection.Echt(obj);
    }

    private int Serial(int index)
    {
        ulong table = _memory.U64(_objects + 16), block = _memory.U64(table + (ulong)(index >> 16) * 8);
        var bytes = _memory.Lies(block + (ulong)(index & 65535) * 24 + 16, 4);
        return bytes == null ? -1 : BitConverter.ToInt32(bytes, 0);
    }

    private bool Same(Value value) => Live(value.Object) && _reflection.Besitzer(value.Object) == value.Pawn && _reflection.Klasse(value.Object) == value.Class &&
        _memory.I32(value.Object + 12) == value.Index && value.Serial >= 0 && Serial(value.Index) == value.Serial;

    private ulong Pointer(ulong obj, string name)
    {
        if (!_reflection.Finde(obj, name, out var field) || field.Typ != "ObjectProperty" || field.Abstand < 40) return 0;
        var bytes = _memory.Lies(obj + (ulong)field.Abstand, 8);
        return bytes == null ? 0 : BitConverter.ToUInt64(bytes, 0);
    }

    private int Role(ulong pawn)
    {
        if (!_reflection.Finde(pawn, "Role", out var role) || role.Typ != "ByteProperty" || role.Abstand < 40) return -1;
        var bytes = _memory.Lies(pawn + (ulong)role.Abstand, 1);
        return bytes != null && bytes[0] <= 3 ? bytes[0] : -1;
    }

    internal bool Refresh()
    {
        Ready = false; HasAuthority = false; _role = -1; _owned = false; Pawn = 0; _values.Clear();
        Health = 0; Maximum = 0;
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
        if (!Live(pawn) || Pointer(controller, "AcknowledgedPawn") != pawn || Pointer(pawn, "Controller") != controller) return false;
        ulong ability = Pointer(pawn, "AbilitySystemComponent");
        if (!Live(ability) || Pointer(ability, "OwnerActor") != pawn || Pointer(ability, "AvatarActor") != pawn) return false;
        if (!_reflection.Finde(ability, "SpawnedAttributes", out var attributes) || attributes.Typ != "ArrayProperty") return false;
        var array = _memory.Lies(ability + (ulong)attributes.Abstand, 16);
        if (array == null) return false;
        int count = BitConverter.ToInt32(array, 8), capacity = BitConverter.ToInt32(array, 12);
        ulong data = BitConverter.ToUInt64(array, 0);
        if (data < 0x10000 || count <= 0 || count > 128 || capacity < count || capacity > 4096) return false;
        var entries = _memory.Lies(data, count * 8);
        if (entries == null) return false;
        var sets = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            ulong obj = BitConverter.ToUInt64(entries, i * 8);
            if (!Live(obj) || _reflection.Besitzer(obj) != pawn) continue;
            string set = _reflection.KlassenName(obj);
            if (!set.StartsWith("ATR_", StringComparison.Ordinal)) continue;
            if (!sets.Add(set)) { _values.Clear(); return false; }
            ReadFields(pawn, obj, set, true);
        }
        ulong movement = Pointer(pawn, "CharacterMovement");
        if (Live(movement) && _reflection.Besitzer(movement) == pawn) ReadFields(pawn, movement, "Movement", false);
        if (!_values.TryGetValue("ATR_Health.Health", out var health) || !_values.TryGetValue("ATR_Health.HealthMax", out var maximum)) return false;
        Pawn = pawn; Health = health.Current; Maximum = maximum.Current; _owned = true;
        _role = Role(pawn); HasAuthority = _role == 3; // ROLE_Authority
        Ready = ValidHealth(Health, Maximum);
        Status = Ready ? HasAuthority ? "Lokale Spielfigur verbunden" : _role is 1 or 2
            ? "Server verwaltet die Spielfigur · Anzeigen verfügbar, Spieländerungen nicht unterstützt."
            : "Spielrolle nicht eindeutig · Anzeigen verfügbar, Spieländerungen gesperrt."
            : Health == 0 ? "Spielfigur besiegt – warte auf Wiederbelebung." : "Lebenswerte momentan nicht gültig.";
        return Ready;
    }

    private void ReadFields(ulong pawn, ulong obj, string prefix, bool attribute)
    {
        ulong cls = _reflection.Klasse(obj);
        int index = _memory.I32(obj + 12), serial = Serial(index);
        if (serial < 0) return;
        foreach (var field in _reflection.Felder(cls))
        {
            if (field.Abstand < 40 || field.Typ != (attribute ? "StructProperty" : "FloatProperty")) continue;
            var bytes = _memory.Lies(obj + (ulong)field.Abstand, attribute ? 16 : 4);
            if (bytes == null) continue;
            if (attribute && (BitConverter.ToUInt64(bytes, 0) < _memory.Basis || BitConverter.ToUInt64(bytes, 0) >= _memory.Basis + 0x10000000)) continue;
            float current = BitConverter.ToSingle(bytes, attribute ? 12 : 0), baseline = attribute ? BitConverter.ToSingle(bytes, 8) : current;
            if (!float.IsFinite(current) || !float.IsFinite(baseline) || Math.Abs(current) > 10_000_000 || Math.Abs(baseline) > 10_000_000) continue;
            _values.TryAdd(prefix + "." + field.Name, new Value(pawn, obj, cls, index, serial, field.Abstand, attribute, baseline, current));
        }
    }

    internal static bool ValidHealth(float current, float maximum) => float.IsFinite(current) && float.IsFinite(maximum) &&
        maximum > 0 && maximum <= 10_000_000 && current > 0 && current <= maximum * 1.01f;

    private bool Write(Value value, float number) => Write(value, number, out _);
    private bool Write(Value value, float number, out bool written)
    {
        written = false;
        if (!_owned || !HasAuthority || Role(Pawn) != 3 || !Same(value) || value.Pawn != Pawn || !float.IsFinite(number)) return false;
        if (!_memory.Schreibe(value.Address, BitConverter.GetBytes(number))) return false;
        written = true;
        var result = _memory.Lies(value.Address, 4);
        return result != null && BitConverter.ToInt32(result, 0) == BitConverter.SingleToInt32Bits(number);
    }

    internal bool ApplyFeature(Feature feature, float setting, out string message, bool refresh = true)
    {
        if (refresh) Refresh();
        if (!float.IsFinite(setting) || setting < feature.Min || setting > feature.Max)
        { message = "Wert außerhalb des erlaubten Bereichs."; return false; }
        if (setting == feature.Neutral) return Restore(feature.Target, out message);
        if (!CanModify) { message = Status; return false; }
        if (!Ready || !_values.TryGetValue(feature.Target, out var value))
        { message = "Dieser Spielwert ist momentan nicht verfügbar."; return false; }
        Value original = value;
        if (_patches.TryGetValue(feature.Target, out var patch) && Identity(value, patch.Original) &&
            (!value.Attribute || value.Base == patch.Original.Base) && value.Current == patch.Last) original = patch.Original;
        float desired = feature.Value(original.Current, setting);
        if (feature.Target == "ATR_Health.HealthMax") desired = Math.Max(1, desired);
        if (!Write(value, desired, out bool written))
        {
            if (written) _patches[feature.Target] = new Patch(original, desired);
            message = "Das Spiel hat die Änderung nicht bestätigt."; return false;
        }
        _patches[feature.Target] = new Patch(original, desired);
        _values[feature.Target] = value with { Current = desired };
        message = "Anpassung aktiv";
        return true;
    }

    private static bool Identity(Value a, Value b) => a.Pawn == b.Pawn && a.Object == b.Object && a.Class == b.Class &&
        a.Index == b.Index && a.Serial == b.Serial && a.Offset == b.Offset;

    private bool Restore(string target, out string message)
    {
        message = "Anpassung beendet";
        if (!_patches.TryGetValue(target, out var patch)) return true;
        if (!_owned) return true; // Keep a pending restore until the same owned pawn becomes readable again.
        if (_values.TryGetValue(target, out var value) && Identity(value, patch.Original) && value.Current == patch.Last &&
            (!value.Attribute || value.Base == patch.Original.Base))
        {
            // Reducing a boosted health maximum must never leave current health above it.
            if (target == "ATR_Health.HealthMax" && _values.TryGetValue("ATR_Health.Health", out var health) && health.Current > patch.Original.Current &&
                !Write(health, patch.Original.Current)) { message = "Lebenspunkte konnten nicht angepasst werden."; return false; }
            if (!Write(value, patch.Original.Current)) { message = "Vorheriger Wert konnte nicht wiederhergestellt werden."; return false; }
            _values[target] = value with { Current = patch.Original.Current };
        }
        _patches.Remove(target);
        return true;
    }

    internal bool RestoreAll(out string message)
    {
        Refresh();
        bool success = true; message = "Anpassungen beendet";
        foreach (string target in new List<string>(_patches.Keys))
            if (!Restore(target, out string error)) { success = false; message = error; }
        if (_patches.Count != 0) { success = false; message = "Zurücksetzen wartet auf die ursprüngliche Spielfigur."; }
        return success;
    }

    internal bool ResourceAvailable(FeatureCatalog.Resource resource) => Ready &&
        _values.TryGetValue(resource.Current, out var current) && _values.TryGetValue(resource.Maximum, out var max) &&
        max.Current > 0 && max.Current <= 10_000 && current.Current >= 0 && current.Current <= max.Current * 1.01f;

    internal bool Refill(FeatureCatalog.Resource resource, out string message, bool refresh = true)
    {
        if (refresh) Refresh();
        if (!CanModify) { message = Status; return false; }
        if (!ResourceAvailable(resource)) { message = "Ressource momentan nicht verfügbar."; return false; }
        var current = _values[resource.Current];
        float maximum = _values[resource.Maximum].Current;
        if (!Write(current, maximum)) { message = "Das Spiel hat die Auffüllung nicht bestätigt."; return false; }
        _values[resource.Current] = current with { Current = maximum };
        message = resource.Label + " aufgefüllt";
        return true;
    }

    internal bool CanGrant(string target, string maximum) => CanModify && _values.TryGetValue(target, out var value) &&
        _values.TryGetValue(maximum, out var max) && value.Attribute && value.Base == value.Current &&
        value.Current >= 0 && value.Current == MathF.Floor(value.Current) && max.Current > 0 && max.Current <= 100_000 && value.Current <= max.Current;

    internal bool Grant(string target, string maximum, float amount, out string message)
    {
        Refresh();
        if (!CanModify) { message = Status; return false; }
        if (!CanGrant(target, maximum)) { message = "Währungsbestand momentan nicht eindeutig verfügbar."; return false; }
        var value = _values[target];
        float next = value.Current + amount, limit = _values[maximum].Current;
        if (!float.IsFinite(amount) || amount < 1 || amount != MathF.Floor(amount) || !float.IsFinite(next) || next > limit)
        { message = $"Ganze Menge zwischen 1 und {MathF.Floor(limit - value.Current):0} erforderlich."; return false; }
        if (!Same(value) || Role(Pawn) != 3) { message = "Spielobjekt oder Spielrolle hat sich geändert."; return false; }
        // A one-time currency grant changes the permanent count, not a temporary
        // multiplier. Base and current are adjacent and written in one operation.
        var bytes = new byte[8];
        Buffer.BlockCopy(BitConverter.GetBytes(next), 0, bytes, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(next), 0, bytes, 4, 4);
        if (!_memory.Schreibe(value.Address - 4, bytes)) { message = "Gutschrift konnte nicht geschrieben werden."; return false; }
        var result = _memory.Lies(value.Address - 4, 8);
        if (result == null || BitConverter.ToSingle(result, 0) != next || BitConverter.ToSingle(result, 4) != next)
        { message = $"Bestand nach dem Schreiben nicht bestätigt. Zielbestand: {next:0}."; return false; }
        _values[target] = value with { Base = next, Current = next };
        message = $"+{amount:0} · Bestand {next:0}";
        return true;
    }

    internal bool Heal(out string message)
    {
        if (!Refresh() || !CanModify) { message = Status; return false; }
        if (!Write(_values["ATR_Health.Health"], Maximum)) { message = "Lebenspunkte konnten nicht geschrieben werden."; return false; }
        if (!Refresh() || Math.Abs(Health - Maximum) > Math.Max(.01f, Maximum * .001f))
        { message = "Das Spiel hat die Änderung nicht bestätigt."; return false; }
        message = "Lebenspunkte aufgefüllt";
        return true;
    }
}
