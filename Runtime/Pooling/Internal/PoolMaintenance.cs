using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Scheduling;

namespace Moirai.Atropos.Pooling.Internal
{
    internal interface IPoolMaintenanceSlice
    {
        double NextDue { get; }
        void MaintainOne(double now);
        void DeferAfterFailure(double nextDue);
    }

    internal sealed class PoolMaintenance
    {
        private readonly IReadOnlyList<IPoolMaintenanceSlice> _pools;
        private readonly IClock _clock;
        private readonly int _maxVisits;
        private readonly double _budgetSeconds;
        private int _cursor;

        internal PoolMaintenance(IReadOnlyList<IPoolMaintenanceSlice> pools, IClock clock,
            int maxVisits, double budgetSeconds)
        {
            if (pools == null) throw new ArgumentNullException(nameof(pools));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (maxVisits <= 0) throw new ArgumentOutOfRangeException(nameof(maxVisits));
            if (!(budgetSeconds > 0d)) throw new ArgumentOutOfRangeException(nameof(budgetSeconds));
            _pools = pools;
            _maxVisits = maxVisits;
            _budgetSeconds = budgetSeconds;
        }

        internal void Tick()
        {
            if (_pools.Count == 0) return;
            double start = _clock.RealtimeSinceStartup;
            int visits = 0;
            while (visits < _maxVisits && _pools.Count > 0)
            {
                int index = _cursor % _pools.Count;
                _cursor = (_cursor + 1) % _pools.Count;
                IPoolMaintenanceSlice pool = _pools[index];
                double now = _clock.RealtimeSinceStartup;
                if (pool.NextDue > now) { visits++; continue; }
                try { pool.MaintainOne(now); }
                catch (Exception) { pool.DeferAfterFailure(now + 1d); }
                visits++;
                if (_clock.RealtimeSinceStartup - start >= _budgetSeconds) break;
            }
        }
    }
}
