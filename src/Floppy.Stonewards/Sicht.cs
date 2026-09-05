using System;
using System.Collections.Generic;
using UnityEngine;

namespace Floppy.Stonewards
{
    /// <summary>Markierungen für Gegner, Truhen, Beute, Mitspieler und Fundstellen.
    ///
    /// Rein bei dir: Es wird nichts am Spiel geändert, nur zusätzlich auf deinen
    /// Bildschirm gezeichnet. Mitspieler sehen davon nichts.
    ///
    /// Dass die Markierungen durch Wände zu sehen sind, ergibt sich von selbst - was in
    /// OnGUI gezeichnet wird, liegt über dem fertigen Bild und weiß von Wänden nichts.
    /// Die Umrisse sind deshalb ein echtes Durchsehen und kein Nachbau.</summary>
    internal class Sicht : MonoBehaviour
    {
        public static bool Gegner;
        public static bool Truhen;
        public static bool Beute;
        public static bool Mitspieler;
        public static bool Haustiere;
        public static bool Fundstellen;

        public static bool Umrisse;              // Kästen statt Punkte
        public static float Reichweite = 120f;

        public static int Schriftgroesse = 11;
        public static float Punktgroesse = 4f;
        public static bool MitEntfernung = true;

        private static GUIStyle _stil;
        private static Texture2D _punkt;

        /// <summary>Ein markiertes Ding. Der Renderer wird mitgeführt, weil sich aus
        /// seinen Ausmaßen der Kasten ergibt - und die wachsen mit, wenn sich etwas
        /// bewegt oder duckt.</summary>
        private struct Ziel
        {
            public Transform Wo;
            public Renderer Umfang;
            public string Text;
            public Color Farbe;
            public float Hoehe;
        }

        private static readonly List<Ziel> _ziele = new List<Ziel>();
        private static float _zuletzt;

        public static void Spawn()
        {
            var traeger = new GameObject("Floppy.Sicht");
            traeger.AddComponent<Sicht>();
            DontDestroyOnLoad(traeger);
        }

        private static bool IrgendwasAn =>
            Gegner || Truhen || Beute || Mitspieler || Haustiere || Fundstellen;

        private void OnGUI()
        {
            if (!IrgendwasAn) return;

            var kamera = Camera.main;
            if (kamera == null || Game.LocalPlayer == null) return;

            Stil();
            Suche();

            Vector3 auge = kamera.transform.position;

            foreach (var ziel in _ziele)
            {
                if (ziel.Wo == null) continue;
                Male(kamera, auge, ziel);
            }
        }

        // ---------------------------------------------------------------- Sammeln

        private static void Suche()
        {
            if (Time.unscaledTime - _zuletzt < 1f) return;
            _zuletzt = Time.unscaledTime;

            _ziele.Clear();

            if (Gegner)
                foreach (var g in UnityEngine.Object.FindObjectsOfType<EnemyController>())
                    if (g != null && !g.isDead)
                        Nimm(g.transform, g.IsEliteBoss ? "Elite" : "Gegner",
                             g.IsEliteBoss ? new Color(1f, 0.4f, 0.2f) : Color.red, 1f);

            if (Truhen)
            {
                // Truhen gibt es in diesem Spiel in mehreren Gestalten: die zum Öffnen,
                // die zum Tragen, und die noch vergrabene Fundstelle. Vorher haben wir
                // nur die erste gesucht - deshalb blieb der Bildschirm leer.
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Chest>(true))
                    if (t != null) Nimm(t.transform, "Truhe", new Color(1f, 0.85f, 0.3f), 0.5f);

                foreach (var t in UnityEngine.Object.FindObjectsOfType<TreasureChest>(true))
                    if (t != null) Nimm(t.transform, "Schatztruhe", new Color(1f, 0.7f, 0.2f), 0.5f);

                foreach (var t in UnityEngine.Object.FindObjectsOfType<DiggingWaypointChest>(true))
                    if (t != null) Nimm(t.transform, "Truhe (vergraben)",
                                        new Color(0.9f, 0.7f, 0.4f), 0.5f);
            }

            if (Beute)
                foreach (var b in UnityEngine.Object.FindObjectsOfType<PickableItem>())
                    if (b != null) Nimm(b.transform, BeuteName(b), new Color(0.6f, 1f, 0.6f), 0.3f);

            if (Haustiere)
                foreach (var h in UnityEngine.Object.FindObjectsOfType<RescuePet>(true))
                    if (h != null) Nimm(h.transform, "Haustier", new Color(1f, 0.6f, 0.9f), 0.5f);

            if (Fundstellen)
                foreach (var f in UnityEngine.Object.FindObjectsOfType<DiggingWaypointBase>(true))
                    if (f != null && !(f is DiggingWaypointChest))
                        Nimm(f.transform, Fundstelle(f), new Color(0.5f, 0.8f, 1f), 0.3f);

            if (Mitspieler)
                foreach (var spieler in FirstPersonController.LocalPlayers)
                    if (spieler != null && !ReferenceEquals(spieler, Game.LocalPlayer))
                        Nimm(spieler.transform, "Mitspieler", Color.cyan, 1.8f);
        }

        private static void Nimm(Transform wo, string text, Color farbe, float hoehe)
        {
            _ziele.Add(new Ziel
            {
                Wo = wo,
                Umfang = wo.GetComponentInChildren<Renderer>(),
                Text = text,
                Farbe = farbe,
                Hoehe = hoehe
            });
        }

