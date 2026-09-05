using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Floppy.Unrailed2
{
    /// <summary>Ändert nur die Entwicklerschalter und bewahrt die erste Originalfassung.</summary>
    internal static class Einstellungen
    {
        internal static readonly string[] Schalter = { "EnableWebDebug", "EnableCheats" };
        internal static string Sicherung(string datei) => datei + ".floppy.bak";

        internal static byte[] Lies(string datei)
        {
            try { return File.ReadAllBytes(datei); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        internal static string[] Zeilen(byte[] daten)
        {
            if (daten == null) return Array.Empty<string>();
            using var reader = new StreamReader(new MemoryStream(daten), Encoding.UTF8, true);
            return reader.ReadToEnd().Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        }

        private static string Name(string zeile) => zeile.TrimStart().Split(new[] { ' ', '\t' }, 2)[0];

        internal static bool Angeschaltet(string datei)
        {
            var zeilen = Zeilen(Lies(datei));
            return Schalter.All(name => zeilen.Any(z =>
                Name(z) == name && z.Trim().Substring(name.Length).Trim().Equals("True", StringComparison.OrdinalIgnoreCase)));
        }

        internal static void Setze(string datei, bool an)
        {
            // Ein Lesefehler ist kein leeres Dokument: in diesem Fall nichts schreiben.
            var original = Lies(datei);
            var zeilen = Zeilen(original).Where(z => !Schalter.Contains(Name(z))).ToList();
            while (zeilen.Count > 0 && zeilen[zeilen.Count - 1].Length == 0) zeilen.RemoveAt(zeilen.Count - 1);
            if (an) zeilen.AddRange(Schalter.Select(name => name + " True"));

            string ordner = Path.GetDirectoryName(Path.GetFullPath(datei));
            Directory.CreateDirectory(ordner);
            if (original != null) SchreibeAtomar(Sicherung(datei), original, nurNeu: true);

            SchreibeAtomar(datei, Encoding.UTF8.GetBytes(string.Join(Environment.NewLine, zeilen) + Environment.NewLine));
        }

        internal static void StelleWiederHer(string datei)
        {
            var original = Lies(Sicherung(datei));
            if (original == null) throw new FileNotFoundException("Keine Sicherung der Einstellungen vorhanden");
            SchreibeAtomar(datei, original);
        }

        private static void SchreibeAtomar(string datei, byte[] daten, bool nurNeu = false)
        {
            if (nurNeu && File.Exists(datei)) return;
            string temp = datei + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(daten, 0, daten.Length);
                    stream.Flush(true);
                }
                // Replace erhält den bisherigen Inhalt vollständig, wenn das Ersetzen scheitert.
                if (nurNeu)
                {
                    try { File.Move(temp, datei); }
                    catch (IOException) when (File.Exists(datei)) { }
                }
                else if (File.Exists(datei)) File.Replace(temp, datei, null);
                else File.Move(temp, datei);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }
    }
}
