namespace Moirai.Atropos.Rendering
{
    public sealed class GraphicsOptions
    {
        public ResolutionInfo DefaultResolution { get; set; } = new ResolutionInfo(1920, 1080, 60, 1);
        public bool DefaultFullScreen { get; set; } = true;
        public WindowMode DefaultWindowMode { get; set; } = WindowMode.FullScreenWindow;
        public bool DefaultVSync { get; set; } = true;
    }
}
