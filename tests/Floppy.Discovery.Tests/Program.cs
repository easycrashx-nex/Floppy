using System.Security.AccessControl;
using System.Security.Principal;
using Floppy.App;

internal static class Program
{
    private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "Floppy.Discovery.Tests-" + Guid.NewGuid().ToString("N"));
    private static readonly (string AppId, string Exe, string? Marker)[] Games =
    {
        ("4001890", "How to Fish.exe", "How to Fish_Data"),
        ("4502710", "Stonewards.exe", "Stonewards_Data"),
        ("2896260", "ODDCORE.exe", "ODDCORE_Data"),
        ("4026250", "projectpitt.exe", "projectpitt.pck"),
        ("2584270", "MortalShell2-Win64-Shipping.exe", null),
        ("2211170", "Unrailed2.exe", null)
    };
    private static int _checks;

    private static int Main()
    {
        var cases = new (string Name, Action Run)[]
        {
            ("known games resolve past unrelated launchers and nested folders", KnownGames),
            ("foreign executables and incomplete game folders are not installations", MissingGames),
            ("Steam libraries preserve spaces, Unicode and escaped paths", LibraryPaths),
            ("manual folders resolve the selected game without Steam", ManualFolders),
            ("unreadable Steam metadata cannot abort discovery", DeniedMetadata)
        };
        Directory.CreateDirectory(Scratch);
        int passed = 0;
        try
        {
            foreach (var test in cases)
            {
                try { test.Run(); passed++; Console.WriteLine("PASS " + test.Name); }
                catch (Exception error) { Console.Error.WriteLine("FAIL " + test.Name + ": " + error.Message); }
            }
            Console.WriteLine($"{passed}/{cases.Length} discovery groups; {_checks} checks; temporary files only, no games or registry queried");
            return passed == cases.Length ? 0 : 1;
        }
        finally
        {
            string resolved = Path.GetFullPath(Scratch);
            string prefix = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "Floppy.Discovery.Tests-");
            if (resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) Directory.Delete(resolved, true);
        }
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static string Library(string name)
    {
        string path = Path.Combine(Scratch, name, "steamapps");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Manifest(string library, string appId, string installName)
    {
        File.WriteAllText(Path.Combine(library, "appmanifest_" + appId + ".acf"), "\"AppState\" { \"installdir\" \"" + installName + "\" }");
        string path = Path.Combine(library, "common", installName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void GameFiles(string directory, string exe, string? marker)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, exe), "fixture, never executed");
        if (marker?.EndsWith("_Data", StringComparison.Ordinal) == true) Directory.CreateDirectory(Path.Combine(directory, marker));
        else if (marker != null) File.WriteAllText(Path.Combine(directory, marker), "fixture");
    }

    private static void KnownGames()
    {
        string library = Library("known games");
        foreach (var (id, exe, marker) in Games)
        {
            string root = Manifest(library, id, "Install " + id);
            File.WriteAllText(Path.Combine(root, "launcher.exe"), "unrelated fixture");
            string actual = Path.Combine(root, "Spiel Ä", "Binaries", "Win64");
            GameFiles(actual, exe, marker);
            Check(SteamLibrary.FindInstallDir(new[] { library }, id) == actual,
                id + ": the expected game files must win over a generic root launcher");
        }
    }

    private static void MissingGames()
    {
        string library = Library("incomplete");
        foreach (var (id, exe, marker) in Games)
        {
            string root = Manifest(library, id, "Missing " + id);
            File.WriteAllText(Path.Combine(root, "other-game.exe"), "unrelated fixture");
            Check(SteamLibrary.FindInstallDir(new[] { library }, id) == null, id + ": an unrelated EXE cannot identify the game");
            if (marker == null) continue;
            File.WriteAllText(Path.Combine(root, exe), "incomplete fixture");
            Check(SteamLibrary.FindInstallDir(new[] { library }, id) == null, id + ": a missing data/package marker must reject installation");
        }
    }

    private static void LibraryPaths()
    {
        string main = Library("Steam Hauptordner");
        string extra = Library("Bibliothek mit Ä und Leerzeichen");
        string libraryRoot = Path.GetDirectoryName(extra)!;
        File.WriteAllText(Path.Combine(main, "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" +
            libraryRoot.Replace("\\", "\\\\") + "\" } \"1\" { \"path\" \"" + libraryRoot.Replace("\\", "\\\\") + "\" } }");
        var folders = SteamLibrary.FindLibraryFolders(Path.GetDirectoryName(main)!);
        Check(folders.SequenceEqual(new[] { main, extra }), "multiple drives/paths must be unescaped and deduplicated");
        string game = Manifest(extra, "2211170", "Unrailed! 2 Ä");
        GameFiles(game, "Unrailed2.exe", null);
        Check(SteamLibrary.FindInstallDir(folders, "2211170") == game, "a game in another library with Unicode and spaces must resolve");
    }

    private static void DeniedMetadata()
    {
        string first = Library("unreadable");
        string second = Library("readable");
        string firstGame = Manifest(first, "2211170", "first");
        GameFiles(firstGame, "Unrailed2.exe", null);
        string secondGame = Manifest(second, "2211170", "second");
        GameFiles(secondGame, "Unrailed2.exe", null);
        DenyRead(Path.Combine(first, "appmanifest_2211170.acf"), () =>
            Check(SteamLibrary.FindInstallDir(new[] { first, second }, "2211170") == secondGame,
                "an unreadable manifest must not prevent checking another library"));

        string vdf = Path.Combine(first, "libraryfolders.vdf");
        File.WriteAllText(vdf, "\"libraryfolders\" {}");
        DenyRead(vdf, () => Check(SteamLibrary.FindLibraryFolders(Path.GetDirectoryName(first)!).SequenceEqual(new[] { first }),
            "an unreadable library index must preserve the readable main library"));
    }

    private static void ManualFolders()
    {
        string parent = Path.Combine(Scratch, "Manuell Ä");
        string actual = Path.Combine(parent, "How to Fish");
        GameFiles(actual, "How to Fish.exe", "How to Fish_Data");
        var manual = new Dictionary<string, string> { ["How to Fish"] = parent };
        var game = GameCatalog.Scan(manual, steamPath: null).Single(g => g.ProductName == "How to Fish");
        Check(game.Installed && game.InstallDir == actual, "manual discovery must normalize a parent folder even without Steam");
        string foreign = Path.Combine(Scratch, "Fremdes Spiel");
        GameFiles(foreign, "other-game.exe", null);
        manual["How to Fish"] = foreign;
        game = GameCatalog.Scan(manual, steamPath: null).Single(g => g.ProductName == "How to Fish");
        Check(!game.Installed && game.InstallDir == null, "an existing manual directory with another EXE must not count as installed");
        manual["How to Fish"] = Path.Combine(Scratch, "nicht vorhanden");
        game = GameCatalog.Scan(manual, steamPath: null).Single(g => g.ProductName == "How to Fish");
        Check(!game.Installed, "a missing saved folder must remain unavailable");
    }

    private static void DenyRead(string path, Action run)
    {
        // Only the named temporary fixture file changes ACL; the exact original descriptor is restored.
        var file = new FileInfo(path);
        byte[] descriptor = file.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorBinaryForm();
        var original = new FileSecurity();
        original.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Access);
        var denied = new FileSecurity();
        denied.SetSecurityDescriptorBinaryForm(descriptor, AccessControlSections.Access);
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            file.SetAccessControl(denied);
            bool blocked = false;
            try { File.ReadAllText(path); }
            catch (UnauthorizedAccessException) { blocked = true; }
            Check(blocked, "the fixture ACL must actually reproduce read access denial");
            run();
        }
        finally { file.SetAccessControl(original); }
    }
}
