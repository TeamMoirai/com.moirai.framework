using System;

namespace Moirai.Atropos.Screens
{
    public readonly struct UIOpenResult
    {
        private UIOpenResult(bool opened, bool cancelled, Exception error)
        {
            Opened = opened;
            Cancelled = cancelled;
            Error = error;
        }

        public bool Opened { get; }
        public bool Cancelled { get; }
        public Exception Error { get; }
        public bool Succeeded => Opened && Error == null;

        public static UIOpenResult Success() => new UIOpenResult(true, false, null);
        public static UIOpenResult Cancellation() => new UIOpenResult(false, true, null);
        public static UIOpenResult Failure(Exception error) => new UIOpenResult(false, false, error);
    }
}
