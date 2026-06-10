using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.Input.Prompts
{
    /// <summary>
    /// 显示输入设备的图标
    /// </summary>
    [RequireComponent(typeof(Image))]
    public class PromptDeviceIcon : InputSystemPromptBase
    {
        // Bindings
        private const string BINDINGS_GROUP = "绑定 [Bindings]";
        
        [BoxGroup(BINDINGS_GROUP), ReadOnly]
        [Tooltip("要应用提示图标的图像")]
        [SerializeField] private Image m_Image;
        
        // Settings
        private const string SETTINGS_GROUP = "设置 [Settings]";

        [BoxGroup(SETTINGS_GROUP), Required]
        [Tooltip("要使用的自定义 sprite 名称，对应 DeviceSpriteEntries 中的配置")]
        [SerializeField] private string m_DeviceSpriteName = "";
        
        [BoxGroup(SETTINGS_GROUP)]
        [Tooltip("将UI元素的大小设置为其原始图像的大小")]
        [SerializeField] private bool m_SetNativeSize = true;

        protected override bool IsValid => m_Image != null;
        
        private void Reset()
        {
            m_Image ??= GetComponent<Image>();
        }
        
        protected override void RefreshPrompt()
        {
            if (string.IsNullOrEmpty(m_DeviceSpriteName)) return;
            
            var sourceSprite = InputDevicePromptSystem.GetDeviceSprite(m_DeviceSpriteName);
            if (sourceSprite == null) return;

            m_Image.sprite = sourceSprite;
            if (m_SetNativeSize) m_Image.SetNativeSize();
        }
    }
}