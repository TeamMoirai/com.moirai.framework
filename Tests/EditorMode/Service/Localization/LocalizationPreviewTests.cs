using System.Collections.Generic;
using System.Linq;
using Moirai.Atropos.ConfigTable;
using Moirai.Atropos.Localization;
using NUnit.Framework;

namespace Service.Localization
{
    /// <summary>
    /// 编辑器预览解析的门禁：预览走的是与运行期同一套存储与解析，且「没这条」与「这格没翻」分得开。
    /// <para>预览刻意不把缺译伪装成 ID——组件预览要先能说出「这一格没翻」，才谈得上拿译文当地址去查资产；
    /// 要露 ID 的调用方（<c>Localize</c> 标记）按状态自己决定。</para>
    /// </summary>
    public sealed class LocalizationPreviewTests
    {
        [TearDown]
        public void TearDown()
        {
            // 预览存储是静态缓存：用完就丢，别把这一格读到的表带给下一条用例
            LocalizationService.InvalidateEditorPreview();
        }

        [Test]
        public void EmptyId_ResolvesAsMissingId_WithoutText()
        {
            var nullStatus = LocalizationService.ResolvePreviewText(null, out var nullText, out _);
            Assert.AreEqual(EPreviewResolveStatus.MissingId, nullStatus);
            Assert.IsNull(nullText);

            var emptyStatus = LocalizationService.ResolvePreviewText(string.Empty, out var emptyText, out _);
            Assert.AreEqual(EPreviewResolveStatus.MissingId, emptyStatus);
            Assert.IsNull(emptyText);
        }

        [Test]
        public void UnknownId_ResolvesAsMissingId_WithoutExposingTheKey()
        {
            // 未解析时必须给 null，而不是把 key 当译文回给调用方
            var status = LocalizationService.ResolvePreviewText("__no_such_localization_key__", out var text, out _);
            Assert.AreEqual(EPreviewResolveStatus.MissingId, status);
            Assert.IsNull(text);
        }

        /// <summary>
        /// 预览不要求服务世界：摘掉运行期注册的那份处理器，预览仍该从 Settings 里那份读到同一张表。
        /// <para>"不进 Play 也能预览"这条承诺的全部依赖就在这里——它借的是 settings 里那份实例，
        /// 而不是已注册、已初始化的运行期处理器。</para>
        /// </summary>
        [Test]
        public void PreviewWorksWhileTheServiceIsNotRegistered()
        {
            var original = ConfigTableService.Internal_PeekHandler();
            ConfigTableService.Internal_UseHandler(null);
            LocalizationService.InvalidateEditorPreview();

            try
            {
                // 前置只能问"工程里有没有可读的表"，不能问运行期入口——未注册时它恒为 null，
                // 拿它当门禁等于让本用例在自己要验的那个缺陷上永远 Ignore
                var strings = ConfigTableService.GetAllLocalizedStringsForEditor();
                if (strings == null || strings.Count == 0)
                {
                    Assert.Ignore("工程里没有可直读的多语言表（Settings 未配处理器或表未生成）。");
                    return;
                }

                Assert.IsNull(ConfigTableService.GetAllLocalizedStrings(),
                    "对照：运行期入口只认已注册的处理器，摘掉就取不到表");
                Assert.IsTrue(LocalizationService.IsEditorPreviewAvailable,
                    "摘掉运行期注册的处理器后预览也应可用：它读的是 Settings 里那份处理器");
            }
            finally
            {
                ConfigTableService.Internal_UseHandler(original);
                LocalizationService.InvalidateEditorPreview();
            }
        }

        /// <summary>
        /// 同源门禁：预览译文必须等于编辑器直读那张表里同一格，不能是另一条解析路径。
        /// </summary>
        [Test]
        public void PreviewResolvesFromTheSameTableTheEditorReadsDirectly()
        {
            var strings = ConfigTableService.GetAllLocalizedStringsForEditor();
            if (strings == null || strings.Count == 0)
            {
                Assert.Ignore("工程里没有可直读的多语言表，预览正向用例无从验证。");
                return;
            }

            LocalizationService.InvalidateEditorPreview();
            var status = LocalizationService.ResolvePreviewText(strings.Keys.First(), out var text, out var language);
            if (status == EPreviewResolveStatus.Unavailable)
            {
                Assert.Ignore("预览语言未自报或表未就绪，同源用例无从验证。");
                return;
            }

            // 任选一条能命中的 key，要求译文与直读表同一格逐字相等
            foreach (var pair in strings)
            {
                var resolveStatus = LocalizationService.ResolvePreviewText(pair.Key, out var resolved, out var usedLanguage);
                if (resolveStatus != EPreviewResolveStatus.Resolved) continue;

                var column = IndexOfLanguage(strings, pair.Key, usedLanguage);
                if (column < 0 || column >= pair.Value.Count) continue;

                Assert.AreEqual(pair.Value[column], resolved,
                    "预览译文与 ConfigTable 编辑器直读不是同一格：两条解析路径分叉了");
                Assert.IsNotNull(usedLanguage);
                return;
            }

            Assert.Ignore("这份表在预览语言下每一格都是空的，命中用例无从验证。");
        }

