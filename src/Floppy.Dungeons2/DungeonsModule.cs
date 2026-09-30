#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Floppy.Core.Api;
using Floppy.Unreal;

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
    public string ProductName => "MinecraftDungeons2";
    public string DisplayName => "Minecraft Dungeons II";
    public int ProcessId => _memory.Pid;

    public List<CheatCategory> BuildCategories()
    {
        _keep.IsAvailable = () => _session?.Ready == true;
        _keep.OnChanged = option =>
        {
            if (option.BoolValue && !Heal(out string message)) { option.BoolValue = false; option.Fail(message); }
        };
        var heal = new CheatOption { Id = "health.heal", Label = "Leben auffüllen", Kind = OptionKind.Button,
            Description = "Füllt ausschließlich die Lebenspunkte deiner lokalen Spielfigur auf.", IsAvailable = () => _session?.Ready == true };
        heal.OnInvoke = option => { if (Heal(out string message)) option.Message = message; else option.Fail(message); };
        return new()
        {
            new CheatCategory("Übersicht").Add(_state).Add(new CheatOption { Id = "connection.build", Label = "Adapter", Kind = OptionKind.Info,
                TextValue = "Prototyp · Steam-Build 25041023", Description = "Andere Spielversionen werden erst nach Prüfung freigeschaltet." }),
            new CheatCategory("Spielfigur").Add(_health).Add(heal).Add(_keep)
        };
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
            if (ready && _keep.BoolValue && !_session.Heal(out string message))
            { _keep.BoolValue = false; _keep.Fail(message); }
            _health.TextValue = ready ? $"{_session.Health:0.##} / {_session.Maximum:0.##}" : "–";
        }
        else _health.TextValue = "–";
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

    public void Dispose() { _keep.BoolValue = false; _session = null; _memory.Dispose(); }
}
