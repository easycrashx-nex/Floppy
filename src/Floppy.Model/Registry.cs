using System;
using System.Collections.Generic;
using System.Linq;
using Floppy.Core.Api;

namespace Floppy.Core
{
    /// <summary>Zentrale Ablage: welches Modul läuft, welche Cheats es gibt,
    /// und wie man sie über ihre ID anspricht. Overlay und IPC benutzen beide nur das hier.</summary>
    public static class Registry
    {
        public static IGameModule Module { get; private set; }
        public static List<CheatCategory> Categories { get; private set; } = new List<CheatCategory>();
        public static int SchemaVersion { get; private set; }

        private static Dictionary<string, CheatOption> _byId =
            new Dictionary<string, CheatOption>(StringComparer.OrdinalIgnoreCase);

        public static string GameName => Module?.DisplayName ?? "Kein Spielmodul geladen";

        public static bool Ready(out string status)
        {
            if (Module == null)
            {
                status = "Kein Modul für dieses Spiel gefunden";
                return false;
            }
            return Module.IsReady(out status);
        }

        public static void SetModule(IGameModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            var previous = Module;
            Module = module;
            try { ReplaceCategories(false); }
            catch { Module = previous; throw; }
        }

        /// <summary>Nach einer Änderung der Spielstruktur, im selben Takt wie Update aufrufen.</summary>
        public static void RefreshCategories()
        {
            if (Module != null) ReplaceCategories(true);
        }

        private static void ReplaceCategories(bool preserveValues)
        {
            var categories = Module.BuildCategories() ?? new List<CheatCategory>();

            // Profile bekommt jedes Spiel automatisch dazu - die Rubrik besteht aus
            // denselben Bausteinen wie die Cheats und erscheint dadurch von selbst
            // im Ingame-Menü und im Client.
            categories = new List<CheatCategory>(categories) { Profile.BaueRubrik() };
            var byId = new Dictionary<string, CheatOption>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in categories.SelectMany(c => c.Options))
            {
                if (option == null || string.IsNullOrEmpty(option.Id))
                    throw new InvalidOperationException("Option hat keine Id");
                if (byId.ContainsKey(option.Id))
                    throw new InvalidOperationException($"Doppelte Cheat-Id '{option.Id}'");
                byId[option.Id] = option;
                if (preserveValues && _byId.TryGetValue(option.Id, out var old) && old.Kind == option.Kind &&
                    option.Kind != OptionKind.Info && option.Kind != OptionKind.Button)
                {
                    var values = OptionValues.Capture(old);
                    if (option.Scope != CheatScope.Selectable) values.Share = false;
                    if (option.Kind == OptionKind.Slider || option.Kind == OptionKind.Number)
                        values.Number = Math.Max(option.Min, Math.Min(option.Max, values.Number));
                    if (option.Kind == OptionKind.Choice)
                    {
                        string selected = old.Choices != null && old.ChoiceIndex >= 0 && old.ChoiceIndex < old.Choices.Length
                            ? old.Choices[old.ChoiceIndex] : null;
                        int index = Array.IndexOf(option.Choices ?? Array.Empty<string>(), selected);
                        values.Choice = index >= 0 ? index : 0;
                    }
                    values.Write(option);
                }
            }
            Categories = categories;
            _byId = byId;
            SchemaVersion++;
        }

        public static CheatOption Find(string id)
        {
            _byId.TryGetValue(id ?? "", out var option);
            return option;
        }

        public static IEnumerable<CheatOption> AllOptions => _byId.Values;
    }
}
