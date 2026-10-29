using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Assets;
using Moirai.Atropos.Scenes;
using Moirai.Atropos.Scenes.Internal;
using SceneLease = Moirai.Atropos.Scenes.ISceneLease;
using NUnit.Framework;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Scenes
{
    public sealed class SceneServiceTests
    {
        private sealed class Lease : SceneLease
        {
            internal int Unloads;
            public UnityEngine.SceneManagement.Scene Scene => default;
            public UniTask UnloadAsync(CancellationToken cancellationToken)
            {
                Unloads++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class Operation : ISceneLoadOperation
        {
            internal readonly UniTaskCompletionSource<SceneLease> Gate =
                new UniTaskCompletionSource<SceneLease>();
            internal int Activations;
            public float Progress => 0f;
            public void AllowActivation() => Activations++;
            public UniTask<SceneLease> Completion => Gate.Task;
        }

        private sealed class Backend : ISceneBackend
        {
            internal Operation Operation = new Operation();
            public ISceneLoadOperation BeginLoad(ResourceKey key, LoadSceneParameters parameters,
                bool suspendActivation) => Operation;
        }

        [UnityTest]
        public IEnumerator CancelledWait_CleansLateLease_WithoutStoppingNative() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend();
            var service = new SceneService(backend);
            var lease = new Lease();
            using (var caller = new CancellationTokenSource())
            {
                var loading = service.LoadAsync(new ResourceKey("scene"),
                    new LoadSceneParameters(LoadSceneMode.Additive), false, caller.Token);
                caller.Cancel();
                var cancelled = await loading.SuppressCancellationThrow();
                Assert.IsTrue(cancelled.IsCanceled);
                Assert.AreEqual(0, lease.Unloads);
                backend.Operation.Gate.TrySetResult(lease);
                await UniTask.Yield();
                await UniTask.Yield();
                Assert.AreEqual(1, lease.Unloads);
            }
        });

        [UnityTest]
        public IEnumerator SuccessfulLoad_DeliversLease_AndUnloadDelegates() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend();
            var service = new SceneService(backend);
            var lease = new Lease();
            var loading = service.LoadAsync(new ResourceKey("scene"),
                new LoadSceneParameters(LoadSceneMode.Additive), true);
            backend.Operation.Gate.TrySetResult(lease);
            SceneLease loaded = await loading;
            Assert.AreSame(lease, loaded);
            await service.UnloadAsync(loaded);
            Assert.AreEqual(1, lease.Unloads);
        });
    }
}
