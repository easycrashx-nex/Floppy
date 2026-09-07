using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Floppy.App;

public sealed record ItemEntry(int Index, string Id, string Name, string Category, string? IconPath);

/// <summary>Presentation metadata only. The game's original choice index and ID remain authoritative.</summary>
public static class ItemCatalog
{
    public const string AllCategories = "Alle Gegenstände";
    private const string Other = "Sonstiges";
    private static readonly Lazy<IReadOnlyDictionary<string, Metadata>> Bundled =
        new(() => ReadMetadata(AppContext.BaseDirectory));

    // Conservative grouping of the observed IDs; uncertain items remain in Other.
    // Bundled metadata can replace these labels without changing the selection identity.
    private static readonly IReadOnlyDictionary<string, string> KnownCategories = BuildCategories();
    private sealed record Metadata(string? Name, string? Category, string? Icon);

    public static IReadOnlyList<ItemEntry> FromChoices(string[]? choices) =>
        Create(choices, Bundled.Value, AppContext.BaseDirectory);

    internal static IReadOnlyList<ItemEntry> FromChoices(string[]? choices, string baseDirectory) =>
        Create(choices, ReadMetadata(baseDirectory), baseDirectory);

    public static IReadOnlyList<ItemEntry> Filter(IEnumerable<ItemEntry> entries, string category, string query)
    {
        string search = query?.Trim() ?? "";
        bool all = string.IsNullOrWhiteSpace(category) || string.Equals(category, AllCategories, StringComparison.OrdinalIgnoreCase);
        return entries.Where(entry =>
            (all || string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase)) &&
            (search.Length == 0 || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                entry.Category.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
    }

    private static IReadOnlyList<ItemEntry> Create(string[]? choices, IReadOnlyDictionary<string, Metadata> metadata, string baseDirectory)
    {
        if (choices == null || choices.Length == 0) return Array.Empty<ItemEntry>();
        var entries = new ItemEntry[choices.Length];
        for (int i = 0; i < choices.Length; i++)
        {
            string id = choices[i] ?? "";
            metadata.TryGetValue(id, out var item);
            string name = item?.Name ?? Humanize(id);
            string category = item?.Category ?? (KnownCategories.TryGetValue(id, out string? known) ? known : Other);
            entries[i] = new ItemEntry(i, id, name, category, IconPath(baseDirectory, item?.Icon));
        }
        return entries;
    }

    private static IReadOnlyDictionary<string, string> BuildCategories()
    {
        var categories = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string category, params string[] ids)
        {
            foreach (string id in ids) categories.Add(id, category);
        }
        Add("Währungen", "Gloom", "Coin", "Glimpses");
        Add("Heilung & Verbrauch", "Mango", "Moonshine", "BagheadMoonshine",
            "Tarspore", "Weltcap", "Trollweed", "ParadoxicalScripture", "UniqueScripture", "ShellRespec");
        Add("Materialien", "Ventrium", "Laterite", "Dorsalite", "Thoracium", "ThoraciumPrime", "Tarcore");
        // These definitions are under PermProgression; the forge items below alias unlock definitions.
        Add("Verbesserungen", "AshMartyrs", "Bloodseed", "BurnedEffigy", "StrangeGear", "HealingAmount", "HealingCharge", "Sheep", "Grisha");
        Add("Sammlerstücke", "VartkoFeetPic1", "VartkoFeetPic2", "VartkoFeetPic3", "VartkoFeetPic4", "VartkoFeetPic5", "VartkoFeetPic6");
        Add("Artefakte", "CommonArtifact", "RareArtifact", "AncientArtifact", "EpicArtifact", "PearlArtifact",
            "LegendaryArtifact", "FinalBossArtifact", "TwisSestersArtifact", "MonolithArtifact", "OffspringArtifact", "ParasiteGolemArtifact");
        Add("Schlüssel & Quests", "MushroomVillageKey", "MissingLever", "MissingCannonBall", "GraveyardCryptKey",
            "GrishaCageKey", "TreasureRoomKeyTiel", "MarrowKey", "VlasCageKey");
        Add("Karten & Freischaltungen", "TarforgeUnlockWeapon", "TarforgeUnlockSidearm", "TarforgeUnlockSmelt",
            "TarforgeUnlockTemper", "MapLocationHeavyHammer", "MapLocationMartyrBlade", "MapLocationAxeDagger",
            "MapLocationBlackNeedle", "FastTravel", "FoundryStone", "MuradeanActuator", "EtchingNeedles", "ObsidianLathe");
        return categories;
    }

    private static string Humanize(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "Unbenannter Gegenstand";
        var name = new StringBuilder(id.Length + 8);
        for (int i = 0; i < id.Length; i++)
        {
            char current = id[i];
            if (current is '_' or '-' || char.IsWhiteSpace(current))
            {
                if (name.Length > 0 && name[name.Length - 1] != ' ') name.Append(' ');
                continue;
            }
            if (i > 0 && name.Length > 0 && name[name.Length - 1] != ' ')
            {
                char before = id[i - 1];
                bool word = char.IsUpper(current) && (char.IsLower(before) ||
                    (char.IsUpper(before) && i + 1 < id.Length && char.IsLower(id[i + 1])));
                bool number = char.IsDigit(current) && char.IsLetter(before);
                bool afterNumber = char.IsLetter(current) && char.IsDigit(before);
                if (word || number || afterNumber) name.Append(' ');
            }
            name.Append(current);
        }
        string result = name.ToString().Trim();
        return result.Length == 0 ? "Unbenannter Gegenstand" : result;
    }

    private static IReadOnlyDictionary<string, Metadata> ReadMetadata(string baseDirectory)
    {
        var metadata = new Dictionary<string, Metadata>(StringComparer.Ordinal);
        try
        {
            string path = Path.Combine(baseDirectory, "Assets", "MortalShell2", "items.json");
            using var file = File.OpenRead(path);
            using var json = JsonDocument.Parse(file);
            if (json.RootElement.ValueKind != JsonValueKind.Object) return metadata;
            foreach (var item in json.RootElement.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.Object) continue;
                metadata[item.Name] = new Metadata(Text(item.Value, "name"), Text(item.Value, "category"), Text(item.Value, "icon"));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { /* Optional assets must never prevent the game's choice list from opening. */ }
        return metadata;
    }

    private static string? Text(JsonElement item, string key)
    {
        if (!item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return null;
        string? text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string? IconPath(string baseDirectory, string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        string name = icon.Replace('\\', '/');
        if (name.StartsWith("icons/", StringComparison.Ordinal)) name = name.Substring(6);
        if (name.Contains('/') || name.Contains(':') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !string.Equals(Path.GetExtension(name), ".png", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            string directory = Path.GetFullPath(Path.Combine(baseDirectory, "Assets", "MortalShell2", "icons"));
            string path = Path.GetFullPath(Path.Combine(directory, name));
            return string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase) && File.Exists(path)
                ? path : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return null; }
    }
}
