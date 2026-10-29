using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Rendering;
using Moirai.Atropos.Rendering.Internal;
using Moirai.Atropos.Settings;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Graphics
{
    public sealed class GraphicsServiceTests
    {
        private sealed class Applier : IGraphicsApplier
        {
            internal int Applies;
            internal ResolutionInfo LastResolution;
            internal bool LastFullScreen;
            internal WindowMode LastMode;
            internal Exception Failure;
            public bool VSync { get; set; }

            public UniTask<GraphicsState> GetCurrentAsync(CancellationToken cancellationToken)
                => UniTask.FromResult(default(GraphicsState));

            public UniTask ApplyResolutionAsync(ResolutionInfo resolution, bool fullScreen,
                WindowMode mode, CancellationToken cancellationToken)
            {
                Applies++;
                LastResolution = resolution;
                LastFullScreen = fullScreen;
                LastMode = mode;
                return Failure == null ? UniTask.CompletedTask : UniTask.FromException(Failure);
            }
        }

        private sealed class Store : IKeyValueStore
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
            public float GetFloat(string key, float defaultValue = 0f) => defaultValue;
            public string GetString(string key, string defaultValue = "")
                => Values.TryGetValue(key, out string value) ? value : defaultValue;
            public void SetBool(string key, bool value) => Values[key] = value.ToString();
            public void SetInt(string key, int value) => Values[key] = value.ToString();
            public void SetFloat(string key, float value) => Values[key] = value.ToString();
            public void SetString(string key, string value) => Values[key] = value;
            public UniTask<long> FlushAsync(CancellationToken cancellationToken)
            {
                Flushes++;
                return UniTask.FromResult(0L);
            }
        }

        private static GraphicsService Create(Applier applier, out GraphicsOptions options, out Store store)
        {
            options = new GraphicsOptions
            {
                DefaultResolution = new ResolutionInfo(1280, 720, 60, 1),
                DefaultFullScreen = false,
                DefaultWindowMode = WindowMode.Windowed,
                DefaultVSync = false
            };
            store = new Store();
            return new GraphicsService(applier, options, store);
        }

        [UnityTest]
        public IEnumerator Apply_UpdatesStateOnlyAfterApplierCompletes() => UniTask.ToCoroutine(async () =>
        {
            var applier = new Applier();
            var service = Create(applier, out _, out _);
            var target = new ResolutionInfo(1920, 1080, 120, 1);
            await service.SetResolutionAsync(target, CancellationToken.None);
            Assert.AreEqual(target, service.State.Resolution);
            Assert.AreEqual(1, applier.Applies);
            Assert.AreEqual(false, applier.LastFullScreen);
            Assert.AreEqual(WindowMode.Windowed, applier.LastMode);
        });

        [UnityTest]
        public IEnumerator Failure_KeepsPreviousState() => UniTask.ToCoroutine(async () =>
        {
            var expected = new InvalidOperationException("display rejected");
            var applier = new Applier { Failure = expected };
            var service = Create(applier, out _, out Store store);
            GraphicsState before = service.State;
            Exception observed = null;
            try { await service.SetFullScreenAsync(true, CancellationToken.None); }
            catch (Exception failure) { observed = failure; }
            Assert.That(observed, Is.SameAs(expected));
            Assert.AreEqual(before.FullScreen, service.State.FullScreen);
            Assert.AreEqual(before.Mode, service.State.Mode);
            Assert.That(store.Values, Is.Empty);
            Assert.That(store.Flushes, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Vsync_IsSynchronousAndMirrored() => UniTask.ToCoroutine(async () =>
        {
            var applier = new Applier();
            var service = Create(applier, out _, out _);
            service.SetVSync(true);
            Assert.IsTrue(applier.VSync);
            Assert.IsTrue(service.State.VSync);
            await UniTask.Yield();
        });

        [Test]
        public void Init_ReadsKeysOverOptions()
        {
            var options = new GraphicsOptions
            {
                DefaultResolution = new ResolutionInfo(1280, 720, 60, 1),
                DefaultFullScreen = false,
                DefaultWindowMode = WindowMode.Windowed,
                DefaultVSync = false
            };
            var store = new Store();
            store.Values[GraphicsSettingKeys.Resolution] = "2560x1440@60000/1001";
            store.Values[GraphicsSettingKeys.FullScreen] = "True";
            store.Values[GraphicsSettingKeys.WindowMode] = "FullScreenWindow";
            store.Values[GraphicsSettingKeys.VSync] = "True";

            var service = new GraphicsService(new Applier(), options, store);

            Assert.That(service.State.Resolution.Width, Is.EqualTo(2560));
            Assert.That(service.State.Resolution.RateNumerator, Is.EqualTo(60000));
            Assert.That(service.State.FullScreen, Is.True);
            Assert.That(service.State.Mode, Is.EqualTo(WindowMode.FullScreenWindow));
            Assert.That(service.State.VSync, Is.True);
        }

        [Test]
        public void Init_InvalidValuesFallBackToOptions()
        {
            var options = new GraphicsOptions
            {
                DefaultResolution = new ResolutionInfo(1280, 720, 60, 1),
                DefaultFullScreen = false,
                DefaultWindowMode = WindowMode.Windowed,
                DefaultVSync = false
            };
            var store = new Store();
            store.Values[GraphicsSettingKeys.Resolution] = "not-a-resolution";
            store.Values[GraphicsSettingKeys.WindowMode] = "NotAMode";

            var service = new GraphicsService(new Applier(), options, store);

            Assert.That(service.State.Resolution, Is.EqualTo(options.DefaultResolution));
            Assert.That(service.State.Mode, Is.EqualTo(WindowMode.Windowed));
            Assert.That(service.State.FullScreen, Is.False);
        }

        [UnityTest]
        public IEnumerator SetResolution_WritesStableValueAndRaisesOnce() => UniTask.ToCoroutine(async () =>
        {
            var service = Create(new Applier(), out _, out Store store);
            int changes = 0;
            service.ResolutionChanged += _ => changes++;
            var target = new ResolutionInfo(1920, 1080, 60000, 1001);

            await service.SetResolutionAsync(target, CancellationToken.None);

            Assert.That(store.Values[GraphicsSettingKeys.Resolution],
                Is.EqualTo("1920x1080@60000/1001"));
            Assert.That(store.Flushes, Is.Zero);
            Assert.That(changes, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator SetFullScreenFalse_SwitchesToWindowedAndWritesBothKeys()
            => UniTask.ToCoroutine(async () =>
            {
                var service = Create(new Applier(), out _, out Store store);
                await service.SetFullScreenAsync(true, CancellationToken.None);
                store.Values.Clear();

                await service.SetFullScreenAsync(false, CancellationToken.None);

                Assert.That(service.State.Mode, Is.EqualTo(WindowMode.Windowed));
                Assert.That(store.Values[GraphicsSettingKeys.FullScreen], Is.EqualTo("False"));
                Assert.That(store.Values[GraphicsSettingKeys.WindowMode], Is.EqualTo("Windowed"));
                Assert.That(store.Flushes, Is.Zero);
            });

        [UnityTest]
        public IEnumerator SetWindowMode_WritesDerivedFullScreen() => UniTask.ToCoroutine(async () =>
        {
            var service = Create(new Applier(), out _, out Store store);

            await service.SetWindowModeAsync(WindowMode.ExclusiveFullScreen, CancellationToken.None);

            Assert.That(service.State.FullScreen, Is.True);
            Assert.That(store.Values[GraphicsSettingKeys.FullScreen], Is.EqualTo("True"));
            Assert.That(store.Values[GraphicsSettingKeys.WindowMode],
                Is.EqualTo("ExclusiveFullScreen"));
        });

        [UnityTest]
        public IEnumerator SetVSync_SameValueDoesNothing() => UniTask.ToCoroutine(async () =>
        {
            var service = Create(new Applier(), out _, out Store store);
            int changes = 0;
            service.VSyncChanged += _ => changes++;
            service.SetVSync(true);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(store.Values[GraphicsSettingKeys.VSync], Is.EqualTo("True"));

            service.SetVSync(true);

            Assert.That(changes, Is.EqualTo(1));
            Assert.That(store.Flushes, Is.Zero);
        });

        [Test]
        public void RemoveAllSettings_RemovesFourKeysOnly()
        {
            var service = Create(new Applier(), out _, out Store store);
            store.Values[GraphicsSettingKeys.Resolution] = "1920x1080@60/1";
            store.Values[GraphicsSettingKeys.FullScreen] = "True";
            store.Values[GraphicsSettingKeys.WindowMode] = "FullScreenWindow";
            store.Values[GraphicsSettingKeys.VSync] = "True";
            store.Values["unrelated"] = "keep";

            service.RemoveAllSettings();

            Assert.That(store.Values.Keys, Is.EquivalentTo(new[] { "unrelated" }));
            Assert.That(store.Flushes, Is.Zero);
        }
    }
}
