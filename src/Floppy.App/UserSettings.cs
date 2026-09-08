using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Floppy.App;

public sealed record AppPreferences(bool AutoConnect, bool StartMinimized, bool RememberWindowPosition,
    string OverlayHotkey, bool CheckUpdatesOnStartup);

public sealed class UserSettings
{
    public bool AutoConnect { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool RememberWindowPosition { get; set; } = true;
    public string OverlayHotkey { get; set; } = "F1";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 760;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public bool Maximized { get; set; }
    public string LastGame { get; set; } = "";
    public Dictionary<string, List<string>> Favorites { get; set; } = new();
    public Dictionary<string, Dictionary<string, string>> Shortcuts { get; set; } = new();
    public Dictionary<string, string> GameFolders { get; set; } = new();
    public static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Floppy", "app.json");

    public AppPreferences GetPreferences() => new(AutoConnect, StartMinimized, RememberWindowPosition,
        NormalizeHotkey(OverlayHotkey), CheckUpdatesOnStartup);

    public void ApplyPreferences(AppPreferences preferences)
    {
        AutoConnect = preferences.AutoConnect;
        StartMinimized = preferences.StartMinimized;
        RememberWindowPosition = preferences.RememberWindowPosition;
        OverlayHotkey = NormalizeHotkey(preferences.OverlayHotkey);
        CheckUpdatesOnStartup = preferences.CheckUpdatesOnStartup;
    }

    public static string NormalizeHotkey(string? value)
    {
        string key = value?.Trim().ToUpperInvariant() ?? "";
        // Windows reserves F12 for the debugger, so it cannot be a reliable global hotkey.
        for (int number = 1; number <= 11; number++)
            if (key == "F" + number.ToString(CultureInfo.InvariantCulture)) return key;
        return "F1";
    }

    public static UserSettings Load(string? path = null)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path ?? FilePath)) ?? new();
            settings.Favorites ??= new();
            settings.Shortcuts ??= new();
            settings.GameFolders ??= new();
            settings.OverlayHotkey = NormalizeHotkey(settings.OverlayHotkey);
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { return new(); }
    }

    public bool Save(out string error, string? path = null)
    {
        path ??= FilePath;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { error = ex.Message; return false; }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
}

public static class NumberInput
{
    // Gruppierungszeichen sind absichtlich nicht erlaubt: 1,5 darf nie 15 werden.
    public static bool TryParse(string text, out double number) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float,
            CultureInfo.InvariantCulture, out number) && double.IsFinite(number);
}
