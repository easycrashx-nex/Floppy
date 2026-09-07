using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Floppy.Unreal;

internal static class Probe
{
    // Explicit opt-in: regular tests never inspect a real game.
    internal static int Run(string[] args)
    {
        if (args.Length is < 1 or > 2 || args[0] != "--probe-game")
        {
            Console.Error.WriteLine("Usage: Floppy.Unreal.Tests [--probe-game [report.json]]");
            return 2;
        }
        var spiel = new Spiel();
        var watch = Stopwatch.StartNew();
        try
        {
            bool found = spiel.Verbinde();
            string report = JsonSerializer.Serialize(new
            {
                connected = spiel.Verbunden,
                ready = spiel.Bereit,
                found,
                error = spiel.LetzterFehler,
                pid = spiel.Sp.Pid,
                executable = spiel.Sp.Programmpfad,
                objects = spiel.Ref?.Anzahl ?? 0,
                world = spiel.Welt,
                player = spiel.Figur.ToString("X"),
                controller = spiel.Steuerung.ToString("X"),
                root = spiel.Wurzel.ToString("X"),
                positionOffset = spiel.PositionAbstand,
                attributes = spiel.Attribute.Select(a => new { set = a.Satz, name = a.Name, value = a.Wert(spiel.Sp) }),
                items = spiel.Gegenstaende.Count,
                elapsedMs = watch.ElapsedMilliseconds
            }, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(report);
            if (args.Length == 2) File.WriteAllText(Path.GetFullPath(args[1]), report);
            return spiel.Bereit ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 2; }
        finally { spiel.Trenne(); }
    }
}
