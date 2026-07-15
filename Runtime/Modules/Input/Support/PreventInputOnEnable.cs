using UnityEngine;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 启用时，阻止用户输入。
    /// </summary>
    public sealed class PreventInputOnEnable : MonoBehaviour
    {
        [Tooltip("禁止角色控制器移动")]
        [SerializeField] private bool m_LockPlayerController = false;
        [Tooltip("禁止交互UI")]
        [SerializeField] private bool m_PreventInteractionUI = false;
        
        private bool _lockPlayerController;
        private bool _preventInteractionUI;
        
        private void OnEnable()
        {
            if (GameModule.Input == null) return;
            
            if (m_LockPlayerController)
            {
                _lockPlayerController = GameModule.Input.LockPlayerController;
                GameModule.Input.LockPlayerController = true;
            }

            if (m_PreventInteractionUI)
            {
                _preventInteractionUI = GameModule.Input.PreventInteractionUI;
                GameModule.Input.PreventInteractionUI = true;
            }
        }
        
        private void OnDisable()
        {
            if (GameModule.Input == null) return;
            
            GameModule.Input.LockPlayerController = _lockPlayerController;
            GameModule.Input.PreventInteractionUI = _preventInteractionUI;
        }
    }
}