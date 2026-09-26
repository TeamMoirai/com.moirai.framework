using System;
using System.Collections.Generic;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Localization
{
    /// <summary>
    /// 热路径回归锁：注册去重集合与列表的并进退一致性、重注入/广播的池化快照遍历语义、单趟取值 <c>TryGetTextFromId</c>。
    /// <para>去重与异常隔离的行为面由 HandlerTests/HardeningTests 既有夹具锁定，这里补的是只有改坏内部容器
    /// 才会暴露的回归（快照遍历期间集合被改动、HashSet 与 List 失同步）与新增单趟查询的语义面；
    /// 0-GC 本身在编辑器 Mono 下不可计量（分配计数器恒 0），不在本夹具断言。</para>
    /// </summary>
    [TestFixture]
    public sealed class LocalizationHotPathTests
    {
        private static readonly Language English = Language.English;
        private static readonly Language Chinese = Language.ChineseSimplified;

        private L10nProbeHandler _handler;
        private LocalizationServiceHandler _originalFacadeHandler;
        private GameObject _container;

        [SetUp]
        public void SetUp()
        {
            _handler = new L10nProbeHandler();
            _handler.Internal_Init();
            _originalFacadeHandler = LocalizationService.Internal_PeekHandler();
        }

        [TearDown]
        public void TearDown()
        {
            if (_container != null)
            {
                UObject.DestroyImmediate(_container);
                _container = null;
            }

            LocalizationService.Internal_UseHandler(_originalFacadeHandler);
            _originalFacadeHandler = null;

            _handler?.Internal_Shutdown();
            _handler = null;
        }

        private void LoadStrings(string key, string english, string chinese)
        {
            _handler.Languages = new List<Language> { English, Chinese };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                [key] = new List<string> { english, chinese },
            };
        }

        /// <summary>装载批并完成首启语言解析，返回切换目标语言（与当前语言相异）。</summary>
        private Language LoadAndResolveTargetLanguage()
        {
            LoadStrings("ui.title", "Title", "标题");
            _ = _handler.EntryCount; // 触发懒加载 + 首启语言落位
            var current = _handler.CurrentLanguage;
            return current == English ? Chinese : English;
        }

        private L10nProbeLocalizer NewProbe(string name)
        {
            _container ??= new GameObject(nameof(LocalizationHotPathTests));
            return _container.AddComponent<L10nProbeLocalizer>();
        }

        #region 注册表并进退 [REGISTRY LOCKSTEP]

        [Test]
        public void RemoveThenAddSameInstance_RegistersAgainAndInjectsOnce()
        {
            var target = LoadAndResolveTargetLanguage();
            var localizer = NewProbe(nameof(RemoveThenAddSameInstance_RegistersAgainAndInjectsOnce));

            _handler.AddLocalizer(localizer);
            _handler.RemoveLocalizer(localizer);
            // 摘除后再注册：去重集合与列表必须同步放行（失同步会表现为「永不再注入」）
            _handler.AddLocalizer(localizer);

            _handler.ChangeLanguage(target);

            Assert.AreEqual(1, localizer.LocalizeCount, "摘除后重注册的实例必须重新参与注入");
        }

        [Test]
        public void Reinject_RemoveDuringIteration_SnapshotStillInjectsLaterLocalizers()
        {
            var target = LoadAndResolveTargetLanguage();
            var remover = NewProbe("remover");
            var victim = NewProbe("victim");
            var tail = NewProbe("tail");

            // remover 在本轮注入回调里摘除 victim：快照遍历下 victim 与 tail 本轮都必须被注入
            remover.OnLocalized = () => _handler.RemoveLocalizer(victim);
            _handler.AddLocalizer(remover);
            _handler.AddLocalizer(victim);
            _handler.AddLocalizer(tail);

            _handler.ChangeLanguage(target);

            Assert.AreEqual(1, victim.LocalizeCount, "遍历中途被摘除的实例本轮仍按快照注入（与 ToArray 语义一致）");
            Assert.AreEqual(1, tail.LocalizeCount, "中途摘除不得让后续本地化器被跳过");

            // 第二轮：victim 已不在注册表，remover 与 tail 照常
            var next = target == English ? Chinese : English;
            _handler.ChangeLanguage(next);

            Assert.AreEqual(1, victim.LocalizeCount, "摘除后不再注入");
            Assert.AreEqual(2, remover.LocalizeCount);
            Assert.AreEqual(2, tail.LocalizeCount);
        }

        [Test]
        public void Reinject_AddDuringIteration_NewLocalizerWaitsForNextRound()
        {
            var target = LoadAndResolveTargetLanguage();
            var first = NewProbe("first");
            var late = NewProbe("late");

            first.OnLocalized = () => _handler.AddLocalizer(late);
            _handler.AddLocalizer(first);

            _handler.ChangeLanguage(target);

            Assert.AreEqual(1, first.LocalizeCount);
            Assert.AreEqual(0, late.LocalizeCount, "遍历中途新注册的实例不得插进本轮快照（与 ToArray 语义一致）");

            var next = target == English ? Chinese : English;
            _handler.ChangeLanguage(next);

            Assert.AreEqual(1, late.LocalizeCount, "下一轮正常注入");
        }

        [Test]
        public void LanguageChanged_SelfDisposeDuringCallback_OthersStillNotified()
        {
            var target = LoadAndResolveTargetLanguage();

            var selfDisposingCalls = 0;
            var otherCalls = 0;

            IDisposable selfDisposing = null;
            selfDisposing = _handler.SubscribeLanguageChanged(_ =>
            {
                selfDisposingCalls++;
                selfDisposing?.Dispose();
            });
            _handler.SubscribeLanguageChanged(_ => otherCalls++);

            _handler.ChangeLanguage(target);

            Assert.AreEqual(1, selfDisposingCalls);
            Assert.AreEqual(1, otherCalls, "订阅者回调内自 Dispose 不得截断同一次派发的其余订阅者");

            var next = target == English ? Chinese : English;
            _handler.ChangeLanguage(next);

            Assert.AreEqual(1, selfDisposingCalls, "Dispose 后不再收到派发");
            Assert.AreEqual(2, otherCalls);
        }

        #endregion

        #region 单趟取值 [TRY-GET QUERIES]

        [Test]
        public void TryGetTextFromId_Hit_ReturnsText()
        {
            LoadAndResolveTargetLanguage();

            Assert.IsTrue(_handler.TryGetTextFromId("ui.title", out var text));
            Assert.AreEqual(_handler.CurrentLanguage == English ? "Title" : "标题", text, "命中时给译文，缺译才轮到露 key");
        }

        [Test]
        public void TryGetTextFromId_Missing_ReturnsFalseWithoutIdEcho_AndTracksOnce()
        {
            LoadAndResolveTargetLanguage();

            UtfLogExpect.Warning();
            Assert.IsFalse(_handler.TryGetTextFromId("ui.missing", out var first));
            Assert.IsNull(first, "缺失时给 null，不得把 ID 伪装成译文");
            Assert.IsFalse(_handler.TryGetTextFromId("ui.missing", out var second));
            Assert.IsNull(second);

            Assert.AreEqual(1, _handler.MissingKeyCount, "同一 key 只追踪一次（与 GetTextFromId 同口径）");
            Assert.AreEqual(2, _handler.MissingKeyEventCount);
        }

        [Test]
        public void TryGetTextFromId_BlankCell_ReturnsFalse()
        {
            LoadStrings("ui.title", "Title", null);
            _ = _handler.EntryCount;
            _handler.ChangeLanguage(Chinese);

            UtfLogExpect.Warning();
            Assert.IsFalse(_handler.TryGetTextFromId("ui.title", out var text));
            Assert.IsNull(text, "当前语言留空即缺译，命中判定必须为 false");
            Assert.AreEqual(1, _handler.MissingKeyCount);
        }

        [Test]
        public void TryGetTextFromId_OverlayWinsOverTableText()
        {
            LoadAndResolveTargetLanguage();
            var current = _handler.CurrentLanguage;

            _handler.SetStringOverlay("hot-ops", current, new Dictionary<string, string> { ["ui.title"] = "Hot Ops Text" });

            Assert.IsTrue(_handler.TryGetTextFromId("ui.title", out var text));
            Assert.AreEqual("Hot Ops Text", text, "覆盖层优先于批内译文");
        }

        [Test]
        public void TryGetTextFromId_FacadeDegradation_WhenHandlerMissing()
        {
            LocalizationService.Internal_UseHandler(null);

            Assert.IsFalse(LocalizationService.TryGetTextFromId("ui.title", out var text));
            Assert.IsNull(text, "外观未就绪时按降级契约返回 false + null，不抛异常");
        }

        [Test]
        public void TryGetTextFromId_Facade_ResolvesThroughInstalledHandler()
        {
            LocalizationService.Internal_UseHandler(_handler);
            LoadAndResolveTargetLanguage();

            Assert.IsTrue(LocalizationService.TryGetTextFromId("ui.title", out var text));
            Assert.IsFalse(LocalizationService.TryGetTextFromId("ui.absent", out _));
            Assert.AreEqual(1, LocalizationService.MissingKeyCount, "外观入口同样喂缺译追踪");
        }

        #endregion
    }
}
