using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace Moirai.Atropos.Editor.ModulesUI
{
    public enum UIFieldCodeStyle
    {
        /// <summary>
        /// 字段名称以下划线开头 (e.g: _variable)
        /// </summary>
        [InspectorName("字段名称以下划线开头 (e.g: _variable)")]
        UnderscorePrefix,
        
        /// <summary>
        /// 字段名称以 m_ 前缀开头 (e.g: m_variable)
        /// </summary>
        [InspectorName("字段名称以 m_ 前缀开头 (e.g: m_variable)")]
        MPrefix,
    }
    
    [Serializable]
    public class ScriptGenerateRuler
    {
        [SerializeField] private string m_UIElementRegex;
        public string UIElementRegex => m_UIElementRegex;

        [SerializeField] private string m_ComponentName;
        public string ComponentName => m_ComponentName;

        [SerializeField] private bool m_IsUIWidget = false;
        public bool IsUIWidget => m_IsUIWidget;

        public ScriptGenerateRuler(string uiElementRegex, string componentName, bool isUIWidget = false)
        {
            m_UIElementRegex = uiElementRegex;
            m_ComponentName = componentName;
            m_IsUIWidget = isUIWidget;
        }
    }

    [Serializable]
    public class UIGenType
    {
        [SerializeField] private string m_UITypeName;
        public string UITypeName => m_UITypeName;

        [SerializeField] private bool m_IsGeneric;
        /// <summary>是否是泛型</summary>
        public bool IsGeneric => m_IsGeneric;

        public UIGenType(string uiTypeName, bool isGeneric)
        {
            m_UITypeName = uiTypeName;
            m_IsGeneric = isGeneric;
        }
    }

    [CustomPropertyDrawer(typeof(ScriptGenerateRuler))]
    public class ScriptGenerateRulerDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);
            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            var uiElementRegexRect = new Rect(position.x, position.y, 120, position.height);
            var componentNameRect = new Rect(position.x + 125, position.y, 175, position.height);
            var isUIWidgetRect = new Rect(position.x + 310, position.y, 150, position.height);
            EditorGUI.PropertyField(uiElementRegexRect, property.FindPropertyRelative("m_UIElementRegex"), GUIContent.none);
            EditorGUI.PropertyField(componentNameRect, property.FindPropertyRelative("m_ComponentName"), GUIContent.none);
            EditorGUI.PropertyField(isUIWidgetRect, property.FindPropertyRelative("m_IsUIWidget"), GUIContent.none);
            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
    
    public class ScriptGeneratorSettings : ScriptableObject
    {
        /// <!-- 组件绑定 -->
        private const string BIND_COMPONENT_GROUP = "组件绑定";

        [BoxGroup(BIND_COMPONENT_GROUP)]
        [Tooltip("通过自动生成组件绑定代码，提高组件查找性能。否则，通过Transform.Find查找组件")]
        [LabelText("启用组件绑定")]
        [SerializeField] private bool m_UseBindComponent;
        public static bool UseBindComponent => Instance.m_UseBindComponent;

        [BoxGroup(BIND_COMPONENT_GROUP)]
        [ShowIf(nameof(m_UseBindComponent))]
        [FolderPath]
        [LabelText("组件绑定代码保存路径")]
        [SerializeField] private string m_GenCodePath = "Assets/Scripts/Runtime/UI/Gen";
        public static string GenCodePath => Instance.m_GenCodePath;
        [BoxGroup(BIND_COMPONENT_GROUP)]
        [ShowIf(nameof(m_UseBindComponent))]
        [FolderPath]
        [LabelText("功能实现代码保存路径")]
        [SerializeField] private string m_ImpCodePath = "Assets/Scripts/Runtime/UI";
        public static string ImpCodePath => Instance.m_ImpCodePath;

        /// <!-- 通用设置 -->

        [LabelText("绑定代码命名空间")]
        [SerializeField] private string m_Namespace = "Moirai.GameLogic";
        public static string Namespace => Instance.m_Namespace;

        [Tooltip("视为小组件整体，不会往下继续遍历")]
        [LabelText("Widget 名称前缀")]
        [SerializeField] private string m_WidgetName = "m_item";
        public static string WidgetName => Instance.m_WidgetName;
        
        [LabelText("代码风格")]
        [SerializeField] private UIFieldCodeStyle m_CodeStyle = UIFieldCodeStyle.UnderscorePrefix;
        public static UIFieldCodeStyle CodeStyle => Instance.m_CodeStyle;

        [InfoBox("非泛型: public class TestWindow : UIWindow\n" +
                 "泛型: public class TestEventItem : UIEventItem<TestEventItem>")]
        [SerializeField] private List<UIGenType> m_UIGenTypes = new List<UIGenType>()
        {
            new UIGenType("UIWindow", false),
            new UIGenType("UIWidget", false),
        };
        public static List<UIGenType> UIGenTypes => Instance.m_UIGenTypes;

        [LabelText("支持Nullable风格")]
        [SerializeField] private bool m_NullableEnable;
        public static bool NullableEnable => Instance.m_NullableEnable;

        [InfoBox(
            "规则匹配优先级: 从上到下依次匹配\n" +
            "• 命名前缀: UI元素名称前缀\n" +
            "• 组件类型: 生成的组件类型\n" +
            "• 是否Widget: 标记是否为独立Widget组件")]
        [ListDrawerSettings(NumberOfItemsPerPage = 30)]
        [SerializeField] private List<ScriptGenerateRuler> m_ScriptGenerateRules = new List<ScriptGenerateRuler>()
        {
            // 系统组件
            new ScriptGenerateRuler("m_go", "GameObject"),
            new ScriptGenerateRuler("m_item", "GameObject", true),
            new ScriptGenerateRuler("m_tf", "Transform"),
            new ScriptGenerateRuler("m_rect", "RectTransform"),
            new ScriptGenerateRuler("m_text", "Text"),
            new ScriptGenerateRuler("m_btn", "Button"),
            new ScriptGenerateRuler("m_img", "Image"),
            new ScriptGenerateRuler("m_rImg", "RawImage"),
            new ScriptGenerateRuler("m_scrollBar", "Scrollbar"),
            new ScriptGenerateRuler("m_scroll", "ScrollRect"),
            new ScriptGenerateRuler("m_input", "InputField"),
            new ScriptGenerateRuler("m_glg", "GridLayoutGroup"),
            new ScriptGenerateRuler("m_hlg", "HorizontalLayoutGroup"),
            new ScriptGenerateRuler("m_vlg", "VerticalLayoutGroup"),
            new ScriptGenerateRuler("m_csf", "ContentSizeFitter"),
            new ScriptGenerateRuler("m_slider", "Slider"),
            new ScriptGenerateRuler("m_group", "ToggleGroup"),
            new ScriptGenerateRuler("m_drop", "Dropdown"),
            new ScriptGenerateRuler("m_curve", "AnimationCurve"),
            new ScriptGenerateRuler("m_canvasGroup", "CanvasGroup"),
            new ScriptGenerateRuler("m_toggle", "Toggle"),
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
            new ScriptGenerateRuler("m_tmpInput","TMP_InputField"),
            new ScriptGenerateRuler("m_tmpDropdown","TMP_Dropdown"),
            new ScriptGenerateRuler("m_tmp","TextMeshProUGUI"),
#endif

            // 框架组件
            new ScriptGenerateRuler("m_label","UILabel"),
            new ScriptGenerateRuler("m_sBtn","ButtonSuper"),
            new ScriptGenerateRuler("m_menu","UIMenu"),
            new ScriptGenerateRuler("m_menuItem","UIMenuItem"),
#if MOIRAI_CLOTHO_UIPRO
            new ScriptGenerateRuler("m_carousel","Carousel"),
            new ScriptGenerateRuler("m_listCarousel","ListCarousel"),
            new ScriptGenerateRuler("m_slideToggle","SlideToggle"),
#endif
        };

        public static List<ScriptGenerateRuler> ScriptGenerateRules => Instance.m_ScriptGenerateRules;

        public static string GetPrefixNameByCodeStyle(UIFieldCodeStyle style)
        {
            return style switch
            {
                UIFieldCodeStyle.UnderscorePrefix => "_",
                UIFieldCodeStyle.MPrefix => "m_",
                _ => "m_"
            };
        }

        public static string GetUIComponentWithoutPrefixName(string uiComponentName)
        {
            if (Instance.m_ScriptGenerateRules == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < Instance.m_ScriptGenerateRules.Count; i++)
            {
                var rule = Instance.m_ScriptGenerateRules[i];

                if (rule.ComponentName == uiComponentName)
                {
                    return rule.UIElementRegex.Substring(rule.UIElementRegex.IndexOf("_", StringComparison.Ordinal) + 1);
                }
            }
            return string.Empty;
        }

        public static UIGenType GetUIGenType(string uiGenTypeName)
        {
            if (string.IsNullOrEmpty(uiGenTypeName))
            {
                return null;
            }
            var tempList = Instance.m_UIGenTypes;
            for (int i = 0; i < tempList.Count; i++)
            {
                var uiGenType = tempList[i];

                if (string.Equals(uiGenTypeName, uiGenType.UITypeName, StringComparison.Ordinal))
                {
                    return uiGenType;
                }
            }
            return null;
        }

        #region 设置单例

        private const string SETTINGS_DATA_NAME = "ScriptGeneratorSettings";
        private const string SETTINGS_DATA_FILE = "Assets/Settings/Framework/Editor/" + SETTINGS_DATA_NAME + ".asset";
        private static ScriptGeneratorSettings s_Instance;
        public static ScriptGeneratorSettings Instance
        {
            get
            {
                if (s_Instance == null)
                {
                    s_Instance = SettingHelper.LoadSettingSO<ScriptGeneratorSettings>(SETTINGS_DATA_FILE);
                }
                return s_Instance;
            }
        }

        [MenuItem("Tools/Settings/" + SETTINGS_DATA_NAME, priority = -500)]
        private static void CreateAutoBindGlobalSetting()
        {
            Selection.activeObject = SettingHelper.LoadSettingSO<ScriptGeneratorSettings>(SETTINGS_DATA_FILE);
        }

        #endregion
    }
}