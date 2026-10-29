using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Sound;
using System.Globalization;
using Moirai.Atropos.Settings;
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
            internal int FailStopVoice = -1;
            internal Exception StopFailure;
            public void Stop(int voice, float fadeSeconds)
            {
                Calls.Add("stop:" + voice);
                if (voice == FailStopVoice && StopFailure != null) throw StopFailure;
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

        private sealed class Settings : IKeyValueStore
        {
            internal readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            internal int Flushes;

            public int Count => Values.Count;
            public bool IsDirty => false;
            public bool Has(string key) => Values.ContainsKey(key);
            public bool Remove(string key) => Values.Remove(key);
            public void RemoveAll() => Values.Clear();
            public void GetAllKeys(List<string> results)
            {
                results.Clear();
                results.AddRange(Values.Keys);
            }
            public bool GetBool(string key, bool defaultValue = false)
                => Values.TryGetValue(key, out string value) && bool.TryParse(value, out bool parsed)
                    ? parsed : defaultValue;
            public int GetInt(string key, int defaultValue = 0) => defaultValue;
            public float GetFloat(string key, float defaultValue = 0f)
                => Values.TryGetValue(key, out string value) &&
                   float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                       out float parsed)
                    ? parsed : defaultValue;
            public string GetString(string key, string defaultValue = "")
                => Values.TryGetValue(key, out string value) ? value : defaultValue;
            public void SetBool(string key, bool value) => Values[key] = value.ToString();
            public void SetInt(string key, int value) => Values[key] = value.ToString();
            public void SetFloat(string key, float value)
                => Values[key] = value.ToString("R", CultureInfo.InvariantCulture);
            public void SetString(string key, string value) => Values[key] = value;
            public UniTask<long> FlushAsync(CancellationToken cancellationToken)
            {
                Flushes++;
                return UniTask.FromResult(0L);
            }
        }

        private static AudioService Create(Backend backend, Source source, Settings settings = null)
            => new AudioService(backend, source, new AudioOptions(), settings ?? new Settings());

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

        [Test]
        public void Init_ReadsStoredMasterVolumeAndMute()
        {
            var backend = new Backend();
            var settings = new Settings();
            settings.Values[AudioSettingKeys.MasterVolume] = "0.25";
            settings.Values[AudioSettingKeys.MasterMuted] = "True";
            var options = new AudioOptions { MasterVolume = 0.8f };

            var service = new AudioService(backend, new Source(), options, settings);

            Assert.That(service, Is.Not.Null);
            Assert.That(backend.Calls[backend.Calls.Count - 1], Is.EqualTo("volume:0"));
            Assert.That(settings.Flushes, Is.Zero);
        }

        [Test]
        public void SetMasterVolume_WritesKeyWithoutFlushing()
        {
            var backend = new Backend();
            var settings = new Settings();
            var service = Create(backend, new Source(), settings);

            service.SetMasterVolume(0.5f);

            Assert.That(settings.Values[AudioSettingKeys.MasterVolume], Is.EqualTo("0.5"));
            Assert.That(settings.Flushes, Is.Zero);
            Assert.That(backend.Calls[backend.Calls.Count - 1], Is.EqualTo("volume:0.5"));
        }

        [Test]
        public void SetMasterMuted_AppliesSilenceAndRestoresVolume()
        {
            var backend = new Backend();
            var settings = new Settings();
            var service = Create(backend, new Source(), settings);
            service.SetMasterVolume(0.4f);
            int before = backend.Calls.Count;

            service.SetMasterMuted(true);
            service.SetMasterMuted(true);

            Assert.That(backend.Calls.Count, Is.EqualTo(before + 1));
            Assert.That(backend.Calls[backend.Calls.Count - 1], Is.EqualTo("volume:0"));
            Assert.That(settings.Values[AudioSettingKeys.MasterMuted], Is.EqualTo("True"));
            Assert.That(settings.Values[AudioSettingKeys.MasterVolume], Is.EqualTo("0.4"));

            service.SetMasterMuted(false);
            Assert.That(backend.Calls[backend.Calls.Count - 1], Is.EqualTo("volume:0.4"));
            Assert.That(settings.Flushes, Is.Zero);
        }

        [Test]
        public void TryPlay_InvalidArguments_ThrowBeforeTouchingSource()
        {
            var backend = new Backend();
            var source = new Source();
            var service = Create(backend, source);
            backend.Calls.Clear();

            Assert.Throws<ArgumentNullException>(() => service.TryPlay(null));
            Assert.Throws<ArgumentException>(() => service.TryPlay(string.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.TryPlay("hit", -0.1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.TryPlay("hit", 1.1f));
            Assert.That(source.Acquires, Is.Zero);
            Assert.That(backend.Calls, Is.Empty);
        }

        [UnityTest]
        public IEnumerator PlayAsync_InvalidArguments_ThrowBeforeAcquiring() => UniTask.ToCoroutine(async () =>
        {
            var backend = new Backend();
            var source = new Source();
            var service = Create(backend, source);
            backend.Calls.Clear();
            var errors = new List<Exception>();
            try
            {
                try { await service.PlayAsync(null, 1f, false); }
                catch (Exception error) { errors.Add(error); }
                try { await service.PlayAsync(string.Empty, 1f, false); }
                catch (Exception error) { errors.Add(error); }
                try { await service.PlayAsync("hit", 2f, false); }
                catch (Exception error) { errors.Add(error); }
            }
            finally { service.Dispose(); }

            Assert.That(errors.Count, Is.EqualTo(3));
            Assert.That(errors[0], Is.InstanceOf<ArgumentException>());
            Assert.That(errors[1], Is.InstanceOf<ArgumentException>());
            Assert.That(errors[2], Is.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(source.Acquires, Is.Zero);
            Assert.That(backend.Calls.FindAll(x => x.StartsWith("play")), Is.Empty);
        });

        [UnityTest]
        public IEnumerator Stop_BackendFailure_KeepsVoiceForRetry() => UniTask.ToCoroutine(async () =>
        {
            var clip = AudioClip.Create("probe", 8, 1, 44100, false);
            var backend = new Backend();
            var source = new Source { Clip = clip };
            var service = Create(backend, source);
            try
            {
                await service.PlayAsync("hit", 1f, false);
                var failure = new InvalidOperationException("backend refused stop");
                backend.FailStopVoice = 1;
                backend.StopFailure = failure;

                Assert.Throws<InvalidOperationException>(() => service.Stop(1, 0f));
                Assert.That(source.Disposals, Is.Zero);
                Assert.That(backend.IsPlaying(1), Is.True);

                backend.FailStopVoice = -1;
                service.Stop(1, 0f);
                Assert.That(source.Disposals, Is.EqualTo(1));
                Assert.That(backend.IsPlaying(1), Is.False);
            }
            finally { service.Dispose(); UnityEngine.Object.DestroyImmediate(clip); }
        });

        [UnityTest]
        public IEnumerator Dispose_OneVoiceStopFails_StopsOthersAndReleasesAll() => UniTask.ToCoroutine(async () =>
        {
            var first = AudioClip.Create("first", 8, 1, 44100, false);
            var second = AudioClip.Create("second", 8, 1, 44100, false);
            var backend = new Backend();
            var source = new Source { Clip = first };
            var service = Create(backend, source);
            Exception observed = null;
            try
            {
                await service.PlayAsync("first", 1f, false);
                source.Clip = second;
                await service.PlayAsync("second", 1f, false);
                var failure = new InvalidOperationException("first voice refused stop");
                backend.FailStopVoice = 1;
                backend.StopFailure = failure;

                try { service.Dispose(); }
                catch (Exception error) { observed = error; }

                Assert.That(observed, Is.InstanceOf<AggregateException>());
                Assert.That(observed.InnerException, Is.SameAs(failure));
                CollectionAssert.AreEqual(new[] { 1 }, backend.Playing);
                Assert.That(source.Disposals, Is.EqualTo(2));
            }
            finally
            {
                service.Dispose();
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        });
    }
}
