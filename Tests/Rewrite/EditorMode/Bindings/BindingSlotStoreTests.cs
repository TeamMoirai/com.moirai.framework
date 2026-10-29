using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Bindings;
using Moirai.Atropos.Bindings.Internal;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Bindings
{
    public sealed class BindingSlotStoreTests
    {
        private sealed class Lease : IBindingAssetLease
        {
            internal Lease(object asset, Action onDispose) { Asset = asset; OnDispose = onDispose; }
            public object Asset { get; }
            internal Action OnDispose { get; }
            public void Dispose() => OnDispose();
        }

        private sealed class Source : IBindingAssetSource
        {
            internal object Asset;
            internal int Disposals;
            internal UniTaskCompletionSource<bool> Gate;
            internal bool BlockFirst;
            internal Exception Failure;
            private int _calls;
            public async UniTask<IBindingAssetLease> AcquireAsync(string addressKey,
                CancellationToken cancellationToken)
            {
                if (BlockFirst && _calls++ == 0 && Gate != null) await Gate.Task;
                if (Failure != null) throw Failure;
                return new Lease(Asset, () => Disposals++);
            }
        }

        private sealed class Target : IBindingTarget
        {
            internal object Applied;
            internal bool Alive = true;
            internal Exception Failure;
            public bool IsAlive => Alive;
            public void Apply(object asset)
            {
                if (Failure != null) throw Failure;
                Applied = asset;
            }
        }

        [UnityTest]
        public IEnumerator Replace_AppliesNew_ThenDisposesOld() => UniTask.ToCoroutine(async () =>
        {
            var source = new Source { Asset = "first" };
            var store = new BindingSlotStore(source);
            var target = new Target();
            await store.BindAsync("slot", "a", target);
            Assert.AreEqual("first", target.Applied);
            source.Asset = "second";
            await store.BindAsync("slot", "b", target);
            Assert.AreEqual("second", target.Applied);
            Assert.AreEqual(1, source.Disposals);
            store.Unbind("slot");
            Assert.AreEqual(2, source.Disposals);
        });

        [UnityTest]
        public IEnumerator ApplyFailure_KeepsOldBinding_AndDisposesNew() => UniTask.ToCoroutine(async () =>
        {
            var source = new Source { Asset = "first" };
            var store = new BindingSlotStore(source);
            var target = new Target();
            await store.BindAsync("slot", "a", target);
            var failure = new InvalidOperationException("apply failed");
            target.Failure = failure;
            Exception observed = null;
            try { await store.BindAsync("slot", "b", target); }
            catch (Exception error) { observed = error; }
            Assert.That(observed, Is.SameAs(failure));
            Assert.AreEqual("first", target.Applied);
            Assert.AreEqual(1, source.Disposals);
            store.Unbind("slot");
            Assert.AreEqual(2, source.Disposals);
        });

        [UnityTest]
        public IEnumerator SupersededBind_IsAbandoned() => UniTask.ToCoroutine(async () =>
        {
            var source = new Source
            {
                Asset = "second",
                BlockFirst = true,
                Gate = new UniTaskCompletionSource<bool>()
            };
            var store = new BindingSlotStore(source);
            var first = new Target();
            var pending = store.BindAsync("slot", "a", first);
            var second = new Target();
            await store.BindAsync("slot", "b", second);
            source.Gate.TrySetResult(true);
            Exception observed = null;
            try { await pending; }
            catch (Exception error) { observed = error; }
            Assert.That(observed, Is.InstanceOf<InvalidOperationException>());
            Assert.AreEqual("second", second.Applied);
            store.Unbind("slot");
        });
    }
}
