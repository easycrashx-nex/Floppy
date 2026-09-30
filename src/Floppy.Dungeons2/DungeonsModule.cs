#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Floppy.Core.Api;
using Floppy.Unreal;
using Floppy.Core;

namespace Floppy.Dungeons2;

public sealed class DungeonsModule : IGameModule, IDisposable
{
    public const string ProcessName = "Dungeons-Win64-Shipping";
    private const string SupportedHash = "7C83AFBF0AD34A40B853CDB25A22FFFB605D08E2A1E2D431974D7C7C1EE0BA54";
    private readonly Speicher _memory = new();
    private Session? _session;
    private long _nextAttach;
    private string _status = "Minecraft Dungeons II wird gesucht…";
    private readonly CheatOption _keep = new() { Id = "health.keep", Label = "Leben halten", Kind = OptionKind.Toggle,
        Description = "Füllt deine aktuellen Lebenspunkte regelmäßig auf. Bei tödlichen Treffern ist das kein garantierter Schutz." };
    private readonly CheatOption _health = new() { Id = "health.current", Label = "Lebenspunkte", Kind = OptionKind.Info, TextValue = "–" };
    private readonly CheatOption _state = new() { Id = "connection.state", Label = "Verbindung", Kind = OptionKind.Info };
    private readonly Dictionary<string, CheatOption> _features = new();
    private readonly Dictionary<string, CheatOption> _resources = new();
    private readonly Dictionary<string, CheatOption> _resourceInfo = new();
    private readonly Dictionary<string, (CheatOption Option, string Target, float Scale)> _info = new();
    public string ProductName => "MinecraftDungeons2";
    public string DisplayName => "Minecraft Dungeons II";
    public int ProcessId => _memory.Pid;

