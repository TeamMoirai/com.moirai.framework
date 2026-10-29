using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Sound;
using Moirai.Atropos.Sound.Internal;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Sound
{
    public sealed class AudioServiceTests
    {
        private sealed class Backend : IAudioBackend
        {
            internal readonly List<string> Calls = new List<string>();
            internal readonly List<int> Playing = new List<int>();
            public void Play(int voice, AudioClip clip, float volume, bool loop)
            {
                Calls.Add("play:" + voice);
                Playing.Add(voice);
            }
            public void Stop(int voice, float fadeSeconds)
            {
                Calls.Add("stop:" + voice);
                Playing.Remove(voice);
            }
            public void SetVolume(int voice, float volume) => Calls.Add("volume:" + volume);
            public bool IsPlaying(int voice) => Playing.Contains(voice);
        }

        private sealed class Lease : IAudioClipLease
        {
            internal Lease(AudioClip clip, Action onDispose) { Clip = clip; OnDispose = onDispose; }
            public AudioClip Clip { get; }
            internal Action OnDispose { get; }
            public void Dispose() => OnDispose();
        }

        private sealed class Source : IAudioClipLeaseSource
        {
            internal int Acquires;
            internal int Disposals;
            internal UniTaskCompletionSource<bool> Gate;
            internal AudioClip Clip;
            public async UniTask<IAudioClipLease> AcquireAsync(string clipKey,
                CancellationToken cancellationToken)
            {
                Acquires++;
                if (Gate != null) await Gate.Task;
                return new Lease(Clip, () => Disposals++);
            }
        }

        private static AudioService Create(Backend backend, Source source)
            => new AudioService(backend, source, new AudioOptions());

        [UnityTest]
        public IEnumerator ColdLoad_CachesAndPlays_CachedPlaySkipsSource() => UniTask.ToCoroutine(async () =>
        {
            var clip = AudioClip.Create("probe", 8, 1, 44100, false);
            var backend = new Backend();
            var source = new Source { Clip = clip };
            var service = Create(backend, source);
            try
            {
                Assert.IsFalse(service.TryPlay("hit"));
                await service.PlayAsync("hit", 1f, false);
                Assert.AreEqual(1, source.Acquires);
                Assert.IsTrue(service.TryPlay("hit"));
                Assert.AreEqual(1, source.Acquires);
            }
            finally { service.Dispose(); UnityEngine.Object.DestroyImmediate(clip); }
        });

        [UnityTest]
        public IEnumerator Closing_DuringLoad_DisposesLateLeaseWithoutPlay() => UniTask.ToCoroutine(async () =>
        {
            var clip = AudioClip.Create("probe", 8, 1, 44100, false);
            var backend = new Backend();
            var source = new Source { Clip = clip, Gate = new UniTaskCompletionSource<bool>() };
            var service = Create(backend, source);
            UniTask playing = service.PlayAsync("hit", 1f, false);
            service.Dispose();
            source.Gate.TrySetResult(true);
            Exception observed = null;
            try { await playing; }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.InstanceOf<ObjectDisposedException>());
            Assert.AreEqual(1, source.Disposals);
            Assert.IsEmpty(backend.Calls.FindAll(x => x.StartsWith("play")));
            UnityEngine.Object.DestroyImmediate(clip);
        });

        [UnityTest]
        public IEnumerator Stop_ReleasesClipOnlyWhenUnreferenced() => UniTask.ToCoroutine(async () =>
        {
            var clip = AudioClip.Create("probe", 8, 1, 44100, false);
            var backend = new Backend();
            var source = new Source { Clip = clip };
            var service = Create(backend, source);
            try
            {
                await service.PlayAsync("hit", 1f, false);
                service.Stop(1, 0.1f);
                Assert.AreEqual(1, source.Disposals);
                Assert.IsFalse(service.TryPlay("unknown"));
            }
            finally { service.Dispose(); UnityEngine.Object.DestroyImmediate(clip); }
        });
    }
}
