using UnityEngine;

namespace Floppy.Core.Menu
{
    /// <summary>Eine kleine eigene Anordnungshilfe.
    ///
    /// Unitys GUILayout wäre dafür da - aber es ist nicht überall verfügbar: Unity schneidet
    /// beim Bauen ungenutzten Code heraus, und Spiele, die selbst kein IMGUI benutzen,
    /// haben es schlicht nicht mehr drin. ODDCORE ist so ein Fall.
    ///
    /// Deshalb rechnen wir die Rechtecke selbst aus. Kostet ein paar Zeilen, funktioniert
    /// dafür in jedem Spiel gleich - und die Layout-Eigenheiten von IMGUI (Inhalt kippt
    /// zwischen Vermessen und Malen) fallen ersatzlos weg.</summary>
    internal struct Layouter
    {
        private Rect _flaeche;
        private float _y;
        private readonly float _abstand;

        public Layouter(Rect flaeche, float abstand = 6f)
        {
            _flaeche = flaeche;
            _y = flaeche.y;
            _abstand = abstand;
        }

        /// <summary>Wie weit der Zeiger schon nach unten gewandert ist.</summary>
        public float Y { get { return _y; } }

        public float Hoehe { get { return _y - _flaeche.y; } }

        public float Breite { get { return _flaeche.width; } }

        /// <summary>Nächste Zeile über die volle Breite.</summary>
        public Rect Zeile(float hoehe)
        {
            var r = new Rect(_flaeche.x, _y, _flaeche.width, hoehe);
            _y += hoehe + _abstand;
            return r;
        }

        /// <summary>Zeile ohne Abstand danach - für Dinge, die direkt aneinander liegen.</summary>
        public Rect Eng(float hoehe)
        {
            var r = new Rect(_flaeche.x, _y, _flaeche.width, hoehe);
            _y += hoehe;
            return r;
        }

        public void Luecke(float hoehe)
        {
            _y += hoehe;
        }

        /// <summary>Teilt ein Rechteck waagerecht: links ein fester Anteil, rechts der Rest.</summary>
        public static void Teile(Rect r, float linksBreite, float luecke,
                                 out Rect links, out Rect rechts)
        {
            links = new Rect(r.x, r.y, linksBreite, r.height);
            rechts = new Rect(r.x + linksBreite + luecke, r.y,
                              r.width - linksBreite - luecke, r.height);
        }

        /// <summary>Schneidet rechts einen festen Anteil ab.</summary>
        public static void TeileRechts(Rect r, float rechtsBreite, float luecke,
                                       out Rect links, out Rect rechts)
        {
            rechts = new Rect(r.xMax - rechtsBreite, r.y, rechtsBreite, r.height);
            links = new Rect(r.x, r.y, r.width - rechtsBreite - luecke, r.height);
        }
    }

    /// <summary>Texthöhen ausrechnen - mit Rückfalllösung, falls auch das wegoptimiert wurde.</summary>
    internal static class Text
    {
        private static bool _calcGeprueft;
        private static bool _calcGeht;

        public static float Hoehe(GUIStyle stil, string text, float breite)
        {
            if (string.IsNullOrEmpty(text)) return 0f;

            if (!_calcGeprueft)
            {
                _calcGeprueft = true;
                try
                {
                    stil.CalcHeight(new GUIContent("Probe"), 100f);
                    _calcGeht = true;
                }
                catch (System.Exception)
                {
                    _calcGeht = false;
                    Log.Warning("Texthöhen werden geschätzt - CalcHeight ist in diesem Spiel nicht verfügbar");
                }
            }

            if (_calcGeht)
            {
                try { return stil.CalcHeight(new GUIContent(text), breite); }
                catch (System.Exception) { _calcGeht = false; }
            }

            // Grobe Schätzung: wie viele Zeichen passen in eine Zeile
            float zeichenbreite = Mathf.Max(5f, stil.fontSize > 0 ? stil.fontSize * 0.52f : 6.5f);
            int proZeile = Mathf.Max(10, Mathf.FloorToInt(breite / zeichenbreite));
            int zeilen = Mathf.CeilToInt(text.Length / (float)proZeile);
            float zeilenhoehe = stil.fontSize > 0 ? stil.fontSize + 4f : 16f;

            return zeilen * zeilenhoehe;
        }

        public static float Breite(GUIStyle stil, string text)
        {
            try { return stil.CalcSize(new GUIContent(text)).x; }
            catch (System.Exception)
            {
                float zeichenbreite = stil.fontSize > 0 ? stil.fontSize * 0.55f : 7f;
                return text.Length * zeichenbreite + 24f;
            }
        }
    }
}
