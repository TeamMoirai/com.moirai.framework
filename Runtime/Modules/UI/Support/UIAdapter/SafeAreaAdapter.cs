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
        private static CanvasScaler _scaler;

        public static void Init(CanvasScaler scaler)
        {
            SafeAreaAdapter._scaler = scaler;
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
            if (_scaler == null) return;

            var safeArea = Screen.safeArea;
            int width = (int)(_scaler.referenceResolution.x * (1 - _scaler.matchWidthOrHeight) +
                _scaler.referenceResolution.y * Screen.width / Screen.height * _scaler.matchWidthOrHeight);
            int height = (int)(_scaler.referenceResolution.y * _scaler.matchWidthOrHeight -
              _scaler.referenceResolution.x * Screen.height / Screen.width * (_scaler.matchWidthOrHeight - 1));
            float ratio = _scaler.referenceResolution.y * _scaler.matchWidthOrHeight / Screen.height -
                _scaler.referenceResolution.x * (_scaler.matchWidthOrHeight - 1) / Screen.width;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = new Vector2(safeArea.position.x * ratio, safeArea.position.y * ratio);
            _rect.offsetMax = new Vector2(safeArea.position.x * ratio + safeArea.width * ratio - width, -(height - safeArea.position.y * ratio - safeArea.height * ratio));
        }
    }
}