using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Floppy.Core.Api;
using FS.Build;
using UnityEngine;

namespace Floppy.DumbWays;

/// <summary>Controls the game's local player through its own IL2CPP bindings.</summary>
public sealed class DumbWaysModule : IGameModule
{
    public string ProductName => "Dumb Ways to Build";
    public string DisplayName => ProductName;
    private bool _supported;
    private PlayerData _player;
    private bool _god, _stamina, _durability, _noClip, _wind;
    private bool _originalGod, _originalNoClip, _originalWind;
    private bool _godApplied, _clipApplied, _windApplied;
    private float _speed = 1, _jump = 1;
    private PlayerMovementModifiers _modifiers;
    private CheatOption _healthInfo, _staminaInfo, _movementInfo, _toolInfo, _tokensInfo, _amount;
    private PlayerState _lockedState;
    private Il2CppSystem.Object _inputToken;
    private float _nextInfo;

    public void Initialize()
    {
        string root = Path.GetDirectoryName(Application.dataPath);
        _supported = Matches(Path.Combine(root, "GameAssembly.dll"),
            "589EB5932608DA47AB38E6B0487883F778599712DB11E2905738268FA24C40EA") &&
            Matches(Path.Combine(Application.dataPath, "il2cpp_data/Metadata/global-metadata.dat"),
            "E3C949B484F4FFBB6058A0C955C8A976062312C9A2A8CBA400F75C56358E9998");
        if (!_supported) Floppy.Core.Log.Warning("Dumb Ways to Build: ungeprüfte Spielversion; Änderungen gesperrt.");
    }

