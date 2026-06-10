using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Input.Prompts
{
    /// <summary>
    /// 同一主题的按键提示配置
    /// </summary>
    [CreateAssetMenu(menuName = "Moirai Framework/Input/Glyph Collection", order = 2)]
    public class GlyphCollection : ScriptableObject
    {
        [Tooltip("唯一标识，用于在运行时引用此集合（如果不是默认集合）。不得包含空格、特殊字符或大小写。")]
        [SerializeField] private string m_Key = "default";
        
        [Header("Controller Maps")]
        [Tooltip("要应用的设备按键提示映射。")]
        [ListDrawerSettings(ShowFoldout = false, ShowPaging = false)]
        [SerializeField] private GlyphMap[] m_PromptMaps = Array.Empty<GlyphMap>();
        
        [HideLabel, BoxGroup("Disconnect Glyph")]
        [Tooltip("指定设备未连接时,显示的图标")]
        [SerializeField] private PromptGlyph m_DisconnectGlyph;
        [HideLabel, BoxGroup("Null Glyph")]
        [Tooltip("InputSystem 不存在指定 action 时,显示的图标")]
        [SerializeField] private PromptGlyph m_NullGlyph;
        [HideLabel, BoxGroup("Unbound Glyph")]
        [Tooltip("当 action 有效，但该 action 的没有输入提示时的图标")]
        [SerializeField] private PromptGlyph m_UnboundGlyph;
        
        // 生成相关配置
#if UNITY_EDITOR
        // 相当于手动调整 Sprite Glyph Table 的 Global Offset & Scale
        [Header("TMP_Sprite Asset Setting")]
        [Tooltip("水平偏移。调整sprite在X轴对齐基线")]
        [SerializeField] internal float m_OffsetX = 0f;
        [Tooltip("垂直偏移。调整sprite在Y轴对齐基线")]
        [SerializeField] internal float m_OffsetY = 0f;
        [Tooltip("前进值。控制该字符后预留的水平空间")]
        [SerializeField] internal float m_Advance = 0f;
        [Tooltip("缩放系数。显示尺寸的缩放比例")]
        [SerializeField] internal float m_ScaleFactor = 1f;
#endif
        
        public string Key => m_Key;
        
        public GlyphMap[] PromptMaps => m_PromptMaps;
        
        /// <summary>
        /// 当未连接指定设备时显示的图标
        /// </summary>
        public PromptGlyph DisconnectGlyph => m_DisconnectGlyph;
        /// <summary>
        /// 当 InputSystem 不存在指定 action 时的图标
        /// </summary>
        public PromptGlyph NullGlyph => m_NullGlyph;
        /// <summary>
        /// 当 action 有效，但该 action 的没有输入提示时的图标
        /// </summary>
        public PromptGlyph UnboundGlyph => m_UnboundGlyph;
    }
}