using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Floppy.Core
{
    /// <summary>Unity darf nur aus dem Mainthread angefasst werden. Der IPC-Server läuft
    /// aber auf eigenen Threads - der schiebt seine Arbeit deshalb hier durch.</summary>
    public static class Dispatcher
    {
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static readonly object _pumpGate = new object();
        [ThreadStatic] private static bool _pumping;

        public static void Enqueue(Action action)
        {
            if (action == null) return;
            lock (_queue) _queue.Enqueue(action);
        }

        /// <summary>Führt die anstehende Arbeit aus. Nur aus dem Mainthread aufrufen.</summary>
        public static void Pump()
        {
            if (_pumping || !Monitor.TryEnter(_pumpGate)) return;
            _pumping = true;
            try
            {
                while (true)
                {
                    Action action;
                    lock (_queue)
                    {
                        if (_queue.Count == 0) return;
                        action = _queue.Dequeue();
                    }
                    try { action(); }
                    catch (Exception ex) { Log.Error("Fehler in eingereihter Aktion: " + ex); }
                }
            }
            finally { _pumping = false; Monitor.Exit(_pumpGate); }
        }

        /// <summary>Reiht die Aktion ein und wartet bis zu <paramref name="timeoutMs"/> auf ihr Ergebnis.</summary>
        public static T Run<T>(Func<T> func, int timeoutMs = 2000)
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            if (timeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs));
            if (_pumping) return func();
            T result = default;
            Exception error = null;
            // 0 wartet, 1 läuft, 2 ist abgelaufen. Nur einer darf den Auftrag übernehmen.
            int state = 0;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Enqueue(() =>
            {
                if (Interlocked.CompareExchange(ref state, 1, 0) != 0) return;
                try { result = func(); }
                catch (Exception ex) { error = ex; }
                finally { done.TrySetResult(true); }
            });

            if (!done.Task.Wait(timeoutMs))
            {
                bool cancelled = Interlocked.CompareExchange(ref state, 2, 0) == 0;
                throw new TimeoutException(cancelled
                    ? "Spiel hat nicht rechtzeitig geantwortet; Auftrag wurde nicht ausgeführt"
                    : "Aktion läuft noch; Ergebnis unbekannt. Bitte nicht erneut ausführen");
            }
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }
    }
}
