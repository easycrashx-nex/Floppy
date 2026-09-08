using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Floppy.Core;
using Floppy.Hosting;

namespace Floppy.Unrailed2
{
    /// <summary>Laesst das Unrailed-Modul im Client laufen.
    ///
    /// Wie beim Unreal-Modul liegt nichts von uns im Spiel. Der Dienst, mit dem sich die
    /// Oberflaeche sonst zum Spiel verbindet, laeuft deshalb hier bei uns selbst.
    ///
    /// Ein Unterschied zu allen anderen Spielen: Dieses Modul ist auch dann sinnvoll,
    /// wenn Unrailed 2 gar nicht laeuft. Der Knopf, der den Entwicklerzugang anschaltet,
    /// muss ja gerade dann erreichbar sein - er schreibt in die Einstellungsdatei, und
    /// die ueberschreibt das Spiel beim Beenden. Der Dienst startet deshalb, sobald man
    /// Unrailed 2 in der Liste auswaehlt, und nicht erst mit dem Spiel.</summary>
    public static class Host
    {
        private static readonly object _lebenszyklus = new();
        private static HostTakt _takt;
        private static Unrailed2Module _modul;

        public static bool Laeuft { get { lock (_lebenszyklus) return _takt != null && !_takt.Beendet.IsCompleted; } }

        /// <summary>Ob das Spiel gerade laeuft - fuer die Oberflaeche.</summary>
        public static bool SpielLaeuft => Spiel.Laeuft;

        public static string Prozessname => Spiel.Prozess;

        public static bool Starte(out string meldung)
        {
            lock (_lebenszyklus) return StarteIntern(out meldung);
        }

        private static bool StarteIntern(out string meldung)
        {
            if (Laeuft)
            {
                meldung = _takt.WirdBeendet ? "Der bisherige Dienst wird noch beendet" : "läuft bereits";
                return !_takt.WirdBeendet;
            }

            if (PortBelegt(IpcServer.Port))
            {
                meldung = "Port " + IpcServer.Port + " ist belegt - läuft noch ein anderes Spiel mit Floppy?";
                return false;
            }

            _modul = new Unrailed2Module();
            Registry.SetModule(_modul);
            IpcServer.Start();

            // Der Dienst antwortet aus fremden Faeden; die eingereihte Arbeit muss
            // jemand abarbeiten. Bei den Spielmodulen macht das die Bildschleife -
            // hier ein Zeitgeber.
            //
            // Eine Sekunde ist mit Absicht traege: Jeder Takt fragt das Spiel ueber
            // HTTP, und das Spiel beantwortet diese Anfragen in seinem eigenen
            // Bildtakt. Haeufiger nachzufragen kostet dem Spielenden Bilder, ohne dass
            // die Anzeige spuerbar frischer waere.
            var modul = _modul;
            bool initialisiert = false;
            _takt = new HostTakt(() =>
            {
                if (_takt.WirdBeendet) return;
                Dispatcher.Pump();
                if (_takt.WirdBeendet) return;
                if (!initialisiert)
                {
                    modul.Initialize();
                    initialisiert = true;
                }

                if (_takt.WirdBeendet) return;
                modul.Update();
                if (!_takt.WirdBeendet) Dispatcher.Pump();
            }, () => { }, ex => Log.Error("Unrailed2-Takt: " + ex.Message), 500, 1000);

            meldung = Spiel.Laeuft ? "Unrailed 2 angebunden" : "Bereit - Unrailed 2 läuft noch nicht";
            return true;
        }

        public static void Stoppe()
        {
            lock (_lebenszyklus)
            {
                if (_takt == null || _takt.Beendet.IsCompleted) return;
                IpcServer.Stop();
                _takt.Dispose();
            }
        }

        /// <summary>Bricht keine HTTP-Antwort ab; False meldet einen noch laufenden letzten Takt.</summary>
        public static Task<bool> StoppeAsync(TimeSpan? zeitgrenze = null)
        {
            lock (_lebenszyklus)
            {
                Stoppe();
                return _takt?.StoppeAsync(zeitgrenze ?? TimeSpan.FromSeconds(5)) ?? Task.FromResult(true);
            }
        }

        private static bool PortBelegt(int port)
        {
            try
            {
                using var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return false;
            }
            catch (SocketException)
            {
                return true;
            }
        }
    }
}
