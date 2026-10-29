using Moirai.Atropos.Diagnostics.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Debugger
{
    public sealed class UnityDebuggerOverlayTests
    {
        private GameObject _hostObject;
        private DebuggerOverlayHost _host;

        [SetUp]
        public void SetUp()
        {
            _hostObject = new GameObject("DebuggerOverlayHostTests");
            _host = _hostObject.AddComponent<DebuggerOverlayHost>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hostObject != null) Object.DestroyImmediate(_hostObject);
        }

        [Test]
        public void SetVisible_ForwardsToHost()
        {
            UnityDebuggerOverlay overlay = new UnityDebuggerOverlay(_host);

            overlay.SetVisible(true);
            Assert.That(overlay.IsVisible, Is.True);
            Assert.That(_host.IsVisible, Is.True);

            overlay.SetVisible(false);
            Assert.That(overlay.IsVisible, Is.False);
            Assert.That(_host.IsVisible, Is.False);
        }

        [Test]
        public void DestroyedHost_ReportsHidden_AndIgnoresSetVisible()
        {
            UnityDebuggerOverlay overlay = new UnityDebuggerOverlay(_host);
            overlay.SetVisible(true);

            Object.DestroyImmediate(_hostObject);

            Assert.That(overlay.IsVisible, Is.False);
            Assert.DoesNotThrow(() => overlay.SetVisible(true));
            Assert.That(overlay.IsVisible, Is.False);
        }
    }
}
