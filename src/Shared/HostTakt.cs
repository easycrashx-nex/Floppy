using System;
using System.Threading;
using System.Threading.Tasks;

namespace Floppy.Hosting
{
    /// <summary>Ein Spieltakt zugleich; Beenden wartet auf den letzten laufenden Takt.</summary>
    internal sealed class HostTakt : IDisposable
    {
        private readonly object _schloss = new();
        private readonly Action _arbeit;
        private readonly Action _aufräumen;
        private readonly Action<Exception> _fehler;
        private readonly TaskCompletionSource<bool> _beendet = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Timer _timer;
        private bool _arbeitet;
        private bool _stop;
        private bool _räumtAuf;

        public HostTakt(Action arbeit, Action aufräumen, Action<Exception> fehler, int verzögerung, int intervall)
        {
            _arbeit = arbeit;
            _aufräumen = aufräumen;
            _fehler = fehler;
            _timer = new Timer(Tick, null, verzögerung, intervall);
        }

        public Task Beendet => _beendet.Task;
        public bool WirdBeendet { get { lock (_schloss) return _stop; } }

        private void Tick(object state)
        {
            lock (_schloss)
            {
                if (_stop || _arbeitet) return;
                _arbeitet = true;
            }

            try { _arbeit(); }
            catch (Exception ex) { _fehler(ex); }
            finally
            {
                bool aufräumen;
                lock (_schloss)
                {
                    _arbeitet = false;
                    aufräumen = ÜbernehmeAufräumen();
                }
                if (aufräumen) RäumeAuf();
            }
        }

        private bool ÜbernehmeAufräumen()
        {
            if (!_stop || _arbeitet || _räumtAuf) return false;
            _räumtAuf = true;
            return true;
        }

        private void RäumeAuf()
        {
            try { _aufräumen(); }
            catch (Exception ex) { _fehler(ex); }
            finally { _beendet.TrySetResult(true); }
        }

        public void Dispose()
        {
            bool aufräumen;
            lock (_schloss)
            {
                _stop = true;
                _timer?.Dispose();
                _timer = null;
                aufräumen = ÜbernehmeAufräumen();
            }
            if (aufräumen) RäumeAuf();
        }

        public async Task<bool> StoppeAsync(TimeSpan zeitgrenze)
        {
            if (zeitgrenze < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(zeitgrenze));
            Dispose();
            return await Task.WhenAny(_beendet.Task, Task.Delay(zeitgrenze)).ConfigureAwait(false) == _beendet.Task;
        }
    }
}
