using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos.Rendering.Internal
{
    internal sealed class GraphicsService : IGraphicsService
    {
        private readonly IGraphicsApplier _applier;
        private GraphicsState _state;

        internal GraphicsService(IGraphicsApplier applier, GraphicsOptions options)
        {
            _applier = applier ?? throw new ArgumentNullException(nameof(applier));
            if (options == null) throw new ArgumentNullException(nameof(options));
            _state = new GraphicsState(options.DefaultResolution, options.DefaultFullScreen,
                options.DefaultWindowMode, options.DefaultVSync);
        }

        public GraphicsState State => _state;

        public async UniTask SetResolutionAsync(ResolutionInfo resolution,
            CancellationToken cancellationToken)
        {
            await _applier.ApplyResolutionAsync(resolution, _state.FullScreen, _state.Mode, cancellationToken);
            _state = new GraphicsState(resolution, _state.FullScreen, _state.Mode, _state.VSync);
        }

        public async UniTask SetFullScreenAsync(bool fullScreen, CancellationToken cancellationToken)
        {
            await _applier.ApplyResolutionAsync(_state.Resolution, fullScreen, _state.Mode, cancellationToken);
            _state = new GraphicsState(_state.Resolution, fullScreen, _state.Mode, _state.VSync);
        }

        public async UniTask SetWindowModeAsync(WindowMode mode, CancellationToken cancellationToken)
        {
            await _applier.ApplyResolutionAsync(_state.Resolution, _state.FullScreen, mode, cancellationToken);
            _state = new GraphicsState(_state.Resolution, _state.FullScreen, mode, _state.VSync);
        }

        public void SetVSync(bool enabled)
        {
            _applier.VSync = enabled;
            _state = new GraphicsState(_state.Resolution, _state.FullScreen, _state.Mode, enabled);
        }
    }
}
