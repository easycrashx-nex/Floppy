using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Floppy.Stonewards
{
    /// <summary>Sich an einen anderen Ort setzen - zu etwas Bestimmtem oder zu einem
    /// selbst gemerkten Punkt.
    ///
    /// Der CharacterController rechnet die Position selbst weiter und würde eine
    /// Änderung sofort überschreiben. Deshalb wird er kurz abgeschaltet, die Position
    /// gesetzt und er wieder eingeschaltet - so macht es das Spiel an seinen eigenen
    /// Umsetzpunkten auch.
    ///
    /// Betrifft nur dich: Deine Position meldet das Spiel ohnehin selbst weiter.</summary>
    internal static class Sprung
    {
        private sealed class Ort
        {
            public string Name;
            public Vector3 Stelle;
        }

        private static readonly List<Ort> _orte = new List<Ort>();

        /// <summary>In welcher Szene die gemerkten Orte gelten.
        ///
        /// Ein Punkt aus der letzten Karte ist in der nächsten wertlos - dort liegt an
        /// denselben Koordinaten etwas völlig anderes, im Zweifel Fels. Deshalb werden
        /// sie beim Kartenwechsel verworfen.</summary>
        private static string _szene = "";

        public static void PruefeSzene()
        {
            string jetzt = SceneManager.GetActiveScene().name;
            if (jetzt == _szene) return;

            _szene = jetzt;

            if (_orte.Count > 0)
            {
                Floppy.Core.Log.Info("Floppy: Karte gewechselt, " + _orte.Count +
                                     " gemerkte Orte verworfen");
                _orte.Clear();
            }
        }

        public static string[] Namen()
        {
            return _orte.Select(o => o.Name).ToArray();
        }

        public static int Anzahl => _orte.Count;

        public static string Merke(string name)
        {
            var spieler = Game.LocalPlayer;
            if (spieler == null) return "Keine Runde";

            PruefeSzene();

            name = (name ?? "").Trim();
            if (name.Length == 0) name = "Ort " + (_orte.Count + 1);

            // Gleicher Name: überschreiben statt doppelt anlegen
            var vorhanden = _orte.FirstOrDefault(o =>
                string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

            if (vorhanden != null)
            {
                vorhanden.Stelle = spieler.transform.position;
                return "\"" + name + "\" überschrieben";
            }

            _orte.Add(new Ort { Name = name, Stelle = spieler.transform.position });
            return "\"" + name + "\" gemerkt";
        }

        public static string SpringeZu(int index)
        {
            if (_orte.Count == 0) return "Noch kein Ort gemerkt";

            var ort = _orte[Mathf.Clamp(index, 0, _orte.Count - 1)];
            return Setze(ort.Stelle, "\"" + ort.Name + "\"", hoehe: 0f);
        }

        public static string Loesche(int index)
        {
            if (_orte.Count == 0) return "Noch kein Ort gemerkt";

            int i = Mathf.Clamp(index, 0, _orte.Count - 1);
            string name = _orte[i].Name;
            _orte.RemoveAt(i);

            return "\"" + name + "\" gelöscht";
        }

        public static string LoescheAlle()
        {
            int anzahl = _orte.Count;
            _orte.Clear();

            return anzahl == 0 ? "Es war nichts gemerkt" : anzahl + " Orte gelöscht";
        }

        public static string Text()
        {
            if (_orte.Count == 0) return "keine gemerkt";
            return _orte.Count + " auf dieser Karte";
        }

        // ---------------------------------------------------------------- Umsetzen

        private static string Setze(Vector3 ziel, string was, float hoehe = 1.5f)
        {
            var spieler = Game.LocalPlayer;
            if (spieler == null) return "Keine Runde";

            var controller = spieler.Controller;
            if (controller == null) return "Keine Steuerung gefunden";

            bool warAn = controller.enabled;
            controller.enabled = false;
            spieler.transform.position = ziel + Vector3.up * hoehe;
            controller.enabled = warAn;

            return "Bei " + was;
        }

        public static string ZumKoenig()
        {
            var koenig = Game.Koenig;
            if (koenig == null) return "Kein König in dieser Runde";
            return Setze(koenig.transform.position + koenig.transform.forward * 3f, "König");
        }

        /// <summary>Zur nächsten Truhe - in allen drei Gestalten, die das Spiel kennt.</summary>
        public static string ZurNaechstenTruhe()
        {
            var kandidaten = new List<Component>();

            kandidaten.AddRange(UnityEngine.Object.FindObjectsOfType<Chest>(true));
            kandidaten.AddRange(UnityEngine.Object.FindObjectsOfType<TreasureChest>(true));
            kandidaten.AddRange(UnityEngine.Object.FindObjectsOfType<DiggingWaypointChest>(true));

            return ZumNaechsten(kandidaten.ToArray(), "Truhe");
        }

        public static string ZurNaechstenBeute()
        {
            return ZumNaechsten(UnityEngine.Object.FindObjectsOfType<PickableItem>(), "Beute");
        }

        /// <summary>Zum nächstgelegenen Objekt der Liste.</summary>
        private static string ZumNaechsten(Component[] kandidaten, string was)
        {
            var spieler = Game.LocalPlayer;
            if (spieler == null) return "Keine Runde";

            Vector3 hier = spieler.transform.position;
            Component naechster = null;
            float besteEntfernung = float.MaxValue;

            foreach (var kandidat in kandidaten)
            {
                if (kandidat == null) continue;

                float entfernung = Vector3.Distance(hier, kandidat.transform.position);

                // Was direkt vor der Nase liegt, ist als Ziel sinnlos
                if (entfernung < 3f || entfernung >= besteEntfernung) continue;

                besteEntfernung = entfernung;
                naechster = kandidat;
            }

            if (naechster == null) return "Nichts in Reichweite";
            return Setze(naechster.transform.position, was);
        }
    }
}
