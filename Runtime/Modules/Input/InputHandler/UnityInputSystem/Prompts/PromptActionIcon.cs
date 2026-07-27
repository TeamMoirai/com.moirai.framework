#if ENABLE_INPUT_SYSTEM
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Input.Prompts
{
    /// <summary>
    /// 显示输入的提示图标
    /// </summary>
    /// <remarks>用于单独的按键图标提示，图文混排请使用 <see cref="PromptActionText"/></remarks>
    [RequireComponent(typeof(Image))]
    public class PromptActionIcon : InputSystemPromptBase
    {
        private enum EScaleMode { None, Height, Width }

        // Bindings
        private const string BINDINGS_GROUP = "绑定 [Bindings]";

        [BoxGroup(BINDINGS_GROUP), ReadOnly]
        [Tooltip("要应用提示 Sprite 的图像")]
        [SerializeField] private Image m_Image;

        // Settings
        private const string SETTINGS_GROUP = "设置 [Settings]";

        [BoxGroup(SETTINGS_GROUP), Required]
        [Tooltip("完整路径，包括绑定映射和动作，例如 Player/Move")]
        [SerializeField] private string m_Action = "Player/Move";

        [BoxGroup(SETTINGS_GROUP)]
        [Tooltip("是否将复合动作替换位单个的组合图标")]
        [SerializeField] private bool m_IsComposite = false;

        [BoxGroup(SETTINGS_GROUP)]
        [Tooltip("将UI元素的大小设置为其原始图像的大小")]
        [SerializeField] private bool m_SetNativeSize = false;
        [BoxGroup(SETTINGS_GROUP)]
        [Tooltip("设置其原始大小时，缩放基于的基准")]
        [ShowIf(nameof(m_SetNativeSize))]
        [SerializeField] private EScaleMode m_ScaleMode = EScaleMode.Height;

        protected override bool IsValid => m_Image != null;

        /// <summary>
        /// 获取设置绑定的按键。
        /// </summary>
        /// <param name="value">按键的完整路径</param>
        /// <remarks>包括绑定映射和动作，例如 Player/Move</remarks>
        public string Action
        {
            get => m_Action;
            set
            {
                if (m_Action == value) return;

                m_Action = value;
                RefreshPrompt();
            }
        }

        private void Reset()
        {
            m_Image ??= GetComponent<Image>();
        }

        protected override void RefreshPrompt()
        {
            if (string.IsNullOrEmpty(m_Action)) return;

            var sourceSprite = InputDevicePromptSystem.GetActionPathBindingSprite(m_Action, m_IsComposite);
            if (sourceSprite == null) return;
            
            m_Image.sprite = sourceSprite;
            if (m_SetNativeSize)
            {
                var originalValue = m_ScaleMode == EScaleMode.Height ? m_Image.rectTransform.rect.height : m_Image.rectTransform.rect.width;
                m_Image.SetNativeSize();

                if (m_ScaleMode != EScaleMode.None)
                {
                    float scale = originalValue / (m_ScaleMode == EScaleMode.Height ? m_Image.rectTransform.rect.height : m_Image.rectTransform.rect.width);
                    m_Image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, m_Image.rectTransform.rect.height * scale);
                    m_Image.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, m_Image.rectTransform.rect.width * scale);
                }
            }
        }
    }
}
#endif