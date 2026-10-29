using System;
using System.Collections.Generic;
using Moirai.Atropos.Kernel.Scheduling;
using Moirai.Atropos.Pooling.Internal;
using NUnit.Framework;

namespace Pooling
{
    public sealed class PoolMaintenanceTests
    {
        private sealed class Clock : IClock
        {
            public float Time { get; set; }
            public float DeltaTime { get; set; }
            public double RealtimeSinceStartup { get; set; }
        }

        private sealed class Pool : IPoolMaintenanceSlice
        {
            internal readonly string Name;
            internal readonly Clock Clock;
            internal readonly double Cost;
            internal readonly List<double> Visits = new List<double>();
            internal bool Throw;
            internal double DeferredTo = double.MinValue;
            internal Pool(string name, Clock clock, double cost, double due = 0d)
            {
                Name = name;
                Clock = clock;
                Cost = cost;
                NextDue = due;
            }
            public double NextDue { get; private set; }
            public void MaintainOne(double now)
            {
                if (Throw)
                {
                    Throw = false;
                    throw new InvalidOperationException("unit failed");
                }
                Visits.Add(now);
                Clock.RealtimeSinceStartup += Cost;
                NextDue = Clock.RealtimeSinceStartup + 1d;
            }
            public void DeferAfterFailure(double nextDue) => DeferredTo = nextDue;
            public override string ToString() => Name;
        }

        [Test]
        public void Tick_RotatesAcrossPools_EvenWhenSomeNotDue()
        {
            var clock = new Clock();
            var a = new Pool("A", clock, 0d, 10d);
            var b = new Pool("B", clock, 0d);
            var c = new Pool("C", clock, 0d);
            var maintenance = new PoolMaintenance(new[] { a, b, c }, clock, 1, 10d);
            maintenance.Tick();
            maintenance.Tick();
            maintenance.Tick();
            Assert.AreEqual(1, b.Visits.Count);
            Assert.AreEqual(1, c.Visits.Count);
            Assert.AreEqual(0, a.Visits.Count);
        }

        [Test]
        public void Tick_StopsBetweenUnits_WhenBudgetExceeded()
        {
            var clock = new Clock();
            var a = new Pool("A", clock, 0.6d);
            var b = new Pool("B", clock, 0.6d);
            var c = new Pool("C", clock, 0.6d);
            var maintenance = new PoolMaintenance(new[] { a, b, c }, clock, 10, 1d);
            maintenance.Tick();
            Assert.AreEqual(2, a.Visits.Count + b.Visits.Count + c.Visits.Count);
            Assert.AreEqual(1.2d, clock.RealtimeSinceStartup, 0.0001d);
        }

        [Test]
        public void Tick_IsolatesFailure_AndDefersThatPool()
        {
            var clock = new Clock();
            var failing = new Pool("A", clock, 0d) { Throw = true };
            var healthy = new Pool("B", clock, 0d);
            var maintenance = new PoolMaintenance(new[] { failing, healthy }, clock, 2, 10d);
            maintenance.Tick();
            Assert.AreEqual(0, failing.Visits.Count);
            Assert.Greater(failing.DeferredTo, 0d);
            Assert.AreEqual(1, healthy.Visits.Count);
        }
    }
}
