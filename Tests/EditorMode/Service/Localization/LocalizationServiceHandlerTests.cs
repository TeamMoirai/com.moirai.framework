using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos.Localization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Localization
{
    /// <summary>
    /// 本地化处理器（<see cref="LocalizationServiceHandler"/>）行为测试：
    /// 首启语言可解析性、首查询取译、缺译回退链、重注入与事件时序、格式化异常隔离、加载期校验。
    /// <para>处理器级用例直接构造桩数据源（与 <c>DefaultProcedureHandlerTests</c> 同约定），
    /// 不碰 <see cref="LocalizationService"/> 的静态 Handler——那是跨用例状态，
    /// 写脏会让 <c>ServiceContractTests</c> 的降级断言按执行顺序随机失败。</para>
    /// <para>首启语言取自检测链（命令行 → 编辑器设置 → 存档 → 系统语言），机器相关，
    /// 因此需要"确实发生切换"的用例一律经 <see cref="OtherLoadedLanguage"/> 取目标语言，不硬编码。</para>
    /// </summary>
    [TestFixture]
    public sealed class LocalizationServiceHandlerTests
    {
        private static readonly Language English = Language.English;
        private static readonly Language Chinese = Language.ChineseSimplified;
        private static readonly Language Japanese = Language.Japanese;

        private L10nProbeHandler _handler;

        [SetUp]
        public void SetUp()
        {
            _handler = new L10nProbeHandler { FallbackLanguageCodes = new[] { "en" } };
            _handler.Internal_Init();
        }

        [TearDown]
        public void TearDown()
        {
            _handler?.Internal_Shutdown();
            _handler = null;
        }

        /// <summary>装载英/中两列词条；<c>null</c> 表示该列缺译。</summary>
        private void LoadStrings(string key, string english, string chinese)
        {
            _handler.Languages = new List<Language> { English, Chinese };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                [key] = new List<string> { english, chinese },
            };
        }

        /// <summary>取一个必定与当前不同的已加载语言——首启语言由检测链决定，用例不能假定。</summary>
        private Language OtherLoadedLanguage()
        {
            _ = _handler.EntryCount;
            return _handler.CurrentLanguage == English ? Chinese : English;
        }

        #region 首启与首查询 [BOOTSTRAP]

        [Test]
        public void InitialLanguage_IsAlwaysPresentInLoadedLanguages()
        {
            // 检测链给出的语言完全可能没进这批词条（中文系统跑只出英日的包）。
            // 此时必须兜到回退链/表头，而不是把当前语言停在表外——那会让每一条查询都露 key
            LocalizationService.RegisterLanguageMap(Chinese.Name);
            _handler.Languages = new List<Language> { English, Japanese };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                ["ui.title"] = new List<string> { "Title", "タイトル" },
            };

            Assert.GreaterOrEqual(_handler.CurrentLanguageIndex, 0);
            Assert.AreEqual(_handler.CurrentLanguage, _handler.Languages[_handler.CurrentLanguageIndex]);
            Assert.AreNotEqual("ui.title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void FirstQuery_ReturnsTranslationInsteadOfKey()
        {
            // 回归：当前语言在数据加载之内才解析，而实参在加载前就求值成了 null，首查询会露 key
            LoadStrings("ui.title", "Title", "标题");

            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(1, _handler.LoadCallCount, "数据只应加载一次");
        }

        [Test]
        public void NullLanguage_MeansCurrentLanguage()
        {
            LoadStrings("ui.title", "Title", "标题");
            var target = OtherLoadedLanguage();
            _handler.ChangeLanguage(target);

            var expected = target == English ? "Title" : "标题";
            Assert.AreEqual(expected, _handler.GetTextFromIdLanguage("ui.title", null));
        }

        #endregion

        #region 缺译回退 [FALLBACK]

        [Test]
        public void MissingTranslation_FallsBackToChain()
        {
            LoadStrings("ui.title", "Title", null);
            _handler.ChangeLanguage(Chinese);

            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void WhitespaceTranslation_CountsAsMissing()
        {
            LoadStrings("ui.title", "Title", "   ");
            _handler.ChangeLanguage(Chinese);

            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void AllLanguagesMissing_ReturnsKey()
        {
            LoadStrings("ui.title", null, "");

            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void EmptyFallbackChain_KeepsLegacyKeyBehaviour()
        {
            LoadStrings("ui.title", "Title", null);
            _handler.FallbackLanguageCodes = Array.Empty<string>();
            _handler.ChangeLanguage(Chinese);

            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(0, _handler.FallbackChain.Count);
        }

        [Test]
        public void UnresolvedFallbackCode_IsSkipped()
        {
            LoadStrings("ui.title", "Title", null);
            // 认不出的语言代码不能被静默折成默认语言放过，否则配置错误会一路带到上线
            _handler.FallbackLanguageCodes = new[] { "zh-CN" };
            _handler.ChangeLanguage(Chinese);

            Assert.AreEqual(0, _handler.FallbackChain.Count);
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void FallbackChain_SkipsLanguagesAbsentFromData()
        {
            _handler.Languages = new List<Language> { English, Japanese };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                ["ui.title"] = new List<string> { "Title", "タイトル" },
            };
            _handler.FallbackLanguageCodes = new[] { "zh-Hans", "ja" };
            _handler.ChangeLanguage(English);

            CollectionAssert.AreEqual(new[] { Japanese }, _handler.FallbackChain);
        }

        #endregion

        #region 切换语义 [LANGUAGE SWITCH]

        [Test]
        public void OnLanguageChanged_FiresAfterLocalizersReinjected()
        {
            LoadStrings("ui.title", "Title", "标题");
            var container = new GameObject(nameof(OnLanguageChanged_FiresAfterLocalizersReinjected));
            var localizer = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                var target = OtherLoadedLanguage();
                var expected = target == English ? "Title" : "标题";
                var order = new List<string>();
                localizer.OnLocalized = () => order.Add($"Localize:{_handler.GetTextFromId("ui.title")}");
                _handler.AddLocalizer(localizer);
                _handler.OnLanguageChanged += language => order.Add($"Event:{_handler.GetTextFromId("ui.title")}");

                _handler.ChangeLanguage(target);

                // 订阅者在回调里取文本必须已是新语言，且本地化器先于事件重注入完成
                CollectionAssert.AreEqual(new[] { $"Localize:{expected}", $"Event:{expected}" }, order);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void ReentrantChangeLanguage_IsIgnored()
        {
            LoadStrings("ui.title", "Title", "标题");
            // 先把首启那一轮切换走完再挂探针，否则注入计数里混着首启那次
            var from = OtherLoadedLanguage() == English ? Chinese : English;
            var container = new GameObject(nameof(ReentrantChangeLanguage_IsIgnored));
            var localizer = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                _handler.AddLocalizer(localizer);
                var nested = true;
                localizer.OnLocalized = () =>
                {
                    if (!nested) return;
                    nested = false;
                    // 注入回调里再切语言会让快照与事件顺序失效，必须拦下而不是递归
                    _handler.ChangeLanguage(from);
                };
                var eventCount = 0;
                _handler.OnLanguageChanged += _ => eventCount++;
                LogAssert.Expect(LogType.Error, new Regex("another language switch is already in progress"));

                _handler.ChangeLanguage(OtherLoadedLanguage());

                Assert.AreEqual(1, localizer.LocalizeCount, "嵌套切换不得引发第二次重注入");
                Assert.AreEqual(1, eventCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void FailingLocalizer_DoesNotBlockOthersOrTheEvent()
        {
            LoadStrings("ui.title", "Title", "标题");
            var target = OtherLoadedLanguage();
            var container = new GameObject(nameof(FailingLocalizer_DoesNotBlockOthersOrTheEvent));
            var broken = container.AddComponent<L10nProbeLocalizer>();
            var healthy = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                broken.ThrowOnLocalize = true;
                _handler.AddLocalizer(broken);
                _handler.AddLocalizer(healthy);
                var eventFired = false;
                _handler.OnLanguageChanged += _ => eventFired = true;
                LogAssert.Expect(LogType.Error, new Regex("probe localizer failed"));

                _handler.ChangeLanguage(target);

                Assert.AreEqual(1, healthy.LocalizeCount, "单个本地化器抛异常不得影响其余");
                Assert.IsTrue(eventFired);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void ChangeLanguage_UnregisteredLanguage_KeepsCurrent()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(OtherLoadedLanguage());
            var before = _handler.CurrentLanguage;

            _handler.ChangeLanguage(Japanese);

            Assert.AreEqual(before, _handler.CurrentLanguage);
        }

        [Test]
        public void ActivateNextLanguage_WrapsAroundLoadedLanguages()
        {
            LoadStrings("ui.title", "Title", "标题");
            var from = OtherLoadedLanguage();
            _handler.ChangeLanguage(from);

            var next = _handler.ActivateNextLanguage();

            Assert.AreEqual(from == English ? Chinese.Name : English.Name, next);
            Assert.AreEqual(_handler.CurrentLanguage, from == English ? Chinese : English);
        }

        #endregion

        #region 查询兜底 [QUERY SAFETY]

        [Test]
        public void FormatMismatch_ReturnsRawTextAndDoesNotThrow()
        {
            // 一条文案写坏占位符，不该把整块界面的查询抛出去
            LoadStrings("ui.price", "Price: {0} and {1}", null);
            _handler.ChangeLanguage(English);
            LogAssert.Expect(LogType.Error, new Regex("invalid placeholders for 1 argument"));

            Assert.AreEqual("Price: {0} and {1}", _handler.GetTextFromId("ui.price", 10));
        }

        [Test]
        public void FormatWithMatchingArguments_Succeeds()
        {
            LoadStrings("ui.price", "Price: {0}", null);
            _handler.ChangeLanguage(English);

            Assert.AreEqual("Price: 42", _handler.GetTextFromId("ui.price", 42));
        }

        [Test]
        public void MissingKey_ReturnsKey()
        {
            LoadStrings("ui.title", "Title", "标题");

            Assert.AreEqual("nope", _handler.GetTextFromId("nope"));
            Assert.IsFalse(_handler.Has("nope"));
            Assert.IsTrue(_handler.Has("ui.title"));
        }

        [Test]
        public void DataNotReady_ReturnsKeyWithoutFailing()
        {
            // 未转表要报一次 Error 让用户知道为什么全是 key；"只打一次"由这条 Expect 锁住
            LogAssert.Expect(LogType.Error, new Regex("generate config first"));

            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(0, _handler.EntryCount);
            Assert.AreEqual(-1, _handler.CurrentLanguageIndex);
            Assert.GreaterOrEqual(_handler.LoadCallCount, 1);
        }

        [Test]
        public void ColumnCountMismatch_RejectsWholeDataset()
        {
            // 语言列数错位表现为"显示了别的语言"而不是报错，必须在加载期整批拦下
            _handler.Languages = new List<Language> { English, Chinese };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                ["ui.title"] = new List<string> { "Title" },
            };
            LogAssert.Expect(LogType.Error, new Regex("has 1 language columns, but 2 languages are registered"));

            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(0, _handler.LanguageCount);
            Assert.AreEqual(0, _handler.TotalTextLength, "半损坏数据不得留下可读的规模统计");
        }

        [Test]
        public void Shutdown_ResetsLoadedState()
        {
            LoadStrings("ui.title", "Title", "标题");
            Assert.AreEqual(1, _handler.EntryCount);

            _handler.Internal_Shutdown();
            // 懒加载会重新向数据源取数，只有数据源同时空掉才观察得到运行时状态是否被复位
            _handler.Languages = new List<Language>();
            _handler.Strings = new Dictionary<string, List<string>>();
            LogAssert.Expect(LogType.Error, new Regex("generate config first"));

            Assert.AreEqual(0, _handler.EntryCount);
            Assert.AreEqual(-1, _handler.CurrentLanguageIndex);
            Assert.AreEqual(0, _handler.FallbackChain.Count);
            Assert.AreEqual(0, _handler.TotalTextLength);
        }

        [Test]
        public void Diagnostics_ReflectLoadedData()
        {
            LoadStrings("ui.title", "Title", "标题");

            Assert.AreEqual(1, _handler.EntryCount);
            Assert.AreEqual(2, _handler.LanguageCount);
            Assert.AreEqual("Title".Length + "标题".Length, _handler.TotalTextLength);
        }

        #endregion

        #region 语言解析 [LANGUAGE RESOLUTION]

        [Test]
        public void BuiltInLanguages_AreSharedInstances()
        {
            Assert.AreSame(Language.English, Language.English);
            Assert.AreSame(Language.BuiltinLanguages, Language.BuiltinLanguages);
            Assert.AreSame(Language.BuiltinLanguages[0], Language.Unspecified);
        }

        [Test]
        public void SystemLanguage_ConvertsBothWays()
        {
            Language detected = SystemLanguage.ChineseSimplified;
            Assert.AreSame(Chinese, detected);
            Assert.AreEqual(SystemLanguage.ChineseSimplified, (SystemLanguage)Chinese);
            // 自定义语言不参与 SystemLanguage 转换
            Assert.AreEqual(SystemLanguage.Unknown, (SystemLanguage)new Language("Klingon", "tlh"));
            // Unknown 不在内置语言表里，转换结果落到 Unspecified
            Assert.AreEqual(Language.Unspecified, (Language)SystemLanguage.Unknown);
        }

        [Test]
        public void ToLanguage_IgnoresCaseAndFallsBackToDefault()
        {
            Assert.AreEqual(English, LocalizationService.ToLanguage("EN", false));
            Assert.AreEqual(Chinese, LocalizationService.ToLanguage("chineseSimplified", false));
            Assert.AreEqual(Chinese, LocalizationService.ToLanguage("zh-hans", false));
            Assert.AreEqual(LocalizationService.defaultLanguage, LocalizationService.ToLanguage("klingon", false));
        }

        [Test]
        public void TryGetBuiltInLanguage_DoesNotSilentlyDefault()
        {
            Assert.IsTrue(LocalizationService.TryGetBuiltInLanguage("zh-Hans", out var chinese));
            Assert.AreEqual(Chinese, chinese);
            Assert.IsTrue(LocalizationService.TryGetBuiltInLanguage("JAPANESE", out var japanese));
            Assert.AreEqual(Japanese, japanese);
            // ToLanguage 会把认不出的输入落到默认语言，配置校验要能区分"写错了"和"就是要默认语言"
            Assert.IsFalse(LocalizationService.TryGetBuiltInLanguage("zh-CN", out _));
            Assert.IsFalse(LocalizationService.TryGetBuiltInLanguage(null, out _));
        }

        #endregion
    }

    /// <summary>桩本地化器——记录重注入次序，可切换为抛异常。文件级类型：<c>AddComponent</c> 不接受嵌套类型。</summary>
    internal sealed class L10nProbeLocalizer : LocalizerBase
    {
        public Action OnLocalized;
        public int LocalizeCount;
        public bool ThrowOnLocalize;

        protected override void Prepare()
        {
        }

        internal override void Localize()
        {
            LocalizeCount++;
            if (ThrowOnLocalize) throw new InvalidOperationException("probe localizer failed");
            OnLocalized?.Invoke();
        }
    }

    /// <summary>桩本地化数据源——按生产约定在解析词条的同时注册可用语言。</summary>
    internal sealed class L10nProbeHandler : LocalizationServiceHandler
    {
        public List<Language> Languages = new List<Language>();
        public Dictionary<string, List<string>> Strings = new Dictionary<string, List<string>>();
        public int LoadCallCount;

        protected override (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData()
        {
            LoadCallCount++;
            foreach (var language in Languages)
            {
                LocalizationService.RegisterLanguageMap(language.Name);
            }

            return (Languages, Strings);
        }
    }
}
