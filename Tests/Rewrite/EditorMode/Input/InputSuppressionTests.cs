using System;
using Moirai.Atropos.Input;
using NUnit.Framework;

namespace Input
{
    public sealed class InputSuppressionTests
    {
        [Test]
        public void Owners_AreIndependent_AndDisposeIsIdempotent()
        {
            var state = new InputSuppression();
            IDisposable ui = state.Acquire(InputSuppressionOwner.UI);
            IDisposable component = state.Acquire(InputSuppressionOwner.Component);
            ui.Dispose();
            ui.Dispose();
            Assert.IsTrue(state.IsSuppressed);
            component.Dispose();
            Assert.IsFalse(state.IsSuppressed);
        }

        [Test]
        public void MultipleLeases_CountPerOwner()
        {
            var state = new InputSuppression();
            IDisposable first = state.Acquire(InputSuppressionOwner.UI);
            IDisposable second = state.Acquire(InputSuppressionOwner.UI);
            first.Dispose();
            Assert.IsTrue(state.IsSuppressed);
            second.Dispose();
            Assert.IsFalse(state.IsSuppressed);
        }

        [Test]
        public void InvalidOwner_IsRejectedBeforeCountChange()
        {
            var state = new InputSuppression();
            Assert.Throws<ArgumentOutOfRangeException>(() => state.Acquire((InputSuppressionOwner)7));
            Assert.IsFalse(state.IsSuppressed);
        }
    }
}
