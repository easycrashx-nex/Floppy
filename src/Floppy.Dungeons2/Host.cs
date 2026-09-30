using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Floppy.Core;
using Floppy.Hosting;

namespace Floppy.Dungeons2
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

            var processes = System.Diagnostics.Process.GetProcessesByName(DungeonsModule.ProcessName);
            bool running = processes.Length != 0;
            foreach (var process in processes) process.Dispose();
            if (!running)
            {
                meldung = "Minecraft Dungeons II läuft nicht";
                return false;
            }

            if (PortBelegt(IpcServer.Port))
            {
                meldung = "Port " + IpcServer.Port + " ist belegt - läuft noch ein anderes Spiel mit Floppy?";
                return false;
            }

            var modul = new DungeonsModule();
            Registry.SetModule(modul);
            IpcServer.Start();

            // Der Dienst antwortet aus fremden Fäden; die eingereihte Arbeit muss
            // jemand abarbeiten. Bei den Spielmodulen macht das die Bildschleife -
            // hier ein Zeitgeber.
            bool initialisiert = false;
            _takt = new HostTakt(() => Bediene(modul, ref initialisiert, () => _takt.WirdBeendet),
                modul.Dispose, ex => Log.Error("Dungeons-Takt: " + ex.Message), 100, 100);

            meldung = "Minecraft Dungeons II angebunden";
            return true;
        }

        internal static void Bediene(DungeonsModule modul, ref bool initialisiert, Func<bool> wirdBeendet)
        {
            if (wirdBeendet()) return;
            // Diagnose/schema requests must still complete when attaching to the
            // game fails. Initialization retries belong to the module's slow tick.
            Dispatcher.Pump();
            if (wirdBeendet()) return;
            if (!initialisiert)
            {
                initialisiert = true;
                modul.Initialize();
            }
            if (!wirdBeendet()) modul.Update();
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

