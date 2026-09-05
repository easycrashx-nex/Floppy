using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using Floppy.Core.Api;
using HarmonyLib;
using UnityEngine;

namespace Floppy.Core
{
    [BepInPlugin("dev.easycrashx.crashbox", "Floppy", "1.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static Harmony Harmony { get; private set; }

        private ExternalOverlay _externalOverlay;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Der engine-freie Teil kennt BepInEx nicht - hier wird sein Protokoll
            // an unseres angeschlossen.
            Core.Log.OnInfo = Logger.LogInfo;
            Core.Log.OnWarning = Logger.LogWarning;
            Core.Log.OnError = Logger.LogError;
            Harmony = new Harmony("dev.easycrashx.crashbox");

            _externalOverlay = new ExternalOverlay();

            LoadModules();

            // External control is the only menu; old disabled-IPC settings no longer apply.
            IpcServer.Start(externalOverlay: true, overlayChanged: _externalOverlay.SetOpen);

            Log.LogInfo("Floppy bereit. Bedienung über die externe Desktop-App.");
        }

        /// <summary>Sucht neben der Core-DLL nach Spielmodulen und nimmt das, dessen
        /// ProductName zum laufenden Spiel passt.</summary>
        private void LoadModules()
        {
            string product = Application.productName;
            string folder = Path.GetDirectoryName(Info.Location);
            var found = new List<IGameModule>();

            foreach (string file in Directory.GetFiles(folder, "Floppy.*.dll", SearchOption.AllDirectories))
            {
                // Unsere eigenen Bausteine sind keine Spielmodule
                string name = Path.GetFileName(file);
                if (name.Equals("Floppy.Model.dll", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Floppy.Unity.", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    var types = assembly.GetTypes()
                        .Where(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                    foreach (var type in types)
                        found.Add((IGameModule)Activator.CreateInstance(type));
                }
                catch (Exception ex)
                {
                    Log.LogWarning("Modul " + Path.GetFileName(file) + " nicht ladbar: " + ex.Message);
                }
            }

            var match = found.FirstOrDefault(m =>
                string.Equals(m.ProductName, product, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                Log.LogWarning("Kein Modul für \"" + product + "\" gefunden. " +
                               "Gefundene Module: " + (found.Count == 0 ? "keine" : string.Join(", ", found.Select(m => m.ProductName))));
                return;
            }

            try
            {
                match.Initialize();
                Registry.SetModule(match);
                Log.LogInfo("Modul geladen: " + match.DisplayName + " (" +
                            Registry.Categories.Sum(c => c.Options.Count) + " Cheats)");
            }
            catch (Exception ex)
            {
                Log.LogError("Modul " + match.DisplayName + " ist beim Start abgestürzt: " + ex);
            }
        }

        private void Update()
        {
            // Arbeit der externen App einsammeln, solange wir sicher im Mainthread sind.
            Dispatcher.Pump();
            IpcServer.TickOverlay();

            if (Registry.Module != null)
            {
                try { Registry.Module.Update(); }
                catch (Exception ex) { Log.LogError("Fehler im Modul-Update: " + ex); }
            }
        }

        private void LateUpdate() => _externalOverlay?.Tick();

        private void OnDestroy()
        {
            IpcServer.Stop();
            Dispatcher.Pump();
            _externalOverlay?.Dispose();
            Harmony?.UnpatchSelf();
        }
    }
}
