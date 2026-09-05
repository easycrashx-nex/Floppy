using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Floppy.Core;
using Floppy.Hosting;

namespace Floppy.Unreal
{
    /// <summary>Lässt das Unreal-Modul im Client laufen.
    ///
    /// Bei Unity und Godot sitzt das Modul im Spiel und der Client verbindet sich
    /// dorthin. Hier gibt es nichts im Spiel - also stellen wir denselben Dienst im
    /// Client selbst bereit. Für die Oberfläche ändert sich dadurch nichts: Sie
    /// verbindet sich wie immer auf 127.0.0.1, nur ist das Gegenüber diesmal sie selbst.</summary>
    public static class Host
    {
        private static readonly object _lebenszyklus = new();
        private static HostTakt _takt;
        private static MortalShellModule _modul;

        public static bool Laeuft { get { lock (_lebenszyklus) return _takt != null && !_takt.Beendet.IsCompleted; } }

        /// <summary>Startet den Dienst, falls das Spiel läuft und der Platz frei ist.</summary>
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

            if (System.Diagnostics.Process.GetProcessesByName(Spiel.Prozess).Length == 0)
            {
                meldung = "Mortal Shell II läuft nicht";
                return false;
            }

            if (PortBelegt(IpcServer.Port))
            {
                meldung = "Port " + IpcServer.Port + " ist belegt - läuft noch ein anderes Spiel mit Floppy?";
                return false;
            }

            _modul = new MortalShellModule();
            Registry.SetModule(_modul);
            IpcServer.Start();

            // Der Dienst antwortet aus fremden Fäden; die eingereihte Arbeit muss
            // jemand abarbeiten. Bei den Spielmodulen macht das die Bildschleife -
            // hier ein Zeitgeber.
            var modul = _modul;
            bool initialisiert = false;
            _takt = new HostTakt(() =>
            {
                if (!initialisiert)
                {
                    modul.Initialize();
                    initialisiert = true;
                }

                if (_takt.WirdBeendet) return;
                Dispatcher.Pump();
                if (!_takt.WirdBeendet) modul.Update();
            }, modul.Dispose, ex => Log.Error("Unreal-Takt: " + ex.Message), 200, 200);

            meldung = "Mortal Shell II angebunden";
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

        /// <summary>False heißt: der letzte Aufruf läuft noch; seine Ressourcen bleiben gültig.</summary>
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
