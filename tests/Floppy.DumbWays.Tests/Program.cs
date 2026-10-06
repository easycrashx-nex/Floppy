using Floppy.DumbWays;

string root = Path.Combine(Path.GetTempPath(), "Floppy-DumbWays-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int checks = 0;
void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
void Reject(Action action, string label)
{
    try { action(); }
    catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException) { checks++; return; }
    throw new Exception(label);
}
try
{
    string path = Path.Combine(root, "checkpoints.json");
    var store = new CheckpointStore(path);
    Check(store.ForLevel("Lobby").Length == 0, "empty store");
    store.Save(new("Lobby", "Lager äöü \"1\"", 12.25f, -3.5f, 6));
    store.Save(new("Level1", "Lager äöü \"1\"", 20, 21, 22));
    var loaded = new CheckpointStore(path);
    Check(loaded.LoadError == "", "load valid JSON");
    Check(loaded.ForLevel("Lobby").Single().X == 12.25f, "position persists exactly");
    Check(loaded.ForLevel("Level1").Single().X == 20, "levels stay isolated with identical names");
    loaded.Save(new("Lobby", "lager äöü \"1\"", 99, 98, 97));
    Check(loaded.ForLevel("Lobby").Length == 1 && loaded.ForLevel("Lobby").Single().X == 99, "case-insensitive replacement");
    loaded.Delete(loaded.ForLevel("Lobby").Single());
    Check(new CheckpointStore(path).ForLevel("Lobby").Length == 0 && loaded.ForLevel("Level1").Length == 1, "delete affects selected level only");
    foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f })
        Reject(() => loaded.Save(new("Level1", "Invalid", value, 0, 0)), "invalid position accepted");
    foreach (string name in new[] { "", " ", "x\ny", new string('x', 49) })
        Reject(() => loaded.Save(new("Level1", name, 0, 0, 0)), "invalid name accepted");
    byte[] original = File.ReadAllBytes(path);
    using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        Reject(() => loaded.Save(new("Level1", "Blocked", 0, 0, 0)), "locked file accepted");
    Check(File.ReadAllBytes(path).SequenceEqual(original), "failed write preserves disk");
    Check(loaded.ForLevel("Level1").Length == 1, "failed write preserves memory");
    Check(!Directory.EnumerateFiles(root, "*.tmp").Any(), "failed write leaves no temporary file");
    foreach (string corrupt in new[]
    {
        "broken", "{\"format\":2,\"points\":[]}",
        "{\"format\":1,\"points\":[{\"level\":\"A\",\"name\":\"N\",\"x\":100001,\"y\":0,\"z\":0}]}",
        "{\"format\":1,\"points\":[{\"level\":\"A\",\"name\":\"N\",\"x\":0,\"y\":0,\"z\":0},{\"level\":\"A\",\"name\":\"n\",\"x\":1,\"y\":0,\"z\":0}]}"
    })
    {
        File.WriteAllText(path, corrupt);
        var invalid = new CheckpointStore(path);
        Check(invalid.LoadError.Length != 0 && invalid.ForLevel("A").Length == 0, "corrupt store exposed positions");
        Reject(() => invalid.Save(new("A", "Overwrite", 0, 0, 0)), "corrupt store overwritten");
        Check(File.ReadAllText(path) == corrupt, "corrupt source preserved");
    }
    string boundedPath = Path.Combine(root, "bounded.json");
    var bounded = new CheckpointStore(boundedPath);
    for (int i = 0; i < 128; i++) bounded.Save(new("Level1", "Point " + i, i, 0, 0));
    Reject(() => bounded.Save(new("Level2", "Overflow", 0, 0, 0)), "capacity limit ignored");
    bounded.Save(new("Level1", "Point 0", 10, 0, 0));
    Check(new CheckpointStore(boundedPath).ForLevel("Level1").Length == 128, "replace at capacity survives restart");
    string largePath = Path.Combine(root, "large.json");
    var large = new CheckpointStore(largePath);
    bool sizeRejected = false;
    for (int i = 0; i < 128; i++)
    {
        try { large.Save(new(new string('界', 512), "Point " + i, i, 0, 0)); }
        catch (InvalidOperationException) { sizeRejected = true; break; }
    }
    Check(sizeRejected, "UTF-8 byte limit enforced before writing unreadable data");
    Check(new CheckpointStore(largePath).LoadError == "", "size rejection preserves loadable previous data");
    string saves = Path.Combine(root, "saves"), backups = Path.Combine(root, "backups");
    Directory.CreateDirectory(Path.Combine(saves, "Steam"));
    byte[] saveBytes = { 0, 1, 255, 42, 17 };
    File.WriteAllBytes(Path.Combine(saves, "Steam", "Charlie.data"), saveBytes);
    File.WriteAllText(Path.Combine(saves, "Player.log"), "not a save");
    string backup = SaveBackup.Create(saves, backups);
    Check(File.ReadAllBytes(Path.Combine(backup, "Steam", "Charlie.data")).SequenceEqual(saveBytes), "save backup is byte exact");
    Check(File.ReadAllBytes(Path.Combine(saves, "Steam", "Charlie.data")).SequenceEqual(saveBytes), "backup preserves source");
    Check(File.Exists(Path.Combine(backup, "Sicherung.json")) && !File.Exists(Path.Combine(backup, "Player.log")), "manifest exists and logs excluded");
    Check(SaveBackup.Create(saves, backups) != backup, "backups never overwrite each other");
    Reject(() => SaveBackup.Create(saves, Path.Combine(saves, "nested")), "recursive backup accepted");
    string emptySaves = Path.Combine(root, "empty");
    Directory.CreateDirectory(emptySaves);
    Reject(() => SaveBackup.Create(emptySaves, backups), "empty backup accepted");
    using (var locked = new FileStream(Path.Combine(saves, "Steam", "Charlie.data"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        Reject(() => SaveBackup.Create(saves, backups), "locked save accepted");
    for (int i = 0; i < 257; i++) File.WriteAllBytes(Path.Combine(emptySaves, i + ".data"), Array.Empty<byte>());
    Reject(() => SaveBackup.Create(emptySaves, backups), "unbounded file count accepted");
    Console.WriteLine($"PASS: {checks} checkpoint and backup checks; temporary files only, no game accessed.");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally { Directory.Delete(root, true); }
