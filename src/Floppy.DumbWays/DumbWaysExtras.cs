using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Floppy.Core.Api;
using Fusion;
using FS.Build;
using UnityEngine;

namespace Floppy.DumbWays;

public sealed partial class DumbWaysModule
{
    private CheckpointStore _checkpoints;
    private CheckpointStore.Point[] _levelPoints = Array.Empty<CheckpointStore.Point>();
    private CheatOption _pointName, _pointChoice, _positionInfo, _pointInfo;
    private CheatOption _spawnChoice, _spawnAmount, _spawnInfo, _spawnGroup, _lastSpawnInfo;
    private NetworkObject _lastSpawned;
    private string _lastSpawnName = "";
    private string _level = "";
    private NetworkRunner _catalogRunner;
    private int _scanIndex;
    private readonly List<int> _pendingPrefabs = new();
    private readonly List<SpawnEntry> _spawnEntries = new();
    private SpawnEntry[] _visibleEntries = Array.Empty<SpawnEntry>();
    private sealed class SpawnEntry
    {
        internal NetworkPrefabId Id { get; }
        internal string Name { get; }
        internal string Group { get; }
        internal SpawnEntry(NetworkPrefabId id, string name, string group) { Id = id; Name = name; Group = group; }
    }
    private NetworkRunner Runner => Ready ? LocalPlayer.Runner : null;
    private bool CanSpawn => Alive && Runner && Runner.IsRunning &&
        (Runner.IsServer || Runner.IsSharedModeMasterClient || Runner.GameMode == GameMode.Single);
    private string LevelKey
    {
        get
        {
            if (!Ready) return "";
            var scene = LocalPlayer.gameObject.scene;
            var gameplay = GameplayManager.Instance;
            int index = gameplay && gameplay.Object && gameplay.Object.IsValid ? gameplay.SceneListIndex : -1;
            return (string.IsNullOrEmpty(scene.path) ? scene.name : scene.path) + "#" + index;
        }
    }
    private CheckpointStore.Point SelectedPoint => _pointChoice != null && _pointChoice.ChoiceIndex >= 0 &&
        _pointChoice.ChoiceIndex < _levelPoints.Length ? _levelPoints[_pointChoice.ChoiceIndex] : null;

    private void InitializeExtras()
    {
        _checkpoints = new CheckpointStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Floppy", "Checkpoints", "DumbWays-2.1.89.json"));
        if (_checkpoints.LoadError.Length != 0) Floppy.Core.Log.Warning(_checkpoints.LoadError);
    }
    private void TickExtras()
    {
        if (_pointChoice == null) return;
        string level = LevelKey;
        if (_level != level) { _level = level; RefreshPoints(); }
        var player = Ready ? LocalPlayer : null;
        _positionInfo.TextValue = player ? $"{player.gameObject.scene.name} · X {player.transform.position.x:0.##} · Y {player.transform.position.y:0.##} · Z {player.transform.position.z:0.##}" : "Lade eine Runde.";
        var point = SelectedPoint;
        _pointInfo.TextValue = _checkpoints.LoadError.Length != 0 ? _checkpoints.LoadError : point == null ? "Noch kein Checkpoint in diesem Level." :
            $"{point.Name} · X {point.X:0.##} · Y {point.Y:0.##} · Z {point.Z:0.##}";
        ScanPrefabs();
        _lastSpawnInfo.TextValue = _lastSpawned && _lastSpawned.IsValid ?
            $"{_lastSpawnName} · X {_lastSpawned.transform.position.x:0.##} · Y {_lastSpawned.transform.position.y:0.##} · Z {_lastSpawned.transform.position.z:0.##}" +
            (player ? $" · {Vector3.Distance(player.transform.position, _lastSpawned.transform.position):0.#} m entfernt" : "") : "Noch kein erzeugter Gegenstand in dieser Sitzung.";
    }
    private void RefreshPoints(string selectedName = null)
    {
        selectedName ??= SelectedPoint?.Name;
        _levelPoints = _checkpoints.ForLevel(_level);
        _pointChoice.Choices = _levelPoints.Select(p => p.Name).ToArray();
        int index = Array.FindIndex(_levelPoints, p => p.Name == selectedName);
        _pointChoice.ChoiceIndex = index < 0 ? 0 : index;
    }
    private string SavePoint()
    {
        string level = LevelKey;
        if (!Alive || level.Length == 0) throw new InvalidOperationException("Keine lebende lokale Spielfigur.");
        string name = _pointName.TextValue.Trim();
        if (name.Length == 0)
        {
            int index = 1;
            var points = _checkpoints.ForLevel(level);
            while (points.Any(p => p.Name == "Checkpoint " + index)) index++;
            name = "Checkpoint " + index;
        }
        var position = LocalPlayer.transform.position;
        _checkpoints.Save(new CheckpointStore.Point(level, name, position.x, position.y, position.z));
        _level = level;
        RefreshPoints(name);
        return "Checkpoint gespeichert: " + name;
    }
    private string TeleportToPoint()
    {
        var point = SelectedPoint;
        if (!Alive || point == null || point.Level != LevelKey) throw new InvalidOperationException("Checkpoint gehört nicht zum aktuellen Level.");
        var player = LocalPlayer;
        player.TeleportToWorldPosition(new Vector3(point.X, point.Y, point.Z));
        player.HealthController.ResetFallDamage(true);
        player.MovementController.NotifyPoseWasTeleported();
        return "Zum Checkpoint teleportiert: " + point.Name;
    }
    private CheatCategory CheckpointsCategory()
    {
        _positionInfo = Info("checkpoint.position", "Level und Position");
        _pointInfo = Info("checkpoint.info", "Gespeicherter Punkt");
        _pointName = new CheatOption
        {
            Id = "checkpoint.save", Label = "Checkpoint speichern", Kind = OptionKind.Text,
            Description = "Name eingeben und ausführen. Leer erzeugt einen neuen Namen; ein bestehender Name wird in diesem Level ersetzt.",
            IsAvailable = () => Alive && _checkpoints.LoadError.Length == 0,
            OnInvoke = option => Execute(option, SavePoint)
        };
        _pointChoice = new CheatOption
        {
            Id = "checkpoint.select", Label = "Checkpoint", Kind = OptionKind.Choice,
            IsAvailable = () => Ready && _levelPoints.Length != 0, OnChanged = _ => TickExtras()
        };
        return new CheatCategory("Checkpoints").Add(_positionInfo).Add(_pointName).Add(_pointChoice).Add(_pointInfo)
            .Add(Button("checkpoint.teleport", "Zum Checkpoint teleportieren", () => Alive && SelectedPoint != null && SelectedPoint.Level == LevelKey, TeleportToPoint))
            .Add(Button("checkpoint.delete", "Ausgewählten Checkpoint löschen", () => Ready && SelectedPoint != null && SelectedPoint.Level == LevelKey, () =>
            {
                var point = SelectedPoint;
                _checkpoints.Delete(point);
                RefreshPoints();
                return "Checkpoint gelöscht: " + point.Name;
            }));
    }

