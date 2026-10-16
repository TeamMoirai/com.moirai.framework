using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Localization;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Service.Localization
{
    /// <summary>
    /// 导出语言自报的门禁：语言选择面由数据源决定，编辑器候选与渠道烘焙候选同源。
    /// </summary>
    /// <remarks>
    /// 「未自报即只剩哨兵」那一格用包内兜底后端 <c>DefaultConfigTableHandler</c> 造：本装配不建
    /// <see cref="ConfigTableServiceHandler"/> 子类，那类经 [SerializeReference] 进生产资产的 Inspector 下拉。
    /// </remarks>
    [TestFixture]
    public sealed class LocalizationExportedLanguageTests
    {
        private ConfigTableServiceHandler _savedConfigTableHandler;
        private LocalizationServiceHandler _savedFacadeHandler;
        private string _savedEditorLanguage;

        [SetUp]
        public void SetUp()
        {
            _savedConfigTableHandler = ConfigTableService.Internal_PeekHandler();
            // 本组主语优先读外观上注册的那份处理器；别的夹具会往里塞探针，不快照就会看错对象
            _savedFacadeHandler = LocalizationService.Internal_PeekHandler();
            _savedEditorLanguage = LocalizationServiceSettings.EditorLanguage;
        }

        [TearDown]
        public void TearDown()
        {
            ConfigTableService.Internal_UseHandler(_savedConfigTableHandler);
            LocalizationService.Internal_UseHandler(_savedFacadeHandler);
            LocalizationServiceSettings.EditorLanguage = _savedEditorLanguage;
            LocalizationService.InvalidateEditorPreview();
        }

        [Test]
        public void ExportedLanguageCodes_FollowTheSelfReportedOrder()
        {
            var reported = ConfigTableService.GetLocalizationLanguageCodesForEditor();
            if (reported.Count == 0)
            {
                Assert.Ignore("宿主数据源没有自报语言（未转表），列序对齐无从验证。");
                return;
            }

            var exported = LocalizationService.ExportedLanguageCodes;
            Assert.AreEqual(reported.Count, exported.Count, "导出语言数等于数据源自报数");
            for (var i = 0; i < reported.Count; i++)
            {
                Assert.AreEqual(reported[i], exported[i], "列序即自报序，第 " + i + " 格不得错位");
            }
        }

        [Test]
        public void ExportedLanguages_PairWithTheirCodesAndSkipTheSentinel()
        {
            var languages = LocalizationService.ExportedLanguages;
            if (languages.Count == 0)
            {
                Assert.Ignore("宿主数据源没有自报语言（未转表），成对关系无从验证。");
                return;
            }

            var codes = LocalizationService.ExportedLanguageCodes;
            // 先钉长度再逐格比：自报里有空码或重码时 resolved 会比 raw 短，只比下标就永远指错格子
            Assert.AreEqual(codes.Count, languages.Count, "本宿主的自报没有空码与重码，两列应等长");
            for (var i = 0; i < languages.Count; i++)
            {
                Assert.AreEqual(codes[i], languages[i].Code, "本宿主自报的码本就是解析出的那门语言，逐格对齐");
                Assert.AreNotEqual(Language.Unspecified, languages[i], "Unspecified 是未选哨兵，不该由后端当语言报出来");
            }
        }

        [Test]
        public void CapturePreference_IgnoresAnEmptyEditorSettingWithoutShortCircuiting()
        {
            LocalizationServiceSettings.EditorLanguage = string.Empty;

            // 空值必须继续往下走存档/渠道烘焙/系统语言，而不是当"用户选了空串"提前返回
            Assert.AreNotEqual("EditorSetting", LocalizationService.CapturePreference().Source,
                "空字符串不是语言选择：它曾把整条检测链跳掉，最终静默落成默认语言");
        }

        [Test]
        public void HandlerWithoutSelfReport_YieldsNoLanguages()
        {
            // 契约的默认实现：后端不覆写 ExportedLanguageCodes 就没有语言可报，框架不替它编一份
            var handler = new L10nProbeHandler();
            Assert.AreEqual(0, handler.ExportedLanguageCodes.Count, "不自报的后端给出空序列");
            Assert.AreEqual(0, handler.ExportedLanguages.Count);
        }

        /// <summary>
        /// 自报面与装载面分档：后端未注册时语言头仍按「未就绪」给空，而自报面从 Settings 那份读得到。
        /// </summary>
        /// <remarks>把两档钉在同一格里，是因为把它们并成一条读取会让「未就绪保持重试」的语义静默失效。</remarks>
        [Test]
        public void SelfReportStaysAvailableWhileTheLoadPathStillSeesNoBackend()
        {
            var reported = ConfigTableService.GetLocalizationLanguageCodesForEditor();
            if (reported.Count == 0)
            {
                Assert.Ignore("宿主数据源没有自报语言（未转表），两档分界无从验证。");
                return;
            }

            var original = ConfigTableService.Internal_PeekHandler();
            ConfigTableService.Internal_UseHandler(null);
            try
            {
                var probe = new LocalizationWave2Tests.ConfigTableBridgeProbe();

                Assert.AreEqual(0, probe.PeekHeader().Count, "装载路径只认已注册的后端：未注册时语言头为空、按未就绪重试");
                Assert.AreEqual(reported.Count, probe.ExportedLanguageCodes.Count, "自报面在非播放态仍从 Settings 里那份取数");
            }
            finally
            {
                ConfigTableService.Internal_UseHandler(original);
            }
        }

        [Test]
        public void ExportedLanguages_ReuseOneCacheWhileTheSourceIsUnchanged()
        {
            var first = LocalizationService.ExportedLanguages;
            var second = LocalizationService.ExportedLanguages;
            Assert.AreSame(second, first, "自报序列没换就不重解析：候选每次重绘都读，语言与代码数组不该重复分配");

            foreach (var language in first)
            {
                if (!LocalizationService.TryGetBuiltInLanguage(language.Code, out var builtIn)) continue;
                Assert.AreSame(builtIn, language, "命中内置语言要复用全局语言表那一份，不新建对象");
            }
        }

        [Test]
        public void LanguageCandidateCodes_ListTheSentinelThenEveryExportedCode()
        {
            var candidates = LocalizationServiceSettings.GetLanguageCandidateCodes();
            var exported = LocalizationService.ExportedLanguageCodes;

            Assert.AreEqual(Language.Unspecified.Code, candidates[0], "首格恒为未选哨兵");
            Assert.AreEqual(exported.Count + 1, candidates.Count, "候选 = 哨兵 + 导出语言，不铺内置语言全集");
            for (var i = 0; i < exported.Count; i++)
            {
                Assert.AreEqual(exported[i], candidates[i + 1], "候选值即落盘值：语言 Code");
            }
        }

        [Test]
        public void LanguageCandidateCodes_KeepOnlyTheSentinelWhileNothingIsReported()
        {
            UtfLogExpect.Error();
            ConfigTableService.Internal_UseHandler(new DefaultConfigTableHandler());

            Assert.AreEqual(0, LocalizationService.ExportedLanguageCodes.Count, "兜底后端不自报语言");
            Assert.AreEqual(0, LocalizationService.ExportedLanguages.Count);

            var candidates = LocalizationServiceSettings.GetLanguageCandidateCodes();
            Assert.AreEqual(1, candidates.Count, "数据源没自报语言时只剩未选哨兵");
            Assert.AreEqual(Language.Unspecified.Code, candidates[0]);

            ConfigTableService.Internal_UseHandler(_savedConfigTableHandler);
            // 自报缓存只在解析那趟问后端：换回数据源后要显式作废，这也正是「转表回写」走的同一条口子
            LocalizationService.InvalidateEditorPreview();
            if (ConfigTableService.GetLocalizationLanguageCodesForEditor().Count == 0)
            {
                Assert.Ignore("宿主数据源没有自报语言（未转表），还原后的刷新无从验证。");
                return;
            }

            Assert.Greater(LocalizationServiceSettings.GetLanguageCandidateCodes().Count, 1,
                "还原处理器后候选要跟着回来：缓存按自报序列换旧，不能钉死在空自报上");
        }

        [Test]
        public void FormatLanguageOptionLabel_ShowsTheCodeThatGetsStored()
        {
            foreach (var code in LocalizationServiceSettings.GetLanguageCandidateCodes())
            {
                StringAssert.Contains(code, LocalizationServiceSettings.FormatLanguageOptionLabel(code),
                    "标签点明选中后落盘的那串 Code");
            }

            // 认不出的代码原样显示 Code，不伪装成某门内置语言
            Assert.AreEqual("xx-YY", LocalizationServiceSettings.FormatLanguageOptionLabel("xx-YY"));
        }

        [Test]
        public void IsEditorLanguageUnspecified_TreatsBothSentinelSpellingsAsUnchosen()
        {
            Assert.IsTrue(LocalizationService.IsEditorLanguageUnspecified(Language.Unspecified.Code));
            Assert.IsTrue(LocalizationService.IsEditorLanguageUnspecified(Language.Unspecified.Name));
            Assert.IsTrue(LocalizationService.IsEditorLanguageUnspecified("unspecified"), "哨兵判定不认大小写");

            Assert.IsFalse(LocalizationService.IsEditorLanguageUnspecified(Language.ChineseSimplified.Code));
            Assert.IsFalse(LocalizationService.IsEditorLanguageUnspecified(null), "空值交给检测链往下解析");
            Assert.IsFalse(LocalizationService.IsEditorLanguageUnspecified(string.Empty));
        }
    }
}
