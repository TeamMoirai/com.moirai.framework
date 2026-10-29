using System;

namespace Moirai.Atropos.Diagnostics.Unity
{
    public sealed class UnityDebuggerOverlay : IDebuggerOverlay
    {
        private readonly DebuggerOverlayHost _host;

        public UnityDebuggerOverlay(DebuggerOverlayHost host)
        {
            _host = host != null ? host : throw new ArgumentNullException(nameof(host));
        }

        public bool IsVisible => _host != null && _host.IsVisible;

        public void SetVisible(bool visible)
        {
            if (_host == null) return;
            _host.SetVisible(visible);
        }
    }
}
