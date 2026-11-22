using System;
using Moirai.Atropos;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// PlayMode 音频测试宿主：注入最小 <see cref="AudioGroupConfig"/>，把实例换入 <see cref="AudioService"/> 的门面槽位并占住容器注册位。
    /// </summary>
    /// <remarks>
    /// 回摆后 Tick 由容器直达注册实例（不再经门面转发到槽位）：槽位换入的测试 handler 必须同时换绑进默认世界， <br />
    /// SO 播放时长/淡出等容器驱动的时间语义才能到达测试 handler。 <br />
    /// 关键路径夹具不得因工程 Settings 未配置而 <c>Assert.Ignore</c>——那会把整套验收洗成「全绿零覆盖」；配置缺失时这里直接 <c>Assert.Fail</c>。
    /// </remarks>
    internal sealed class AudioServiceTestHost : IDisposable
    {
        private readonly IAudioService _previousHandler;
        private bool _disposed;

        public UnityAudioHandler Handler { get; }

        /// <summary>
        /// 构造后立即可播；配置建不出来会 Fail，不会静默跳过。
        /// </summary>
        public AudioServiceTestHost(params EAudioTrack[] tracks)
        {
            if (tracks == null || tracks.Length == 0)
            {
                tracks = new[]
                {
                    EAudioTrack.Music, EAudioTrack.Sfx, EAudioTrack.Voice, EAudioTrack.Ambience,
                };
            }

            var configs = new AudioGroupConfig[tracks.Length];
            for (int i = 0; i < tracks.Length; i++)
            {
                configs[i] = CreateGroup(tracks[i]);
            }

            Handler = new UnityAudioHandler();
            // 先以容器语义占住已初始化态（Context 缺席 → 后端直驱，仅 Init(null 配置) + 混音接线），
            // 再覆盖测试最小配置——注册进容器时状态已是 Initialized，RegisterRuntime 不再驱动 OnInit
            ((IServiceLifecycle)Handler).Initialize();
            Handler.Initialize(null, null, configs);

            var categories = Handler.AudioCategories;
            Assert.IsNotNull(categories, "最小配置初始化后 AudioCategories 不得为 null");
            Assert.IsNotEmpty(categories, "最小 AudioGroupConfigs 应至少产出一个 AudioCategory");

            _previousHandler = AudioService.Internal_PeekHandler();
            AudioService.Internal_UseHandler(Handler);

            // 容器换绑：摘掉 warm 绑定，把测试 handler 注册进默认世界（容器 Tick 直达测试 handler）
            if (GameServices.Default.TryGetScope(EServiceScopeKind.App, out var scope) &&
                scope.TryGet(typeof(IAudioService), out var bound))
            {
                GameServices.UnregisterHandler(typeof(IAudioService), bound);
            }

            GameServices.RegisterService(EServiceScopeKind.App, typeof(IAudioService), Handler);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                // 注册位上的测试 handler 走容器销毁（OnShutdown 由容器驱动，含缓存失效钩子）
                if (GameServices.Default.TryGetScope(EServiceScopeKind.App, out var scope) &&
                    scope.TryGet(typeof(IAudioService), out var bound) &&
                    ReferenceEquals(bound, Handler))
                {
                    GameServices.UnregisterHandler(typeof(IAudioService), Handler);
                }
                else
                {
                    Handler?.StopAll(0f);
                    ((IServiceLifecycle)Handler)?.Destroy();
                }
            }
            finally
            {
                AudioService.Internal_UseHandler(_previousHandler);
            }
        }

        private static AudioGroupConfig CreateGroup(EAudioTrack track)
        {
            var config = new AudioGroupConfig
            {
                // internal setter（InternalsVisibleTo）
                AudioTrack = track,
            };

            config.m_MaxChannel = 8;
            config.m_CanExpand = true;
            config.m_DefaultVolume = 1f;
            return config;
        }
    }
}
