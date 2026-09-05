// Minimal stand-in for the three Unity properties used by ExternalOverlay.
// No Unity runtime or game process is loaded by the tests.
namespace UnityEngine
{
    internal enum CursorLockMode { None, Locked, Confined }
    internal static class Application
    {
        public static bool runInBackground { get; set; }
    }
    internal static class Cursor
    {
        public static bool visible { get; set; }
        public static CursorLockMode lockState { get; set; }
    }
}
