using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.L10n;
using Moirai.Atropos.L10n.Internal;
using Moirai.Atropos.Settings;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace L10n
{
    public sealed class LocalizationTests
    {
        private sealed class Store : ILocalizationStore
        {
            private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
            internal Store(params string[] languages) { SupportedLanguages = languages; }
            internal void Add(string language, string key, string text) => _texts[language + "|" + key] = text;
            public IReadOnlyList<string> SupportedLanguages { get; }
            public bool TryGet(string language, string key, out string text)
                => _texts.TryGetValue(language + "|" + key, out text);
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

        private static LocalizationService Create(ILocalizationStore store, string language,
            Settings settings = null)
            => new LocalizationService(store, settings ?? new Settings(), language);

        private sealed class Table { internal int Value; }

        private sealed class Source : IConfigTableSource
        {
            internal int Loads;
            internal UniTaskCompletionSource<bool> Gate;
            internal Exception Failure;
            public async UniTask<T> LoadAsync<T>(CancellationToken cancellationToken) where T : class
            {
                Loads++;
                if (Gate != null) await Gate.Task;
                if (Failure != null) throw Failure;
                return (T)(object)new Table { Value = 7 };
            }
        }

        [UnityTest]
        public IEnumerator LastSwitchWins_AndMissingKeyReturnsKey() => UniTask.ToCoroutine(async () =>
        {
            var store = new Store("en", "zh");
            store.Add("en", "hello", "Hello");
            store.Add("zh", "hello", "你好");
            var service = Create(store, "en");
            Assert.AreEqual("Hello", service.GetText("hello"));
            Assert.AreEqual("missing", service.GetText("missing"));
            var first = service.SetLanguageAsync("zh");
            var second = service.SetLanguageAsync("en");
            await UniTask.WhenAll(first, second);
            Assert.AreEqual("en", service.CurrentLanguage);
            Assert.AreEqual("Hello", service.GetText("hello"));
        });

        [UnityTest]
        public IEnumerator CancelledSwitch_KeepsPreviousLanguage() => UniTask.ToCoroutine(async () =>
        {
            var store = new Store("en", "zh");
            var settings = new Settings();
            var service = Create(store, "en", settings);
            using (var caller = new CancellationTokenSource())
            {
                var switching = service.SetLanguageAsync("zh", caller.Token);
                caller.Cancel();
                bool cancelled = await switching.SuppressCancellationThrow();
                Assert.IsTrue(cancelled);
            }
            Assert.AreEqual("en", service.CurrentLanguage);
            Assert.That(settings.Values, Is.Empty);
            Assert.That(settings.Flushes, Is.Zero);
        });

        [UnityTest]
        public IEnumerator SuccessfulSwitch_WritesLanguageKey_WithoutFlushing()
            => UniTask.ToCoroutine(async () =>
            {
                var store = new Store("en", "zh");
                var settings = new Settings();
                var service = Create(store, "en", settings);
                int changes = 0;
                service.LanguageChanged += _ => changes++;

                await service.SetLanguageAsync("zh");

                Assert.AreEqual("zh", service.CurrentLanguage);
                Assert.That(settings.Values[LocalizationSettingKeys.Language], Is.EqualTo("zh"));
                Assert.That(settings.Flushes, Is.Zero);
                Assert.That(changes, Is.EqualTo(1));

                await service.SetLanguageAsync("zh");
                Assert.That(changes, Is.EqualTo(1));
            });

        [Test]
        public void Init_PrefersStoredSupportedLanguage()
        {
            var store = new Store("en", "zh");
            var settings = new Settings();
            settings.Values[LocalizationSettingKeys.Language] = "zh";

            var service = Create(store, "en", settings);

            Assert.AreEqual("zh", service.CurrentLanguage);
            Assert.That(settings.Flushes, Is.Zero);
        }

        [Test]
        public void Init_IgnoresUnsupportedStoredLanguage()
        {
            var store = new Store("en", "zh");
            var settings = new Settings();
            settings.Values[LocalizationSettingKeys.Language] = "de";

            var service = Create(store, "en", settings);

            Assert.AreEqual("en", service.CurrentLanguage);
        }

        [UnityTest]
        public IEnumerator ConfigTable_LoadsOnce_ForConcurrentCallers() => UniTask.ToCoroutine(async () =>
        {
            var source = new Source { Gate = new UniTaskCompletionSource<bool>() };
            var service = new ConfigTableService(source);
            Assert.IsFalse(service.TryGetTable<Table>(out Table none));
            var first = service.GetTableAsync<Table>();
            var second = service.GetTableAsync<Table>();
            source.Gate.TrySetResult(true);
            Table a = await first;
            Table b = await second;
            Assert.AreSame(a, b);
            Assert.AreEqual(7, a.Value);
            Assert.AreEqual(1, source.Loads);
            Assert.IsTrue(service.TryGetTable<Table>(out Table cached));
            Assert.AreSame(a, cached);
        });

        [UnityTest]
        public IEnumerator ConfigTable_FailureIsNotCached() => UniTask.ToCoroutine(async () =>
        {
            var failure = new InvalidOperationException("bad table");
            var source = new Source { Failure = failure };
            var service = new ConfigTableService(source);
            Exception observed = null;
            try { await service.GetTableAsync<Table>(); }
            catch (Exception error) { observed = error; }
            Assert.That(observed, Is.SameAs(failure));
            Assert.IsFalse(service.TryGetTable<Table>(out Table none));
        });
    }
}
