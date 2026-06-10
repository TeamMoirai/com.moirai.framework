#if UNITY_EDITOR
using UnityEngine;

namespace Moirai.Atropos.UI
{
    public partial class UIBindComponent
    {
        [SerializeField, HideInInspector] private string m_GenCodePath;
        [SerializeField, HideInInspector] internal string m_ClassName;
        [SerializeField, HideInInspector] private string m_ImpCodePath;
        [SerializeField, HideInInspector] private bool m_IsGenImpClass;
        [SerializeField, HideInInspector] internal string m_UIType;

        public void AddComponent(Component component)
        {
            if (m_Components != null && !m_Components.Contains(component))
            {
                m_Components.Add(component);
            }
        }

        public void Clear()
        {
            m_Components?.Clear();
        }
    }
}

#endif