using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Floppy.App;

/// <summary>Findet die Steam-Installation, ihre Bibliotheksordner und die Cover, die Steam
/// sich ohnehin schon heruntergeladen hat. Nichts davon geht ins Netz.</summary>
public static class SteamLibrary
{
    /// <summary>Wo Steam installiert ist - oder null, wenn nichts gefunden wurde.</summary>
    public static string? FindSteamPath()
    {
        // Steam trägt seinen Pfad selbst in die Registry ein.
        string?[] candidates =
        {
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string,
            @"C:\Program Files (x86)\Steam",
            @"C:\Program Files\Steam"
        };

        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string path = candidate.Replace('/', '\\');
            if (Directory.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>Alle Bibliotheksordner - Steam verteilt Spiele auf mehrere Laufwerke.</summary>
    public static List<string> FindLibraryFolders(string steamPath)
    {
        var folders = new List<string>();

        string main = Path.Combine(steamPath, "steamapps");
        if (Directory.Exists(main)) folders.Add(main);

        string vdf = Path.Combine(main, "libraryfolders.vdf");
        if (!File.Exists(vdf)) return folders;

        try
        {
            // Die VDF-Datei ist Valves eigenes Format. Uns interessieren nur die "path"-Zeilen.
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                string path = match.Groups[1].Value.Replace(@"\\", @"\");
                string steamapps = Path.Combine(path, "steamapps");

                if (Directory.Exists(steamapps) &&
                    !folders.Any(f => string.Equals(f, steamapps, StringComparison.OrdinalIgnoreCase)))
                {
                    folders.Add(steamapps);
                }
            }
        }
        catch (IOException) { /* Steam schreibt gerade - beim nächsten Start klappt es */ }

        return folders;
    }

    /// <summary>Ist dieses Spiel installiert? Liefert den Installationsordner zurück.</summary>
    public static string? FindInstallDir(IEnumerable<string> libraryFolders, string appId)
    {
        foreach (string folder in libraryFolders)
        {
            string manifest = Path.Combine(folder, $"appmanifest_{appId}.acf");
            if (!File.Exists(manifest)) continue;

            try
            {
                var match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
                if (!match.Success) continue;

                string dir = Path.Combine(folder, "common", match.Groups[1].Value);
                if (!Directory.Exists(dir)) continue;

                return OrdnerMitExe(dir);
            }
            catch (IOException) { }
        }

        return null;
    }

    /// <summary>Der Ordner, in dem die Spiel-Exe wirklich liegt.
    ///
    /// Steams Manifest nennt nur den obersten Ordner, und manche Spiele - How to Fish
    /// zum Beispiel - packen ihre Dateien eine Ebene tiefer. Der Lader muss aber direkt
    /// neben der Exe liegen, sonst passiert schlicht nichts.</summary>
    private static string OrdnerMitExe(string start)
    {
        if (SpielExe(start, SearchOption.TopDirectoryOnly) != null) return start;

        string? tiefer = SpielExe(start, SearchOption.AllDirectories);
        return tiefer != null ? Path.GetDirectoryName(tiefer)! : start;
    }

    /// <summary>Die erste Exe, die nach dem Spiel selbst aussieht - Absturzhelfer und
    /// mitgelieferte Installationsprogramme zählen nicht.</summary>
    private static string? SpielExe(string ordner, SearchOption tiefe)
    {
        string[] ignorieren = { "UnityCrashHandler", "vc_redist", "DXSETUP", "dotnet", "crashpad" };

        try
        {
            return Directory
                .EnumerateFiles(ordner, "*.exe", tiefe)
                .Where(f => !ignorieren.Any(i =>
                    Path.GetFileName(f).StartsWith(i, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => f.Count(c => c == Path.DirectorySeparatorChar))
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Das Hochformat-Cover, das Steam in seiner Bibliothek zeigt.
    ///
    /// Steam legt die Bilder unter appcache\librarycache\&lt;appid&gt;\&lt;hash&gt;\ ab -
    /// die Ordnernamen sind Prüfsummen, deshalb wird gesucht statt geraten.</summary>
    public static string? FindCoverImage(string steamPath, string appId)
    {
        string cache = Path.Combine(steamPath, "appcache", "librarycache", appId);
        if (!Directory.Exists(cache)) return null;

        // Reihenfolge nach Eignung: Hochformat zuerst, dann Querformat, dann Logo.
        string[] preferred = { "library_capsule.jpg", "library_header.jpg", "logo.png" };

        foreach (string name in preferred)
        {
            try
            {
                string? hit = Directory
                    .EnumerateFiles(cache, name, SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (hit != null) return hit;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return null;
    }
}