    public List<CheatCategory> BuildCategories()
    {
        _keep.IsAvailable = () => _session?.CanModify == true;
        _keep.OnChanged = option =>
        {
            if (option.BoolValue && !Heal(out string message)) { option.BoolValue = false; option.Fail(message); }
        };
        var heal = new CheatOption { Id = "health.heal", Label = "Leben auffüllen", Kind = OptionKind.Button,
            Description = "Füllt die Lebenspunkte, sofern das Spiel sie lokal verwaltet. In einer Server-Sitzung nicht unterstützt.", IsAvailable = () => _session?.CanModify == true };
        heal.OnInvoke = option => { if (Heal(out string message)) option.Message = message; else option.Fail(message); };
        var categories = new List<CheatCategory>
        {
            new CheatCategory("Übersicht").Add(_state).Add(new CheatOption { Id = "connection.build", Label = "Adapter", Kind = OptionKind.Info,
                TextValue = "Steam-Build 25041023", Description = "Andere Spielversionen werden erst nach Prüfung freigeschaltet." }),
            new CheatCategory("Überleben").Add(_health).Add(heal).Add(_keep)
        };
        var mode = new CheatOption { Id = "connection.mode", Label = "Unterstützung in dieser Sitzung", Kind = OptionKind.Info,
            Description = "Eine eigene Lobby kann trotzdem von einem externen Server verwaltet werden. Der Adapter unterstützt dort Anzeigen, keine Änderung der serververwalteten Spielwerte." };
        _info[mode.Id] = (mode, "", 1);
        categories[0].Add(mode);
        CheatCategory Category(string name)
        {
            var category = categories.Find(c => c.Name == name);
            if (category != null) return category;
            category = new CheatCategory(name); categories.Add(category); return category;
        }
        foreach (var feature in FeatureCatalog.All)
        {
            if (!_features.TryGetValue(feature.Id, out var option))
            {
                option = new CheatOption { Id = feature.Id, Label = feature.Label, Kind = OptionKind.Slider,
                    Min = feature.Min, Max = feature.Max, Step = feature.Step, NumberValue = feature.Neutral, Ruhewert = feature.Neutral,
                    Description = feature.Description.Length != 0 ? feature.Description : "Temporäre Anpassung. Mit dem Normalwert oder „Alles aus“ zurücksetzen." };
                _features.Add(feature.Id, option);
            }
            option.IsAvailable = () => _session?.CanModify == true && _session.Values.ContainsKey(feature.Target);
            option.OnChanged = changed =>
            {
                if (_session == null) { changed.Fail(_status); return; }
                if (!_session.ApplyFeature(feature, changed.NumberValue, out string message)) changed.Fail(message);
            };
            Category(feature.Category).Add(option);
        }
        foreach (var resource in FeatureCatalog.Resources)
        {
            var info = new CheatOption { Id = resource.Id + ".current", Label = resource.Label, Kind = OptionKind.Info, TextValue = "–" };
            _resourceInfo[resource.Id] = info;
            var refill = new CheatOption { Id = resource.Id + ".refill", Label = resource.Label + " auffüllen", Kind = OptionKind.Button,
                Description = "Füllt den Ressourcenbestand. Bereits laufende Cooldown-Effekte werden dadurch nicht entfernt.",
                IsAvailable = () => _session?.CanModify == true && _session.ResourceAvailable(resource) };
            refill.OnInvoke = changed =>
            {
                if (_session == null) { changed.Fail(_status); return; }
                if (_session.Refill(resource, out string message)) changed.Message = message; else changed.Fail(message);
            };
            if (!_resources.TryGetValue(resource.Id, out var keep))
            { keep = new CheatOption { Id = resource.Id + ".keep", Label = resource.Label + " halten", Kind = OptionKind.Toggle,
                Description = "Füllt die Ressource regelmäßig bis zum aktuellen Maximum auf." }; _resources.Add(resource.Id, keep); }
            keep.IsAvailable = () => _session?.CanModify == true && _session.ResourceAvailable(resource);
            keep.OnChanged = changed =>
            {
                if (!changed.BoolValue) return;
                if (_session == null) { changed.Fail(_status); return; }
                if (!_session.Refill(resource, out string message)) changed.Fail(message);
            };
            Category(resource.Category).Add(info).Add(refill).Add(keep);
        }
        var summary = categories[0];
        var ammunition = new CheatOption { Id = "ammo.loaded", Label = "Geladene Munition %", Kind = OptionKind.Info,
            TextValue = "–", Description = "Geladene Schüsse werden getrennt von der Munitionsreserve verwaltet. Die Regler unter Kampf beschleunigen das normale Nachladen." };
        _info[ammunition.Id] = (ammunition, "ATR_RangedAttack.RangedAttackCurrentAmmoPercentage", 100);
        Category("Kampf").Add(ammunition);
        foreach (var row in new[] { ("progress.level", "Level", "ATR_XP.Level"), ("progress.xp.current", "Erfahrung", "ATR_XP.XP"),
            ("progress.xp.next", "Nächstes Level bei", "ATR_XP.XPForNextLevel"), ("currency.emeralds", "Smaragde", "ATR_Currency.Emeralds") })
        {
            var info = new CheatOption { Id = row.Item1, Label = row.Item2, Kind = OptionKind.Info, TextValue = "–" };
            _info[info.Id] = (info, row.Item3, 1);
            summary.Add(info);
        }
        foreach (var currency in new[] { ("emerald", "Smaragde", "ATR_Currency.Emeralds", "ATR_Currency.EmeraldsMax", 9999f),
            ("springstone", "Springstone", "ATR_Currency.SpringStone", "ATR_Currency.SpringStoneMax", 100f) })
        {
            var amount = new CheatOption { Id = currency.Item1 + ".amount", Label = currency.Item2 + ": Menge", Kind = OptionKind.Number,
                Min = 1, Max = currency.Item5, Step = 1, NumberValue = currency.Item1 == "emerald" ? 100 : 1,
                Description = "Ganze Menge für die einmalige Gutschrift." };
            var give = new CheatOption { Id = currency.Item1 + ".give", Label = currency.Item2 + " geben", Kind = OptionKind.Button,
                Description = "Einmalige Gutschrift. Die Spielanzeige kann bis zum nächsten normalen Währungsereignis verzögert sein. „Alles aus“ nimmt geschenkte Währung nicht zurück.",
                IsAvailable = () => _session?.CanGrant(currency.Item3, currency.Item4) == true };
            give.OnInvoke = changed =>
            {
                if (_session == null) { changed.Fail(_status); return; }
                if (_session.Grant(currency.Item3, currency.Item4, amount.NumberValue, out string message)) changed.Message = message;
                else changed.Fail(message);
            };
            Category("Währungen").Add(amount).Add(give);
        }
        return categories;
    }

