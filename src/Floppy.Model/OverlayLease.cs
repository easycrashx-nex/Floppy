using System;

namespace Floppy.Core
{
    /// <summary>Mainthread-only ownership of a short-lived external overlay input lock.</summary>
    internal sealed class OverlayLease
    {
        internal const int DurationMs = 3000;
        private readonly Action<bool> _setOpen;
        private readonly Func<long> _clock;
        private object _owner;
        private long _expires;

        internal OverlayLease(Action<bool> setOpen, Func<long> clock)
        {
            _setOpen = setOpen;
            _clock = clock;
        }

        internal bool IsOpen => _owner != null;

        internal void Apply(object owner, bool open)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            Tick();
            if (_owner != null && !ReferenceEquals(_owner, owner))
                throw new InvalidOperationException("Das Overlay gehört bereits einer anderen Verbindung");
            if (!open) { Release(owner); return; }
            _expires = _clock() + DurationMs;
            if (_owner != null) return; // A heartbeat must not overwrite the saved cursor/input state.
            _owner = owner;
            try { _setOpen(true); }
            catch
            {
                try { Release(owner); } catch { }
                throw;
            }
        }

        internal void Tick()
        {
            if (_owner != null && _clock() >= _expires) Release(_owner);
        }

        internal void Release(object owner)
        {
            if (_owner == null || !ReferenceEquals(_owner, owner)) return;
            _owner = null;
            _expires = 0;
            _setOpen(false);
        }

        internal void Close()
        {
            if (_owner != null) Release(_owner);
        }
    }
}
