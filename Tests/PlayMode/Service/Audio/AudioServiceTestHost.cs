using System;
using System.Reflection;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// PlayMode 音频测试宿主：注入最小 <see cref="AudioGroupConfig"/> 并把实例换入 <c>AudioService.s_Handler</c>。
    /// <para>关键路径夹具不得因工程 Settings 未配置而 <c>Assert.Ignore</c>——那会把整套验收洗成「全绿零覆盖」。
    /// 配置缺失时这里直接 <c>Assert.Fail</c>。</para>
    /// </summary>
    internal sealed class AudioServiceTestHost : IDisposable
    {
        private static readonly FieldInfo s_HandlerField = typeof(AudioService).GetField(
            "s_Handler", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo s_InitializeMethod = typeof(UnityAudioHandler).GetMethod(
            "Initialize", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly object _previousHandler;
        private bool _disposed;

        public UnityAudioHandler Handler { get; }

        /// <summary>构造后立即可播；配置建不出来会 Fail，不会静默跳过。</summary>
        public AudioServiceTestHost(params EAudioTrack[] tracks)
        {
            Assert.IsNotNull(s_HandlerField, "AudioService.s_Handler 字段应存在");
            Assert.IsNotNull(s_InitializeMethod, "UnityAudioHandler.Initialize 应存在");

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
            s_InitializeMethod.Invoke(Handler, new object[] { null, null, configs });

            var categories = Handler.AudioCategories;
            Assert.IsNotNull(categories, "最小配置初始化后 AudioCategories 不得为 null");
            Assert.IsNotEmpty(categories, "最小 AudioGroupConfigs 应至少产出一个 AudioCategory");

            _previousHandler = s_HandlerField.GetValue(null);
            s_HandlerField.SetValue(null, Handler);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                Handler?.StopAll(0f);
                Handler?.Internal_Shutdown();
            }
            finally
            {
                s_HandlerField.SetValue(null, _previousHandler);
            }
        }

        private static AudioGroupConfig CreateGroup(EAudioTrack track)
        {
            var config = new AudioGroupConfig
            {
                // internal setter（InternalsVisibleTo）
                AudioTrack = track,
            };

            SetPrivateField(config, "m_MaxChannel", 8);
            SetPrivateField(config, "m_CanExpand", true);
            SetPrivateField(config, "m_DefaultVolume", 1f);
            return config;
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name}.{name} 字段应存在");
            field.SetValue(target, value);
        }
    }
}
