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
        public void DataSourceThrows_ReportsCauseInsteadOfMissingConfig()
        {
            // 读表抛异常≠没生成配置：兜底语会把排查方向整个带偏
            _handler.ThrowOnLoad = new InvalidOperationException("table read failed");
            // Expect 只配一条：既锁住真因可见，也锁住"不随每次查询重播异常"——多落一条按意外日志判负
            LogAssert.Expect(LogType.Error, new Regex("table read failed"));

            var attempts = _handler.LoadCallCount;
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.GreaterOrEqual(_handler.LoadCallCount - attempts, 2, "取数失败不置已加载标记，每次查询继续重试");
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
            LogAssert.Expect(LogType.Error, new Regex("column count that mismatches the 2 declared languages"));

            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(0, _handler.LanguageCount);
            Assert.AreEqual(0, _handler.ResidentChars, "半损坏数据不得留下可读的规模统计");
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
            Assert.AreEqual(0, _handler.ResidentChars);
        }

        [Test]
        public void Diagnostics_ReflectLoadedData()
        {
            LoadStrings("ui.title", "Title", "标题");

            Assert.AreEqual(1, _handler.EntryCount);
            Assert.AreEqual(2, _handler.LanguageCount);
            Assert.AreEqual("Title".Length + "标题".Length, _handler.ResidentChars);
        }

        #endregion

        #region 运行时覆盖 [OVERLAY]

        [Test]
        public void Overlay_WinsOverTableText()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);

            Assert.AreEqual(1, _handler.SetStringOverlay("remote-ops", English,
                new[] { new KeyValuePair<string, string>("ui.title", "Title!") }));
            Assert.AreEqual("Title!", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void Overlay_IsPerLanguageAndAppliesOnFallbackStepToo()
        {
            LoadStrings("ui.title", "Title", null);
            _handler.ChangeLanguage(Chinese);

            // 覆盖英语列：中文缺译回退到英语时，拿到的也必须是覆盖后的那版
            _handler.SetStringOverlay("remote-ops", English, new[] { new KeyValuePair<string, string>("ui.title", "Title!") });
            Assert.AreEqual("Title!", _handler.GetTextFromId("ui.title"));

            // 覆盖只落在被指定的语言上，不越界污染另一列
            _handler.SetStringOverlay("qa-force", Chinese, new[] { new KeyValuePair<string, string>("ui.title", "标题QA") });
            Assert.AreEqual("标题QA", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void Overlay_EmptyValueIsNotAnOverride()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);

            _handler.SetStringOverlay("remote-ops", English, new[] { new KeyValuePair<string, string>("ui.title", "   ") });

            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void Overlay_ClearBySource_LeavesOtherSourcesAlone()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);
            _handler.SetStringOverlay("remote-ops", English, new[] { new KeyValuePair<string, string>("ui.title", "A") });
            _handler.SetStringOverlay("qa-force", English, new[] { new KeyValuePair<string, string>("ui.title", "B") });

            // 后注册的一层优先
            Assert.AreEqual("B", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(2, _handler.StringOverlayLayerCount);

            Assert.IsTrue(_handler.ClearStringOverlay("qa-force"));
            Assert.AreEqual("A", _handler.GetTextFromId("ui.title"));
            Assert.IsFalse(_handler.ClearStringOverlay("qa-force"));

            _handler.ClearAllStringOverlays();
            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(0, _handler.StringOverlayLayerCount);
        }

        [Test]
        public void Overlay_RejectsLanguageAbsentFromBatch()
        {
            LoadStrings("ui.title", "Title", "标题");

            Assert.AreEqual(-1, _handler.SetStringOverlay("remote-ops", Japanese,
                new[] { new KeyValuePair<string, string>("ui.title", "X") }));
            Assert.AreEqual(0, _handler.StringOverlayLayerCount);
        }

        [Test]
        public void Overlay_DoesNotOutliveShutdown()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);
            _handler.SetStringOverlay("remote-ops", English, new[] { new KeyValuePair<string, string>("ui.title", "Title!") });
            Assert.AreEqual("Title!", _handler.GetTextFromId("ui.title"));

            _handler.Internal_Shutdown();
            _handler.Internal_Init();

            // 关服后热改内容不得串进下一次会话（此时数据源仍是同一批，只有覆盖层被丢弃）
            Assert.AreEqual(0, _handler.StringOverlayLayerCount);
            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
        }

        #endregion

        #region 不装箱格式化 [TYPED FORMAT]

        [Test]
        public void TypedOverloads_FormatByArity()
        {
            // 一批四键：批只会被加载一次（后改数据源不会生效，要重取得 Shutdown+Init），
            // 所以四种 arity 必须落在同一批数据上验
            _handler.Languages = new List<Language> { English, Chinese };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                ["fmt.a"] = new List<string> { "A:{0}", "甲:{0}" },
                ["fmt.b"] = new List<string> { "B:{0}/{1}", null },
                ["fmt.c"] = new List<string> { "C:{0}/{1}/{2}", null },
                ["fmt.d"] = new List<string> { "D:{0}/{1}/{2}/{3}", null },
            };
            _handler.ChangeLanguage(English);

            Assert.AreEqual("A:1", _handler.GetTextFromId("fmt.a", 1));
            Assert.AreEqual("B:1/2", _handler.GetTextFromId("fmt.b", 1, 2));
            Assert.AreEqual("C:1/2/3", _handler.GetTextFromId("fmt.c", 1, 2, 3));
            Assert.AreEqual("D:1/2/3/4", _handler.GetTextFromId("fmt.d", 1, 2, 3, 4));
            Assert.AreEqual("甲:7", _handler.GetTextFromIdLanguage("fmt.a", Chinese, 7));
        }

        [Test]
        public void TypedOverloads_MissingKeyReturnsKey_AndBadPlaceholderDoesNotThrow()
        {
            LoadStrings("fmt.a", "A:{0} and {1}", null);
            _handler.ChangeLanguage(English);
            LogAssert.Expect(LogType.Error, new Regex("invalid placeholders for 1 argument"));

            Assert.AreEqual("A:{0} and {1}", _handler.GetTextFromId("fmt.a", 1));
            Assert.AreEqual("nope", _handler.GetTextFromId<int>("nope", 1));
        }

        [Test]
        public void TypedOverloads_ResolveFallbackChainToo()
        {
            LoadStrings("fmt.a", "A:{0}", null);
            _handler.ChangeLanguage(Chinese);

            Assert.AreEqual("A:7", _handler.GetTextFromId("fmt.a", 7));
        }

        #endregion

        #region 句柄订阅 [SUBSCRIPTION]

        [Test]
        public void SubscribeLanguageChanged_FiresWithEvent_AndStopsOnDispose()
        {
            LoadStrings("ui.title", "Title", "标题");
            var target = OtherLoadedLanguage();   // 先把首启那一轮切换走完再挂订阅，否则派发计数里混着首启那次
            var order = new List<string>();
            var handle = _handler.SubscribeLanguageChanged(language => order.Add("Handle"));
            _handler.OnLanguageChanged += language => order.Add("Event");

            _handler.ChangeLanguage(target);
            CollectionAssert.AreEquivalent(new[] { "Event", "Handle" }, order, "静态事件与句柄订阅应在同一次派发里各命中一次");

            order.Clear();
            handle.Dispose();
            _handler.ChangeLanguage(OtherLoadedLanguage());
            CollectionAssert.AreEqual(new[] { "Event" }, order);
        }

        [Test]
        public void SubscribeLanguageChanged_InvalidatedOnShutdown()
        {
            LoadStrings("ui.title", "Title", "标题");
            var subscription = (LanguageChangeSubscription)_handler.SubscribeLanguageChanged(_ => { });

            Assert.IsTrue(subscription.IsSubscribed);
            _handler.Internal_Shutdown();
            Assert.IsFalse(subscription.IsSubscribed);

            // 作废之后再 Dispose 只是空操作，不该抛也不该碰到已释放的表
            Assert.DoesNotThrow(() => subscription.Dispose());
        }

        [Test]
        public void SubscribeLanguageChanged_ThrowingSubscriberDoesNotBlockOthers()
        {
            LoadStrings("ui.title", "Title", "标题");
            var target = OtherLoadedLanguage();   // 先走完首启那一轮，否则一次派发会算成两次
            var second = new List<string>();
            LogAssert.Expect(LogType.Error, new Regex("probe subscriber failed"));

            _handler.SubscribeLanguageChanged(_ => throw new InvalidOperationException("probe subscriber failed"));
            _handler.SubscribeLanguageChanged(language => second.Add(language.Name));

            _handler.ChangeLanguage(target);

            Assert.AreEqual(1, second.Count, "抛出异常的订阅者不得挡掉同一次派发里的其余订阅者");
        }

        #endregion

        #region 数据交付契约 [BATCH CONTRACT]

        [Test]
        public void Batch_DefinesColumnOrder_NotTheGlobalLanguageRegistry()
        {
            // 语言头随批自报：列序与注册先后无关，也不必再靠"先取词条才会填注册表"的求值顺序
            _handler.Languages = new List<Language> { Chinese, English };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                ["ui.title"] = new List<string> { "标题", "Title" },
            };
            _handler.ChangeLanguage(English);

            Assert.AreEqual(1, _handler.CurrentLanguageIndex);
            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));
        }

        [Test]
        public void Store_RejectsBadBatchAndKeepsLastGoodSnapshot()
        {
            var store = new LocalizationStore();
            var good = new LocalizationTextBatch(new[] { English },
                new Dictionary<string, List<string>> { ["k"] = new List<string> { "T" } }, "test");
            Assert.IsTrue(store.TryApply(good, out _));

            var bad = new LocalizationTextBatch(new[] { English, Chinese },
                new Dictionary<string, List<string>> { ["k"] = new List<string> { "T" } }, "test");

            Assert.IsFalse(store.TryApply(bad, out var rejectedKey));
            Assert.AreEqual("k", rejectedKey);
            // 换批失败不该把本来能显示的文案一起抹掉
            Assert.AreEqual(1, store.EntryCount);
            Assert.AreEqual(1, store.LanguageCount);
            Assert.AreEqual("T", store.Resolve("k", English, 0, null, null));
        }

        [Test]
        public void Store_ReportsResidentCharsAndRejectsEmptyBatch()
        {
            var store = new LocalizationStore();

            Assert.IsFalse(store.TryApply(LocalizationTextBatch.Empty, out _));
            Assert.AreEqual(0, store.EntryCount);

            var batch = new LocalizationTextBatch(new[] { English, Chinese },
                new Dictionary<string, List<string>> { ["k"] = new List<string> { "Title", "标题" } }, "test");
            Assert.IsTrue(store.TryApply(batch, out _));
            Assert.AreEqual("Title".Length + "标题".Length, batch.ResidentChars);
            Assert.AreEqual("test", batch.SourceId);
        }

        [Test]
        public void ConfigTableHandler_WithoutSelfReportedLanguages_RejectsBatchAndKeepsRetryable()
        {
            // 语言必须随表自报：EditMode 下 ConfigTableService 降级（无处理器），codes 为空 →
            // 整批拒载、保持未就绪可重试，不再回落任何全局注册表
            var handler = new ConfigTableLocalizationHandler { FallbackLanguageCodes = new[] { "en" } };
            handler.Internal_Init();

            try
            {
                LogAssert.Expect(LogType.Error, new Regex("generate config first"));

                Assert.AreEqual("ui.title", handler.GetTextFromId("ui.title"));
                Assert.AreEqual("ui.title", handler.GetTextFromId("ui.title"), "拒载后必须保持重试语义而非哑死");
                Assert.AreEqual(0, handler.EntryCount);
                Assert.AreEqual(0, handler.LoadedLanguages.Count);
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void ResolveLanguages_UnknownCodesBecomeCustomLanguagesInOrder()
        {
            // 项目自定义语言（不在内置表）随表发行：按自定义语言直通且列序不被重排
            var languages = LocalizationService.ResolveLanguages(new[] { "zh-Hans", "Klingon" });

            Assert.AreEqual(2, languages.Count);
            Assert.AreSame(Language.ChineseSimplified, languages[0]);
            Assert.IsTrue(languages[1].Custom);
            Assert.AreEqual("Klingon", languages[1].Code);
        }

        #endregion

        #region 加载失败闸门 [LOAD FAILURE GATES]

        [Test]
        public void LoadThrowing_LogsOnlyOnceAcrossQueries()
        {
            // 数据源抛异常必须与空批同待遇：表未就绪期间每个 localizer/查询都在重试，无闸门即异常堆栈风暴
            // 注：LogUtility.Error(ex) 经 DefaultLogHandler 以 LogType.Error 渲染（异常文本内嵌）
            _handler.ThrowOnLoad = new InvalidOperationException("probe tables not ready");
            LogAssert.Expect(LogType.Error, new Regex("probe tables not ready"));

            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));
            Assert.GreaterOrEqual(_handler.LoadCallCount, 1, "未就绪时应保持重试语义");
        }

        [Test]
        public void NoLanguage_LogsOnlyOnceAcrossSwitchAttempts()
        {
            LogAssert.Expect(LogType.Error, new Regex("generate config first"));
            LogAssert.Expect(LogType.Error, new Regex("No language available"));

            _handler.ChangeLanguage(English);
            _handler.ChangeLanguage(Chinese);
            Assert.IsNull(_handler.ActivateNextLanguage());
            Assert.IsNull(_handler.ActivatePreviousLanguage());
        }

        #endregion

        #region 数据重载 [RELOAD]

        [Test]
        public void ReloadTexts_SwapsBatch()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);
            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"));

            _handler.Strings["ui.title"] = new List<string> { "TitleV2", "标题V2" };
            _handler.ReloadTexts();

            Assert.AreEqual("TitleV2", _handler.GetTextFromId("ui.title"));
            Assert.AreEqual(2, _handler.LoadCallCount);
        }

        [Test]
        public void ReloadTexts_WhenSourceBroken_KeepsPreviousSnapshot()
        {
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);

            _handler.ThrowOnLoad = new InvalidOperationException("probe hotfix corrupted");
            LogAssert.Expect(LogType.Error, new Regex("probe hotfix corrupted"));
            _handler.ReloadTexts();

            Assert.AreEqual("Title", _handler.GetTextFromId("ui.title"), "重载失败必须保留上一份可用快照");
            Assert.AreEqual(English, _handler.CurrentLanguage);
        }

        [Test]
        public void ReloadTexts_SameLanguage_ReinjectsAndRaisesEvent()
        {
            // 语言未变时 ChangeLanguage 早退，但词条内容可能已更新——重载必须强制重注入并广播
            LoadStrings("ui.title", "Title", "标题");
            _handler.ChangeLanguage(English);
            var container = new GameObject(nameof(ReloadTexts_SameLanguage_ReinjectsAndRaisesEvent));
            var localizer = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                _handler.AddLocalizer(localizer);
                var eventCount = 0;
                _handler.OnLanguageChanged += _ => eventCount++;
                var localizeBefore = localizer.LocalizeCount;

                _handler.ReloadTexts();

                Assert.Greater(localizer.LocalizeCount, localizeBefore, "语言未变也必须强制重注入");
                Assert.AreEqual(1, eventCount, "语言未变也必须广播一次，订阅方才拿得到新文案");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        #endregion

        #region 外观注册挂起 [FACADE PENDING]

        // 外观静态状态跨用例共享：经生成的 Internal_PeekHandler / Internal_UseHandler 快照与复位，
        // 保证 ServiceContractTests 的降级断言不受执行顺序影响

        private int CountRegistrationOf(LocalizerBase localizer)
        {
            var count = 0;
            foreach (var item in _handler._localizers)
            {
                if (ReferenceEquals(item, localizer)) count++;
            }

            return count;
        }

        [Test]
        public void AddLocalizer_BeforeHandlerReady_PendsAndReplaysOnce()
        {
            // 场景物体的 Awake 可能早于世界初始化：注册先挂起，就绪后回放且仅回放一次
            var original = LocalizationService.Internal_UseHandler(null);
            var container = new GameObject(nameof(AddLocalizer_BeforeHandlerReady_PendsAndReplaysOnce));
            var localizer = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                LocalizationService.AddLocalizer(localizer);
                LocalizationService.AddLocalizer(localizer); // 重复注册不得重复入队

                LocalizationService.Internal_UseHandler(_handler);
                LocalizationService.ReplayPendingLocalizers();

                Assert.AreEqual(1, CountRegistrationOf(localizer), "挂起注册应回放一次且仅一次");
            }
            finally
            {
                LocalizationService.Internal_UseHandler(original);
                _handler.RemoveLocalizer(localizer);
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void RemoveLocalizer_BeforeReplay_CancelsPendingRegistration()
        {
            var original = LocalizationService.Internal_UseHandler(null);
            var container = new GameObject(nameof(RemoveLocalizer_BeforeReplay_CancelsPendingRegistration));
            var localizer = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                LocalizationService.AddLocalizer(localizer);
                LocalizationService.RemoveLocalizer(localizer); // 尚在挂起即取消

                LocalizationService.Internal_UseHandler(_handler);
                LocalizationService.ReplayPendingLocalizers();

                Assert.AreEqual(0, CountRegistrationOf(localizer), "已取消的挂起注册不得回放进处理器");
            }
            finally
            {
                LocalizationService.Internal_UseHandler(original);
                UnityEngine.Object.DestroyImmediate(container);
            }
        }

        [Test]
        public void AddLocalizer_SameInstanceTwice_RegistersOnce()
        {
            var container = new GameObject(nameof(AddLocalizer_SameInstanceTwice_RegistersOnce));
            var localizer = container.AddComponent<L10nProbeLocalizer>();

            try
            {
                _handler.AddLocalizer(localizer);
                _handler.AddLocalizer(localizer);
                _handler.AddLocalizer(null); // 空引用直接忽略

                Assert.AreEqual(1, CountRegistrationOf(localizer));
            }
            finally
            {
                _handler.RemoveLocalizer(localizer);
                UnityEngine.Object.DestroyImmediate(container);
            }
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

    /// <summary>桩本地化数据源——语言经返回元组随批自报（语言头与词条同源同序）。</summary>
    internal sealed class L10nProbeHandler : LocalizationServiceHandler
    {
        public List<Language> Languages = new List<Language>();
        public Dictionary<string, List<string>> Strings = new Dictionary<string, List<string>>();
        public int LoadCallCount;
        /// <summary>非空即在本次取数时抛出，用于走数据源失败的回路。</summary>
        public Exception ThrowOnLoad;

        protected override (List<Language> languages, Dictionary<string, List<string>> strings) LoadLocalizedData()
        {
            LoadCallCount++;
            if (ThrowOnLoad != null) throw ThrowOnLoad;

            return (Languages, Strings);
        }
    }
}
