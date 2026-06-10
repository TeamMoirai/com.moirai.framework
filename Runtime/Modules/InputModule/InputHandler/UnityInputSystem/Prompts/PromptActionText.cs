using Moirai.Atropos.Attributes;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;

namespace Moirai.Atropos.Input.Prompts
{
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class PromptActionText : InputSystemPromptBase
    {
        // Bindings
        private const string BINDINGS_GROUP = "绑定 [Bindings]";
        
        [BoxGroup(BINDINGS_GROUP), ReadOnly]
        [Tooltip("要应用提示图标的图像 TextMeshProUGUI 组件")]
        [SerializeField] private TextMeshProUGUI m_TextField;
        
        // Settings
        private const string SETTINGS_GROUP = "设置 [Settings]";
        
        [BoxGroup(SETTINGS_GROUP), Required, TextAreaResizable]
        [Tooltip("要应用的文本")]
        [SerializeField] private string m_OriginalText = "Press {action:UI/Submit}";
        
        [BoxGroup(SETTINGS_GROUP)]
        [Tooltip("是否将复合动作替换位单个的组合图标")]
        [SerializeField] private bool m_IsComposite = false;
        
        protected override bool IsValid => m_TextField != null;
        
        private void Reset()
        {
            m_TextField ??= GetComponent<TextMeshProUGUI>();
        }
        
        protected override void RefreshPrompt()
        {
            if (string.IsNullOrEmpty(m_OriginalText)) return;

            if (m_TextField == null) return;
            m_TextField.text = InputDevicePromptSystem.InsertPromptSprites(m_OriginalText, m_IsComposite);
        }
    }
}