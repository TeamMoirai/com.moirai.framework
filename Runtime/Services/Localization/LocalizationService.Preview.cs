#if UNITY_EDITOR
using System;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    partial class LocalizationService
    {
        #region 编辑器预览 [EDITOR PREVIEW]

        private static LocalizationStore s_PreviewStore;
        // 缓存键 = 当时的编辑器语言；语言变了就重取
        private static string s_PreviewLanguageSetting;
        // 预览数据取不到时只认一次，Inspector 每帧重绘不能每帧刷一条 Error
        private static bool s_PreviewFailed;
        // 本方法挂在 projectChanged 上，而它读设置（Instance → 缺资产时创建 → 再派发 projectChanged）。
        // 重入时只丢本地缓存、不再回头读写设置，切断这条同步自递归。
        private static bool s_IsInvalidating;

        /// <summary>
        /// 丢弃编辑器预览缓存。改了编辑器语言、或重新转表之后调用。
        /// </summary>
        public static void InvalidateEditorPreview()
        {
            s_PreviewStore = null;
            s_PreviewLanguageSetting = null;
            s_PreviewFailed = false;

            if (s_IsInvalidating) return;

            s_IsInvalidating = true;
            try
            {
                // 转表回写之后数据源的语言集合可能已变：候选缓存跟着作废（换批那侧由基类在装载成功时自动作废）
                ExportedLanguageSource?.InvalidateExportedLanguages();
                // 编辑器语言是检测链的一环：改它就等于改了处理器该用的偏好，趁这一次作废一起交接过去
                ApplyEditorLanguagePreference();
            }
            finally
            {
                s_IsInvalidating = false;
            }
        }

        /// <summary>编辑器预览是否已就绪（非播放态、且表数据取到了）。</summary>
        internal static bool IsEditorPreviewAvailable => GetEditorPreviewStore() != null;

        /// <summary>编辑器预览用的语言：Inspector 里设的编辑器语言优先，未设或该语言不在表内时取表内的英语列，再退到首列。</summary>
        internal static Language EditorPreviewLanguage
        {
            get
            {
                var store = GetEditorPreviewStore();
                var index = GetPreviewLanguageIndex(store);
                return index < 0 ? DefaultLanguage : store.Batch.Languages[index];
            }
        }

        /// <summary>
        /// 解析预览译文：播放态走服务，非播放态走配置表的编辑器直读。
        /// </summary>
        /// <remarks>
        /// 唯一的预览解析入口，按 <see cref="EPreviewResolveStatus"/> 分得开「表内无此 ID」与「该语言留空」——
        /// 组件预览要说得出「这一格没翻」，才谈得上拿译文当地址去查资产。本方法<strong>不</strong>把缺译伪装成 ID； <br />
        /// 要露 ID 的调用方（<see cref="Localize"/> 标记）按状态自己决定。
        /// </remarks>
        /// <param name="id">词条 ID。</param>
        /// <param name="text">解析出的译文；仅 <see cref="EPreviewResolveStatus.Resolved"/> 时非 <c>null</c>。</param>
        /// <param name="language">本次解析所用的语言（未就绪时为 <c>null</c>），供预览点名是哪一门。</param>
        /// <returns>解析档位。</returns>
        public static EPreviewResolveStatus ResolvePreviewText(string id, out string text, out Language language)
        {
            text = null;
            language = null;
            if (string.IsNullOrEmpty(id)) return EPreviewResolveStatus.MissingId;

            if (Application.isPlaying)
            {
                if (!IsValid) return EPreviewResolveStatus.Unavailable;

                language = CurrentLanguage;
                text = s_Handler.PeekText(id, language);
                if (text != null) return EPreviewResolveStatus.Resolved;

                // PeekText 不计缺译；Has 只断言词条存在，用来拆开「没这条」与「这格没翻」
                return s_Handler.Has(id) ? EPreviewResolveStatus.BlankCell : EPreviewResolveStatus.MissingId;
            }

            var store = GetEditorPreviewStore();
            if (store == null) return EPreviewResolveStatus.Unavailable;

            var index = GetPreviewLanguageIndex(store);
            if (index < 0) return EPreviewResolveStatus.Unavailable;

            language = store.Batch.Languages[index];
            text = store.Resolve(id, language, index);
            if (text != null) return EPreviewResolveStatus.Resolved;

            return store.HasKey(id) ? EPreviewResolveStatus.BlankCell : EPreviewResolveStatus.MissingId;
        }

        /// <summary>编辑器预览用的语言列下标（预览不可用时为 -1）。</summary>
        internal static int EditorPreviewLanguageIndex => GetPreviewLanguageIndex(GetEditorPreviewStore());

        private static int GetPreviewLanguageIndex(LocalizationStore store)
        {
            if (store == null) return -1;

            var languages = store.Batch.Languages;

            if (TryGetBuiltInLanguage(LocalizationServiceSettings.EditorLanguage, out var preferred))
            {
                var preferredIndex = store.IndexOf(preferred);
                if (preferredIndex >= 0) return preferredIndex;
            }

            var fallbackIndex = store.IndexOf(DefaultLanguage);
            return fallbackIndex >= 0 ? fallbackIndex : (languages.Length > 0 ? 0 : -1);
        }

        /// <summary>
        /// 取（并按需重建）编辑器预览用的存储。
        /// </summary>
        /// <remarks>播放态恒返回 <c>null</c>：预览只服务非播放态的 Inspector/Scene，运行期只允许一条数据路径。</remarks>
        private static LocalizationStore GetEditorPreviewStore()
        {
            if (Application.isPlaying) return null;

            var languageSetting = LocalizationServiceSettings.EditorLanguage;
            if (s_PreviewLanguageSetting != languageSetting)
            {
                s_PreviewStore = null;
                s_PreviewFailed = false;
                s_PreviewLanguageSetting = languageSetting;
            }

            if (s_PreviewStore != null || s_PreviewFailed) return s_PreviewStore;

            try
            {
                // 取数向数据源要：预览用哪份处理器与导出语言同源（已注册的那份优先，非播放态退到 Settings 里配置的那份）
                var source = ExportedLanguageSource;
                var batch = source != null ? source.LoadEditorPreviewBatch() : null;
                if (batch == null || batch.Languages.Length == 0)
                {
                    s_PreviewFailed = true;
                    var reported = source != null ? source.ExportedLanguageCodes : Array.Empty<string>();
                    // 分得开「没转表」与「转了表却给不出预览批」：后者念"先生成配置"会把人引去重转一遍，而重转不修这个问题
                    if (reported.Count == 0)
                    {
                        LogUtility.Warning("Localization preview unavailable: generate config first!");
                    }
                    else
                    {
                        LogUtility.Error("Localization preview unavailable: {0} reports {1} language(s) but supplies no preview batch.",
                            source.GetType().Name, reported.Count);
                    }

                    return null;
                }

                var store = new LocalizationStore();
                if (!store.TryApply(batch, out var rejectedKey))
                {
                    s_PreviewFailed = true;
                    LogUtility.Error("Localization preview unavailable: entry '{0}' column count mismatches {1} languages.",
                        rejectedKey, batch.Languages.Length);
                    return null;
                }

                s_PreviewStore = store;
                return store;
            }
            catch (Exception ex)
            {
                // 预览面在 Inspector 的重绘路径上，任何一次抛出都会打断编辑器；失败即静默降级为显示 ID
                s_PreviewFailed = true;
                LogUtility.Error(ex);
                return null;
            }
        }

        #endregion
    }
}
#endif