using System;

namespace Moirai.Atropos.Diagnostics
{
    public interface IDebuggerOverlay
    {
        bool IsVisible { get; }
        void SetVisible(bool visible);
    }

    public sealed class DebuggerController : IDisposable
    {
        private readonly bool _available;
        private readonly IDebuggerOverlay _overlay;
        private bool _disposed;

        public DebuggerController(bool available, IDebuggerOverlay overlay)
        {
            _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
            _available = available;
            if (!_available) _overlay.SetVisible(false);
        }

        public bool IsAvailable => _available && !_disposed;

        public bool IsVisible => !_disposed && _available && _overlay.IsVisible;

        public bool Toggle()
        {
            if (!IsAvailable) return false;
            _overlay.SetVisible(!_overlay.IsVisible);
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(DebuggerController));
            _overlay.SetVisible(_available && visible);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _overlay.SetVisible(false);
        }
    }
}
