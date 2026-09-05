using System;
using System.IO;
using System.Linq;
using System.Text;
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
            var root = (Grid)window.Content;
            foreach (double width in new[] { 980.0, 1180.0 })
            {
                root.Measure(new Size(width, 760));
                root.Arrange(new Rect(0, 0, width, 760));
                root.UpdateLayout();
                var actions = ((Grid)root.Children[0]).Children.OfType<WrapPanel>().Single();
                Check(actions.ActualWidth <= width, "WPF-Fensterbreite " + width);
            }
            string[] required = {
                "runtime/mono/plugins/Floppy.Model.dll", "runtime/mono/plugins/Floppy.Unity.Mono.dll",
                "runtime/mono/plugins/Floppy.HowToFish.dll", "runtime/mono/plugins/Floppy.Stonewards.dll",
                "runtime/il2cpp/plugins/Floppy.Model.dll", "runtime/il2cpp/plugins/Floppy.Unity.IL2CPP.dll",
                "runtime/il2cpp/plugins/Floppy.Oddcore.dll", "runtime/pitt/floppy.gd", "runtime/pitt/floppy_overlay.gd"
            };
            foreach (string path in required) Check(File.Exists(Path.Combine(AppContext.BaseDirectory, path)), path);
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
