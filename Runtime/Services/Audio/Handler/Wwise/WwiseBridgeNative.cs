#if WWISE_INSTALLED
using UnityEngine;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Wwise
{
    /// <summary>
    /// 真实 Wwise 桥接。需导入 Wwise Unity 插件并定义 <c>WWISE_INSTALLED</c>。
    /// <para>约定：事件路径用 Wwise 事件名（如 Sfx/Hit）；总线 RTPC/Volume 用 bus:/ 前缀映射。</para>
    /// </summary>
    public sealed class WwiseBridgeNative : IAudioMiddlewareBridge
    {
        private readonly System.Collections.Generic.Dictionary<ulong, uint> _handleToPlayingId =
            new System.Collections.Generic.Dictionary<ulong, uint>(64);
        private ulong _nextHandle = 1UL;
        // 常驻发声体：避免每次 Play 分配临时 GameObject（原实现 Destroy(go, 30f) 会截断长音）
        private GameObject _emitter;

        public bool Initialize(Transform instanceRoot)
        {
            // AkInitializer 由 Wwise 场景对象驱动；此处确保 SoundEngine 可用
            if (!AkSoundEngine.IsInitialized()) return false;

            _emitter = new GameObject("[WwiseBridgeEmitter]");
            if (instanceRoot != null)
            {
                _emitter.transform.SetParent(instanceRoot, false);
            }

            UnityEngine.Object.DontDestroyOnLoad(_emitter);
            return true;
        }

        public void Shutdown()
        {
            AkSoundEngine.ClearBanks();

            if (_emitter != null)
            {
                UnityEngine.Object.Destroy(_emitter);
                _emitter = null;
            }

            _handleToPlayingId.Clear();
        }

        public void Update(float unscaledDeltaTime)
        {
            // AkTerminator / AkInitializer 自身 Tick
        }

        public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
        {
            if (string.IsNullOrEmpty(eventPath) || _emitter == null) return 0UL;

            // 单发声体复用：位置在 PostEvent 时采样；如需持续跟随需配合 RTPC/骨骼挂点
            if (position3D.HasValue)
            {
                _emitter.transform.position = position3D.Value;
            }

            uint playingId = AkSoundEngine.PostEvent(eventPath, _emitter);
            if (playingId == AkSoundEngine.AK_INVALID_PLAYING_ID) return 0UL;

            ulong handle = _nextHandle++;
            _handleToPlayingId[handle] = playingId;

            AkSoundEngine.SetRTPCValue("Volume", Mathf.Clamp01(volume), playingId);
            if (Mathf.Abs(pitch - 1f) > 0.001f)
            {
                AkSoundEngine.SetRTPCValue("Pitch", pitch, playingId);
            }

            return handle;
        }

        public void StopInstance(ulong instanceId, bool immediate)
        {
            if (!_handleToPlayingId.TryGetValue(instanceId, out var playingId)) return;

            // 非立即 = 短衰减停止（原先误映射为 Pause，会留下无法恢复也无法停止的悬挂声）
            AkSoundEngine.ExecuteActionOnPlayingId(
                AkActionOnEventType.AkActionOnEventType_Stop,
                playingId,
                immediate ? 0u : 250u,
                AkCurveInterpolation.AkCurveInterpolation_Linear);
            _handleToPlayingId.Remove(instanceId);
        }

        public void SetPaused(ulong instanceId, bool paused)
        {
            if (!_handleToPlayingId.TryGetValue(instanceId, out var playingId)) return;
            AkSoundEngine.ExecuteActionOnPlayingId(
                paused ? AkActionOnEventType.AkActionOnEventType_Pause : AkActionOnEventType.AkActionOnEventType_Resume,
                playingId);
        }

        public void SetInstanceVolume(ulong instanceId, float volume)
        {
            if (!_handleToPlayingId.TryGetValue(instanceId, out var playingId)) return;
            AkSoundEngine.SetRTPCValue("Volume", Mathf.Clamp01(volume), playingId);
        }

        public void SetBusVolume(string busPath, float volume)
        {
            // bus:/Music → RTPC "MusicVolume"
            string rtpc = BusPathToRtpc(busPath);
            AkSoundEngine.SetRTPCValue(rtpc, Mathf.Clamp01(volume));
        }

        public float GetBusVolume(string busPath)
        {
            string rtpc = BusPathToRtpc(busPath);
            AkSoundEngine.GetRTPCValue(rtpc, gameObject: null, playingID: AkSoundEngine.AK_INVALID_PLAYING_ID,
                out float value, out _, out _);
            return value;
        }

        private static string BusPathToRtpc(string busPath)
        {
            if (string.IsNullOrEmpty(busPath)) return "MasterVolume";
            // bus:/Master → MasterVolume
            string name = busPath.StartsWith("bus:/") ? busPath.Substring(5) : busPath;
            return name + "Volume";
        }

        public bool IsPlaying(ulong instanceId)
        {
            if (!_handleToPlayingId.TryGetValue(instanceId, out var playingId)) return false;
            return AkSoundEngine.GetSourcePlayPosition(playingId, out _, false) == AKRESULT.AK_Success;
        }

        public string GetEventPathFromClip(AudioClip clip)
            => clip == null || string.IsNullOrEmpty(clip.name) ? null : clip.name;
    }
}
#endif