    private void ScanPrefabs()
    {
        var runner = Runner;
        if (!runner || runner.Prefabs == null) return;
        if (!_catalogRunner || _catalogRunner.Pointer != runner.Pointer)
        {
            _catalogRunner = runner;
            _scanIndex = 0;
            _pendingPrefabs.Clear();
            _spawnEntries.Clear();
            _lastSpawned = null;
            RefreshSpawnChoices();
        }
        var table = runner.Prefabs;
        int count = Math.Min(table._sources.Count, 5000);
        bool changed = false;
        for (int scanned = 0; scanned < 12 && _scanIndex < count; scanned++)
        {
            int index = _scanIndex++;
            if (!TryCatalogPrefab(table, index, out bool pending)) { if (pending) _pendingPrefabs.Add(index); }
            else changed = true;
        }
        for (int scanned = 0; scanned < 4 && _pendingPrefabs.Count != 0; scanned++)
        {
            int index = _pendingPrefabs[0];
            _pendingPrefabs.RemoveAt(0);
            if (TryCatalogPrefab(table, index, out bool pending)) changed = true;
            else if (pending) _pendingPrefabs.Add(index);
        }
        if (changed) RefreshSpawnChoices();
        _spawnInfo.TextValue = $"{_spawnEntries.Count} Gegenstände · {_scanIndex}/{count} Vorlagen geprüft" +
            (_pendingPrefabs.Count != 0 ? $" · {_pendingPrefabs.Count} werden geladen" : "");
    }
    private bool TryCatalogPrefab(NetworkPrefabTable table, int index, out bool pending)
    {
        pending = false;
        try
        {
            var id = NetworkPrefabId.FromIndex(index);
            var prefab = table.Load(id, false);
            if (!prefab) { pending = true; return false; }
            if (prefab.GetComponent<PlayerData>()) return false;
            bool tool = prefab.GetComponent<ItemBase>();
            if (!tool && !prefab.GetComponent<Pickupable>()) return false;
            if (_spawnEntries.Any(e => e.Id == id)) return false;
            string rawName = prefab.name;
            if (Regex.IsMatch(rawName, @"(?:^|_)(?:Template|Temp)(?:_|\.|$)", RegexOptions.IgnoreCase)) return false;
            string name = Regex.Replace(rawName, @"^(?:P_)?(?:BO_|IO_|HZ_|Item_)", "");
            name = Regex.Replace(name.Replace("_", " "), "([a-z])([A-Z])", "$1 $2").Trim();
            _spawnEntries.Add(new SpawnEntry(id, name, tool ? "Gegenstände & Werkzeuge" : "Materialien & Objekte"));
            return true;
        }
        catch (Exception error)
        {
            Floppy.Core.Log.Warning("Spawn-Vorlage " + index + " nicht verfügbar: " + error.Message);
            return false;
        }
    }
    private void RefreshSpawnChoices()
    {
        if (_spawnChoice == null) return;
        var selected = _spawnChoice.ChoiceIndex >= 0 && _spawnChoice.ChoiceIndex < _visibleEntries.Length ? _visibleEntries[_spawnChoice.ChoiceIndex] : null;
        _visibleEntries = _spawnEntries.Where(e => _spawnGroup.ChoiceIndex == 0 || e.Group == _spawnGroup.Choices[_spawnGroup.ChoiceIndex])
            .OrderBy(e => e.Group).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id.RawValue).ToArray();
        _spawnChoice.Choices = _visibleEntries.Select(e => e.Group + " · " + e.Name + " [" + e.Id.AsIndex + "]").ToArray();
        int index = selected == null ? -1 : Array.FindIndex(_visibleEntries, e => e.Id == selected.Id);
        _spawnChoice.ChoiceIndex = index < 0 ? 0 : index;
    }
    private string SpawnSelected()
    {
        if (!CanSpawn || _spawnChoice.ChoiceIndex < 0 || _spawnChoice.ChoiceIndex >= _visibleEntries.Length)
            throw new InvalidOperationException("Keine spawnfähige Sitzung oder kein Gegenstand ausgewählt.");
        int count = (int)_spawnAmount.NumberValue;
        if (count < 1 || count > 10 || count != _spawnAmount.NumberValue) throw new InvalidOperationException("Anzahl muss eine ganze Zahl von 1 bis 10 sein.");
        var entry = _visibleEntries[_spawnChoice.ChoiceIndex];
        var player = LocalPlayer;
        var runner = Runner;
        var forward = player.transform.forward;
        forward.y = 0;
        if (forward.sqrMagnitude < .01f) forward = Vector3.forward;
        forward.Normalize();
        var right = Vector3.Cross(Vector3.up, forward);
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            var position = player.transform.position + forward * (2.5f + (i / 3) * 1.5f) + right * (count == 1 ? 0 : (i % 3 - 1) * 1.5f) + Vector3.up;
            // Generated interop unboxes every nullable value; its default null argument is invalid.
            var obj = runner.Spawn(entry.Id, new Il2CppSystem.Nullable<Vector3>(position),
                new Il2CppSystem.Nullable<Quaternion>(Quaternion.identity), new Il2CppSystem.Nullable<PlayerRef>(PlayerRef.None));
            if (!obj || !obj.IsValid) throw new InvalidOperationException($"Spiel bestätigte nur {spawned} von {count} Objekten.");
            _lastSpawned = obj;
            _lastSpawnName = entry.Name;
            spawned++;
        }
        return $"{spawned} × {entry.Name} vor deiner Figur gespawnt.";
    }
    private CheatCategory SpawnCategory()
    {
        _spawnInfo = Info("spawn.info", "Verfügbare Gegenstände");
        _lastSpawnInfo = Info("spawn.last", "Zuletzt erzeugter Gegenstand");
        _spawnGroup = new CheatOption
        {
            Id = "spawn.group", Label = "Kategorie", Kind = OptionKind.Choice,
            Choices = new[] { "Alle", "Gegenstände & Werkzeuge", "Materialien & Objekte" }, OnChanged = _ => RefreshSpawnChoices()
        };
        _spawnChoice = new CheatOption { Id = "spawn.item", Label = "Gegenstand", Kind = OptionKind.Choice, IsAvailable = () => CanSpawn && _visibleEntries.Length != 0 };
        _spawnAmount = new CheatOption { Id = "spawn.amount", Label = "Anzahl", Kind = OptionKind.Number, Min = 1, Max = 10, Step = 1, NumberValue = 1 };
        return new CheatCategory("Gegenstände spawnen").Add(_spawnInfo).Add(_spawnGroup).Add(_spawnChoice).Add(_spawnAmount).Add(_lastSpawnInfo)
            .Add(new CheatOption
            {
                Id = "spawn.create", Label = "Vor der Figur spawnen", Kind = OptionKind.Button, Scope = CheatScope.Everyone,
                Description = "Erzeugt echte Spielobjekte für die Runde. Benötigt eine eigene beziehungsweise von dir verwaltete Sitzung.",
                IsAvailable = () => CanSpawn && _visibleEntries.Length != 0, OnInvoke = option => Execute(option, SpawnSelected)
            });
    }
}
