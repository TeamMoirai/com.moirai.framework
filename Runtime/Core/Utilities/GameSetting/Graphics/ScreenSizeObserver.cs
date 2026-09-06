using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 屏幕大小观察者，每帧检查屏幕尺寸，并提供可注册的 <see cref="onScreenSizeChanged"/> 事件。
    /// </summary>
    public sealed class ScreenSizeObserver : SingletonMono_Persistent<ScreenSizeObserver>
    {
        public delegate void OnScreenSizeChangedDelegate(Resolution resolution);
        public OnScreenSizeChangedDelegate onScreenSizeChanged;

        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void OnEnable()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }

        private void Update()
        {
            if (_lastScreenWidth != Screen.width || _lastScreenHeight != Screen.height)
            {
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;

                var resolution = new Resolution();
                // 无法使用 Screen.currentResolution，因为在窗口模式下，该属性始终返回的是全屏分辨率而非窗口实际分辨率。
                resolution.width = Screen.width;
                resolution.height = Screen.height;
#if UNITY_2022_2_OR_NEWER
                resolution.refreshRateRatio = Screen.currentResolution.refreshRateRatio;
#else
                resolution.refreshRate = Screen.currentResolution.refreshRate;
#endif
                onScreenSizeChanged?.Invoke(resolution);
            }
        }
    }
}