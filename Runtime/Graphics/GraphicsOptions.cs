using System;

namespace Moirai.Atropos.Rendering
{
    public sealed class GraphicsOptions
    {
        public ResolutionInfo DefaultResolution { get; set; } = new ResolutionInfo(1920, 1080, 60, 1);
        public bool DefaultFullScreen { get; set; } = true;
        public WindowMode DefaultWindowMode { get; set; } = WindowMode.FullScreenWindow;
        public bool DefaultVSync { get; set; } = true;
        public ResolutionInfo[] CustomResolutions { get; set; } = Array.Empty<ResolutionInfo>();
        public bool LimitMaxResolutionToDisplayResolution { get; set; } = true;
        public bool LimitToCurrentRefreshRate { get; set; } = false;
        public bool SkipRefreshRatesWith59Hz { get; set; } = true;
        public bool LimitToUniqueResolutions { get; set; } = true;
        public bool AllowResolutionChangeOnMobile { get; set; } = false;
        public double[] AllowedAspectRatios { get; set; } = Array.Empty<double>();
        public double AllowedAspectRatioDelta { get; set; } = 0.02d;
    }
}
