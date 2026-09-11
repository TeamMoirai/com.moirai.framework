using Moirai.Atropos;
using Sirenix.OdinInspector;
using UnityEngine;

namespace GameLogic
{
    public class CameraManager : SingletonMono_Persistent<CameraManager>
    {
        [DisableIf(nameof(m_MainCamera))]
        [SerializeField] private Camera m_MainCamera;

        public Camera MainCamera
        {
            get
            {
                if (m_MainCamera == null) m_MainCamera = GetComponentInChildren<Camera>();
                return m_MainCamera;
            }
        }

        private void Reset()
        {
            m_MainCamera = GetComponentInChildren<Camera>();
        }
    }
}