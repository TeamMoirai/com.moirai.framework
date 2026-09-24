using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Service.Localization
{
    /// <summary>
    /// 本地化第二波生产特性测试：异步批加载骨架（LoadAsync）与按语言列加载契约。
    /// <para>首启语言由检测链（命令行 → 编辑器设置 → 存档 → 系统语言）决定、机器相关，
    /// 需要"确实落在某语言"的用例一律经 <see cref="SeedEditorLanguage"/> 显式播种，不硬编码。</para>
    /// </summary>
    [TestFixture]
    public sealed class LocalizationWave2Tests
    {
        private static readonly Language English = Language.English;
        private static readonly Language Chinese = Language.ChineseSimplified;
        private static readonly Language Japanese = Language.Japanese;

        private readonly List<LocalizationServiceHandler> _handlers = new List<LocalizationServiceHandler>(2);
        private string _originalEditorLanguage;

        [SetUp]
        public void SetUp()
        {
            // 默认播简体中文：首启检测链路 Editor 分支优先，机器/存档无关
            _originalEditorLanguage = LocalizationServiceSettings.EditorLanguage;
            SeedEditorLanguage(Chinese);
        }

        [TearDown]
        public void TearDown()
        {
            for (var i = 0; i < _handlers.Count; i++)
            {
                _handlers[i]?.Internal_Shutdown();
            }

            _handlers.Clear();
            LocalizationServiceSettings.EditorLanguage = _originalEditorLanguage;
        }

        private static void SeedEditorLanguage(Language language) => LocalizationServiceSettings.EditorLanguage = language.Name;

        private T Track<T>(T handler) where T : LocalizationServiceHandler
        {
            handler.Internal_Init();
            _handlers.Add(handler);
            return handler;
        }

        /// <summary>构造整批数据源探针：languages 列序即词条列序，entries 为 (key, 各语言译文)。</summary>
        private static L10nProbeHandler CreateBatchProbe(Language[] languages, params (string key, string[] texts)[] entries)
        {
            var handler = new L10nProbeHandler { Languages = languages.ToList() };
            foreach (var (key, texts) in entries)
            {
                handler.Strings[key] = texts.ToList();
            }

            return handler;
        }

        #region 异步批加载 [ASYNC BATCH LOADING]

        [Test]
        public async Task LoadAsync_CompletesLoadAndResolvesLanguage()
        {
            var handler = Track(new AsyncProbeHandler());
            handler.Languages.AddRange(new[] { English, Chinese });
            handler.Strings["ui.title"] = new List<string> { "Title", "标题" };

            await handler.LoadAsync();

            Assert.IsTrue(handler.IsDataLoaded);
            Assert.IsFalse(handler.IsLoading);
            Assert.AreEqual(1, handler.AsyncLoadCallCount);
            Assert.AreNotEqual("ui.title", handler.GetTextFromIdLanguage("ui.title", null));
        }

        [Test]
        public async Task LoadAsync_InFlight_DeduplicatesAndDefersSyncQueries()
        {
            var handler = Track(new AsyncProbeHandler());
            handler.Languages.AddRange(new[] { English, Chinese });
            handler.Strings["ui.title"] = new List<string> { "Title", "标题" };
            var gate = new UniTaskCompletionSource<LocalizationTextBatch>();
            handler.Gate = gate;

            var first = handler.LoadAsync();
            var second = handler.LoadAsync();

            Assert.AreEqual(1, handler.AsyncLoadCallCount, "并发 LoadAsync 必须共享同一在途任务");
            Assert.IsTrue(handler.IsLoading);

            // 在途期间同步查询按「未就绪」降级：返回 key、不触发第二次加载、不计缺译
            Assert.AreEqual("ui.title", handler.GetTextFromId("ui.title"));
            Assert.AreEqual(0, handler.MissingKeyCount);

            gate.TrySetResult(new LocalizationTextBatch(handler.Languages, handler.Strings, "async-probe"));
            await first;
            await second;

            Assert.IsFalse(handler.IsLoading);
            Assert.IsTrue(handler.IsDataLoaded);
            Assert.AreNotEqual("ui.title", handler.GetTextFromIdLanguage("ui.title", null));
        }

        [Test]
        public async Task LoadAsync_WhenSourceKeepsFailing_StaysNotReadyAndRetries()
        {
            var handler = Track(new AsyncProbeHandler());
            // 数据源全空：异步加载完成后仍是未就绪，且允许再次 LoadAsync 重试

            UtfLogExpect.Error(); // "generate config first" 一次性报错
            await handler.LoadAsync();

            Assert.IsFalse(handler.IsDataLoaded);
            Assert.AreEqual(1, handler.AsyncLoadCallCount);

            handler.Languages.AddRange(new[] { English });
            handler.Strings["ui.title"] = new List<string> { "Title" };
            await handler.LoadAsync();

            Assert.AreEqual(2, handler.AsyncLoadCallCount, "失败后必须允许再次加载");
            Assert.IsTrue(handler.IsDataLoaded);
        }

        #endregion

        #region 按语言列加载 [PER-LANGUAGE COLUMNS]

        [Test]
        public void PerLanguage_FirstQuery_LoadsHeaderAndOnlyNeededColumns()
        {
            var handler = CreateTrilingualProbe();
            handler.FallbackLanguageCodes = new[] { "en" };

            handler.ChangeLanguage(Chinese);

            // 目标列 + 回退列就位；日语列不允许被顺带装载
            CollectionAssert.AreEquivalent(new[] { "zh-Hans", "en" }, handler.LoadedColumnCodes);
            Assert.AreEqual("标题", handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void PerLanguage_Switch_LoadsTargetColumnLazilyOnlyOnce()
        {
            var handler = CreateTrilingualProbe();
            handler.FallbackLanguageCodes = Array.Empty<string>();

            handler.ChangeLanguage(Chinese); // 首启落在播种的中文：装载 zh-Hans 一列
            handler.LoadedColumnCodes.Clear();

            handler.ChangeLanguage(Japanese);
            Assert.AreEqual(1, CountColumnLoads(handler, "ja"), "日语列按需装载且只装一次");
            Assert.AreEqual("タイトル", handler.GetTextFromId("ui.title"));

            handler.ChangeLanguage(Chinese);
            Assert.AreEqual(0, CountColumnLoads(handler, "zh-Hans"), "已装载的列切换回来不得重取源");
            Assert.AreEqual(0, CountColumnLoads(handler, "en"), "空回退链下英语列不应被装载");
        }

        [Test]
        public void PerLanguage_MissingColumn_FailsSwitchAndKeepsCurrent()
        {
            SeedEditorLanguage(English);
            var handler = Track(new PerLanguageProbeHandler());
            handler.Header.AddRange(new[] { English, Chinese });
            handler.FallbackLanguageCodes = Array.Empty<string>();
            handler.Columns["en"] = new Dictionary<string, string> { ["ui.title"] = "Title" };
            // zh-Hans 列返回 null：视为暂缺可重试，切换必须被拒绝

            _ = handler.EntryCount; // 首启：英语列正常装载
            var before = handler.CurrentLanguage;
            Assert.AreEqual(English, before);

            UtfLogExpect.Error();
            handler.ChangeLanguage(Chinese);

            Assert.AreEqual(before, handler.CurrentLanguage, "目标列装载失败必须保持当前语言");
        }

        [Test]
        public void PerLanguage_EmptyColumn_IsLoadedOnceAndTracksMisses()
        {
            SeedEditorLanguage(English);
            var handler = Track(new PerLanguageProbeHandler());
            handler.Header.AddRange(new[] { English, Chinese });
            handler.FallbackLanguageCodes = Array.Empty<string>();
            handler.Columns["en"] = new Dictionary<string, string> { ["ui.title"] = "Title" };
            handler.Columns["zh-Hans"] = new Dictionary<string, string>(); // 已加载的空列：不重试

            _ = handler.EntryCount; // 首启英文
            handler.ChangeLanguage(Chinese);
            Assert.AreEqual(1, CountColumnLoads(handler, "zh-Hans"));

            handler.ChangeLanguage(English);
            handler.ChangeLanguage(Chinese);
            Assert.AreEqual(1, CountColumnLoads(handler, "zh-Hans"), "空列只装一次，不许反复取源");

            UtfLogExpect.Warning();
            Assert.AreEqual("ui.title", handler.GetTextFromId("ui.title"));
            Assert.AreEqual(1, handler.MissingKeyCount, "空列里的 key 属真缺译，必须进缺译追踪");
        }

        [Test]
        public void PerLanguage_GetDictionaryFromId_LoadsAllColumnsOnDemand()
        {
            var handler = CreateTrilingualProbe();
            handler.FallbackLanguageCodes = Array.Empty<string>();

            handler.ChangeLanguage(Chinese); // 首启中文：只装 zh-Hans
            handler.LoadedColumnCodes.Clear();

            var dict = handler.GetDictionaryFromId("ui.title");

            Assert.AreEqual("Title", dict["English"]);
            Assert.AreEqual("标题", dict["ChineseSimplified"]);
            Assert.AreEqual("タイトル", dict["Japanese"]);
            CollectionAssert.AreEquivalent(new[] { "en", "ja" }, handler.LoadedColumnCodes,
                "全语言字典必须按需补齐未装载的列（当前列此前已装载）");
        }

        [Test]
        public void PerLanguage_OverlayStillAppliesOnTopOfSparseColumns()
        {
            var handler = CreateTrilingualProbe();
            handler.FallbackLanguageCodes = Array.Empty<string>();
            handler.ChangeLanguage(Chinese);

            Assert.AreEqual(1, handler.SetStringOverlay("qa", Chinese, new[]
            {
                new KeyValuePair<string, string>("ui.title", "标题-热改"),
            }));

            Assert.AreEqual("标题-热改", handler.GetTextFromId("ui.title"), "覆盖层在列模式同样压过表内译文");
        }

        [Test]
        public void PerLanguage_FallbackResolvesFromSparseColumns()
        {
            var handler = CreateTrilingualProbe();
            handler.FallbackLanguageCodes = new[] { "en" };
            handler.Columns["zh-Hans"]["ui.title"] = null; // 中文列该格缺译

            handler.ChangeLanguage(Chinese);

            Assert.AreEqual("Title", handler.GetTextFromId("ui.title"), "列模式缺译同样走回退链");
            CollectionAssert.AreEquivalent(new[] { "zh-Hans", "en" }, handler.LoadedColumnCodes);
        }

        private PerLanguageProbeHandler CreateTrilingualProbe()
        {
            var handler = Track(new PerLanguageProbeHandler());
            handler.Header.AddRange(new[] { English, Chinese, Japanese });
            handler.Columns["en"] = new Dictionary<string, string> { ["ui.title"] = "Title" };
            handler.Columns["zh-Hans"] = new Dictionary<string, string> { ["ui.title"] = "标题" };
            handler.Columns["ja"] = new Dictionary<string, string> { ["ui.title"] = "タイトル" };
            return handler;
        }

        private static int CountColumnLoads(PerLanguageProbeHandler handler, string code)
        {
            var count = 0;
            for (var i = 0; i < handler.LoadedColumnCodes.Count; i++)
            {
                if (handler.LoadedColumnCodes[i] == code) count++;
            }

            return count;
        }

        #endregion

        #region RTL 识别 [RTL DETECTION]

        [Test]
        public void RightToLeft_RecognizedByLanguageCode()
        {
            Assert.IsTrue(Language.Arabic.IsRightToLeft);
            Assert.IsTrue(Language.Hebrew.IsRightToLeft);
            Assert.IsTrue(new Language("Farsi", "fa").IsRightToLeft, "波斯语按 Code 识别 RTL");
            Assert.IsTrue(new Language("Urdu", "ur").IsRightToLeft);
            Assert.IsFalse(Language.English.IsRightToLeft);
            Assert.IsFalse(Language.ChineseSimplified.IsRightToLeft);
            Assert.IsFalse(Language.Japanese.IsRightToLeft);
        }

        [Test]
        public void IsCurrentLanguageRightToLeft_FollowsSwitch()
        {
            var handler = Track(CreateBatchProbe(new[] { English, Chinese, Language.Arabic },
                ("ui.title", new[] { "Title", "标题", "عنوان" })));
            handler.FallbackLanguageCodes = Array.Empty<string>();

            handler.ChangeLanguage(Language.Arabic);
            Assert.IsTrue(handler.IsCurrentLanguageRightToLeft);

            handler.ChangeLanguage(Chinese);
            Assert.IsFalse(handler.IsCurrentLanguageRightToLeft);
        }

        #endregion

        #region 复数 [PLURALS]

        [Test]
        public void PluralRules_ResolveCategory_Matrix()
        {
            // one iff n==1 族与默认回落
            Assert.AreEqual("one", LocalizationPluralRules.ResolveCategory("en", 1));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("en", 2));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("en", 0));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory(null, 1));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("xx-custom", 5));

            // 无形态变化族
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("zh-Hans", 1));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("ja", 2));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("tr", 1));

            // one for 0..1 族
            Assert.AreEqual("one", LocalizationPluralRules.ResolveCategory("fr", 0));
            Assert.AreEqual("one", LocalizationPluralRules.ResolveCategory("fr", 1));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("fr", 2));

            // 斯拉夫基数族
            Assert.AreEqual("one", LocalizationPluralRules.ResolveCategory("ru", 21));
            Assert.AreEqual("few", LocalizationPluralRules.ResolveCategory("ru", 22));
            Assert.AreEqual("many", LocalizationPluralRules.ResolveCategory("ru", 25));
            Assert.AreEqual("many", LocalizationPluralRules.ResolveCategory("ru", 111));
            Assert.AreEqual("few", LocalizationPluralRules.ResolveCategory("ru", 122));

            // 波兰语
            Assert.AreEqual("one", LocalizationPluralRules.ResolveCategory("pl", 1));
            Assert.AreEqual("few", LocalizationPluralRules.ResolveCategory("pl", 3));
            Assert.AreEqual("many", LocalizationPluralRules.ResolveCategory("pl", 5));
            Assert.AreEqual("few", LocalizationPluralRules.ResolveCategory("pl", 103));

            // 阿拉伯语（六种形态）
            Assert.AreEqual("zero", LocalizationPluralRules.ResolveCategory("ar", 0));
            Assert.AreEqual("one", LocalizationPluralRules.ResolveCategory("ar", 1));
            Assert.AreEqual("two", LocalizationPluralRules.ResolveCategory("ar", 2));
            Assert.AreEqual("few", LocalizationPluralRules.ResolveCategory("ar", 5));
            Assert.AreEqual("many", LocalizationPluralRules.ResolveCategory("ar", 25));
            Assert.AreEqual("other", LocalizationPluralRules.ResolveCategory("ar", 700));
        }

        [Test]
        public void Plural_ResolvesCategoryAndFormatsCount()
        {
            var handler = Track(CreateBatchProbe(new[] { English, Language.Russian },
                ("quest.items#one", new[] { "{0} item in bag", "{0} предмет" }),
                ("quest.items#few", new[] { "{0} item-ish", "{0} предмета" }),
                ("quest.items#other", new[] { "{0} items in bag", "{0} предметов" })));
            handler.FallbackLanguageCodes = Array.Empty<string>();

            handler.ChangeLanguage(English);
            Assert.AreEqual("1 item in bag", handler.GetPluralTextFromId("quest.items", 1));
            Assert.AreEqual("5 items in bag", handler.GetPluralTextFromId("quest.items", 5));

            handler.ChangeLanguage(Language.Russian);
            Assert.AreEqual("21 предмет", handler.GetPluralTextFromId("quest.items", 21));
            Assert.AreEqual("22 предмета", handler.GetPluralTextFromId("quest.items", 22));
            Assert.AreEqual("25 предметов", handler.GetPluralTextFromId("quest.items", 25));
        }

        [Test]
        public void Plural_ExtraArguments_ShiftAfterCount()
        {
            var handler = Track(CreateBatchProbe(new[] { English },
                ("greeting", new[] { "Hello {1}, you have {0} coins" })));
            handler.FallbackLanguageCodes = Array.Empty<string>();

            handler.ChangeLanguage(English);

            Assert.AreEqual("Hello Moirai, you have 3 coins", handler.GetPluralTextFromId("greeting", 3, "Moirai"));
        }

        [Test]
        public void Plural_BareKeyFallback_WhenNoCategoryEntries()
        {
            var handler = Track(CreateBatchProbe(new[] { English },
                ("loot", new[] { "x{0}" })));
            handler.FallbackLanguageCodes = Array.Empty<string>();

            handler.ChangeLanguage(English);

            Assert.AreEqual("x7", handler.GetPluralTextFromId("loot", 7));
        }

        [Test]
        public void Plural_FullChainMiss_TracksBaseKeyOnce()
        {
            var handler = Track(CreateBatchProbe(new[] { English },
                ("ui.title", new[] { "Title" })));
            handler.FallbackLanguageCodes = Array.Empty<string>();
            handler.ChangeLanguage(English);

            UtfLogExpect.Warning();
            Assert.AreEqual("quest.missing", handler.GetPluralTextFromId("quest.missing", 1));
            Assert.AreEqual("quest.missing", handler.GetPluralTextFromId("quest.missing", 5));

            Assert.AreEqual(1, handler.MissingKeyCount, "复数全链落空只按基础 key 计一次缺译");
            Assert.AreEqual(2, handler.MissingKeyEventCount);
        }

        #endregion

        #region 渠道烘焙 [CHANNEL BAKE]

        [Test]
        public void BakeLanguage_WritesAssetAndClear_Removes()
        {
            try
            {
                Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.BakeLanguage("French");
                Assert.AreEqual("fr", Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.GetBakedLanguageCode(),
                    "烘焙按语言 Code 落盘，与运行期消费面一致");

                Assert.IsTrue(Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.ClearBaked());
                Assert.IsNull(Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.GetBakedLanguageCode());
                Assert.IsFalse(Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.ClearBaked(), "重复清除应返回 false");
            }
            finally
            {
                Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.ClearBaked();
            }
        }

        [Test]
        public void BakeLanguage_UnknownCode_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                Moirai.Atropos.Localization.Editor.LocalizationBuildBaker.BakeLanguage("klingon"));
        }

        #endregion

        #region 探针处理器 [PROBE HANDLERS]

        internal sealed class AsyncProbeHandler : LocalizationServiceHandler
        {
            public readonly List<Language> Languages = new List<Language>();
            public readonly Dictionary<string, List<string>> Strings = new Dictionary<string, List<string>>();
            public UniTaskCompletionSource<LocalizationTextBatch> Gate;
            public int AsyncLoadCallCount;

            internal override UniTask<LocalizationTextBatch> LoadLocalizedTextBatchAsync()
            {
                AsyncLoadCallCount++;
                if (Gate != null) return Gate.Task;

                return UniTask.FromResult(new LocalizationTextBatch(Languages, Strings, "async-probe"));
            }
        }

        internal sealed class PerLanguageProbeHandler : LocalizationServiceHandler
        {
            public readonly List<Language> Header = new List<Language>();
            public readonly Dictionary<string, Dictionary<string, string>> Columns = new Dictionary<string, Dictionary<string, string>>();
            public readonly List<string> LoadedColumnCodes = new List<string>();

            protected override bool SupportsPerLanguageLoad => true;

            protected override IReadOnlyList<Language> LoadLanguageHeader() => Header;

            protected override Dictionary<string, string> LoadLanguageColumn(Language language)
            {
                LoadedColumnCodes.Add(language.Code);
                return Columns.TryGetValue(language.Code, out var column) ? column : null;
            }
        }

        #endregion
    }
}
