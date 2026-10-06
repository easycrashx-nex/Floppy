using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Floppy.Core.Api;
using FS.Build;
using FS.Build.Certification;
using PlaySide.Platform;
using PlaySide.Utilities.Assets;
using UnityEngine;

namespace Floppy.DumbWays;

public sealed partial class DumbWaysModule
{
    private CheatOption _unlockInfo, _unlockBackupInfo;
    private UnlockableItemConfig[] _unlockItems = Array.Empty<UnlockableItemConfig>();
    private CertificationConfig[] _unlockCertificates = Array.Empty<CertificationConfig>();
    private LevelSelectScreen _levelScreen;
    private float _nextUnlockScan;
    private CertificationService Certifications => CertificationService.TryGetInstance(out var service) && service.IsReady ? service : null;
    private bool CanUnlockItems => _supported && Certifications != null && BufferedSaveDataService.Instance != null && _unlockCertificates.Length != 0;
    private bool CanUnlockLevels => Ready && GameplayManager.Instance && GameplayManager.Instance.HasStateAuthority &&
        DifficultyManager.Instance && DifficultyManager.Instance.HasStateAuthority && _levelScreen && GameplayManager.Instance.SceneList;

    private void TickUnlocks()
    {
        if (_unlockInfo == null) return;
        if (Time.unscaledTime >= _nextUnlockScan)
        {
            _nextUnlockScan = Time.unscaledTime + 5;
            _unlockItems = Resources.FindObjectsOfTypeAll<UnlockableItemConfig>().Where(item => item &&
                item.Visability != UnlockableItemConfig.VisabilityState.Unavailable && string.IsNullOrEmpty(item.TwitchDropBenefitId)).ToArray();
            var certificates = new Dictionary<int, CertificationConfig>();
            foreach (var database in Resources.FindObjectsOfTypeAll<CertificationDatabase>())
            {
                var entries = database.ForCertifications().GetEnumerator();
                var cursor = entries.Cast<Il2CppSystem.Collections.IEnumerator>();
                while (cursor.MoveNext())
                {
                    var config = entries.Current;
                    if (config && config.AssetId.IsValid) certificates[config.AssetId._id] = config;
                }
            }
            foreach (var item in _unlockItems)
                if (item.UnlockingCertification && item.UnlockingCertification.AssetId.IsValid)
                    certificates[item.UnlockingCertification.AssetId._id] = item.UnlockingCertification;
            _unlockCertificates = certificates.Values.ToArray();
            _levelScreen = Resources.FindObjectsOfTypeAll<LevelSelectScreen>().FirstOrDefault(screen => screen && screen.gameObject.scene.IsValid());
        }
        var service = Certifications;
        if (service == null) { _unlockInfo.TextValue = "Spielprofil wird geladen."; return; }
        int unlocked = _unlockCertificates.Count(service.IsCertificationUnlocked);
        int cosmetics = _unlockItems.Count(item => item.IsUnlocked);
        string levels = "Level: eigene Lobby laden";
        if (CanUnlockLevels)
        {
            var manager = GameplayManager.Instance;
            int count = manager.SceneList.AvailableScenes.Count, normal = 0, hard = 0;
            for (int i = 0; i < count; i++)
            {
                if (manager.IsCampaignLevelAlreadyPlayable(i, Difficulty.Normal, DifficultyManager.Instance)) normal++;
                if (DifficultyManager.Instance.IsHardModeUnlockedForLevel(i)) hard++;
            }
            levels = $"Level {normal}/{count} · Schwer {hard}/{count}";
        }
        _unlockInfo.TextValue = $"{levels} · Kosmetik & Emotes {cosmetics}/{_unlockItems.Length} · Zertifikate {unlocked}/{_unlockCertificates.Length}";
    }
    private string BackupProgress()
    {
        var saves = BufferedSaveDataService.Instance;
        if (saves == null) throw new InvalidOperationException("Speicherdienst noch nicht bereit.");
        saves.Flush();
        if (saves.HasPendingWork) throw new IOException("Spielstand konnte noch nicht vollständig gespeichert werden.");
        string path = SaveBackup.Create(Application.persistentDataPath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Floppy", "Backups", "DumbWays"));
        _unlockBackupInfo.TextValue = path;
        return path;
    }
    private void SaveCertifications(CertificationService service)
    {
        var saves = BufferedSaveDataService.Instance;
        saves.WriteObject(service._currentUser, CertificationService.CERTIFICATIONS_SAVE_PATH, service._unlockedCertifications);
        saves.Flush();
        if (saves.HasPendingWork) throw new IOException("Speicherung nicht bestätigt. Sicherung: " + _unlockBackupInfo.TextValue);
    }
    private int UnlockItems(string group)
    {
        var service = Certifications ?? throw new InvalidOperationException("Spielprofil nicht bereit.");
        var targets = group == "certificates" ? _unlockCertificates : _unlockItems.Where(item => group == "all" ||
            (group == "emotes" ? item.TryCast<Emote>() != null : item.TryCast<CosmeticItemConfig>() != null))
            .Select(item => item.UnlockingCertification).Where(config => config).ToArray();
        int changed = 0;
        foreach (var config in targets)
        {
            if (service.IsCertificationUnlocked(config)) continue;
            // Local game progress only: do not call UnlockCertification, which can award platform achievements.
            service._unlockedCertifications[config.AssetId] = true;
            changed++;
        }
        if (changed != 0) SaveCertifications(service);
        if (targets.Any(config => !service.IsCertificationUnlocked(config))) throw new InvalidOperationException("Spiel bestätigte nicht alle Freischaltungen.");
        return changed;
    }
    private void UnlockLevels(bool hard)
    {
        if (!CanUnlockLevels) throw new InvalidOperationException("Eigene Lobby mit Levelauswahl benötigt.");
        var manager = GameplayManager.Instance;
        if (hard)
        {
            DifficultyManager.Instance.UnlockHardMode();
            DifficultyManager.Instance.UnlockAllHardModeProgress(manager.SceneList);
            DifficultyManager.Instance.SaveHardModeData();
        }
        else _levelScreen.UnlockAllLevels();
        BufferedSaveDataService.Instance.Flush();
        if (BufferedSaveDataService.Instance.HasPendingWork) throw new IOException("Level-Speicherung nicht bestätigt.");
        for (int i = 0; i < manager.SceneList.AvailableScenes.Count; i++)
            if (hard ? !DifficultyManager.Instance.IsHardModeUnlockedForLevel(i) :
                !manager.IsCampaignLevelAlreadyPlayable(i, Difficulty.Normal, DifficultyManager.Instance))
                throw new InvalidOperationException("Spiel bestätigte nicht alle Level-Freischaltungen.");
        _levelScreen.RefreshUnlockState();
        _levelScreen.RefreshHardModeButtonState();
    }
    private string UnlockProgress(string group)
    {
        BackupProgress();
        int changed = 0;
        if (group is "all" or "levels") UnlockLevels(false);
        if (group is "all" or "hard") UnlockLevels(true);
        if (group is "all" or "cosmetics" or "emotes" or "certificates") changed = UnlockItems(group == "all" ? "certificates" : group);
        TickUnlocks();
        return $"Freischaltungen angewendet · {changed} neue Zertifikate. Sicherung erstellt.";
    }
    private CheatCategory UnlockCategory()
    {
        _unlockInfo = Info("unlock.info", "Freischaltungsstand");
        _unlockBackupInfo = Info("unlock.backup.info", "Letzte Spielstandsicherung");
        _unlockBackupInfo.TextValue = "Wird vor jeder Freischaltung automatisch erstellt.";
        var category = new CheatCategory("Freischaltungen").Add(_unlockInfo);
        foreach (var entry in new[] { ("all", "Alles freischalten"), ("levels", "Alle Level freischalten"),
            ("hard", "Schweren Modus und Level freischalten"), ("cosmetics", "Alle Kosmetik freischalten"),
            ("emotes", "Alle Emotes freischalten"), ("certificates", "Alle lokalen Zertifikate freischalten") })
        {
            string group = entry.Item1;
            var option = Button("unlock." + group, entry.Item2,
                () => CanUnlockItems && (group is not ("all" or "levels" or "hard") || CanUnlockLevels), () => UnlockProgress(group));
            if (group is "all" or "levels" or "hard") option.Scope = CheatScope.Everyone;
            option.Description = "Dauerhafter Spielstand-Fortschritt mit automatischer Sicherung. Keine Twitch-/Besitzfreischaltungen oder Plattform-Erfolge. Level benötigen deine eigene Lobby.";
            category.Add(option);
        }
        return category.Add(Button("unlock.backup", "Spielstand jetzt sichern", () => CanUnlockItems, () => "Sicherung erstellt: " + BackupProgress())).Add(_unlockBackupInfo);
    }
}