    public void Initialize() => Update();
    public bool IsReady(out string why) { why = _status; return _session?.Ready == true; }
    public void SetMenuOpen(bool open) { }
    private bool Heal(out string message)
    {
        if (_session == null) { message = _status; return false; }
        return _session.Heal(out message);
    }

    public void Update()
    {
        if (!_memory.Lebt())
        {
            _session = null;
            _keep.BoolValue = false;
            foreach (var option in _resources.Values) option.BoolValue = false;
            if (Environment.TickCount64 >= _nextAttach)
            {
                _nextAttach = Environment.TickCount64 + 5000;
                Attach();
            }
        }
        if (_session != null)
        {
            bool ready = _session.Refresh();
            _status = _session.Status;
            if (!_session.CanModify)
            {
                _keep.BoolValue = false;
                foreach (var option in _resources.Values) option.BoolValue = false;
                foreach (var feature in FeatureCatalog.All)
                    if (_features.TryGetValue(feature.Id, out var option)) option.NumberValue = feature.Neutral;
            }
            foreach (var feature in FeatureCatalog.All)
            {
                if (!_features.TryGetValue(feature.Id, out var option)) continue;
                if (!ready && option.NumberValue != feature.Neutral) continue;
                if (!_session.ApplyFeature(feature, option.NumberValue, out string message, refresh: false))
                { option.NumberValue = feature.Neutral; option.Fail(message); }
            }
            foreach (var resource in FeatureCatalog.Resources)
            {
                if (_resources.TryGetValue(resource.Id, out var option) && option.BoolValue && _session.ResourceAvailable(resource) &&
                    _session.Values[resource.Current].Current < _session.Values[resource.Maximum].Current && !_session.Refill(resource, out string message, refresh: false))
                { option.BoolValue = false; option.Fail(message); }
                if (_resourceInfo.TryGetValue(resource.Id, out var info)) info.TextValue = _session.ResourceAvailable(resource)
                    ? $"{_session.Values[resource.Current].Current:0.##} / {_session.Values[resource.Maximum].Current:0.##}" : "–";
            }
            if (ready && _keep.BoolValue && !_session.Heal(out string healMessage))
            { _keep.BoolValue = false; _keep.Fail(healMessage); }
            _health.TextValue = ready ? $"{_session.Health:0.##} / {_session.Maximum:0.##}" : "–";
        }
        else _health.TextValue = "–";
        foreach (var row in _info.Values)
            row.Option.TextValue = row.Target.Length == 0 ? _session?.Ready == true ? _session.Mode : "–"
                : _session?.Ready == true && _session.Values.TryGetValue(row.Target, out var value)
                ? $"{value.Current * row.Scale:0.##}" + (row.Scale == 100 ? " %" : "") : "–";
        _state.TextValue = _status;
    }

    private void Attach()
    {
        if (!_memory.Verbinde(ProcessName, ProcessName + ".exe")) { _status = _memory.LetzterFehler; return; }
        try
        {
            using var file = new FileStream(_memory.Programmpfad, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (Convert.ToHexString(SHA256.HashData(file)) != SupportedHash)
            {
                _status = "Diese Spielversion ist noch nicht geprüft. Adapter für Steam-Build 25041023 erforderlich.";
                _memory.Trenne();
                return;
            }
            _session = new Session(_memory, 0xBEA8BF0, 0xBDC5040);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _status = "Spielversion konnte nicht geprüft werden: " + ex.Message; _memory.Trenne(); }
    }

    public void Dispose()
    {
        _keep.BoolValue = false;
        foreach (var option in _resources.Values) option.BoolValue = false;
        if (_session != null && !_session.RestoreAll(out string message)) Log.Warning("Dungeons-II-Zurücksetzen: " + message);
        _session = null; _memory.Dispose();
    }
}