        /// <summary>Was da eigentlich liegt - Name und Anzahl statt bloß "Beute".</summary>
        private static string BeuteName(PickableItem beute)
        {
            try
            {
                var daten = beute.ItemDataSO;
                if (daten == null) return "Beute";

                string name = daten.GetLocalizedName();
                if (string.IsNullOrEmpty(name) || name.Contains("_NAME")) name = daten.itemID;

                int anzahl = beute.NetworksyncCount;
                return anzahl > 1 ? name + " x" + anzahl : name;
            }
            catch (Exception)
            {
                return "Beute";
            }
        }

        /// <summary>Aus dem Klassennamen der Fundstelle einen lesbaren machen.</summary>
        private static string Fundstelle(DiggingWaypointBase stelle)
        {
            string art = stelle.GetType().Name.Replace("DiggingWaypoint", "");

            switch (art)
            {
                case "Barrel": return "Fass";
                case "Destructible": return "Zerstörbares";
                case "DistanceItem": return "Fundstück";
                case "Enemy": return "Gegner (vergraben)";
                case "Hazard": return "Gefahr";
                case "Lamp": return "Lampe";
                case "Log": return "Holz";
                case "LoreLog": return "Aufzeichnung";
                case "Pet": return "Haustier (vergraben)";
                case "SimpleItem": return "Gegenstand";
                default: return art;
            }
        }

        // ---------------------------------------------------------------- Zeichnen

        private static void Male(Camera kamera, Vector3 auge, Ziel ziel)
        {
            Vector3 ort = ziel.Wo.position + Vector3.up * ziel.Hoehe;

            float entfernung = Vector3.Distance(auge, ort);
            if (entfernung > Reichweite) return;

            Vector3 schirm = kamera.WorldToScreenPoint(ort);
            if (schirm.z <= 0f) return;          // hinter uns

            float x = schirm.x;
            float y = Screen.height - schirm.y;

            // Weiter weg heißt blasser, damit der Bildschirm nicht zugemüllt wird.
            float naehe = Mathf.Clamp01(1f - entfernung / Reichweite);
            var blass = new Color(ziel.Farbe.r, ziel.Farbe.g, ziel.Farbe.b, 0.35f + naehe * 0.65f);

            GUI.color = blass;

            float unterkante = y;

            if (Umrisse && ziel.Umfang != null && Kasten(kamera, ziel.Umfang, out Rect kasten))
            {
                Rahmen(kasten);
                unterkante = kasten.yMax;
            }
            else
            {
                float halb = Punktgroesse * 0.5f;
                GUI.DrawTexture(new Rect(x - halb, y - halb, Punktgroesse, Punktgroesse), _punkt);
                unterkante = y + halb;
            }

            _stil.normal.textColor = blass;
            _stil.fontSize = Schriftgroesse;

            string beschriftung = MitEntfernung
                ? ziel.Text + "  " + Mathf.RoundToInt(entfernung) + "m"
                : ziel.Text;

            var groesse = _stil.CalcSize(new GUIContent(beschriftung));
            GUI.Label(new Rect(x - groesse.x / 2f, unterkante + 2f, groesse.x, groesse.y),
                      beschriftung, _stil);

            GUI.color = Color.white;
        }

        /// <summary>Den Umriss eines Objekts als Rechteck auf dem Bildschirm.
        ///
        /// Dafür werden die acht Ecken seines Ausmaßes einzeln umgerechnet und außen
        /// herum das kleinste Rechteck gebildet. Nur den Mittelpunkt zu nehmen ginge
        /// schief, sobald man schräg auf etwas Längliches schaut.</summary>
        private static bool Kasten(Camera kamera, Renderer umfang, out Rect kasten)
        {
            kasten = new Rect();

            Bounds b = umfang.bounds;
            Vector3 mitte = b.center;
            Vector3 halb = b.extents;

            float links = float.MaxValue, rechts = float.MinValue;
            float oben = float.MaxValue, unten = float.MinValue;

            for (int i = 0; i < 8; i++)
            {
                var ecke = new Vector3(
                    mitte.x + (((i & 1) == 0) ? -halb.x : halb.x),
                    mitte.y + (((i & 2) == 0) ? -halb.y : halb.y),
                    mitte.z + (((i & 4) == 0) ? -halb.z : halb.z));

                Vector3 p = kamera.WorldToScreenPoint(ecke);
                if (p.z <= 0f) return false;     // eine Ecke hinter uns: kein Kasten

                float sx = p.x;
                float sy = Screen.height - p.y;

                if (sx < links) links = sx;
                if (sx > rechts) rechts = sx;
                if (sy < oben) oben = sy;
                if (sy > unten) unten = sy;
            }

            kasten = new Rect(links, oben, rechts - links, unten - oben);
            return kasten.width > 1f && kasten.height > 1f;
        }

        /// <summary>Vier dünne Balken als Rahmen - GUI kennt keine Linien.</summary>
        private static void Rahmen(Rect r)
        {
            float d = Mathf.Max(1f, Punktgroesse * 0.35f);

            GUI.DrawTexture(new Rect(r.xMin, r.yMin, r.width, d), _punkt);
            GUI.DrawTexture(new Rect(r.xMin, r.yMax - d, r.width, d), _punkt);
            GUI.DrawTexture(new Rect(r.xMin, r.yMin, d, r.height), _punkt);
            GUI.DrawTexture(new Rect(r.xMax - d, r.yMin, d, r.height), _punkt);
        }

        private static void Stil()
        {
            if (_stil != null) return;

            _stil = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter
            };

            _punkt = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _punkt.SetPixel(0, 0, Color.white);
            _punkt.Apply();
            _punkt.hideFlags = HideFlags.HideAndDontSave;
        }
    }
}
