using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Floppy.Core.Api;
using Floppy.Core.Menu;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace Floppy.Core
{
    /// <summary>Floppy für IL2CPP-Spiele.
    ///
    /// Der Unterschied zur Mono-Ausführung liegt nur im Gerüst: dort erbt das Plugin von
    /// einem MonoBehaviour und bekommt Update und OnGUI geschenkt. Hier gibt es das nicht -
    /// die Laufzeit kennt unsere .NET-Klassen gar nicht. Deshalb melden wir eine eigene
    /// Komponente bei IL2CPP an und hängen sie an ein Objekt in der Szene.
    ///
    /// Menü, Cheat-Modell und Fernsteuerung sind identisch - derselbe Quellcode.</summary>
    [BepInPlugin("dev.easycrashx.floppy", "Floppy", "1.1.0")]
    public class Plugin : BasePlugin
    {
        public static Plugin Instance { get; private set; }
        public static Harmony Harmony { get; private set; }

        public static ConfigEntry<KeyCode> ToggleKey;
        public static ConfigEntry<bool> EnableRemoteApp;

        public override void Load()
        {
            Instance = this;
            Harmony = new Harmony("dev.easycrashx.floppy");

            // Der engine-freie Teil kennt BepInEx nicht - hier wird sein Protokoll
            // an unseres angeschlossen.
            Core.Log.OnInfo = Log.LogInfo;
            Core.Log.OnWarning = Log.LogWarning;
            Core.Log.OnError = Log.LogError;

            ToggleKey = Config.Bind("Bedienung", "Menue-Taste", KeyCode.F1,
                "Taste, die das Overlay im Spiel öffnet und schließt.");

            EnableRemoteApp = Config.Bind("Bedienung", "Externe App erlauben", true,
                "Lässt die Floppy-Desktop-App sich lokal verbinden (127.0.0.1:" + IpcServer.Port + ").");

            LoadModules();

            if (EnableRemoteApp.Value)
                IpcServer.Start();

            // Eigene Komponente bei der Laufzeit anmelden und in die Szene hängen -
            // erst dadurch bekommen wir überhaupt Update und OnGUI.
            ClassInjector.RegisterTypeInIl2Cpp<FloppyRunner>();

            var traeger = new GameObject("Floppy");
            UnityEngine.Object.DontDestroyOnLoad(traeger);
            traeger.hideFlags = HideFlags.HideAndDontSave;
            traeger.AddComponent<FloppyRunner>();

            Core.Log.Info("Floppy bereit. Menü mit " + ToggleKey.Value + " öffnen.");
        }

        /// <summary>Sucht neben der Plugin-DLL nach Spielmodulen und nimmt das, dessen
        /// ProductName zum laufenden Spiel passt.</summary>
        private void LoadModules()
        {
            string product = Application.productName;
            string folder = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
            var gefunden = new List<IGameModule>();

            foreach (string file in Directory.GetFiles(folder, "Floppy.*.dll", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);

                // Unsere eigenen Bausteine sind keine Spielmodule
                if (name.Equals("Floppy.Model.dll", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("Floppy.Unity.", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var assembly = Assembly.LoadFrom(file);
                    var typen = assembly.GetTypes()
                        .Where(t => typeof(IGameModule).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

                    foreach (var typ in typen)
                        gefunden.Add((IGameModule)Activator.CreateInstance(typ));
                }
                catch (Exception ex)
                {
                    Core.Log.Warning("Modul " + name + " nicht ladbar: " + ex.Message);
                }
            }

            var passend = gefunden.FirstOrDefault(m =>
                string.Equals(m.ProductName, product, StringComparison.OrdinalIgnoreCase));

            if (passend == null)
            {
                Core.Log.Warning("Kein Modul für \"" + product + "\" gefunden. Gefunden: " +
                                 (gefunden.Count == 0 ? "keine" : string.Join(", ", gefunden.Select(m => m.ProductName))));
                return;
            }

            try
            {
                passend.Initialize();
                Registry.SetModule(passend);
                Core.Log.Info("Modul geladen: " + passend.DisplayName + " (" +
                              Registry.Categories.Sum(c => c.Options.Count) + " Cheats)");
            }
            catch (Exception ex)
            {
                Core.Log.Error("Modul " + passend.DisplayName + " ist beim Start abgestürzt: " + ex);
            }
        }

        public override bool Unload()
        {
            IpcServer.Stop();
            Harmony?.UnpatchSelf();
            return true;
        }
    }

    /// <summary>Der Träger für Update und OnGUI. IL2CPP-Komponenten brauchen diesen
    /// Zeiger-Konstruktor, sonst kann die Laufzeit sie nicht erzeugen.</summary>
    public class FloppyRunner : MonoBehaviour
    {
        public FloppyRunner(IntPtr handle) : base(handle) { }

        private readonly Overlay _overlay = new Overlay();

        private void Update()
        {
            // Arbeit der externen App einsammeln, solange wir sicher im Mainthread sind.
            Dispatcher.Pump();

            if (Input.GetKeyDown(Plugin.ToggleKey.Value))
                _overlay.Visible = !_overlay.Visible;

            if (Registry.Module != null)
            {
                try { Registry.Module.Update(); }
                catch (Exception ex) { Log.Error("Fehler im Modul-Update: " + ex); }
            }
        }

        private void OnGUI()
        {
            Menu.SelfTest.Draw();
            _overlay.Draw();
        }
    }
}