    private static bool Matches(string path, string hash)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(file)) == hash;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static PlayerData LocalPlayer => PlayerLookup.Instance?.GetLocalPlayerData;
    private static bool Controllable(PlayerData player) => player && player.NetworkReady && player.HasStateAuthority;
    private bool Ready => _supported && Controllable(LocalPlayer);
    private bool Alive => Ready && LocalPlayer.HealthController && LocalPlayer.HealthController.NetworkReady &&
        LocalPlayer.HealthController.CurrentState is PlayerHealthState.Normal or PlayerHealthState.Stunned;
    private bool CanEditTokens => Ready && ReviveManager.Instance && ReviveManager.Instance.HasStateAuthority;
    private ItemBase ActiveTool
    {
        get
        {
            var inventory = Ready ? LocalPlayer.Inventory : null;
            return inventory && inventory.NetworkReady && inventory.TryGetActiveItem(out var item) &&
                item && item.NetworkReady && item.HasStateAuthority && item.BaseItemSettings &&
                item.BaseItemSettings.HasDurability ? item : null;
        }
    }

    public bool IsReady(out string status)
    {
        if (!_supported) { status = "Spielversion ungeprüft. Benötigt Version 2.1.89 / Steam-Build 25644049."; return false; }
        var player = LocalPlayer;
        if (!player || !player.NetworkReady) { status = "Lade eine Runde mit aktiver Spielfigur."; return false; }
        if (!player.HasStateAuthority) { status = "Spielfigur wird fremd verwaltet; Änderungen nicht verfügbar."; return false; }
        status = "Bereit · lokale Spielfigur";
        return true;
    }

    public void Update()
    {
        if (!_supported) return;
        var local = LocalPlayer;
        var current = Controllable(local) ? local : null;
        if ((_player ? _player.Pointer : IntPtr.Zero) != (current ? current.Pointer : IntPtr.Zero))
        {
            RestorePlayer();
            _player = current;
            if (Controllable(_player))
            {
                ApplyMovement();
            }
        }
        if (Controllable(_player))
        {
            var health = _player.HealthController;
            if (health && health.NetworkReady)
            {
                if (_god && !_godApplied) { _originalGod = (health.GodModeLocks & GodModeLock.Debug) != 0; _godApplied = true; }
                if (_god && (health.GodModeLocks & GodModeLock.Debug) == 0) health.GodModeLocks |= GodModeLock.Debug;
                else if (_godApplied) { RestoreGod(health); _godApplied = false; }
            }
            if (_noClip && !_clipApplied) { _originalNoClip = _player.DebugNoClip; _clipApplied = true; _player.SetDebugNoClip(true); }
            else if (!_noClip && _clipApplied) { _player.SetDebugNoClip(_originalNoClip); _clipApplied = false; }
            if (_wind && !_windApplied) { _originalWind = _player.DebugImmuneToWind; _windApplied = true; _player.SetDebugImmuneToWind(true); }
            else if (!_wind && _windApplied) { _player.SetDebugImmuneToWind(_originalWind); _windApplied = false; }
            var movement = _player.MovementController;
            if (_stamina && movement && movement.NetworkReady && Alive)
                movement.Stamina = movement.CurrentMovementVariables.MaxStamina;
            var item = _durability ? ActiveTool : null;
            if (item && item.CurrentUsesFloat > 0) item._currentUses = 0;
        }
        if (Time.unscaledTime < _nextInfo) return;
        _nextInfo = Time.unscaledTime + .2f;
        UpdateInfo();
    }

    private void RestorePlayer()
    {
        if (Controllable(_player))
        {
            var health = _player.HealthController;
            if (_godApplied && health && health.NetworkReady) RestoreGod(health);
            if (_clipApplied) _player.SetDebugNoClip(_originalNoClip);
            if (_windApplied) _player.SetDebugImmuneToWind(_originalWind);
            if (_modifiers != null && _player.MovementController)
                _player.MovementController.RemoveMovementModifierSet(_modifiers);
        }
        _modifiers = null;
        _godApplied = _clipApplied = _windApplied = false;
    }

    private void RestoreGod(PlayerHealthController health) => health.GodModeLocks =
        _originalGod ? health.GodModeLocks | GodModeLock.Debug : health.GodModeLocks & ~GodModeLock.Debug;

    private void ApplyMovement()
    {
        if (!Controllable(_player) || !_player.MovementController) return;
        var movement = _player.MovementController;
        if (_modifiers != null) movement.RemoveMovementModifierSet(_modifiers);
        _modifiers = null;
        if (_speed == 1 && _jump == 1) return;
        _modifiers = new PlayerMovementModifiers
        {
            Modifiers = new Il2CppSystem.Collections.Generic.List<PlayerMovementModifiers.PlayerMovementModifier>()
        };
        _modifiers.Modifiers.Add(new PlayerMovementModifiers.PlayerMovementModifier { Setting = PlayerMovementSetting.Speed, Multiplier = _speed });
        _modifiers.Modifiers.Add(new PlayerMovementModifiers.PlayerMovementModifier { Setting = PlayerMovementSetting.JumpVelocity, Multiplier = _jump });
        movement.AddMovementModifierSet(_modifiers);
    }

    private void UpdateInfo()
    {
        if (_healthInfo == null) return;
        var player = LocalPlayer;
        bool readable = player && player.NetworkReady;
        _healthInfo.TextValue = readable && player.HealthController && player.HealthController.NetworkReady
            ? $"{player.HealthController.CurrentHealth:0.#} / {player.HealthController._settings.BaseHealth:0.#} · {player.HealthController.CurrentState}" +
                (player.HealthController.GodModeActive ? " · Schutz aktiv" : "") : "–";
        _staminaInfo.TextValue = readable && player.MovementController && player.MovementController.NetworkReady
            ? $"{player.MovementController.Stamina:0.#} / {player.MovementController.CurrentMovementVariables.MaxStamina:0.#}" : "–";
        _movementInfo.TextValue = readable && player.MovementController && player.MovementController.NetworkReady
            ? $"Tempo {player.MovementController.CurrentMovementVariables.Speed:0.##} · Sprung {player.MovementController.CurrentMovementVariables.JumpVelocity:0.##}" +
                ((bool)player.DebugNoClip ? " · No-Clip aktiv" : "") + ((bool)player.DebugImmuneToWind ? " · Windschutz aktiv" : "") : "–";
        var item = ActiveTool;
        _toolInfo.TextValue = item ? $"{item.name} · {item.CurrentUsesLeftDisplay} / {item.BaseItemSettings.MaxUses} Anwendungen" : "Kein Werkzeug mit Haltbarkeit ausgerüstet";
        _tokensInfo.TextValue = readable && ReviveManager.Instance ? ReviveManager.Instance.ReviveCurrencyString : "Keine Marken in dieser Szene";
    }

    public void SetMenuOpen(bool open)
    {
        if (_lockedState != null)
        {
            _lockedState.InputLockingObjects.Remove(_inputToken);
            _lockedState.LookLockingObjects.Remove(_inputToken);
            _lockedState.InteractLockingObjects.Remove(_inputToken);
            _lockedState = null;
        }
        if (!open || !_supported || LocalPlayerState.Instance == null) return;
        _inputToken ??= new Il2CppSystem.Object();
        _lockedState = LocalPlayerState.Instance;
        _lockedState.InputLockingObjects.Add(_inputToken);
        _lockedState.LookLockingObjects.Add(_inputToken);
        _lockedState.InteractLockingObjects.Add(_inputToken);
    }

    public List<CheatCategory> BuildCategories()
    {
        _healthInfo = Info("health.info", "Lebenspunkte");
        _staminaInfo = Info("stamina.info", "Ausdauer");
        _movementInfo = Info("movement.info", "Aktuelle Bewegung");
        _toolInfo = Info("tool.info", "Ausgerüstetes Werkzeug");
        _tokensInfo = Info("tokens.info", "Wiederbelebungsmarken");
        _amount = new CheatOption { Id = "tokens.amount", Label = "Anzahl", Kind = OptionKind.Number, Min = 1, Max = 999, Step = 1, NumberValue = 10 };
        return new List<CheatCategory>
        {
            new CheatCategory("Überleben").Add(_healthInfo)
                .Add(Toggle("health.god", "Unverwundbar", "Verwendet den Schutz des Spiels für deine Figur.", value => _god = value))
                .Add(Button("health.heal", "Leben auffüllen", () => Alive, () =>
                {
                    var health = LocalPlayer.HealthController;
                    health.RPC_AddHealth(Math.Max(0, health._settings.BaseHealth - health.CurrentHealth));
                    return "Leben aufgefüllt.";
                }))
                .Add(Button("health.unstun", "Betäubung aufheben", () => Ready && LocalPlayer.HealthController.CurrentState == PlayerHealthState.Stunned,
                    () => LocalPlayer.HealthController.TryUnstunInternal(false) ? "Betäubung aufgehoben." : throw new InvalidOperationException("Das Spiel konnte die Betäubung nicht aufheben.")))
                .Add(Button("health.revive", "Eigene Figur wiederbeleben", () => Ready && LocalPlayer.HealthController.CurrentState == PlayerHealthState.Dead, () =>
                {
                    var player = LocalPlayer;
                    player.HealthController.RPC_Revive(player.transform.position);
                    return "Wiederbelebung angefordert.";
                })),
            new CheatCategory("Bewegung").Add(_staminaInfo).Add(_movementInfo)
                .Add(Toggle("stamina.keep", "Unbegrenzte Ausdauer", "Hält die Ausdauer deiner Figur auf dem Maximum.", value => _stamina = value))
                .Add(Slider("movement.speed", "Laufgeschwindigkeit ×", value => _speed = value))
                .Add(Slider("movement.jump", "Sprungkraft ×", value => _jump = value))
                .Add(Toggle("movement.noclip", "Durch Wände bewegen", "Verwendet den No-Clip-Modus des Spiels. Ausschalten an einer freien Stelle.", value => _noClip = value))
                .Add(Toggle("movement.wind", "Wind ignorieren", "Deine Figur wird nicht vom Wind weggeschoben.", value => _wind = value)),
            new CheatCategory("Werkzeuge").Add(_toolInfo)
                .Add(Button("tool.repair", "Ausgerüstetes Werkzeug reparieren", () => ActiveTool, () =>
                {
                    var item = ActiveTool;
                    item._currentUses = 0;
                    return "Werkzeug repariert.";
                }))
                .Add(Toggle("tool.keep", "Unbegrenzte Haltbarkeit", "Repariert laufend dein aktives Werkzeug; benötigt ein ausgerüstetes Werkzeug mit Haltbarkeit.", value => _durability = value)),
            new CheatCategory("Wiederbelebungsmarken").Add(_tokensInfo).Add(_amount)
                .Add(new CheatOption
                {
                    Id = "tokens.add", Label = "Marken hinzufügen", Kind = OptionKind.Button, Scope = CheatScope.Everyone,
                    Description = "Gilt für den gemeinsamen Bestand. Nur verfügbar, wenn dein Spiel diesen Bestand verwaltet.",
                    IsAvailable = () => CanEditTokens,
                    OnInvoke = option => Execute(option, () =>
                    {
                        int amount = (int)_amount.NumberValue;
                        if (!CanEditTokens || amount < 1 || amount > 999 || _amount.NumberValue != amount)
                            throw new InvalidOperationException("Anzahl muss eine ganze Zahl zwischen 1 und 999 sein.");
                        int before = ReviveManager.Instance.GetReviveCurrency();
                        if (before > int.MaxValue - amount) throw new InvalidOperationException("Bestand zu groß.");
                        ReviveManager.Instance.AddCurrency(amount);
                        int after = ReviveManager.Instance.GetReviveCurrency();
                        if (after != before + amount) throw new InvalidOperationException("Das Spiel hat die Gutschrift nicht bestätigt.");
                        return $"{amount} Marken hinzugefügt · Bestand {after}.";
                    })
                })
        };
    }

    private static CheatOption Info(string id, string label) => new() { Id = id, Label = label, Kind = OptionKind.Info, TextValue = "–" };
    private CheatOption Toggle(string id, string label, string description, Action<bool> set) => new()
    {
        Id = id, Label = label, Description = description, Kind = OptionKind.Toggle,
        IsAvailable = () => Ready,
        OnChanged = option => { if (Ready || !option.BoolValue) set(option.BoolValue); else option.Fail("Keine lokal verwaltete Spielfigur."); }
    };
    private CheatOption Slider(string id, string label, Action<float> set) => new()
    {
        Id = id, Label = label, Kind = OptionKind.Slider, Min = 1, Max = 3, Step = .1f, NumberValue = 1, Ruhewert = 1,
        Description = "Temporärer Faktor für deine Figur. Mit 1 oder „Alles aus“ zurücksetzen.", IsAvailable = () => Ready,
        OnChanged = option => { set(option.NumberValue); ApplyMovement(); }
    };
    private CheatOption Button(string id, string label, Func<bool> available, Func<string> action) => new()
    {
        Id = id, Label = label, Kind = OptionKind.Button, IsAvailable = available,
        OnInvoke = option => Execute(option, () => available() ? action() : throw new InvalidOperationException("Funktion momentan nicht verfügbar."))
    };
    private void Execute(CheatOption option, Func<string> action)
    {
        try { option.Message = action(); UpdateInfo(); }
        catch (Exception error) { option.Fail(error.Message); }
    }
}
