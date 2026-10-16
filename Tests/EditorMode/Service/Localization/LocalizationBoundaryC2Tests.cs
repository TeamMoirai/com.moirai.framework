using System.Collections.Generic;
using Moirai.Atropos.Localization;
using NUnit.Framework;

namespace Service.Localization
{
    /// <summary>
    /// 偏好交接的门禁：处理器只吃装配侧交来的原值与来路，自己不再向外询问，也没有静态注册点。
    /// </summary>
    /// <remarks>
    /// 交接点漏一处不会编译报错，只表现为「首启语言落到语言表首项且没人报错」，所以本组每格都要能区分
    /// <strong>交接了</strong>与<strong>没交接</strong>：批的首列故意放一门与检测链结果不同的语言。
    /// 存档里的语言键与编辑器设置都是跨会话的真实状态，夹具自己清出来再还回去。
    /// </remarks>
    [TestFixture]
    public sealed class LocalizationBoundaryC2Tests
    {
        private string _savedEditorLanguage;
        private string _savedLanguageCode;
        private bool _hadSavedLanguage;
        private LocalizationServiceHandler _savedFacadeHandler;

        [SetUp]
        public void SetUp()
        {
            _savedEditorLanguage = LocalizationServiceSettings.EditorLanguage;
            _savedFacadeHandler = LocalizationService.Internal_PeekHandler();
            _hadSavedLanguage = LocalizationSettingsPolicy.TryLoad(out _savedLanguageCode);
            LocalizationSettingsPolicy.Remove();
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationServiceSettings.EditorLanguage = _savedEditorLanguage;
            LocalizationService.Internal_UseHandler(_savedFacadeHandler);
            LocalizationSettingsPolicy.Remove();
            if (_hadSavedLanguage) LocalizationSettingsPolicy.Save(_savedLanguageCode);
        }

        /// <summary>建一份「首列与检测链结果不同」的两语言批：日语在首列，英语在次列。</summary>
        private static L10nProbeHandler CreateJaEnProbe()
        {
            return new L10nProbeHandler
            {
                Languages = new List<Language> { Language.Japanese, Language.English },
                Strings = new Dictionary<string, List<string>>
                {
                    ["ui.title"] = new List<string> { "タイトル", "Title" }
                }
            };
        }

        [Test]
        public void HandlerWithoutPreference_YieldsUnspecified()
        {
            var handler = CreateJaEnProbe();
            handler.Internal_Init();
            try
            {
                // 没交接过就是未选：处理器不代替装配侧走检测链
                Assert.AreEqual(Language.Unspecified, handler.CurrentLanguage);
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void AppliedPreference_ResolvesAgainstTheHandlerOwnBatch()
        {
            var handler = CreateJaEnProbe();
            handler.ApplyPreference(Language.ChineseSimplified.Code, "EditorSetting");
            handler.Internal_Init();
            try
            {
                _ = handler.GetTextFromId("ui.title");   // 装载并解析首启语言

                Assert.AreEqual(2, handler.LanguageCount, "批已就绪：首列日语、次列英语");
                // 本批没有 zh-Hans：按默认语言英语兜底，而不是退到表首项日语
                Assert.AreEqual(Language.English, handler.CurrentLanguage,
                    "可用性按本实例的批判，批里没有时落默认语言");
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        [Test]
        public void ServiceEntryHandsThePreferenceBeforeTheHandlerUsesIt()
        {
            LocalizationServiceSettings.EditorLanguage = Language.English.Code;

            var probe = CreateJaEnProbe();
            probe.Internal_Init();
            LocalizationService.Internal_UseHandler(probe);
            try
            {
                LocalizationService.ReloadTexts();

                // 表首项是日语：没交接就解析会兜到日语，这一格因此分得出「交接了」与「没交接」
                Assert.AreEqual(Language.English, probe.CurrentLanguage,
                    "服务入口要在处理器用之前把检测链读数交给已注册的那一份");
            }
            finally
            {
                LocalizationService.Internal_UseHandler(_savedFacadeHandler);
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void CapturePreference_ReportsTheEditorSettingAndItsSource()
        {
            LocalizationServiceSettings.EditorLanguage = Language.Japanese.Code;

            var (raw, source) = LocalizationService.CapturePreference();

            Assert.AreEqual(Language.Japanese.Code, raw, "编辑器设置优先于存档与系统语言");
            Assert.AreEqual("EditorSetting", source);
            Assert.AreEqual("EditorSetting", LocalizationService.LastLanguageSource,
                "来源标签要在走检测链那一刻就记下，诊断不问谁在用");
        }

        [Test]
        public void CapturePreference_TreatsTheSentinelAsUnchosen()
        {
            LocalizationServiceSettings.EditorLanguage = Language.Unspecified.Code;

            // 只断「不是编辑器设置这一路」：往下走的是系统语言还是别的由机器决定，钉死就成了假红
            var (raw, source) = LocalizationService.CapturePreference();

            Assert.AreNotEqual("EditorSetting", source, "Unspecified 是未选哨兵，不能当作用户选了一门语言");
            Assert.AreEqual(source, LocalizationService.LastLanguageSource, "记下的来路必须就是这一次检测链的来路");
            Assert.IsFalse(string.IsNullOrEmpty(raw));
        }

        [Test]
        public void PublicTwinStillWorksAfterThePreferenceHandoff()
        {
            LocalizationServiceSettings.EditorLanguage = Language.Japanese.Code;

            string source = null;
            var language = LocalizationService.GetCurrentLanguage(false, ref source);

            Assert.AreEqual("EditorSetting", source, "公共孪生的读数与回传不因交接改造而变");
            Assert.AreEqual(Language.Japanese, language, "身份解析档不碰批，结果与批态无关");
        }

        [Test]
        public void BackendWithoutAPreviewHookSimplyOffersNoPreviewBatch()
        {
            Assert.IsNull(new L10nProbeHandler().LoadEditorPreviewBatch(),
                "基类不代取预览批次：后端没实现就是不给预览。");
        }

        [Test]
        public void SettingSourceLivesOnTheServiceNotTheHandler()
        {
            var handler = CreateJaEnProbe();
            LocalizationService.Internal_UseHandler(handler);
            try
            {
                LocalizationServiceSettings.EditorLanguage = Language.Japanese.Code;
                LocalizationService.ApplyEditorLanguagePreference();

                Assert.AreEqual("EditorSetting", LocalizationService.LastLanguageSource, "来源标签由装配侧持有");
                Assert.AreEqual("EditorSetting", handler.AppliedPreferenceSource,
                    "交接给处理器的是一份数据，不是一份查询能力");

                // 撤销交接：原值与来路一起清空，诊断串不得比语言读数更旧
                handler.ApplyPreference(null, null);
                Assert.IsNull(handler.AppliedPreferenceSource);
                Assert.AreEqual(Language.Unspecified, handler.CurrentLanguage);
            }
            finally
            {
                LocalizationService.Internal_UseHandler(_savedFacadeHandler);
            }
        }
    }
}
