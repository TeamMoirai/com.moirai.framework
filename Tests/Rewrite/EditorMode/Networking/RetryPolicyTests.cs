using System;
using Moirai.Atropos.Networking;
using NUnit.Framework;

namespace Networking
{
    public sealed class RetryPolicyTests
    {
        [Test]
        public void Delay_IsExponential_AndCapped()
        {
            var policy = new RetryPolicy(5, TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMilliseconds(1000), 2d);
            Assert.AreEqual(200d, policy.DelayBefore(1).TotalMilliseconds);
            Assert.AreEqual(400d, policy.DelayBefore(2).TotalMilliseconds);
            Assert.AreEqual(800d, policy.DelayBefore(3).TotalMilliseconds);
            Assert.AreEqual(1000d, policy.DelayBefore(4).TotalMilliseconds);
            Assert.AreEqual(1000d, policy.DelayBefore(9).TotalMilliseconds);
        }

        [Test]
        public void Schedule_StopsAtMaxAttempts()
        {
            var policy = new RetryPolicy(3, TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(1), 2d);
            Assert.IsTrue(policy.TrySchedule(1, TimeSpan.Zero, TimeSpan.FromSeconds(10), out _));
            Assert.IsTrue(policy.TrySchedule(2, TimeSpan.FromMilliseconds(100),
                TimeSpan.FromSeconds(10), out _));
            Assert.IsFalse(policy.TrySchedule(3, TimeSpan.FromMilliseconds(300),
                TimeSpan.FromSeconds(10), out TimeSpan delay));
            Assert.AreEqual(TimeSpan.Zero, delay);
        }

        [Test]
        public void Schedule_RespectsTotalDeadline()
        {
            var policy = new RetryPolicy(5, TimeSpan.FromMilliseconds(400),
                TimeSpan.FromSeconds(2), 2d);
            Assert.IsFalse(policy.TrySchedule(1, TimeSpan.FromMilliseconds(700),
                TimeSpan.FromMilliseconds(1000), out _));
            Assert.IsTrue(policy.TrySchedule(1, TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(1000), out TimeSpan delay));
            Assert.AreEqual(400d, delay.TotalMilliseconds);
        }

        [Test]
        public void Constructor_RejectsInvalidValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RetryPolicy(0, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), 2d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromMilliseconds(2), 2d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RetryPolicy(1, TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), 2d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RetryPolicy(1, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(2), 0.5d));
        }
    }
}
