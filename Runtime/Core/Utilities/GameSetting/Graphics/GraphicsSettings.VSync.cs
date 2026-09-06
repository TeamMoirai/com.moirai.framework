using System;
using UnityEngine;

namespace Moirai.Atropos
{
    public partial class GraphicsSettings
    {
        /// <summary>垂直同步是否启用</summary>
        public static bool VSyncEnabled => QualitySettings.vSyncCount != 0;

        public static event Action<int> OnVSyncChanged;

        /// <summary>
        /// 设置垂直同步
        /// </summary>
        /// <param name="enabled"></param>
        private static void SetVSync(bool enabled)
        {
            int vSyncCount = enabled ? 1 : 0;
            QualitySettings.vSyncCount = vSyncCount;
            OnVSyncChanged?.Invoke(vSyncCount);
        }

        /// <summary>
        /// 设置垂直同步
        /// </summary>
        /// <param name="vSyncCount">是否应启用 VSync（在 60Hz 屏幕上，1：60fps，2：30fps，0：不等待 VSync）</param>
        private static void SetVSync(int vSyncCount)
        {
            QualitySettings.vSyncCount = vSyncCount;
            OnVSyncChanged?.Invoke(vSyncCount);
        }
    }
}