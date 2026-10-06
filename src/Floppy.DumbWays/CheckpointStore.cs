using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Floppy.Core;

namespace Floppy.DumbWays;

/// <summary>Named positions grouped by level, with bounded input and atomic writes.</summary>
internal sealed class CheckpointStore
{
    internal sealed class Point
    {
        internal string Level { get; }
        internal string Name { get; }
        internal float X { get; }
        internal float Y { get; }
        internal float Z { get; }
        internal Point(string level, string name, float x, float y, float z)
        { Level = level; Name = name; X = x; Y = y; Z = z; }
    }
    private readonly string _path;
    private List<Point> _points = new();
    internal string LoadError { get; private set; } = "";
    internal CheckpointStore(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > 128 * 1024) throw new FormatException("Checkpoint-Datei zu groß.");
            var json = Json.Parse(File.ReadAllText(path));
            if (!json.TryGetValue("format", out var format) || !(format is double version) || version != 1 ||
                !json.TryGetValue("points", out var rows) || !(rows is List<object> list) || list.Count > 128)
                throw new FormatException("Checkpoint-Format ungültig.");
            foreach (var row in list)
            {
                if (!(row is Dictionary<string, object> values)) throw new FormatException("Checkpoint ungültig.");
                var point = new Point(Text(values, "level"), Text(values, "name"), Number(values, "x"), Number(values, "y"), Number(values, "z"));
                Validate(point);
                if (_points.Any(p => p.Level == point.Level && string.Equals(p.Name, point.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new FormatException("Checkpoint doppelt vorhanden.");
                _points.Add(point);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            _points.Clear();
            LoadError = "Checkpoints konnten nicht geladen werden: " + error.Message;
        }
    }
    private static string Text(Dictionary<string, object> data, string key) =>
        data.TryGetValue(key, out var value) && value is string text ? text : throw new FormatException("Checkpoint-Text fehlt.");
    private static float Number(Dictionary<string, object> data, string key) =>
        data.TryGetValue(key, out var value) && value is double number && double.IsFinite(number) && Math.Abs(number) <= 100000
            ? (float)number : throw new FormatException("Checkpoint-Koordinate ungültig.");
    private static void Validate(Point point)
    {
        if (string.IsNullOrWhiteSpace(point.Level) || point.Level.Length > 512 ||
            string.IsNullOrWhiteSpace(point.Name) || point.Name.Length > 48 || point.Name.Any(char.IsControl) ||
            !float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) ||
            Math.Abs(point.X) > 100000 || Math.Abs(point.Y) > 100000 || Math.Abs(point.Z) > 100000)
            throw new ArgumentException("Ungültiger Checkpoint: Name maximal 48 Zeichen, Position muss gültig sein.");
    }
    internal Point[] ForLevel(string level) => _points.Where(p => p.Level == level).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    internal void Save(Point point)
    {
        Validate(point);
        var next = _points.Where(p => p.Level != point.Level || !string.Equals(p.Name, point.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        next.Add(point);
        if (next.Count > 128) throw new InvalidOperationException("Maximal 128 Checkpoints. Lösche zuerst einen alten Punkt.");
        Write(next);
    }
    internal void Delete(Point point) => Write(_points.Where(p => p != point).ToList());
    private void Write(List<Point> next)
    {
        if (LoadError.Length != 0) throw new InvalidOperationException(LoadError + " Die vorhandene Datei bleibt erhalten.");
        string json = new Json.Writer().Set("format", 1).Raw("points", Json.Array(next.Select(p => new Json.Writer()
            .Set("level", p.Level).Set("name", p.Name).Set("x", p.X).Set("y", p.Y).Set("z", p.Z).ToString()))).ToString();
        if (Encoding.UTF8.GetByteCount(json) > 128 * 1024) throw new InvalidOperationException("Checkpoint-Datei zu groß. Lösche zuerst alte Punkte.");
        Directory.CreateDirectory(Path.GetDirectoryName(_path));
        string temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, _path, true);
            _points = next;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
