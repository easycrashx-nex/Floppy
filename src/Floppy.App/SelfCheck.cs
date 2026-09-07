using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Floppy.App;

/// <summary>Prüft die ausgelieferte App ohne Verbindung, Fensteranzeige oder Änderung eines Spiels.</summary>
internal static class SelfCheck
{
    internal static int Run(string? reportPath)
    {
        var report = new StringBuilder();
        int errors = 0;
        void Check(bool ok, string label) { report.AppendLine((ok ? "OK " : "FAIL ") + label); if (!ok) errors++; }
        try
        {
            Check(NumberInput.TryParse("1,5", out double number) && number == 1.5, "Dezimalkomma");
            Check(!NumberInput.TryParse("Infinity", out _), "Ungültige Zahl abgewiesen");
            var window = new MainWindow(offline: true);
            Check(new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle() != IntPtr.Zero,
                "Windows-Fensterinitialisierung ohne Anzeige");
            var root = (Grid)window.Content;
            foreach (var size in new[] { new Size(980, 560), new Size(1180, 760), new Size(1440, 900), new Size(960, 520) })
            {
                root.Measure(size);
                root.Arrange(new Rect(new Point(), size));
                root.UpdateLayout();
                Rect Bounds(string name)
                {
                    var element = window.FindName(name) as FrameworkElement
                        ?? throw new InvalidOperationException("WPF-Element fehlt: " + name);
                    return element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                }
                string[] regions = { "LibraryPane", "HeaderPanel", "HeaderActions", "SearchBox", "CategoriesPane", "OptionsPane", "SessionBar" };
                Check(regions.Select(Bounds).All(b => b.Width > 0 && b.Height > 0 && b.Left >= -.5 && b.Top >= -.5
                    && b.Right <= root.ActualWidth + .5 && b.Bottom <= root.ActualHeight + .5), "WPF-Layout " + size);
                bool Separate(string a, string b)
                {
                    Rect first = Bounds(a), second = Bounds(b);
                    return first.Right <= second.Left + .5 || second.Right <= first.Left + .5
                        || first.Bottom <= second.Top + .5 || second.Bottom <= first.Top + .5;
                }
                Check(Separate("LibraryPane", "CategoriesPane") && Separate("CategoriesPane", "OptionsPane")
                    && Separate("HeaderPanel", "OptionsPane") && Separate("HeaderActions", "OptionsPane")
                    && Separate("SearchBox", "HeaderActions") && Separate("SessionBar", "OptionsPane")
                    && Separate("LaunchButton", "ConnectButton") && Separate("AllOffButton", "ToolsButton"),
                    "WPF-Bereiche ohne Überlappung " + size);
            }
            string catalogPath = Path.Combine(AppContext.BaseDirectory, "Assets", "MortalShell2", "items.json");
            Check(File.Exists(catalogPath), "Itemkatalog im Paket");
            if (File.Exists(catalogPath))
            {
                using var catalog = JsonDocument.Parse(File.ReadAllText(catalogPath));
                var rows = catalog.RootElement.EnumerateObject().ToArray();
                var items = ItemCatalog.FromChoices(rows.Select(row => row.Name).ToArray());
                int expectedIcons = rows.Count(row => row.Value.TryGetProperty("icon", out var icon)
                    && icon.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(icon.GetString()));
                var images = new ItemImageConverter();
                Check(items.Count(item => item.IconPath != null) == expectedIcons && items.Where(item => item.IconPath != null)
                    .All(item => images.Convert(item.IconPath!, typeof(object), null!, CultureInfo.InvariantCulture) != null),
                    $"Itembilder vollständig und lesbar ({expectedIcons})");
                var picker = new ItemPicker(items, items.Count > 0 ? 0 : -1);
                picker.Measure(new Size(340, 500));
                picker.Arrange(new Rect(0, 0, 340, 500));
                picker.UpdateLayout();
                Check(items.Count > 0 && ((ListBox)picker.FindName("ItemList")).Items.Count == items.Count,
                    "Gegenstandsauswahl mit ausgeliefertem Katalog");
            }
            string[] required = {
                "runtime/mono/plugins/Floppy.Model.dll", "runtime/mono/plugins/Floppy.Unity.Mono.dll",
                "runtime/mono/plugins/Floppy.HowToFish.dll", "runtime/mono/plugins/Floppy.Stonewards.dll",
                "runtime/il2cpp/plugins/Floppy.Model.dll", "runtime/il2cpp/plugins/Floppy.Unity.IL2CPP.dll",
                "runtime/il2cpp/plugins/Floppy.Oddcore.dll", "runtime/pitt/floppy.gd"
            };
            foreach (string path in required) Check(File.Exists(Path.Combine(AppContext.BaseDirectory, path)), path);
            Check(!File.Exists(Path.Combine(AppContext.BaseDirectory, "runtime/pitt/floppy_overlay.gd")),
                "Kein altes P.I.T.T.-Menü im Paket");
            foreach (string kind in new[] { "mono", "il2cpp" })
            {
                string path = Path.Combine(AppContext.BaseDirectory, "runtime", kind);
                Check(Directory.Exists(path) && Directory.EnumerateFiles(path, "BepInEx-*.zip").Any(), kind + " Loader");
            }
        }
        catch (Exception ex) { report.AppendLine(ex.ToString()); errors++; }
        report.AppendLine("Fehler: " + errors);
        if (reportPath != null)
        {
            try { File.WriteAllText(reportPath, report.ToString()); }
            catch (Exception) { errors++; }
        }
        return errors == 0 ? 0 : 1;
    }
}
