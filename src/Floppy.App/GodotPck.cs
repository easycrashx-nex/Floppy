using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Floppy.App;

/// <summary>Copies an unencrypted Godot 4 PCK v4, replacing only Floppy scripts and its autoload entry.</summary>
internal static class GodotPck
{
    private const int HeaderSize = 112;
    private sealed record Entry(string Name, long Offset, long Size, byte[] Md5, uint Flags);

    public static void Patch(string source, string destination, string scripts)
    {
        byte[] main = File.ReadAllBytes(Path.Combine(scripts, "floppy.gd"));
        byte[] overlay = File.ReadAllBytes(Path.Combine(scripts, "floppy_overlay.gd"));
        using var input = File.OpenRead(source);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var (header, basis, entries) = ReadIndex(reader);
        var settings = entries.SingleOrDefault(e => e.Name == "res://project.binary" || e.Name == "project.binary")
            ?? throw new InvalidDataException("Godot-Projekteinstellungen fehlen im Paket.");
        if (settings.Size > 16 * 1024 * 1024) throw new InvalidDataException("Godot-Projekteinstellungen sind unerwartet groß.");
        input.Position = basis + settings.Offset;
        byte[] configuration = AddAutoload(reader.ReadBytes(checked((int)settings.Size)));
        var replacements = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [settings.Name] = configuration,
            ["res://floppy.gd"] = main,
            ["res://floppy_overlay.gd"] = overlay
        };
        foreach (string name in replacements.Keys)
            if (!entries.Any(e => e.Name == name)) entries.Add(new(name, 0, 0, new byte[16], 0));

        using (var output = File.Create(destination))
        using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
        {
            // Preserve engine version and reserved header fields; only offsets change.
            BitConverter.GetBytes((ulong)HeaderSize).CopyTo(header, 24);
            writer.Write(header);
            var written = new List<Entry>();
            foreach (var entry in entries)
            {
                long offset = output.Position - HeaderSize;
                long size = entry.Size;
                byte[] md5 = entry.Md5;
                if (replacements.TryGetValue(entry.Name, out byte[]? replacement))
                {
                    writer.Write(replacement);
                    size = replacement.Length;
                    md5 = MD5.HashData(replacement);
                }
                else
                {
                    input.Position = basis + entry.Offset;
                    CopyBytes(input, output, size);
                }
                written.Add(entry with { Offset = offset, Size = size, Md5 = md5 });
                while (output.Position % 16 != 0) writer.Write((byte)0);
            }
            long index = output.Position;
            writer.Write(written.Count);
            foreach (var entry in written)
            {
                byte[] name = Encoding.UTF8.GetBytes(entry.Name);
                int length = (name.Length + 3) / 4 * 4;
                writer.Write(length);
                writer.Write(name);
                writer.Write(new byte[length - name.Length]);
                writer.Write((ulong)entry.Offset);
                writer.Write((ulong)entry.Size);
                writer.Write(entry.Md5);
                writer.Write(entry.Flags);
            }
            output.Position = 32;
            writer.Write((ulong)index);
            output.Flush(flushToDisk: true);
        }
        // A malformed result never reaches the installation transaction.
        using var check = new BinaryReader(File.OpenRead(destination), Encoding.UTF8);
        var (_, _, verified) = ReadIndex(check);
        if (verified.Count != entries.Count || replacements.Keys.Any(name => verified.Count(e => e.Name == name) != 1))
            throw new InvalidDataException("Das vorbereitete Godot-Paket ist unvollständig.");
    }

    private static (byte[] Header, long Basis, List<Entry> Entries) ReadIndex(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        byte[] header = reader.ReadBytes(HeaderSize);
        if (header.Length != HeaderSize || Encoding.ASCII.GetString(header, 0, 4) != "GDPC" ||
            BitConverter.ToInt32(header, 4) != 4 || BitConverter.ToInt32(header, 8) != 4 ||
            BitConverter.ToUInt32(header, 20) != 2)
            throw new InvalidDataException("Unterstützt werden unverschlüsselte Godot-4-Pakete im PCK-Format 4. Das Original bleibt unverändert.");
        long basis = checked((long)BitConverter.ToUInt64(header, 24));
        long index = checked((long)BitConverter.ToUInt64(header, 32));
        if (basis < HeaderSize || index < basis || index > stream.Length - 4)
            throw new InvalidDataException("Ungültige Paketgrenzen.");
        stream.Position = index;
        uint count = reader.ReadUInt32();
        if (count > 1_000_000) throw new InvalidDataException("Zu viele Paketeinträge.");
        var entries = new List<Entry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (uint i = 0; i < count; i++)
        {
            uint length = reader.ReadUInt32();
            if (length == 0 || length > 16384) throw new InvalidDataException("Ungültiger Dateiname im Paket.");
            byte[] nameBytes = reader.ReadBytes(checked((int)length));
            if (nameBytes.Length != length) throw new EndOfStreamException();
            string name = new UTF8Encoding(false, true).GetString(nameBytes).TrimEnd('\0');
            long offset = checked((long)reader.ReadUInt64());
            long size = checked((long)reader.ReadUInt64());
            byte[] md5 = reader.ReadBytes(16);
            uint flags = reader.ReadUInt32();
            if (flags != 0 || offset < 0 || offset > index - basis || size < 0 || size > index - basis - offset || !names.Add(name))
                throw new InvalidDataException("Verschlüsselter, doppelter oder beschädigter Paketeintrag: " + name);
            entries.Add(new(name, offset, size, md5, flags));
        }
        return (header, basis, entries);
    }

    private static byte[] AddAutoload(byte[] bytes)
    {
        using var source = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(source, Encoding.UTF8);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "ECFG") throw new InvalidDataException("Godot-Projekteinstellungen sind beschädigt.");
        uint count = reader.ReadUInt32();
        if (count > 100000) throw new InvalidDataException("Zu viele Projekteinstellungen.");
        var values = new List<(byte[] Key, byte[] Value)>();
        for (uint i = 0; i < count; i++)
        {
            byte[] key = ReadBlob(reader);
            byte[] value = ReadBlob(reader);
            if (Encoding.UTF8.GetString(key) != "autoload/Floppy") values.Add((key, value));
        }
        if (source.Position != source.Length) throw new InvalidDataException("Unbekannte zusätzliche Projekteinstellungen.");
        byte[] script = Encoding.UTF8.GetBytes("*res://floppy.gd");
        using var variant = new MemoryStream();
        using (var writer = new BinaryWriter(variant, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(4); // Godot Variant.Type.STRING
            writer.Write(script.Length);
            writer.Write(script);
            writer.Write(new byte[(4 - script.Length % 4) % 4]);
        }
        values.Add((Encoding.UTF8.GetBytes("autoload/Floppy"), variant.ToArray()));
        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("ECFG"));
            writer.Write(values.Count);
            foreach (var (key, value) in values)
            {
                writer.Write(key.Length); writer.Write(key);
                writer.Write(value.Length); writer.Write(value);
            }
        }
        return result.ToArray();
    }

    private static byte[] ReadBlob(BinaryReader reader)
    {
        uint length = reader.ReadUInt32();
        if (length > reader.BaseStream.Length - reader.BaseStream.Position) throw new EndOfStreamException();
        return reader.ReadBytes(checked((int)length));
    }
    private static void CopyBytes(Stream source, Stream destination, long count)
    {
        byte[] buffer = new byte[128 * 1024];
        while (count > 0)
        {
            int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (read == 0) throw new EndOfStreamException();
            destination.Write(buffer, 0, read);
            count -= read;
        }
    }
}
