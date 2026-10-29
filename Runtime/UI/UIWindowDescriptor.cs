using System;

namespace Moirai.Atropos.Screens
{
    public readonly struct UIWindowDescriptor
    {
        public UIWindowDescriptor(string windowId, string trackId)
        {
            WindowId = windowId ?? throw new ArgumentNullException(nameof(windowId));
            TrackId = trackId ?? throw new ArgumentNullException(nameof(trackId));
        }

        public string WindowId { get; }
        public string TrackId { get; }
    }
}
