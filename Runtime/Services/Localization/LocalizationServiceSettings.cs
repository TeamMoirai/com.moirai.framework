using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    [FrameworkSetting("[服务]本地化设置", "多语言数据源配置", -450)]
    public sealed class LocalizationServiceSettings : FrameworkSettings<LocalizationServiceSettings>
    {
#if UNITY_EDITOR
        [DisableInPlayMode, PropertyOrder(-999)]
        [ValueDropdown(nameof(GetLanguageOptions))]
        [InfoBox("候选只有 Unspecified 与配置表导出的语言：未转表时只剩 Unspecified 可选。", InfoMessageType.None)]
        [SerializeField] private string m_EditorLanguage = Language.Unspecified.Code;

        /// <summary>编辑器语言下拉候选：候选值取 <see cref="GetLanguageCandidateCodes"/>，标签点明所存的 Code。</summary>
        private static ValueDropdownList<string> GetLanguageOptions()
        {
            var options = new ValueDropdownList<string>();
            var candidates = GetLanguageCandidateCodes();
            for (var i = 0; i < candidates.Count; i++)
            {
                options.Add(FormatLanguageOptionLabel(candidates[i]), candidates[i]);
            }

            return options;
        }

        /// <summary>
        /// 编辑器语言候选值：Unspecified 的 Code 在前，其后按数据源自报序列出导出语言的 Code。
        /// </summary>
        /// <remarks>
        /// 取 <see cref="LocalizationService.ExportedLanguages"/>（已去空码与去重码）而不是原样自报序列：候选里出现空串会被检测链
        /// 当成「用户选了一门语言」，从而把存档、渠道烘焙、系统语言整条链跳掉。
        /// 数据源没有自报语言时只剩哨兵那一个候选：选择面由数据源决定，不回落内置语言全集。
        /// </remarks>
        internal static IReadOnlyList<string> GetLanguageCandidateCodes()
        {
            var exported = LocalizationService.ExportedLanguages;
            var candidates = new string[exported.Count + 1];
            candidates[0] = Language.Unspecified.Code;
            for (var i = 0; i < exported.Count; i++)
            {
                candidates[i + 1] = exported[i].Code;
            }

            return candidates;
        }

        /// <summary>候选标签：内置语言显示「显示名 (Code)」，认不出的代码原样显示那串 Code。</summary>
        internal static string FormatLanguageOptionLabel(string code) =>
            LocalizationService.TryGetBuiltInLanguage(code, out var language)
                ? $"{language.DisplayName} ({code})"
                : code;

        /// <summary>
        /// 获取或设置编辑器语言（仅编辑器内有效）。
        /// </summary>
        /// <remarks>值为语言 Code（<see cref="LocalizationService.ExportedLanguageCodes"/> 那一族），<see cref="Language.Unspecified"/> 的 Code 表示未选。</remarks>
        public static string EditorLanguage
        {
            // 资产导入完成前 Instance 取不到（见 FrameworkSettings.Loader）：按「未选」降级，检测链继续往下走
            get
            {
                var settings = Instance;
                return settings != null ? settings.m_EditorLanguage : Language.Unspecified.Code;
            }
            set
            {
                var settings = Instance;
                if (settings == null || settings.m_EditorLanguage == value) return;

                settings.m_EditorLanguage = value;
                // 预览缓存以编辑器语言为键，切换即作废
                LocalizationService.InvalidateEditorPreview();
                // 切回未选哨兵不等于「选了一门语言」：拿 und 走一遍切换只会白烧该语言的一次性告警闸门
                if (!LocalizationService.IsEditorLanguageUnspecified(value)) LocalizationService.ChangeLanguage(value);
            }
        }
#endif

        [InfoBox("默认使用配置表数据源。可替换为自定义数据源（如 JSON 文件、远程词库等）。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private LocalizationServiceHandler m_LocalizationServiceHandler = LocalizationService.CreateDefaultHandler();
        /// <summary>本地化处理器实例（由 Inspector 序列化配置，可替换数据源策略）。</summary>
        public static LocalizationServiceHandler LocalizationServiceHandler
        {
            // 资产导入完成前 Instance 取不到（见 FrameworkSettings.Loader）：返回 null 即「暂无数据源」
            get
            {
                var settings = Instance;
                return settings != null ? settings.m_LocalizationServiceHandler : null;
            }
        }
    }
}