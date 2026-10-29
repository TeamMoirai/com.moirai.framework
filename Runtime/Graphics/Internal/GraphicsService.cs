using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Settings;

namespace Moirai.Atropos.Rendering.Internal
{
    internal sealed class GraphicsService : IGraphicsService
    {
        private readonly IGraphicsApplier _applier;
        private readonly GraphicsOptions _options;
        private readonly IKeyValueStore _store;
        private GraphicsState _state;
        private WindowMode _lastFullScreenMode;

        internal GraphicsService(IGraphicsApplier applier, GraphicsOptions options, IKeyValueStore store)
        {
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _lastFullScreenMode = IsFullScreen(options.DefaultWindowMode)
                ? options.DefaultWindowMode : WindowMode.FullScreenWindow;

            ResolutionInfo resolution = options.DefaultResolution;
            if (_store.Has(GraphicsSettingKeys.Resolution))
            {
                try
                {
                    resolution = ResolutionPreference.Decode(
                        _store.GetString(GraphicsSettingKeys.Resolution));
                }
                catch (FormatException) { resolution = options.DefaultResolution; }
            }

            WindowMode parsedMode = default;
            bool hasMode = _store.Has(GraphicsSettingKeys.WindowMode) &&
                Enum.TryParse(_store.GetString(GraphicsSettingKeys.WindowMode), false,
                    out parsedMode) && Enum.IsDefined(typeof(WindowMode), parsedMode);
            WindowMode mode = hasMode ? parsedMode : options.DefaultWindowMode;
            bool fullScreen = hasMode
                ? IsFullScreen(mode)
                : _store.GetBool(GraphicsSettingKeys.FullScreen, options.DefaultFullScreen);
            if (!hasMode && fullScreen) mode = _lastFullScreenMode;
            if (IsFullScreen(mode)) _lastFullScreenMode = mode;

            _state = new GraphicsState(resolution, fullScreen, mode,
                _store.GetBool(GraphicsSettingKeys.VSync, options.DefaultVSync));
            _applier.VSync = _state.VSync;
        }

        public event Action<ResolutionInfo> ResolutionChanged;
        public event Action<bool> FullScreenChanged;
        public event Action<WindowMode> WindowModeChanged;
        public event Action<bool> VSyncChanged;

        public GraphicsState State => _state;

        public async UniTask SetResolutionAsync(ResolutionInfo resolution,
            CancellationToken cancellationToken)
        {
            await _applier.ApplyResolutionAsync(resolution, _state.FullScreen, _state.Mode,
                cancellationToken);
            ResolutionInfo previous = _state.Resolution;
            _state = new GraphicsState(resolution, _state.FullScreen, _state.Mode, _state.VSync);
            _store.SetString(GraphicsSettingKeys.Resolution, ResolutionPreference.Encode(resolution));
            if (!resolution.Equals(previous)) ResolutionChanged?.Invoke(resolution);
        }

        public async UniTask SetFullScreenAsync(bool fullScreen, CancellationToken cancellationToken)
        {
            WindowMode mode = fullScreen ? _lastFullScreenMode : WindowMode.Windowed;
            await _applier.ApplyResolutionAsync(_state.Resolution, fullScreen, mode, cancellationToken);
            GraphicsState previous = _state;
            _state = new GraphicsState(previous.Resolution, fullScreen, mode, previous.VSync);
            if (IsFullScreen(mode)) _lastFullScreenMode = mode;
            WriteDisplayKeys();
            if (previous.FullScreen != fullScreen) FullScreenChanged?.Invoke(fullScreen);
            if (previous.Mode != mode) WindowModeChanged?.Invoke(mode);
        }

        public async UniTask SetWindowModeAsync(WindowMode mode, CancellationToken cancellationToken)
        {
            bool fullScreen = IsFullScreen(mode);
            await _applier.ApplyResolutionAsync(_state.Resolution, fullScreen, mode, cancellationToken);
            GraphicsState previous = _state;
            _state = new GraphicsState(previous.Resolution, fullScreen, mode, previous.VSync);
            if (fullScreen) _lastFullScreenMode = mode;
            WriteDisplayKeys();
            if (previous.FullScreen != fullScreen) FullScreenChanged?.Invoke(fullScreen);
            if (previous.Mode != mode) WindowModeChanged?.Invoke(mode);
        }

        public void SetVSync(bool enabled)
        {
            if (_state.VSync == enabled) return;
            _applier.VSync = enabled;
            _state = new GraphicsState(_state.Resolution, _state.FullScreen, _state.Mode, enabled);
            _store.SetBool(GraphicsSettingKeys.VSync, enabled);
            VSyncChanged?.Invoke(enabled);
        }

        public void RemoveAllSettings()
        {
            _store.Remove(GraphicsSettingKeys.Resolution);
            _store.Remove(GraphicsSettingKeys.FullScreen);
            _store.Remove(GraphicsSettingKeys.WindowMode);
            _store.Remove(GraphicsSettingKeys.VSync);
        }

        private void WriteDisplayKeys()
        {
            _store.SetString(GraphicsSettingKeys.Resolution,
                ResolutionPreference.Encode(_state.Resolution));
            _store.SetBool(GraphicsSettingKeys.FullScreen, _state.FullScreen);
            _store.SetString(GraphicsSettingKeys.WindowMode, _state.Mode.ToString());
        }

        private static bool IsFullScreen(WindowMode mode)
            => mode == WindowMode.ExclusiveFullScreen || mode == WindowMode.FullScreenWindow;
    }
}
