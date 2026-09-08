using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Floppy.App;

/// <summary>Ein Spiel, für das es ein Floppy-Modul gibt.</summary>
public sealed class SupportedGame
{
    public string Name { get; set; } = "";
    public string AppId { get; set; } = "";

    /// <summary>Application.productName im Spiel - so meldet sich das Modul.</summary>
    public string ProductName { get; set; } = "";

    // Wird beim Durchsuchen der Bibliothek gefüllt
    public bool Installed { get; set; }
    public string? InstallDir { get; set; }
    public string? CoverPath { get; set; }

    /// <summary>Was unter dem Namen steht: installiert, läuft gerade, nicht gefunden.</summary>
    public string Status { get; set; } = "";

    public override string ToString() => Name;
}

/// <summary>Welche Spiele Floppy kennt, und welche davon auf diesem Rechner liegen.
///
/// Die Liste steht in games.json neben der Exe. Fehlt die Datei, greift die eingebaute
/// Voreinstellung - die App läuft also auch ohne.</summary>
public static class GameCatalog
{
    private static readonly SupportedGame[] BuiltIn =
    {
        new() { Name = "How to Fish", AppId = "4001890", ProductName = "How to Fish" },
        new() { Name = "ODDCORE",     AppId = "2896260", ProductName = "ODDCORE" },
        new() { Name = "Project P.I.T.T.", AppId = "4026250", ProductName = "Project P.I.T.T." },
        new() { Name = "Stonewards",  AppId = "4502710", ProductName = "Stonewards" },
        new() { Name = "Mortal Shell II", AppId = "2584270", ProductName = "MortalShell2" },
        new() { Name = "Unrailed! 2", AppId = "2211170", ProductName = "Unrailed2" }
    };

    public static string CatalogPath =>
        Path.Combine(AppContext.BaseDirectory, "games.json");

    /// <summary>Lädt die Spieleliste und sucht jedes davon in der Steam-Bibliothek.</summary>
    public static List<SupportedGame> Scan(IReadOnlyDictionary<string, string>? foldersByGame = null)
        => Scan(foldersByGame, SteamLibrary.FindSteamPath());

    // The explicit Steam root also permits isolated discovery tests with temporary libraries.
    internal static List<SupportedGame> Scan(IReadOnlyDictionary<string, string>? foldersByGame, string? steamPath)
    {
        var games = Load();

        var folders = steamPath == null ? new List<string>() : SteamLibrary.FindLibraryFolders(steamPath);

        foreach (var game in games)
        {
            if (string.IsNullOrWhiteSpace(game.AppId)) continue;

            game.InstallDir = foldersByGame != null && foldersByGame.TryGetValue(game.ProductName, out string? manual)
                ? SteamLibrary.FindGameDirectory(manual, game.AppId) : SteamLibrary.FindInstallDir(folders, game.AppId);
            game.Installed = game.InstallDir != null && Directory.Exists(game.InstallDir);

            if (game.Installed && steamPath != null)
                game.CoverPath = SteamLibrary.FindCoverImage(steamPath, game.AppId);
        }

        // Schlicht alphabetisch. Installierte vorzuziehen klingt hilfreich, lässt die
        // Liste aber springen, sobald ein Spiel dazukommt oder verschwindet - und man
        // sucht ein Spiel dort, wo es beim letzten Mal stand.
        return games
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static List<SupportedGame> Load()
    {
        try
        {
            if (File.Exists(CatalogPath))
            {
                string json = File.ReadAllText(CatalogPath);
                var loaded = JsonSerializer.Deserialize<List<SupportedGame>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (loaded is { Count: > 0 })
                {
                    // Neue eingebaute Spiele ergänzen: sonst würde eine alte games.json
                    // aus einer früheren Version jedes später hinzugekommene Spiel
                    // dauerhaft verschlucken.
                    foreach (var eingebaut in BuiltIn)
                    {
                        bool schonDa = loaded.Any(g =>
                            string.Equals(g.AppId, eingebaut.AppId, StringComparison.Ordinal));

                        if (!schonDa)
                            loaded.Add(new SupportedGame
                            {
                                Name = eingebaut.Name,
                                AppId = eingebaut.AppId,
                                ProductName = eingebaut.ProductName
                            });
                    }

                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // Kaputte oder halb geschriebene Datei soll den Start nicht verhindern.
        }

        return BuiltIn.Select(g => new SupportedGame
        {
            Name = g.Name,
            AppId = g.AppId,
            ProductName = g.ProductName
        }).ToList();
    }

    /// <summary>Schreibt die eingebaute Liste als Vorlage heraus, damit du sie erweitern kannst.</summary>
    public static void WriteTemplateIfMissing()
    {
        try
        {
            if (File.Exists(CatalogPath)) return;

            string json = JsonSerializer.Serialize(BuiltIn,
                new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(CatalogPath, json);
        }
        catch (Exception)
        {
            // Kein Schreibrecht - dann bleibt es eben bei der eingebauten Liste.
        }
    }
}
