using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// Window/Widget 组件绑定基类
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIBindComponent : MonoBehaviour
    {
#if UNITY_EDITOR
        [HideInInspector]
        [SerializeField] private Vector3 m_Scale;

        /// <remarks>fix: Apply Override 操作时会将 Scale 重置为 (0,0,0)</remarks>
        protected virtual void OnValidate()
        {
            if (transform.localScale == Vector3.zero)
                transform.localScale = m_Scale;
            else
                m_Scale = transform.localScale;
        }
    }
#endif
}