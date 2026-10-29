using Moirai.Atropos.Backends.Policies;
using NUnit.Framework;

namespace Backends
{
    public sealed class BackendPolicyTests
    {
        [Test]
        public void Session_AcceptsOnlyNextSequence_AndNeverReplays()
        {
            var tracker = new SessionSequenceTracker(10);
            Assert.IsFalse(tracker.TryAccept(10));
            Assert.IsFalse(tracker.TryAccept(12));
            Assert.IsTrue(tracker.TryAccept(11));
            Assert.AreEqual(11, tracker.Confirmed);
            Assert.IsFalse(tracker.TryAccept(11));
            Assert.IsFalse(tracker.TryAcceptStale(11));
        }

        [TestCase(null, 0L, true, CloudCasVerdict.Write)]
        [TestCase(null, 1L, true, CloudCasVerdict.Conflict)]
        [TestCase(3L, 3L, true, CloudCasVerdict.Write)]
        [TestCase(3L, 4L, true, CloudCasVerdict.Conflict)]
        [TestCase(3L, 3L, false, CloudCasVerdict.Unknown)]
        public void Cas_DecidesWriteConflictOrUnknown(long? expected, long current,
            bool reachable, CloudCasVerdict verdict)
        {
            Assert.AreEqual(verdict, CloudCasDecision.Decide(expected, current, reachable));
        }
    }
}