        /// <summary>
        /// 分档门禁：表内无此 ID 与该语言留空必须是两档，文案也不许再合并成一句。
        /// </summary>
        [Test]
        public void DescribeUnresolvedPreview_SplitsMissingIdFromBlankCell()
        {
            var missing = LocalizerBase.DescribeUnresolvedPreview("ui.ok", EPreviewResolveStatus.MissingId);
            var blank = LocalizerBase.DescribeUnresolvedPreview("ui.ok", EPreviewResolveStatus.BlankCell);
            var unavailable = LocalizerBase.DescribeUnresolvedPreview("ui.ok", EPreviewResolveStatus.Unavailable);

            StringAssert.Contains("表内无此 ID", missing);
            StringAssert.DoesNotContain("留空", missing);

            StringAssert.Contains("留空", blank);
            StringAssert.DoesNotContain("表内无此 ID", blank);

            StringAssert.Contains("未就绪", unavailable);
            Assert.AreNotEqual(missing, blank);
        }

        /// <summary>
        /// 预览只读：PeekText 不得把编辑器自身的重复查询算进 QA 的缺译计数与事件数。
        /// </summary>
        [Test]
        public void PeekText_Miss_DoesNotTrackMissingKey()
        {
            var handler = new L10nProbeHandler();
            handler.Internal_Init();
            try
            {
                handler.Languages = new List<Language> { Language.English, Language.ChineseSimplified };
                handler.Strings = new Dictionary<string, List<string>>
                {
                    ["preview.peek"] = new List<string> { null, "预览" },
                };
                _ = handler.EntryCount;

                Assert.IsNull(handler.PeekText("preview.peek", Language.English));
                Assert.AreEqual(0, handler.MissingKeyEventCount, "PeekText 不计缺译事件");
                Assert.AreEqual(0, handler.MissingKeyCount, "PeekText 不进缺译集合");

                // 对照：走正式取文路径才计缺译——证明上面的 0 不是夹具根本没计数能力
                handler.GetTextFromId("__peek_contrast_missing__");
                Assert.Greater(handler.MissingKeyEventCount, 0);
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        /// <summary>
        /// 预览解析分得开「没这条」与「这格没翻」：有 key、英文留空 → BlankCell；无 key → MissingId。
        /// </summary>
        [Test]
        public void PeekText_BlankCell_IsBlankNotMissing()
        {
            var handler = new L10nProbeHandler();
            handler.Internal_Init();
            try
            {
                handler.Languages = new List<Language> { Language.English, Language.ChineseSimplified };
                handler.Strings = new Dictionary<string, List<string>>
                {
                    ["preview.blank"] = new List<string> { "   ", "有译" },
                    ["preview.hit"] = new List<string> { "hit", "命中" },
                };
                _ = handler.EntryCount;

                Assert.IsNull(handler.PeekText("preview.blank", Language.English), "空白应视作没翻");
                Assert.IsTrue(handler.Has("preview.blank"), "有条目 ≠ 已翻");
                Assert.IsFalse(handler.Has("preview.__absent__"));

                Assert.AreEqual("hit", handler.PeekText("preview.hit", Language.English));
            }
            finally
            {
                handler.Internal_Shutdown();
            }
        }

        /// <summary>按解析出的语言取它在直读表里的列下标；对不上时返回 -1。</summary>
        private static int IndexOfLanguage(
            Dictionary<string, List<string>> strings, string key, Language language)
        {
            if (language == null || !strings.TryGetValue(key, out var row)) return -1;

            // 直读表的列序与预览存储同一批语言自报；用行宽与语言列表对齐（问预览入口，非播放态运行期入口恒空）
            var codes = ConfigTableService.GetLocalizationLanguageCodesForEditor();
            if (codes == null) return -1;

            for (var i = 0; i < codes.Count && i < row.Count; i++)
            {
                if (string.Equals(codes[i], language.Code, System.StringComparison.OrdinalIgnoreCase)) return i;
            }

            return -1;
        }
    }
}
