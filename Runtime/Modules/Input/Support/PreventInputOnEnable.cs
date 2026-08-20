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
            var svc = GameApp.Services?.GetService<IInputService>();
            if (svc == null) return;

            if (m_LockPlayerController)
            {
                _lockPlayerController = svc.LockPlayerController;
                svc.LockPlayerController = true;
            }

            if (m_PreventInteractionUI)
            {
                _preventInteractionUI = svc.PreventInteractionUI;
                svc.PreventInteractionUI = true;
            }
        }

        private void OnDisable()
        {
            var svc = GameApp.Services?.GetService<IInputService>();
            if (svc == null) return;

            svc.LockPlayerController = _lockPlayerController;
            svc.PreventInteractionUI = _preventInteractionUI;
        }
    }
}