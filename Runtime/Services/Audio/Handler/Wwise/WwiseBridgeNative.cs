#if WWISE_INSTALLED
using System.Collections.Generic;
using UnityEngine;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Wwise
{
    /// <summary>
    /// 真实 Wwise 桥接。需导入 Wwise Unity 插件并定义 <c>WWISE_INSTALLED</c>。
    /// <para>约定：事件路径用 Wwise 事件名（如 Sfx/Hit）；总线 RTPC/Volume 用 bus:/ 前缀映射。</para>
    /// <para>3D 发声体：按实例租用池化 GameObject（Wwise 持续跟发射体位置，单发射体会让并发 3D 串位）。
    /// 位置在 PostEvent 时固定；持续跟随需业务侧自行挂点/驱动位置。</para>
    /// </summary>
    public sealed class WwiseBridgeNative : IAudioMiddlewareBridge
    {
        /// <summary>非立即停止时使用的短淡出（毫秒）。Middleware 层通常先做音量 Fade 再 immediate Stop。</summary>
        private const uint NON_IMMEDIATE_STOP_MS = 250u;

        private const int MAX_IDLE_EMITTERS = 32;

        private readonly Dictionary<ulong, uint> _handleToPlayingId = new Dictionary<ulong, uint>(64);
        private readonly Dictionary<ulong, GameObject> _handleToEmitter = new Dictionary<ulong, GameObject>(64);
        private readonly Stack<GameObject> _idleEmitters = new Stack<GameObject>(8);
        private readonly List<ulong> _finishedScratch = new List<ulong>(8);
        private readonly HashSet<ulong> _pausedInstances = new HashSet<ulong>();
        private ulong _nextHandle = 1UL;
        private Transform _emitterRoot;

        public bool Initialize(Transform instanceRoot)
        {
            // AkInitializer 由 Wwise 场景对象驱动；此处确保 SoundEngine 可用
            if (!AkSoundEngine.IsInitialized()) return false;

            // 池根必须是根物体才能 DontDestroyOnLoad，因此不挂到 instanceRoot
            _emitterRoot = new GameObject("[WwiseBridgeEmitters]").transform;
            Object.DontDestroyOnLoad(_emitterRoot.gameObject);
            return true;
        }

        public void Shutdown()
        {
            AkSoundEngine.ClearBanks();

            foreach (var emitter in _idleEmitters)
            {
                if (emitter != null) Object.Destroy(emitter);
            }

            _idleEmitters.Clear();

            foreach (var emitter in _handleToEmitter.Values)
            {
                if (emitter != null) Object.Destroy(emitter);
            }

            _handleToEmitter.Clear();
            _handleToPlayingId.Clear();
            _pausedInstances.Clear();

            if (_emitterRoot != null)
            {
                Object.Destroy(_emitterRoot.gameObject);
                _emitterRoot = null;
            }
        }

        public void Update(float unscaledDeltaTime)
        {
            ReclaimFinishedInstances();
        }

        public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
        {
            if (string.IsNullOrEmpty(eventPath) || _emitterRoot == null) return 0UL;

            GameObject emitter = RentEmitter(position3D);
            uint playingId = AkSoundEngine.PostEvent(eventPath, emitter);
            if (playingId == AkSoundEngine.AK_INVALID_PLAYING_ID)
            {
                ReturnEmitter(emitter);
                return 0UL;
            }

            ulong handle = _nextHandle++;
            _handleToPlayingId[handle] = playingId;
            _handleToEmitter[handle] = emitter;

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
                immediate ? 0u : NON_IMMEDIATE_STOP_MS,
                AkCurveInterpolation.AkCurveInterpolation_Linear);
            UnregisterInstance(instanceId);
        }

        public void SetPaused(ulong instanceId, bool paused)
        {
            if (!_handleToPlayingId.TryGetValue(instanceId, out var playingId)) return;
            if (paused) _pausedInstances.Add(instanceId);
            else _pausedInstances.Remove(instanceId);
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

        private GameObject RentEmitter(Vector3? position3D)
        {
            GameObject emitter = null;
            while (_idleEmitters.Count > 0)
            {
                emitter = _idleEmitters.Pop();
                if (emitter != null) break;
            }

            if (emitter == null)
            {
                emitter = new GameObject("[WwiseEmitter]");
                emitter.transform.SetParent(_emitterRoot, false);
            }

            emitter.SetActive(true);
            if (position3D.HasValue)
            {
                emitter.transform.position = position3D.Value;
            }

            return emitter;
        }

        private void ReturnEmitter(GameObject emitter)
        {
            if (emitter == null) return;

            emitter.SetActive(false);
            if (_emitterRoot != null)
            {
                emitter.transform.SetParent(_emitterRoot, false);
            }

            if (_idleEmitters.Count < MAX_IDLE_EMITTERS)
            {
                _idleEmitters.Push(emitter);
            }
            else
            {
                Object.Destroy(emitter);
            }
        }

        private void UnregisterInstance(ulong instanceId)
        {
            _handleToPlayingId.Remove(instanceId);
            _pausedInstances.Remove(instanceId);
            if (_handleToEmitter.TryGetValue(instanceId, out var emitter))
            {
                _handleToEmitter.Remove(instanceId);
                ReturnEmitter(emitter);
            }
        }

        /// <summary>
        /// 自然播完的 oneshot 不会走到 StopInstance：回收发射体并清映射，避免字典与池无界增长。
        /// </summary>
        private void ReclaimFinishedInstances()
        {
            if (_handleToPlayingId.Count == 0) return;

            _finishedScratch.Clear();
            foreach (var kv in _handleToPlayingId)
            {
                // 暂停中的实例 GetSourcePlayPosition 可能失败，不得当作播完回收
                if (_pausedInstances.Contains(kv.Key)) continue;
                if (AkSoundEngine.GetSourcePlayPosition(kv.Value, out _, false) != AKRESULT.AK_Success)
                {
                    _finishedScratch.Add(kv.Key);
                }
            }

            for (int i = 0; i < _finishedScratch.Count; i++)
            {
                UnregisterInstance(_finishedScratch[i]);
            }
        }
    }
}
#endif
