namespace Moirai.Atropos.Rendering
{
    public readonly struct GraphicsState
    {
        public GraphicsState(ResolutionInfo resolution, bool fullScreen, WindowMode mode, bool vSync)
        {
            Resolution = resolution;
            FullScreen = fullScreen;
            Mode = mode;
            VSync = vSync;
        }

        public ResolutionInfo Resolution { get; }
        public bool FullScreen { get; }
        public WindowMode Mode { get; }
        public bool VSync { get; }
    }
}
