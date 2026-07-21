using UnityEngine;
using UnityEngine.UI;

namespace Moirai.Atropos.UI.Adapter
{
    /// <summary>
    /// 安全区域适配器
    /// </summary>
    public class SafeAreaAdapter : AdapterBase
    {
        [Header("是否每帧都计算")]
        public bool CalculateEveryFrame = false;
        
        private RectTransform _rect;
        private static CanvasScaler s_Scaler;

        public static void Init(CanvasScaler scaler)
        {
            SafeAreaAdapter.s_Scaler = scaler;
        }

        private void Awake()
        {
            Init(UnityUtility.FindObjectByType<CanvasScaler>());
            _rect = GetComponent<RectTransform>();
            Adapt();
        }

        private void Update()
        {
            if (CalculateEveryFrame)
            {
                Adapt();
            }
        }

        public override void Adapt()
        {
            if (s_Scaler == null) return;

            var safeArea = Screen.safeArea;
            int width = (int)(s_Scaler.referenceResolution.x * (1 - s_Scaler.matchWidthOrHeight) +
                s_Scaler.referenceResolution.y * Screen.width / Screen.height * s_Scaler.matchWidthOrHeight);
            int height = (int)(s_Scaler.referenceResolution.y * s_Scaler.matchWidthOrHeight -
              s_Scaler.referenceResolution.x * Screen.height / Screen.width * (s_Scaler.matchWidthOrHeight - 1));
            float ratio = s_Scaler.referenceResolution.y * s_Scaler.matchWidthOrHeight / Screen.height -
                s_Scaler.referenceResolution.x * (s_Scaler.matchWidthOrHeight - 1) / Screen.width;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = new Vector2(safeArea.position.x * ratio, safeArea.position.y * ratio);
            _rect.offsetMax = new Vector2(safeArea.position.x * ratio + safeArea.width * ratio - width, -(height - safeArea.position.y * ratio - safeArea.height * ratio));
        }
    }
}