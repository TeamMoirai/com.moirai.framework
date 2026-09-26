using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Localization
{
    /// <summary>
    /// 本地化商业化加固测试：数据未就绪时的本地化器静默延迟、缺译追踪、格式化文化跟随游戏语言。
    /// <para>处理器级用例直接构造桩数据源（复用 <see cref="L10nProbeHandler"/>）；
    /// 外观级用例走生成的 <c>Internal_PeekHandler()</c> / <c>Internal_UseHandler(next)</c> 换入换出，
    /// 不反射私有字段，也不污染跨夹具的静态状态。</para>
    /// </summary>
    [TestFixture]
    public sealed class LocalizationServiceHardeningTests
    {
        private static readonly Language English = Language.English;
        private static readonly Language Chinese = Language.ChineseSimplified;

        private L10nProbeHandler _handler;
        private LocalizationServiceHandler _originalFacadeHandler;
        private GameObject _gameObject;

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
            if (_gameObject != null)
            {
                UObject.DestroyImmediate(_gameObject);
                _gameObject = null;
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

        /// <summary>把桩处理器装到外观静态位（本用例内外观调用都落到它）。</summary>
        private void InstallFacadeHandler()
        {
            LocalizationService.Internal_UseHandler(_handler);
        }

        #region 启动静默延迟 [STARTUP DEFERRAL]

        [Test]
        public void IsDataLoaded_ReflectsHandlerStateWithoutTriggeringLoad()
        {
            InstallFacadeHandler();

            Assert.IsFalse(LocalizationService.IsDataLoaded, "未加载时应为 false");
            Assert.AreEqual(0, _handler.LoadCallCount, "IsDataLoaded 不得触发懒加载");

            LoadStrings("ui.title", "Title", "标题");
            Assert.IsTrue(LocalizationService.Has("ui.title"), "触发加载的查询应命中");
            Assert.IsTrue(LocalizationService.IsDataLoaded, "加载成功后应为 true");
        }

        [Test]
        public void IsDataLoaded_NullHandler_ReturnsFalse()
        {
            LocalizationService.Internal_UseHandler(null);
            Assert.IsFalse(LocalizationService.IsDataLoaded);
        }

        [Test]
        public void ImageLocalizer_IndexMode_DefersWhileDataNotReady_ThenInjectsWhenReady()
        {
            InstallFacadeHandler();

            var texture = new Texture2D(2, 2);
            var spriteEn = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            var spriteZh = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f));
            try
            {
                _gameObject = new GameObject("L10nImageProbe");
                var renderer = _gameObject.AddComponent<SpriteRenderer>();
                var localizer = _gameObject.AddComponent<ImageLocalizer>();
                localizer.sprites = new[] { spriteEn, spriteZh };

                // EditMode 下非 ExecuteInEditMode 组件的 Awake 不执行，Prepare 需手动补齐
                localizer.Internal_Prepare();

                // 数据未就绪：静默推迟，不得注入也不得按缺译处理
                localizer.Localize();
                Assert.IsNull(renderer.sprite, "数据未就绪时不应注入任何内容");

                // 数据就绪：按当前语言索引正常注入
                LoadStrings("ui.title", "Title", "标题");
                var index = LocalizationService.CurrentLanguageIndex;
                Assert.GreaterOrEqual(index, 0, "首载后当前语言必须落在批内");
                Assert.AreEqual(index, LocalizationService.CurrentLanguageIndex);

                localizer.Localize();
                Assert.AreSame(localizer.sprites[index], renderer.sprite, "就绪后应按当前语言索引注入对应 Sprite");
            }
            finally
            {
                UObject.DestroyImmediate(spriteEn);
                UObject.DestroyImmediate(spriteZh);
                UObject.DestroyImmediate(texture);
            }
        }

        #endregion

        #region 缺译追踪 [MISSING TRACKING]

        [Test]
        public void MissingKey_TrackedOncePerKey_AndCountedPerEvent()
        {
            LoadStrings("ui.title", "Title", "标题");
            _ = _handler.EntryCount; // 完成加载

            UtfLogExpect.Warning();
            var first = _handler.GetTextFromId("ui.missing");
            var second = _handler.GetTextFromId("ui.missing");

            Assert.AreEqual("ui.missing", first);
            Assert.AreEqual("ui.missing", second);
            Assert.AreEqual(1, _handler.MissingKeyCount, "同一 key 只记录一次");
            Assert.AreEqual(2, _handler.MissingKeyEventCount, "重复命中仍计事件数");
            CollectionAssert.AreEqual(new[] { "ui.missing" }, _handler.GetMissingKeys());
        }

        [Test]
        public void MissingKey_BlankCurrentLanguage_IsTracked()
        {
            LoadStrings("ui.title", "Title", null);
            _handler.ChangeLanguage(Chinese);

            UtfLogExpect.Warning();
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"), "该格留空即缺译");
            Assert.AreEqual(1, _handler.MissingKeyCount, "缺译必须进追踪");
            Assert.AreEqual(1, _handler.MissingKeyEventCount);
        }

        [Test]
        public void MissingKey_NotTracked_WhenDataNotReady()
        {
            // 桩数据源全空 → 加载失败（报一次错），「查不到」不等于「缺译」
            UtfLogExpect.Error();
            Assert.AreEqual("ui.title", _handler.GetTextFromId("ui.title"));

            Assert.AreEqual(0, _handler.MissingKeyCount);
            Assert.AreEqual(0, _handler.MissingKeyEventCount);
        }

        [Test]
        public void MissingKeys_ClearAndShutdown_Reset()
        {
            LoadStrings("ui.title", "Title", "标题");
            _ = _handler.EntryCount;

            UtfLogExpect.Warning();
            _ = _handler.GetTextFromId("ui.missing");
            Assert.AreEqual(1, _handler.MissingKeyCount);

            _handler.ClearMissingKeys();
            Assert.AreEqual(0, _handler.MissingKeyCount);
            Assert.AreEqual(0, _handler.MissingKeyEventCount);

            UtfLogExpect.Warning();
            _ = _handler.GetTextFromId("ui.missing.again");
            Assert.AreEqual(1, _handler.MissingKeyCount);

            _handler.Internal_Shutdown();
            Assert.AreEqual(0, _handler.MissingKeyCount, "关服后缺译记录不跨会话存活");
            Assert.AreEqual(0, _handler.MissingKeyEventCount);
        }

        [Test]
        public void MissingKey_TrackingCap_SaturatesWithSingleNotice()
        {
            LoadStrings("ui.title", "Title", "标题");
            _ = _handler.EntryCount;

            const int CAP = 256; // 与处理器 MAX_TRACKED_MISSING_KEYS 对齐
            // 注意：本用例会向日志管道输出 256 条逐 key 告警 + 1 条饱和告警，这是被测特性本身的预期输出
            for (var i = 0; i < CAP + 4; i++)
            {
                _ = _handler.GetTextFromId("miss." + i);
            }

            Assert.AreEqual(CAP, _handler.MissingKeyCount, "超出容量的 key 不再逐个记录");
            Assert.AreEqual(CAP + 4, _handler.MissingKeyEventCount, "事件计数不受容量上限影响");
            Assert.AreEqual(CAP, _handler.GetMissingKeys().Length);
        }

        [Test]
        public void MissingKey_Facade_ExposesTracking()
        {
            InstallFacadeHandler();
            LoadStrings("ui.title", "Title", "标题");

            UtfLogExpect.Warning();
            Assert.AreEqual("ui.missing", LocalizationService.GetTextFromId("ui.missing"));

            Assert.AreEqual(1, LocalizationService.MissingKeyCount);
            CollectionAssert.AreEqual(new[] { "ui.missing" }, LocalizationService.GetMissingKeys());

            LocalizationService.ClearMissingKeys();
            Assert.AreEqual(0, LocalizationService.MissingKeyCount);
        }

        #endregion

        #region 格式化文化 [FORMAT CULTURE]

        [Test]
        public void Format_UsesGameLanguageCulture_NotDeviceCulture()
        {
            LoadStrings("ui.price", "Price: {0}", "价格：{0}");
            _ = _handler.EntryCount;
            _handler.ChangeLanguage(English);

            var previousCulture = Thread.CurrentThread.CurrentCulture;
            try
            {
                // 设备文化设为德语：不受控的 string.Format 会把 1.5 显示成 "1,5"
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                // 必须显式走 params 重载：单个 double 实参会优先绑定不装箱泛型重载（ZString 快路径本就不随文化），
                // 那样测不到文化受控的 string.Format 路径
                var text = _handler.GetTextFromId("ui.price", new object[] { 1.5 });
                Assert.AreEqual("Price: 1.5", text, "格式化文化必须跟随游戏语言，而非设备文化");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previousCulture;
            }
        }

        [Test]
        public void FormatCulture_FollowsCurrentLanguage()
        {
            LoadStrings("ui.title", "Title", "标题");
            _ = _handler.EntryCount;

            _handler.ChangeLanguage(Chinese);
            Assert.AreEqual("zh-Hans", _handler.FormatCulture.Name);

            _handler.ChangeLanguage(English);
            Assert.AreEqual("en", _handler.FormatCulture.Name);
        }

        [Test]
        public void FormatCulture_UnknownLanguageCode_FallsBackToInvariant()
        {
            var custom = new Language("NotARealCulture", "xx-notreal");
            _handler.Languages = new List<Language> { custom, English };
            _handler.Strings = new Dictionary<string, List<string>>
            {
                ["ui.title"] = new List<string> { "T", "Title" },
            };
            _ = _handler.EntryCount;

            _handler.ChangeLanguage(custom);

            Assert.AreEqual(CultureInfo.InvariantCulture, _handler.FormatCulture,
                "无法解析为 CultureInfo 的语言 Code 必须回落不变文化");
        }

        #endregion
    }
}
