using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
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
            RegistryPath(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath"),
            RegistryPath(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
            RegistryPath(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath"),
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

    private static string? RegistryPath(string key, string name)
    {
        try { return Registry.GetValue(key, name, null) as string; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return null; }
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
        catch (UnauthorizedAccessException) { /* Andere Bibliotheken bleiben nutzbar. */ }
        catch (SecurityException) { }

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

                string? installed = FindGameDirectory(dir, appId);
                if (installed != null) return installed;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }

        return null;
    }

    /// <summary>Der Ordner, in dem die Spiel-Exe wirklich liegt.
    ///
    /// Steams Manifest nennt nur den obersten Ordner, und manche Spiele - How to Fish
    /// zum Beispiel - packen ihre Dateien eine Ebene tiefer. Der Lader muss aber direkt
    /// neben der Exe liegen, sonst passiert schlicht nichts.</summary>
    public static string? FindGameDirectory(string start, string appId)
    {
        if (!Directory.Exists(start)) return null;
        (string? exe, string? marker) = appId switch
        {
            "4001890" => ("How to Fish.exe", "How to Fish_Data"),
            "4502710" => ("Stonewards.exe", "Stonewards_Data"),
            "2896260" => ("ODDCORE.exe", "ODDCORE_Data"),
            "4026250" => ("projectpitt.exe", "projectpitt.pck"),
            "2584270" => ("MortalShell2-Win64-Shipping.exe", null),
            "2211170" => ("Unrailed2.exe", null),
            "1912410" => ("Dungeons-Win64-Shipping.exe", null),
            "4412320" => ("Dumb Ways to Build.exe", "Dumb Ways to Build_Data"),
            _ => (null, null)
        };
        if (exe == null)
        {
            string? candidate = SpielExe(start, SearchOption.TopDirectoryOnly) ?? SpielExe(start, SearchOption.AllDirectories);
            return candidate == null ? null : Path.GetDirectoryName(candidate);
        }
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, MatchCasing = MatchCasing.CaseInsensitive
            };
            return Directory.EnumerateFiles(start, exe, options)
                .Select(Path.GetDirectoryName)
                .Where(dir => dir != null && (marker == null ||
                    (marker.EndsWith("_Data", StringComparison.Ordinal)
                        ? Directory.Exists(Path.Combine(dir, marker)) : File.Exists(Path.Combine(dir, marker)))))
                .OrderBy(dir => dir!.Count(c => c == Path.DirectorySeparatorChar))
                .ThenBy(dir => dir, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return null; }
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
