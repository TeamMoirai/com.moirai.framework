using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI.Editor
{
    [FrameworkSetting("UI组件生成", "自动生成组件绑定代码设置", -500,
        "Assets/Settings/Framework/Editor/")]
    public class UIGeneratorSettings : FrameworkSettings<UIGeneratorSettings>
    {
        /// <!-- 通用设置 -->
        private const string GENERAL_GROUP = "General";

        [TabGroup(GENERAL_GROUP)]
        [LabelText("组件分隔符")]
        [Tooltip("组件检查分隔符，例如：Button#Close")]
        [SerializeField] private string m_ComCheckSplitName;
        public static string ComCheckSplitName => Instance.m_ComCheckSplitName;

        [TabGroup(GENERAL_GROUP)]
        [LabelText("组件结尾符")]
        [Tooltip("组件结尾分隔符，例如：@End")]
        [SerializeField] private string m_ComCheckEndName;
        public static string ComCheckEndName => Instance.m_ComCheckEndName;

        [TabGroup(GENERAL_GROUP)]
        [LabelText("数组分隔")]
        [Tooltip("数组组件检查分隔符，例如：*Item")]
        [SerializeField] private string m_ArrayComSplitName;
        public static string ArrayComSplitName => Instance.m_ArrayComSplitName;

        [TabGroup(GENERAL_GROUP)]
        [Tooltip("排除的关键字（匹配则不生成）")]
        [SerializeField] private string[] m_ExcludeKeywords;
        public static string[] ExcludeKeywords => Instance.m_ExcludeKeywords;

        [Header("UI脚本生成辅助类")]

        [TabGroup(GENERAL_GROUP)]
        [LabelText("Identifier Formatter")]
        [ValueDropdown(nameof(GetUIIdentifierFormatterTypes))]
        [SerializeField] private string m_UIIdentifierFormatterTypeName;
        public static string UIIdentifierFormatterTypeName => Instance.m_UIIdentifierFormatterTypeName;
        private static IEnumerable<string> GetUIIdentifierFormatterTypes() => GetTypeOptions(typeof(IUIIdentifierFormatter));

        [TabGroup(GENERAL_GROUP)]
        [LabelText("ResourcePath Resolver")]
        [ValueDropdown(nameof(GetUIResourcePathResolverTypes))]
        [SerializeField] private string m_UIResourcePathResolverTypeName;
        public static string UIResourcePathResolverTypeName => Instance.m_UIResourcePathResolverTypeName;
        private static IEnumerable<string> GetUIResourcePathResolverTypes() => GetTypeOptions(typeof(IUIResourcePathResolver));

        [TabGroup(GENERAL_GROUP)]
        [LabelText("ScriptCode Emitter")]
        [ValueDropdown(nameof(GetUIScriptCodeEmitterTypes))]
        [SerializeField] private string m_UIScriptCodeEmitterTypeName;
        public static string UIScriptCodeEmitterTypeName => Instance.m_UIScriptCodeEmitterTypeName;
        private static IEnumerable<string> GetUIScriptCodeEmitterTypes() => GetTypeOptions(typeof(IUIScriptCodeEmitter));

        [TabGroup(GENERAL_GROUP)]
        [LabelText("ScriptFile Writer")]
        [ValueDropdown(nameof(GetUIScriptFileWriterTypes))]
        [SerializeField] private string m_UIScriptFileWriterTypeName;
        public static string UIScriptFileWriterTypeName => Instance.m_UIScriptFileWriterTypeName;
        private static IEnumerable<string> GetUIScriptFileWriterTypes() => GetTypeOptions(typeof(IUIScriptFileWriter));

        /// <!-- 脚本生成 -->
        private const string SCRIPT_GENERATION_GROUP = "Script Generation";

        [TabGroup(SCRIPT_GENERATION_GROUP)]
        [ListDrawerSettings(ShowPaging = false)]
        [Tooltip("UI脚本生成配置（支持多个项目）")]
        [SerializeField] private List<UIScriptGenerateData> m_UIScriptGenerateConfigs;
        public static List<UIScriptGenerateData> UIScriptGenerateConfigs => Instance.m_UIScriptGenerateConfigs;

        /// <!-- 组件绑定 -->
        private const string ELEMENT_MAPPING_GROUP = "Element Mapping";

        [Header("UI生成规则（根据正则匹配）")]
        [TabGroup(ELEMENT_MAPPING_GROUP)]
        [TableList(AlwaysExpanded = true, ShowPaging = true)]
        [SerializeField] private List<UIElementRegexData> m_UIElementRegexConfigs;
        public static List<UIElementRegexData> UIElementRegexConfigs => Instance.m_UIElementRegexConfigs;

        [Header("事件绑定规则（根据组件类型匹配）")]
        [TabGroup(ELEMENT_MAPPING_GROUP)]
        [TableList(AlwaysExpanded = true, ShowPaging = true)]
        [SerializeField] private List<UIEventBindingConfig> m_UIEventBindingConfigs;
        public static List<UIEventBindingConfig> UIEventBindingConfigs => Instance.m_UIEventBindingConfigs;

        protected internal override void Reset()
        {
            m_ComCheckSplitName = "#";
            m_ComCheckEndName = "@";
            m_ArrayComSplitName = "*";
            m_ExcludeKeywords = new []{ "ViewHolder" };

            m_UIIdentifierFormatterTypeName = typeof(DefaultUIIdentifierFormatter).FullName;
            m_UIResourcePathResolverTypeName = typeof(DefaultUIResourcePathResolver).FullName;
            m_UIScriptCodeEmitterTypeName = typeof(DefaultUIScriptCodeEmitter).FullName;
            m_UIScriptFileWriterTypeName = typeof(DefaultUIScriptFileWriter).FullName;

            m_UIScriptGenerateConfigs = new List<UIScriptGenerateData>
            {
                new UIScriptGenerateData(
                    "MainProject",
                    "GameMain.UI",
                    "Assets/Scripts/GameBase/UI",
                    "Assets/Resources/UI/",
                    true
                    ),
                new UIScriptGenerateData(
                    "Hotfix",
                    "GameLogic.UI",
                    "Assets/Scripts/GameLogic/UI",
                    "Assets/AssetRaw/Default/UI/",
                    false
                    )
            };

            m_UIElementRegexConfigs = new List<UIElementRegexData>
            {
                // 系统组件
                new UIElementRegexData("Obj", "GameObject"),
                new UIElementRegexData("Tf", "Transform"),
                new UIElementRegexData("Rect", "RectTransform"),
                new UIElementRegexData("Text", "UnityEngine.UI.Text"),
                new UIElementRegexData("Btn", "UnityEngine.UI.Button"),
                new UIElementRegexData("Slider", "UnityEngine.UI.Slider"),
                new UIElementRegexData("Img", "UnityEngine.UI.Image"),
                new UIElementRegexData("RImg", "UnityEngine.UI.RawImage"),
                new UIElementRegexData("Scrollbar", "UnityEngine.UI.Scrollbar"),
                new UIElementRegexData("ScrollRect", "UnityEngine.UI.ScrollRect"),
                new UIElementRegexData("Input", "UnityEngine.UI.InputField"),
                new UIElementRegexData("GLayout", "UnityEngine.UI.GridLayoutGroup"),
                new UIElementRegexData("HLayout", "UnityEngine.UI.HorizontalLayoutGroup"),
                new UIElementRegexData("VLayout", "UnityEngine.UI.VerticalLayoutGroup"),
                new UIElementRegexData("SizeFitter", "UnityEngine.UI.ContentSizeFitter"),
                new UIElementRegexData("TogGroup", "UnityEngine.UI.ToggleGroup"),
                new UIElementRegexData("Tog", "UnityEngine.UI.Toggle"),
                new UIElementRegexData("Dropdown", "UnityEngine.UI.Dropdown"),
                new UIElementRegexData("Mask2D", "UnityEngine.UI.RectMask2D"),
                new UIElementRegexData("Video", "UnityEngine.Video.VideoPlayer"),
                new UIElementRegexData("CanvasGroup", "CanvasGroup"),
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
                new UIElementRegexData("TmpInput", "TMPro.TMP_InputField"),
                new UIElementRegexData("TmpDropdown", "TMPro.TMP_Dropdown"),
                new UIElementRegexData("Tmp", "TMPro.TextMeshProUGUI"),
#endif

                // 框架组件
                new UIElementRegexData("Label", "Moirai.Clotho.UI.UILabel"),
                new UIElementRegexData("SBtn", "Moirai.Clotho.UI.ButtonSuper"),
                new UIElementRegexData("MenuItem", "Moirai.Clotho.UI.UIMenuItem"),
                new UIElementRegexData("Menu", "Moirai.Clotho.UI.UIMenu"),

#if MOIRAI_CLOTHO_UIPRO
                new UIElementRegexData("Carousel","Moirai.Clotho.UIPro.Carousel"),
                new UIElementRegexData("ListCarousel","Moirai.Clotho.UIPro.ListCarousel"),
                new UIElementRegexData("SlideToggle","Moirai.Clotho.UIPro.SlideToggle"),
#endif
            };

            m_UIEventBindingConfigs = new List<UIEventBindingConfig>
            {
                // 系统组件
                new UIEventBindingConfig("UnityEngine.UI.Button", "onClick", "Click", ""),
                new UIEventBindingConfig("UnityEngine.UI.Toggle", "onValueChanged", "Change", "(bool isOn)"),
                new UIEventBindingConfig("UnityEngine.UI.Slider", "onValueChanged", "Change", "(float value)"),
                new UIEventBindingConfig("UnityEngine.UI.Dropdown", "onValueChanged", "Change", "(int selectedIndex)"),
#if (TEXT_MESH_PRO_INSTALLED || UNITY_UGUI2_INSTALLED)
                new UIEventBindingConfig("TMPro.TMP_Dropdown", "onValueChanged", "Change", "(int selectedIndex)"),
#endif

                // 框架组件
                new UIEventBindingConfig("Moirai.Clotho.UI.ButtonSuper", "onClick", "Click", ""),
                new UIEventBindingConfig("Moirai.Clotho.UI.UIMenuItem", "onSubmit", "Click", "(UIMenuItem item)"),
#if MOIRAI_CLOTHO_UIPRO
                new UIEventBindingConfig("Moirai.Clotho.UIPro.SlideToggle", "onValueChanged", "Change", "(bool isOn)"),
#endif
            };
        }
    }

    [Serializable]
    public class UIScriptGenerateData
    {
        [Header("项目识别信息")]
        [Tooltip("该UI工程的名称（例如：MainProject, HotFix, EditorUI）")]
        [SerializeField] private string m_ProjectName;
        public string ProjectName => m_ProjectName;

        [Tooltip("该UI工程所属命名空间")]
        [SerializeField] private string m_NameSpace;
        public string NameSpace => m_NameSpace;

        [Header("路径设置")]
        [Tooltip("生成的UI脚本路径（相对Assets）")]
        [FolderPath]
        [SerializeField] private string m_GenerateHolderCodePath;
        public string GenerateHolderCodePath => m_GenerateHolderCodePath;

        [Tooltip("UI Prefab根目录")]
        [FolderPath]
        [SerializeField] private string m_UIPrefabRootPath;
        public string UIPrefabRootPath => m_UIPrefabRootPath;

        [Header("加载类型")]
        [Tooltip("UI资源加载方式（本地 / YooAsset）")]
        [SerializeField] private bool m_FromResources;
        public bool FromResources => m_FromResources;

        public UIScriptGenerateData(string projectName, string nameSpace, string generateHolderCodePath, string uiPrefabRootPath, bool fromResources = true)
        {
            m_ProjectName = projectName;
            m_NameSpace = nameSpace;
            m_GenerateHolderCodePath = generateHolderCodePath;
            m_UIPrefabRootPath = uiPrefabRootPath;
            m_FromResources = fromResources;
        }
    }

    [Serializable]
    public class UIElementRegexData
    {
        [Tooltip("匹配UI元素名称的正则表达式")]
        [SerializeField] private string m_UIElementRegex;
        public string UIElementRegex => m_UIElementRegex;

        [Tooltip("匹配到的UI组件类型")]
        [SerializeField] private string m_ComponentType;
        public string ComponentType => m_ComponentType;

        public UIElementRegexData(string uiElementRegex, string componentType)
        {
            m_UIElementRegex = uiElementRegex;
            m_ComponentType = componentType;
        }
    }

    [Serializable]
    public class UIEventBindingConfig
    {
        [Tooltip("组件类型全名（如 UnityEngine.UI.Button）")]
        [SerializeField] private string m_ComponentType;
        public string ComponentType => m_ComponentType;

        [Tooltip("事件成员名（如 onClick）")]
        [SerializeField] private string m_EventMember;
        public string EventMember => m_EventMember;

        [Tooltip("事件触发名（如 Click），用于方法命名 On{Trigger}{VarName}{Abbr}")]
        [SerializeField] private string m_TriggerName;
        public string TriggerName => m_TriggerName;

        [Tooltip("回调方法签名（如 (bool isOn)），为空表示无参数")]
        [SerializeField] private string m_CallbackSignature;
        public string CallbackSignature => m_CallbackSignature;

        public UIEventBindingConfig(string componentType, string eventMember, string triggerName, string callbackSignature)
        {
            m_ComponentType = componentType;
            m_EventMember = eventMember;
            m_TriggerName = triggerName;
            m_CallbackSignature = callbackSignature;
        }
    }
}