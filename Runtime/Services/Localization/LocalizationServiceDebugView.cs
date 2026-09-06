using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化服务调试视图（原生 UI Toolkit，经 <see cref="LocalizationService.OnInit"/> 注册进游戏内调试器 "Profiler/Localization"）。
    /// <para>展示当前语言并支持点击切换可用语言，按 1s 节流重建。</para>
    /// </summary>
    public sealed class LocalizationServiceDebugView : PollingDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private readonly List<Language> _languages = new List<Language>(8);

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化本地化调试视图的新实例。
        /// </summary>
        public LocalizationServiceDebugView() : base(1f)
        {
        }

        #endregion

        #region 构建窗口 [BUILD WINDOW]

        /// <inheritdoc />
        protected override void BuildWindow(VisualElement root)
        {
            if (!LocalizationService.IsValid)
            {
                root.Add(DebuggerUI.CreateSectionTitle("Localization Service"));
                root.Add(DebuggerUI.CreateHintLabel("本地化服务未就绪（需进入运行时并完成初始化）。"));
                return;
            }

            VisualElement card = AddSection(root, "当前语言 [CURRENT LANGUAGE]");
            Language current = LocalizationService.CurrentLanguage;
            AddRow(card, "语言 [Language]", current != null ? current.Name : "<未设置>");
            AddRow(card, "索引 [Index]", LocalizationService.CurrentLanguageIndex.ToString());

            VisualElement switchCard = AddSection(root, "切换语言 [SWITCH LANGUAGE]");
            _languages.Clear();
            _languages.AddRange(LocalizationService.GetAllAvailableLanguages());
            if (_languages.Count == 0)
            {
                switchCard.Add(DebuggerUI.CreateHintLabel("无可用语言（多语言数据尚未加载）。"));
                return;
            }

            VisualElement row = DebuggerUI.CreateToolbarRow();
            for (int i = 0; i < _languages.Count; i++)
            {
                Language language = _languages[i];
                bool isActive = current != null && current.Equals(language);
                row.Add(DebuggerUI.CreateActionButton(language.Name, () =>
                {
                    LocalizationService.ChangeLanguage(language);
                    Rebuild();
                }, isActive ? DebuggerUI.EButtonStyle.Active : DebuggerUI.EButtonStyle.Default));
            }

            switchCard.Add(row);
        }

        #endregion
    }
}
