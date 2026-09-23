using System.Collections.Generic;
using Moirai.Atropos.Debugger;
using UnityEngine.UIElements;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// 本地化服务调试视图（原生 UI Toolkit，经 <see cref="LocalizationService.OnInit"/> 注册进游戏内调试器 "Profiler/Localization"）。
    /// <para>展示当前语言、缺译回退链与常驻词条规模，支持点击切换可用语言，按 1s 节流重建。</para>
    /// <para>常驻规模一栏是"是否需要按语言拆包加载"的量化判据，不要凭感觉决定。</para>
    /// </summary>
    public sealed class LocalizationInformationWindow : PollingDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private readonly List<Language> _languages = new List<Language>(8);

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化本地化调试视图的新实例。
        /// </summary>
        public LocalizationInformationWindow() : base(1f)
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
                root.Add(DebuggerUI.CreateHintLabel("Localization service not ready (enter Play Mode and finish initialization)."));
                return;
            }

            VisualElement card = AddSection(root, "CURRENT LANGUAGE");
            Language current = LocalizationService.CurrentLanguage;
            AddRow(card, "Language", current != null ? current.Name : "<Not set>");
            AddRow(card, "Index", LocalizationService.CurrentLanguageIndex.ToString());
            AddRow(card, "Fallback", DescribeFallback());

            VisualElement dataCard = AddSection(root, "DATA FOOTPRINT");
            AddRow(dataCard, "Entries", LocalizationService.EntryCount.ToString());
            AddRow(dataCard, "Languages", LocalizationService.LoadedLanguageCount.ToString());
            AddRow(dataCard, "Overlay", LocalizationService.StringOverlayLayerCount.ToString());
            // 常驻下限：UTF-16 每字符 2 字节，未计字符串对象头与字典开销
            long characters = LocalizationService.ResidentChars;
            AddRow(dataCard, "Resident Chars", $"{characters:N0} (~{characters * 2 / 1024:N0} KB lower bound)");

            VisualElement switchCard = AddSection(root, "SWITCH LANGUAGE");
            _languages.Clear();
            _languages.AddRange(LocalizationService.LoadedLanguages);
            if (_languages.Count == 0)
            {
                switchCard.Add(DebuggerUI.CreateHintLabel("No available languages (localization data not loaded)."));
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

        private string DescribeFallback()
        {
            var chain = LocalizationService.FallbackChain;
            // Language.ToString() 即 Name
            return chain == null || chain.Count == 0 ? "<Disabled>" : string.Join(" → ", chain);
        }

        #endregion
    }
}
