using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Floppy.Core;

namespace Floppy.DumbWays;

internal static class SaveBackup
{
    internal static string Create(string sourceRoot, string backupRoot)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        backupRoot = Path.GetFullPath(backupRoot);
        if (!Directory.Exists(sourceRoot)) throw new IOException("Spielstandordner fehlt.");
        if (backupRoot == sourceRoot || backupRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Sicherung muss außerhalb des Spielstandordners liegen.");
        var pending = new Stack<string>();
        var files = new List<string>();
        pending.Push(sourceRoot);
        int visited = 0;
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            if (++visited > 256) throw new IOException("Zu viele Spielstandordner.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Verknüpfte Spielstandordner können nicht gesichert werden.");
            foreach (string child in Directory.EnumerateDirectories(directory)) pending.Push(child);
            files.AddRange(Directory.EnumerateFiles(directory, "*.data"));
            if (files.Count > 256) throw new IOException("Zu viele Spielstanddateien.");
        }
        if (files.Count == 0) throw new IOException("Keine Spielstanddateien.");
        if (files.Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0 || new FileInfo(p).Length > 32 * 1024 * 1024) ||
            files.Sum(p => new FileInfo(p).Length) > 128 * 1024 * 1024)
            throw new IOException("Spielstanddateien können nicht sicher gesichert werden.");
        string destination = Path.Combine(backupRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var entries = files.Select(file =>
        {
            string relative = Path.GetRelativePath(sourceRoot, file);
            byte[] data = File.ReadAllBytes(file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { output.Write(data); output.Flush(true); }
            string hash = Convert.ToHexString(SHA256.HashData(data));
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) != hash) throw new IOException("Sicherung konnte nicht bestätigt werden.");
            return new Json.Writer().Set("path", relative).Set("sha256", hash).ToString();
        }).ToArray();
        File.WriteAllText(Path.Combine(destination, "Sicherung.json"), new Json.Writer().Set("format", 1)
            .Raw("files", Json.Array(entries)).ToString());
        return destination;
    }
}
