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
            if (GameApp.Input == null) return;
            
            if (m_LockPlayerController)
            {
                _lockPlayerController = GameApp.Input.LockPlayerController;
                GameApp.Input.LockPlayerController = true;
            }

            if (m_PreventInteractionUI)
            {
                _preventInteractionUI = GameApp.Input.PreventInteractionUI;
                GameApp.Input.PreventInteractionUI = true;
            }
        }
        
        private void OnDisable()
        {
            if (GameApp.Input == null) return;
            
            GameApp.Input.LockPlayerController = _lockPlayerController;
            GameApp.Input.PreventInteractionUI = _preventInteractionUI;
        }
    }
}