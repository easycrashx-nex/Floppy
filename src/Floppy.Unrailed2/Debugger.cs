using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace Floppy.Unrailed2
{
    /// <summary>Die Leitung zum Entwicklerwerkzeug des Spiels.
    ///
    /// Das Spiel macht auf http://localhost:8001/ auf, sobald EnableWebDebug gesetzt ist.
    ///
    /// Eine Eigenart, die man kennen muss, sonst legt man den Server lahm: Er schreibt
    /// seine Antwort mit einem einzigen await auf den Ausgabestrom und faengt dabei
    /// nichts ab. Bricht der Aufrufer die Verbindung ab, waehrend die Antwort noch
    /// laeuft, fliegt in HandleIncomingConnections eine HttpListenerException (995) -
    /// und die Annahmeschleife ist tot. Nicht nur die eine Anfrage: der ganze Server,
    /// bis das Spiel neu startet.
    ///
    /// Deshalb lesen wir jede Antwort IMMER vollstaendig zu Ende, auch wenn uns nur der
    /// Statuscode interessiert, und setzen die Zeitgrenze grosszuegig.</summary>
    internal static class Debugger
    {
        public const int Port = 8001;

        private static readonly HttpClient Draht = Baue();

        private static HttpClient Baue()
        {
            var kunde = new HttpClient
            {
                // Grosszuegig: Eine Zeitueberschreitung bricht die Verbindung ab, und
                // genau das bringt den Server um.
                Timeout = TimeSpan.FromSeconds(30)
            };

            return kunde;
        }

        /// <summary>Zuletzt gesehener Zustand - fuer die Statuszeile.</summary>
        public static string Zustand { get; private set; } = "noch nicht verbunden";

        public static bool Erreichbar { get; private set; }

        /// <summary>Ruft einen Endpunkt auf und gibt die Antwort zurueck, oder null.</summary>
        public static string Hole(string pfad)
        {
            try
            {
                // ResponseContentRead (die Voreinstellung) liest die Antwort vollstaendig,
                // bevor sie zurueckkommt - genau das wollen wir hier.
                using var antwort = Draht.GetAsync("http://localhost:" + Port + "/" + pfad).GetAwaiter().GetResult();
                string text = antwort.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!antwort.IsSuccessStatusCode)
                {
                    Erreichbar = false;
                    Zustand = "Spiel antwortet mit " + (int)antwort.StatusCode;
                    return null;
                }

                Erreichbar = true;
                Zustand = "verbunden";

                // Das Spiel schickt eine Byte-Order-Marke mit; JsonDocument stoert das.
                return text.TrimStart('﻿');
            }
            catch (Exception ex)
            {
                Erreichbar = false;

                Zustand = Spiel.Laeuft
                    ? (Spiel.Angeschaltet
                        ? "Spiel läuft, aber der Zugang antwortet nicht"
                        : "Entwicklerzugang ist noch nicht angeschaltet")
                    : "Unrailed 2 läuft nicht";

                Floppy.Core.Log.Warning("Unrailed2: " + ex.Message);
                return null;
            }
        }

        /// <summary>Wie Hole, aber gleich als JSON geoeffnet. Gibt null bei Fehlern.</summary>
        public static JsonDocument Frage(string pfad)
        {
            string text = Hole(pfad);
            if (string.IsNullOrWhiteSpace(text)) return null;

            try { return JsonDocument.Parse(text); }
            catch (Exception ex)
            {
                Erreichbar = false;
                Zustand = "Unverständliche Antwort vom Spiel";
                Floppy.Core.Log.Warning("Unrailed2: unverständliche Antwort auf " + pfad + " - " + ex.Message);
                return null;
            }
        }

        /// <summary>Ein Aufruf, bei dem uns nur interessiert, ob er durchging.</summary>
        public static bool Loese(string pfad)
        {
            return Hole(pfad) != null;
        }

        public static string Verpacke(string wert)
        {
            return WebUtility.UrlEncode(wert);
        }
    }
}
