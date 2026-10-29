using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Rendering.Unity
{
    public sealed class UnityGraphicsApplier : IGraphicsApplier
    {
        public UniTask<GraphicsState> GetCurrentAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resolution current = Screen.currentResolution;
            long numerator = current.refreshRateRatio.numerator;
            long denominator = current.refreshRateRatio.denominator;
            int rate = denominator <= 0 ? 1 : (int)Math.Max(1L, (numerator + denominator / 2) / denominator);
            var state = new GraphicsState(
                new ResolutionInfo(current.width, current.height, rate, 1),
                Screen.fullScreen, ToWindowMode(Screen.fullScreenMode),
                QualitySettings.vSyncCount != 0);
            return UniTask.FromResult(state);
        }

        public UniTask ApplyResolutionAsync(ResolutionInfo resolution, bool fullScreen,
            WindowMode mode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Screen.SetResolution(resolution.Width, resolution.Height,
                ToFullScreenMode(fullScreen, mode),
                new RefreshRate
                {
                    numerator = (uint)resolution.RateNumerator,
                    denominator = (uint)resolution.RateDenominator
                });
            return UniTask.CompletedTask;
        }

        public bool VSync
        {
            get => QualitySettings.vSyncCount != 0;
            set => QualitySettings.vSyncCount = value ? 1 : 0;
        }

        private static FullScreenMode ToFullScreenMode(bool fullScreen, WindowMode mode)
        {
            if (!fullScreen) return mode == WindowMode.MaximizedWindow
                ? FullScreenMode.MaximizedWindow : FullScreenMode.Windowed;
            return mode == WindowMode.ExclusiveFullScreen
                ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
        }

        private static WindowMode ToWindowMode(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return WindowMode.ExclusiveFullScreen;
                case FullScreenMode.MaximizedWindow: return WindowMode.MaximizedWindow;
                case FullScreenMode.Windowed: return WindowMode.Windowed;
                default: return WindowMode.FullScreenWindow;
            }
        }
    }
}
