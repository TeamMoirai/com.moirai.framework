using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization.Editor
{
    /// <summary>
    /// 渠道默认语言烘焙窗口（Tools/Config/烘焙渠道默认语言）。
    /// <para>下拉选内置语言一键烘焙到 <c>Assets/Resources/LocalizationBuildConfig.asset</c>；
    /// 显示当前烘焙值并支持清除。CI 无人值守场景走 <c>localizationLanguage=xx</c> 参数（详见 <c>LocalizationChannelBuildHook</c>）。</para>
    /// </summary>
    public sealed class LocalizationChannelBakeWindow : EditorWindow
    {
        private static readonly string[] s_LanguageNames =
            LocalizationService.ResolveLanguages(Language.BuiltinLanguages.Select(lang => lang.Code).ToArray())
                .Select(lang => lang.Name).ToArray();

        private int _selectedIndex;

        /// <summary>打开烘焙窗口。</summary>
        [MenuItem("Tools/Config/烘焙渠道默认语言", false, 27)]
        public static void Open()
        {
            var window = GetWindow<LocalizationChannelBakeWindow>(true, "烘焙渠道默认语言");
            window.minSize = new Vector2(320, 110);
            window.Show();
        }

        private void OnEnable()
        {
            var baked = LocalizationBuildBaker.GetBakedLanguageCode();
            if (string.IsNullOrEmpty(baked)) return;

            for (var i = 0; i < s_LanguageNames.Length; i++)
            {
                if (!LocalizationService.TryGetBuiltInLanguage(s_LanguageNames[i], out var language)) continue;
                if (language.Code != baked) continue;

                _selectedIndex = i;
                return;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("渠道默认语言（构建时写入包内，先于系统语言生效）", EditorStyles.boldLabel);
            _selectedIndex = EditorGUILayout.Popup("语言", _selectedIndex, s_LanguageNames);

            var baked = LocalizationBuildBaker.GetBakedLanguageCode();
            EditorGUILayout.LabelField("当前烘焙值", string.IsNullOrEmpty(baked) ? "<未烘焙>" : baked);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("烘焙"))
                {
                    LocalizationBuildBaker.BakeLanguage(s_LanguageNames[_selectedIndex]);
                    GUIUtility.ExitGUI();
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
