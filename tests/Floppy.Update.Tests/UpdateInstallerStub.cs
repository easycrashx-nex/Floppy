namespace Floppy.App;

// The production installer is deliberately not linked into this assembly.
internal static class UpdateInstaller
{
    public static void Start(string path, string hash, string version)
        => throw new InvalidOperationException("A test attempted to start the production installer path.");
}
