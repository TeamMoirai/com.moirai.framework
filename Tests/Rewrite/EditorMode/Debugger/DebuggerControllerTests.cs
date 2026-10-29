using System;
using Moirai.Atropos.Diagnostics;
using NUnit.Framework;

namespace Debugger
{
    public sealed class DebuggerControllerTests
    {
        private sealed class FakeOverlay : IDebuggerOverlay
        {
            internal int SetCalls;
            public bool IsVisible { get; private set; }

            public void SetVisible(bool visible)
            {
                SetCalls++;
                IsVisible = visible;
            }
        }

        [Test]
        public void Unavailable_HidesOverlayOnConstruction_AndToggleIsRejected()
        {
            FakeOverlay overlay = new FakeOverlay();
            overlay.SetVisible(true);

            using DebuggerController controller = new DebuggerController(false, overlay);

            Assert.That(overlay.IsVisible, Is.False);
            Assert.That(controller.IsAvailable, Is.False);
            Assert.That(controller.Toggle(), Is.False);
            Assert.That(overlay.IsVisible, Is.False);
        }

        [Test]
        public void SetVisible_OnUnavailable_StaysHidden()
        {
            FakeOverlay overlay = new FakeOverlay();
            using DebuggerController controller = new DebuggerController(false, overlay);

            controller.SetVisible(true);

            Assert.That(overlay.IsVisible, Is.False);
            Assert.That(controller.IsVisible, Is.False);
        }

        [Test]
        public void Toggle_OnAvailable_FlipsVisibility()
        {
            FakeOverlay overlay = new FakeOverlay();
            using DebuggerController controller = new DebuggerController(true, overlay);

            Assert.That(controller.Toggle(), Is.True);
            Assert.That(controller.IsVisible, Is.True);
            Assert.That(controller.Toggle(), Is.True);
            Assert.That(controller.IsVisible, Is.False);
        }

        [Test]
        public void Dispose_HidesOverlay_AndRejectsFurtherVisibilityChanges()
        {
            FakeOverlay overlay = new FakeOverlay();
            DebuggerController controller = new DebuggerController(true, overlay);
            controller.SetVisible(true);

            controller.Dispose();
            controller.Dispose();

            Assert.That(overlay.IsVisible, Is.False);
            Assert.That(controller.IsAvailable, Is.False);
            Assert.That(controller.Toggle(), Is.False);
            Assert.Throws<ObjectDisposedException>(() => controller.SetVisible(true));
        }
    }
}
