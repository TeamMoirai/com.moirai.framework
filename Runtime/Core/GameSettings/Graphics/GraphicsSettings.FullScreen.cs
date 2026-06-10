using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 全屏
    /// </summary>
    public partial class GraphicsSettings
    {
        private bool? _lastKnownFullScreen = null;
        private int _lastSetFullScreenFrame = 0;

        public static event Action<bool> OnFullScreenChanged;

        public static bool GetFullScreen()
        {
            bool result;

            // 在N帧后重置。假设此时Screen.fullScreen已完成更新。
            if (Time.frameCount - Instance._lastSetFullScreenFrame > 3)
                Instance._lastKnownFullScreen = null;

            if (Instance._lastKnownFullScreen.HasValue)
                result = Instance._lastKnownFullScreen.Value;
            else
                result = Screen.fullScreen;

            return result;
        }

        /// <summary>
        /// 注意：全屏切换不会立即生效，而是在当前帧结束后才会执行。
        /// 详见：https://docs.unity3d.com/ScriptReference/Screen-fullScreen.html
        /// </summary>
        /// <param name="fullScreen">Fullscreen on or off.</param>
        public static void SetFullScreen(bool fullScreen)
        {
            // 请求变更但将实际执行委托给协调器。
            ScreenOrchestrator.Instance.RequestFullScreen(fullScreen);

            // 缓存
            Instance._lastSetFullScreenFrame = Time.frameCount;
            Instance._lastKnownFullScreen = fullScreen;

            OnFullScreenChanged?.Invoke(fullScreen);
        }
    }
}