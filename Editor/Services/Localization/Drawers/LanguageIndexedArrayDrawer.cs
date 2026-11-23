using Moirai.Atropos.Localization;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Localization
{
    /// <summary>
    /// <see cref="LanguageIndexedAttribute"/> 的元素行前缀：按语言列下标给数组元素出「<c>en</c>」语言码标签。
    /// </summary>
    /// <remarks>
    /// 语言清单走 <see cref="LocalizationService.ExportedLanguages"/>（编辑态直读 Settings 里的处理器实例，
    /// 与运行期当前语言下标同走 <see cref="LocalizationService"/> 的去重解析，下标与语言列互指）； <br />
    /// 该属性标在数组字段上、经 Odin 属性传播落到每个元素，本抽屉只改行前缀、不打断数组手柄与拖放； <br />
    /// 清单为空（无多语言/表未生成）整列原样绘制，清单比数组短时越界行不猜语言——错标比不标更害人。
    /// </remarks>
    public sealed class LanguageIndexedArrayDrawer : OdinAttributeDrawer<LanguageIndexedAttribute>
    {
        // 语言码列的固定宽度：够放 ISO 639 双字母码与地区后缀的常见形态，与控件列留白对齐
        private const float CODE_WIDTH = 52f;

        // 前缀样式：正文号 + 左对齐垂直居中（miniLabel 字号太小、与资产名读感不齐）。
        // EditorStyles.label 的默认对齐受皮肤影响，显式钉死 MiddleLeft；样式只建一次
        private static GUIStyle s_CodeStyle;

        private static GUIStyle CodeStyle => s_CodeStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
        };

        protected override void DrawPropertyLayout(GUIContent label)
        {
            var code = ResolveLanguageCode(Property.Index);
            if (code == null)
            {
                CallNextDrawer(label);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            var codeContent = new GUIContent(code, "语言列下标 " + Property.Index + "（数组下标即语言列）");
            GUILayout.Label(codeContent, CodeStyle, GUILayout.Width(CODE_WIDTH),
                GUILayout.ExpandWidth(false));

            // 元素本体仍走 Odin 链（label 置空由本行前缀代位）：对象字段、拖放与数组手柄全部保留
            CallNextDrawer(null);

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>取该元素行下标上的语言码；无清单或越界返回 <c>null</c>（保持无前缀）。</summary>
        private static string ResolveLanguageCode(int index)
        {
            if (index < 0) return null;

            var languages = LocalizationService.ExportedLanguages;
            if (languages == null || (uint)index >= (uint)languages.Count) return null;

            var language = languages[index];
            if (language == null || string.IsNullOrEmpty(language.Code)) return null;

            return language.Code;
        }
    }
}
