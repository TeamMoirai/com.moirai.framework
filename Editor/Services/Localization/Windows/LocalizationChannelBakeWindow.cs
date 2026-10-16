using System;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 渠道默认语言烘焙窗口（菜单：Tools/Config/烘焙渠道默认语言），下拉选配置表导出的语言一键烘焙到 <c>Assets/Resources/LocalizationBuildConfig.asset</c>。
    /// </summary>
    /// <remarks>
    /// 候选随数据源自报（<see cref="LocalizationService.ExportedLanguages"/>）：只烘表里有的语言，烘不出手填的错值。 <br />
    /// 显示当前烘焙值并支持清除；CI 无人值守场景走 <c>localizationLanguage=xx</c> 参数（见 <see cref="LocalizationChannelBuildHook"/>）。
    /// </remarks>
    public sealed class LocalizationChannelBakeWindow : EditorWindow
    {
        // 候选在窗口启用时取：类型初始化那趟可能还没转表，冻成静态数组会把候选永久钉在空档
        private Language[] _languages = Array.Empty<Language>();
        private string[] _labels = Array.Empty<string>();

        private int _selectedIndex;

        /// <summary>
        /// 打开烘焙窗口。
        /// </summary>
        [MenuItem("Tools/Config/烘焙渠道默认语言", false, 27)]
        public static void Open()
        {
            var window = GetWindow<LocalizationChannelBakeWindow>(true, "烘焙渠道默认语言");
            window.minSize = new Vector2(320, 110);
            window.Show();
        }

        private void OnEnable()
        {
            RebuildChoices();
            var bakedIndex = IndexOfBaked(LocalizationBuildBaker.GetBakedLanguageCode());
            if (bakedIndex >= 0) _selectedIndex = bakedIndex;
        }

        private void RebuildChoices()
        {
            var exported = LocalizationService.ExportedLanguages;
            _languages = new Language[exported.Count];
            _labels = new string[exported.Count];
            for (var i = 0; i < exported.Count; i++)
            {
                _languages[i] = exported[i];
                _labels[i] = LocalizationServiceSettings.FormatLanguageOptionLabel(exported[i].Code);
            }

            if (_selectedIndex >= _languages.Length) _selectedIndex = 0;
        }

        /// <summary>已烘焙语言在候选里的下标；未烘焙、或烘的是表里已经没有的语言时为 -1。</summary>
        private int IndexOfBaked(string baked)
        {
            if (string.IsNullOrEmpty(baked)) return -1;

            for (var i = 0; i < _languages.Length; i++)
            {
                if (_languages[i].Code == baked) return i;
            }

            return -1;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("渠道默认语言（构建时写入包内，先于系统语言生效）", EditorStyles.boldLabel);

            if (_languages.Length == 0)
            {
                EditorGUILayout.HelpBox("数据源没有自报导出语言：先生成配置表再来烘焙。当前烘焙值仍可清除。", MessageType.Info);
            }
            else
            {
                _selectedIndex = EditorGUILayout.Popup("语言", _selectedIndex, _labels);
            }

            // 「没转表 + 资产里还留着上一次烘焙」正是最需要清除入口的一档，清除与失配告警不能被候选为空一起摘掉
            var baked = LocalizationBuildBaker.GetBakedLanguageCode();
            EditorGUILayout.LabelField("当前烘焙值", string.IsNullOrEmpty(baked) ? "<未烘焙>" : baked);
            if (!string.IsNullOrEmpty(baked) && IndexOfBaked(baked) < 0)
            {
                EditorGUILayout.HelpBox("已烘焙的语言不在导出语言内：重新烘焙一列，或清除烘焙回到系统语言检测。", MessageType.Warning);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_languages.Length == 0))
                {
                    if (GUILayout.Button("烘焙"))
                    {
                        LocalizationBuildBaker.BakeLanguage(_languages[_selectedIndex].Code);
                        GUIUtility.ExitGUI();
                    }
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(baked)))
                {
                    if (GUILayout.Button("清除烘焙"))
                    {
                        LocalizationBuildBaker.ClearBaked();
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }
    }
}
