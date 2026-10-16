using System.Collections.Generic;
using Core.SettingPersistence;
using Moirai.Atropos;
using Moirai.Atropos.Localization;
using NUnit.Framework;

namespace Service.Localization
{
    /// <summary>语言偏好的写入时机：显式切换只写键、一次都不 flush，首启自动解析与直接调处理器连键都不写。</summary>
    /// <remarks>
    /// 落盘归设置界面那一次确认：预览过又放弃的切换不该已经写进档，所以服务侧的显式入口只把 Code 记进配置项。
    /// 量具换成只记数的 <see cref="RecordingSettingHandler"/>，判据因此是计数而不是 <c>persistentDataPath</c> 的字节。 <br />
    /// 写入点只在服务的显式入口：直接调 <c>handler.ChangeLanguage</c> 换语言不写键，
    /// 首启检测链给出的语言也没有写入路径（两条反格钉的就是这两件事）。
    /// </remarks>
    [TestFixture]
    public sealed class LocalizationPersistenceTests
    {
        private string _savedEditorLanguage;
        private SettingHandler _savedSettingHandler;
        private LocalizationServiceHandler _savedL10nHandler;
        private RecordingSettingHandler _recording;

        [SetUp]
        public void SetUp()
        {
            _savedEditorLanguage = LocalizationServiceSettings.EditorLanguage;
            _savedSettingHandler = SettingUtility.Internal_PeekHandler();
            _savedL10nHandler = LocalizationService.Internal_PeekHandler();
            _recording = new RecordingSettingHandler();
            SettingUtility.Internal_UseHandler(_recording);
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationService.Internal_UseHandler(_savedL10nHandler);
            SettingUtility.Internal_UseHandler(_savedSettingHandler);
            LocalizationServiceSettings.EditorLanguage = _savedEditorLanguage;
        }

        /// <summary>两语言批：首列日语、次列英语；首启交接成英语，之后的「切到日语」才是一次真切换。</summary>
        private static L10nProbeHandler NewEnglishFirstProbe()
        {
            var probe = new L10nProbeHandler
            {
                Languages = new List<Language> { Language.Japanese, Language.English },
                Strings = new Dictionary<string, List<string>>
                {
                    ["ui.title"] = new List<string> { "タイトル", "Title" }
                }
            };
            probe.ApplyPreference(Language.English.Code, "EditorSetting");
            probe.Internal_Init();
            return probe;
        }

        [Test]
        public void ExplicitLanguageSwitchWritesCodeWithoutFlushing()
        {
            var probe = NewEnglishFirstProbe();
            LocalizationService.Internal_UseHandler(probe);
            try
            {
                LocalizationService.ChangeLanguage(Language.Japanese);

                Assert.AreEqual(Language.Japanese, probe.CurrentLanguage, "先确认切换真的发生了");
                Assert.AreEqual(Language.Japanese.Code, ReadStoredLanguage(_recording), "写进配置项的是当前语言的 Code");
                Assert.AreEqual(0, _recording.SaveCount, "显式切换一次都不落盘：落盘时机属于设置界面的确认动作");
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void DirectHandlerSwitchDoesNotFlush()
        {
            var probe = NewEnglishFirstProbe();
            LocalizationService.Internal_UseHandler(probe);
            try
            {
                // 越过服务直接调处理器：换语言照常发生，但持久化是服务的政策（本刀的边界）
                probe.ChangeLanguage(Language.Japanese);

                Assert.AreEqual(Language.Japanese, probe.CurrentLanguage, "切换真的发生了，否则这条判据恒真");
                Assert.AreEqual(0, _recording.SaveCount, "后端没有写入点，也就没有 flush");
                Assert.IsNull(ReadStoredLanguage(_recording));
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        [Test]
        public void FirstAutoResolutionDoesNotPersist()
        {
            var probe = NewEnglishFirstProbe();
            try
            {
                _ = probe.GetTextFromId("ui.title");   // 装载并落实首启语言

                Assert.AreEqual(Language.English, probe.CurrentLanguage, "首启解析按交接来的偏好落到了英语，装载确实发生过");
                Assert.AreEqual(0, _recording.SaveCount, "首启自动解析不落盘：不能把检测出来的语言固化进档");
                Assert.IsNull(ReadStoredLanguage(_recording));
            }
            finally
            {
                probe.Internal_Shutdown();
            }
        }

        private static string ReadStoredLanguage(RecordingSettingHandler recording)
        {
            string value;
            return recording.TryGetStored(GameConstant.Setting.LANGUAGE, out value) ? value : null;
        }
    }
}
