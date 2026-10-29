using System;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Procedure.Internal
{
    internal sealed class ProcedureFrameClock
    {
        private readonly IClock _clock;
        private double _previous;

        internal ProcedureFrameClock(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            Reset();
        }

        internal void Reset() { _previous = _clock.RealtimeSinceStartup; }

        internal void Sample(out float elapsed, out float realElapsed)
        {
            double now = _clock.RealtimeSinceStartup;
            elapsed = _clock.DeltaTime;
            realElapsed = (float)Math.Max(0d, now - _previous);
            _previous = now;
        }
    }
}
