using System.Collections;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Diagnostics.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Debugger
{
    public sealed class DebuggerOverlayHostTests
    {
        [UnityTest]
        public IEnumerator Host_TogglesOverlay_AndClearsOnDestroy() => UniTask.ToCoroutine(async () =>
        {
            var hostObject = new GameObject("DebuggerHost");
            var host = hostObject.AddComponent<DebuggerOverlayHost>();
            try
            {
                Assert.IsFalse(host.IsVisible);
                host.SetVisible(true);
                Assert.IsTrue(host.IsVisible);
                host.SetVisible(false);
                await UniTask.NextFrame();
                Assert.IsFalse(host.IsVisible);
                host.SetVisible(true);
                Assert.IsTrue(host.IsVisible);
            }
            finally
            {
                Object.Destroy(hostObject);
            }
            await UniTask.NextFrame();
            Assert.AreEqual(0, GameObject.FindObjectsOfType<DebuggerOverlayHost>().Length);
        });
    }
}
