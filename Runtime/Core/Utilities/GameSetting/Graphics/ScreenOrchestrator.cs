using System.Collections;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 屏幕设置编排器：屏幕相关设置均异步（帧末）生效且彼此可能冲突，统一以 <c>Screen.fullScreenMode</c> 为优先按序应用。
    /// </summary>
    /// <remarks>
    /// 请求先缓存，在 <c>LateUpdate</c> 中按 全屏 → 全屏模式 → 分辨率/刷新率 的顺序应用，逐步之间让出一帧。 <br />
    /// 同一属性以最后一次请求为准；新请求会取消上一轮未完成的应用协程。
    /// </remarks>
    public sealed class ScreenOrchestrator : SingletonMono_Persistent<ScreenOrchestrator>
    {
        private Resolution? _requestedResolution;
        // 注意：RefreshRate类已于2021_2版本（2021年）添加，但直到2022.2版本（注意是2022年，而非2021年）才在Screen.SetResolution方法中实际使用。
#if UNITY_2022_2_OR_NEWER
        private RefreshRate? _requestedRefreshRate;
#else
        private int? _requestedRefreshRate;
#endif

        private bool? _requestedFullScreen;
        private FullScreenMode? _requestedFullScreenMode;

        private Coroutine _applyCoroutine;

        #region 引擎方法 [UNITY METHODS]

        private void LateUpdate()
        {
            if (_requestedResolution.HasValue || _requestedFullScreen.HasValue || _requestedFullScreenMode.HasValue)
            {
                Apply();
            }
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 设置分辨率。
        /// </summary>
        public void RequestResolution(Resolution resolution)
        {
            _requestedResolution = resolution;
        }

        /// <summary>
        /// 设置刷新率。
        /// </summary>
        /// <remarks>重载依赖 Unity 版本：<c>UNITY_2022_2_OR_NEWER</c> 接受 <c>RefreshRate</c>，更早版本接受 <c>int</c>。</remarks>
#if UNITY_2022_2_OR_NEWER
        public void RequestRefreshRate(RefreshRate refreshRate)
        {
            _requestedRefreshRate = refreshRate;
        }
#else
        public void RequestRefreshRate(int refreshRate)
        {
            _requestedRefreshRate = refreshRate;
        }
#endif

        /// <summary>
        /// 设置全屏。
        /// </summary>
        public void RequestFullScreen(bool fullScreen)
        {
            _requestedFullScreen = fullScreen;
        }

        /// <summary>
        /// 设置全屏模式。
        /// </summary>
        public void RequestFullScreenMode(FullScreenMode fullScreenMode)
        {
            _requestedFullScreenMode = fullScreenMode;
        }

        /// <summary>
        /// 获取当前分辨率。
        /// </summary>
        public static Resolution GetCurrentResolution()
        {
            if (Screen.fullScreenMode == FullScreenMode.Windowed)
            {
                // 在窗口模式下，Screen.currentResolution会返回显示器的分辨率而非原生窗口尺寸，这并非所需。
                // 因此，基于窗口大小自行创建了解决方案。
                var res = new Resolution();
                res.width = Screen.width;
                res.height = Screen.height;
#if UNITY_2022_2_OR_NEWER
                res.refreshRateRatio = Screen.currentResolution.refreshRateRatio;
#else
                res.refreshRate = Screen.currentResolution.refreshRate;
#endif
                return res;
            }
            else
            {
                return Screen.currentResolution;
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void Apply()
        {
            if (_applyCoroutine != null)
            {
                StopCoroutine(_applyCoroutine);
            }

            _applyCoroutine = StartCoroutine(ApplyStaggered());
        }

        private IEnumerator ApplyStaggered()
        {
            // 复制
            var tRequestedFullScreen = _requestedFullScreen;
            var tRequestedFullScreenMode = _requestedFullScreenMode;
            var tRequestedResolution = _requestedResolution;
            var tRequestedRefreshRate = _requestedRefreshRate;

            // 立即重置
            _requestedFullScreen = null;
            _requestedFullScreenMode = null;
            _requestedResolution = null;
            _requestedRefreshRate = null;

            if (tRequestedFullScreen.HasValue)
            {
                if (!tRequestedFullScreen.Value)
                {
                    Screen.fullScreen = false;
                }
                else
                {
                    Screen.fullScreen = true;
                }

                // 等一帧
                yield return null;
            }

            if (tRequestedFullScreenMode.HasValue)
            {
                Screen.fullScreenMode = tRequestedFullScreenMode.Value;

                // 等一帧
                yield return null;
            }

            if (tRequestedResolution.HasValue)
            {
                var resolution = tRequestedResolution.Value;
                // 注意：RefreshRate类已于2021_2版本（2021年）添加，但直到2022.2版本（注意是2022年，而非2021年）才在Screen.SetResolution方法中实际使用。
#if UNITY_2022_2_OR_NEWER
                var refreshRate = tRequestedRefreshRate.HasValue ? tRequestedRefreshRate.Value : tRequestedResolution.Value.refreshRateRatio;
#else
                var refreshRate = tRequestedRefreshRate.HasValue ? tRequestedRefreshRate.Value : tRequestedResolution.Value.refreshRate;
#endif
                var fullScreenMode = tRequestedFullScreenMode.HasValue ? tRequestedFullScreenMode.Value : Screen.fullScreenMode;
                Screen.SetResolution(resolution.width, resolution.height, fullScreenMode, refreshRate);
            }
            else if (tRequestedRefreshRate.HasValue)
            {
                var resolution = GetCurrentResolution();
                var fullScreenMode = tRequestedFullScreenMode.HasValue ? tRequestedFullScreenMode.Value : Screen.fullScreenMode;
                Screen.SetResolution(resolution.width, resolution.height, fullScreenMode, tRequestedRefreshRate.Value);
            }
        }
        
        #endregion
    }
}