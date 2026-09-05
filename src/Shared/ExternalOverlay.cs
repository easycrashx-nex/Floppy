using System;
using UnityEngine;

namespace Floppy.Core
{
    /// <summary>Cursor and background updates for the external desktop overlay. No rendering.</summary>
    internal sealed class ExternalOverlay : IDisposable
    {
        private readonly bool _backgroundBefore;
        private bool _open;
        private bool _disposed;
        private bool _cursorBefore;
        private CursorLockMode _lockBefore;

        internal ExternalOverlay()
        {
            _backgroundBefore = Application.runInBackground;
            Application.runInBackground = true;
        }

        internal void SetOpen(bool open)
        {
            if (_disposed || _open == open) return;
            _open = open;
            if (open)
            {
                _cursorBefore = Cursor.visible;
                _lockBefore = Cursor.lockState;
                Tick();
            }
            else
            {
                Cursor.lockState = _lockBefore;
                Cursor.visible = _cursorBefore;
            }
        }

        internal void Tick()
        {
            if (_disposed) return;
            Application.runInBackground = true;
            if (!_open) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            SetOpen(false);
            Application.runInBackground = _backgroundBefore;
            _disposed = true;
        }
    }
}
