using System;

namespace Moirai.Atropos.Networking
{
    public sealed class RetryPolicy
    {
        public RetryPolicy(int maxAttempts, TimeSpan baseDelay, TimeSpan maxDelay, double multiplier)
        {
            if (maxAttempts <= 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            if (baseDelay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(baseDelay));
            if (maxDelay < baseDelay) throw new ArgumentOutOfRangeException(nameof(maxDelay));
            if (multiplier < 1d) throw new ArgumentOutOfRangeException(nameof(multiplier));
            MaxAttempts = maxAttempts;
            BaseDelay = baseDelay;
            MaxDelay = maxDelay;
            Multiplier = multiplier;
        }

        public int MaxAttempts { get; }
        public TimeSpan BaseDelay { get; }
        public TimeSpan MaxDelay { get; }
        public double Multiplier { get; }

        public TimeSpan DelayBefore(int attempt)
        {
            if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
            double milliseconds = BaseDelay.TotalMilliseconds * Math.Pow(Multiplier, attempt - 1);
            return TimeSpan.FromMilliseconds(Math.Min(milliseconds, MaxDelay.TotalMilliseconds));
        }

        public bool TrySchedule(int attempt, TimeSpan elapsed, TimeSpan totalBudget, out TimeSpan delay)
        {
            if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
            if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
            delay = TimeSpan.Zero;
            if (attempt >= MaxAttempts) return false;
            TimeSpan candidate = DelayBefore(attempt);
            if (elapsed + candidate > totalBudget) return false;
            delay = candidate;
            return true;
        }
    }
}
