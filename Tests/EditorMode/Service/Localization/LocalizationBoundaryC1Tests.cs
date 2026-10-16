using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moirai.Atropos.Localization;
using NUnit.Framework;

namespace Service.Localization
{
    /// <summary>
    /// 可用性判定的归一门禁：公共 ToLanguage(str, onlySupported: true) 与处理器自己的判定必须同源同结果。
    /// </summary>
    /// <remarks>
    /// 判定曾经有两份实现：外观那份问「已注册的处理器」，处理器那份按自己判。独立实例因此会被别人的批态改写答案。
    /// 本组钉的是「实现只剩一份」：外观在判定档委派处理器的 <c>ResolvePreference</c>。
    /// </remarks>
    [TestFixture]
    public sealed class LocalizationBoundaryC1Tests
    {
        private LocalizationServiceHandler _savedFacadeHandler;
        private string _savedLanguageCode;
        private bool _hadSavedLanguage;

        [SetUp]
        public void SetUp()
        {
            _savedFacadeHandler = LocalizationService.Internal_PeekHandler();
            // 语言键是跨会话的真实存档：用例自己把它清出来，收尾再原样还回去，别把答案漏给后面的夹具
            _hadSavedLanguage = LocalizationSettingsPolicy.TryLoad(out _savedLanguageCode);
            LocalizationSettingsPolicy.Remove();
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationService.Internal_UseHandler(_savedFacadeHandler);
            LocalizationSettingsPolicy.Remove();
            if (_hadSavedLanguage) LocalizationSettingsPolicy.Save(_savedLanguageCode);
        }

        private static L10nProbeHandler CreateTwoLanguageProbe()
        {
            var probe = new L10nProbeHandler { Languages = { Language.Japanese, Language.English } };
            probe.Strings["ui.title"] = new List<string> { "タイトル", "Title" };
            // 处理器不再自行走检测链：探针也得有人把偏好交给它，交给批内的英语，首启结果因此与机器无关
            probe.ApplyPreference(Language.English.Code, "EditorSetting");
            probe.Internal_Init();
            // 批得真的装上：处理器在批未就绪时把可用性判定退化成身份解析，那时这一组断言测不到判定档
            _ = probe.GetTextFromId("ui.title");
            return probe;
        }

        [Test]
        public void PublicToLanguage_DelegatesToTheRegisteredHandlerResolution()
        {
            var probe = CreateTwoLanguageProbe();
            try
            {
                LocalizationService.Internal_UseHandler(probe);

                // zh-Hans 是内置语言但不在本批 ⇒ 两扇门必须给同一个答案
                Assert.AreEqual(LocalizationService.DefaultLanguage, LocalizationService.ToLanguage("zh-Hans", true));
                Assert.AreEqual(LocalizationService.DefaultLanguage, probe.ResolvePreference("zh-Hans"));
                Assert.AreEqual(Language.Japanese, probe.ResolvePreference("ja"), "批内的语言按批内结果给");
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void ResolvedViewAndPublicEntry_AgreeOnEveryReportedCode()
        {
            var probe = CreateTwoLanguageProbe();
            try
            {
                LocalizationService.Internal_UseHandler(probe);
                var reported = probe.ExportedLanguageCodes;

                for (var i = 0; i < reported.Count; i++)
                {
                    Assert.AreEqual(probe.ResolvePreference(reported[i]), LocalizationService.ToLanguage(reported[i], true),
                        $"第 {i} 格两扇门必须同解：判定实现只剩一份");
                }
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void UnregisteredHandler_DegradesToIdentityResolution()
        {
            LocalizationService.Internal_UseHandler(null);
            Assert.AreEqual(Language.Japanese, LocalizationService.ToLanguage("ja", true),
                "未注册时无可判对象，退化为身份解析（公共语义保持不变）");
        }

        [Test]
        public void ExplicitSwitch_ThroughService_PersistsTheLanguage()
        {
            var probe = CreateTwoLanguageProbe();
            try
            {
                LocalizationService.Internal_UseHandler(probe);

                LocalizationService.ChangeLanguage(Language.Japanese);

                Assert.AreEqual(Language.Japanese, probe.CurrentLanguage, "先确认切换真的发生了");
                Assert.IsTrue(LocalizationSettingsPolicy.TryLoad(out var code), "显式切换要落存档");
                Assert.AreEqual("ja", code, "落盘的是语言 Code");
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void ExplicitSwitch_ThroughHandlerDirectly_DoesNotPersist()
        {
            var probe = CreateTwoLanguageProbe();
            try
            {
                LocalizationService.Internal_UseHandler(probe);

                // 越过服务外观直接调处理器：切换照常发生，但持久化是服务的政策，后端自己不再写
                probe.ChangeLanguage(Language.English);

                Assert.AreEqual(Language.English, probe.CurrentLanguage, "先确认切换真的发生了，否则这条判据恒真");
                Assert.IsFalse(LocalizationSettingsPolicy.TryLoad(out _), "写入时机已上移服务，后端不得再落存档");
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void FirstLaunchResolution_DoesNotPersist()
        {
            var probe = CreateTwoLanguageProbe();
            try
            {
                LocalizationService.Internal_UseHandler(probe);

                // 装载期自己解析出来的语言（检测链给的，不是用户点的）不得固化进存档
                Assert.AreNotEqual(Language.Unspecified, probe.CurrentLanguage, "先确认首启解析真的给了门语言，否则这条判据恒真");
                Assert.IsFalse(LocalizationSettingsPolicy.TryLoad(out _), "首启自动解析不得写存档");
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void ExplicitSwitch_BeforeBatchLoaded_PersistsTheRecordedIntent()
        {
            // 空批要念两条 Error：装载失败一次（闸门复位前不重复）＋ 无语言可用一次
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new Regex("generate config first"));
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new Regex("No language available"));

            var probe = new L10nProbeHandler();
            probe.Internal_Init();
            try
            {
                LocalizationService.Internal_UseHandler(probe);

                LocalizationService.ChangeLanguage(Language.Japanese);

                // 数据未就绪时切换记成意图：意图就是用户的显式选择，落盘不能等它真的装上（旧口径在装载那一刻才写）
                Assert.IsTrue(LocalizationSettingsPolicy.TryLoad(out var code), "未就绪时的显式切换要按意图落存档");
                Assert.AreEqual("ja", code);
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }
    }
}
