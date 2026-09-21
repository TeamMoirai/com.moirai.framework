using System;
using UnityEngine;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Fmod
{
    /// <summary>
    /// FMOD 桥接桩——无 <c>FMOD_INSTALLED</c> 时可跑通 Handler 生命周期与压测。
    /// </summary>
    internal sealed class FmodBridgeStub : IAudioMiddlewareBridge, IAudioMiddlewareBankControl, IAudioMiddlewareRtpcControl
    {
        private struct StubInstance
        {
            public string Path;
            public float Volume;
            public bool Loop;
            public bool Paused;
            public bool Playing;
        }

        private readonly System.Collections.Generic.Dictionary<ulong, StubInstance> _instances =
            new System.Collections.Generic.Dictionary<ulong, StubInstance>(64);
        private readonly System.Collections.Generic.Dictionary<string, float> _busVolumes =
            new System.Collections.Generic.Dictionary<string, float>(8);
        private ulong _nextId = 1UL;

        public int PlayCount { get; private set; }

        public bool Initialize(Transform instanceRoot)
        {
            _busVolumes["bus:/Master"] = 1f;
            
            var values = (EAudioTrack[])Enum.GetValues(typeof(EAudioTrack));
            for (int i = 0; i < values.Length; i++)
            {
                _busVolumes["bus:/" + values[i]] = 1f;
            }
            
            return true;
        }

        public void Shutdown()
        {
            _instances.Clear();
            _busVolumes.Clear();
        }

        public void Update(float unscaledDeltaTime)
        {
        }

        public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
        {
            if (string.IsNullOrEmpty(eventPath)) return 0UL;
            ulong id = _nextId++;
            _instances[id] = new StubInstance
            {
                Path = eventPath,
                Volume = Mathf.Clamp01(volume),
                Loop = loop,
                Playing = true,
            };
            PlayCount++;
            return id;
        }

        public void StopInstance(ulong instanceId, bool immediate)
        {
            if (_instances.TryGetValue(instanceId, out var inst))
            {
                inst.Playing = false;
                _instances[instanceId] = inst;
            }
        }

        public void SetPaused(ulong instanceId, bool paused)
        {
            if (_instances.TryGetValue(instanceId, out var inst))
            {
                inst.Paused = paused;
                _instances[instanceId] = inst;
            }
        }

        public void SetInstanceVolume(ulong instanceId, float volume)
        {
            if (_instances.TryGetValue(instanceId, out var inst))
            {
                inst.Volume = Mathf.Clamp01(volume);
                _instances[instanceId] = inst;
            }
        }

        public void SetBusVolume(string busPath, float volume)
        {
            if (!string.IsNullOrEmpty(busPath)) _busVolumes[busPath] = Mathf.Clamp01(volume);
        }

        public float GetBusVolume(string busPath)
            => _busVolumes.TryGetValue(busPath, out var v) ? v : 0f;

        public bool IsPlaying(ulong instanceId)
            => _instances.TryGetValue(instanceId, out var inst) && inst.Playing && !inst.Paused;

        public string GetEventPathFromClip(AudioClip clip)
            => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;

        // ===== 可选能力：声音库与实时参数（桩用于跑通契约与断言）=====

        /// <summary>已加载的声音库。</summary>
        public readonly System.Collections.Generic.HashSet<string> Banks =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        /// <summary>已设置的参数值；全局作用域按参数名，实例作用域带 "实例ID:" 前缀。</summary>
        public readonly System.Collections.Generic.Dictionary<string, float> RtpcValues =
            new System.Collections.Generic.Dictionary<string, float>(StringComparer.Ordinal);

        public int BankLoadCount { get; private set; }
        public int RtpcCount { get; private set; }

        public bool LoadBank(string bankPath)
        {
            if (string.IsNullOrEmpty(bankPath) || !Banks.Add(bankPath)) return false;
            BankLoadCount++;
            return true;
        }

        public bool UnloadBank(string bankPath)
            => !string.IsNullOrEmpty(bankPath) && Banks.Remove(bankPath);

        public void SetRtpc(string name, float value, ulong instanceId)
        {
            if (string.IsNullOrEmpty(name)) return;
            RtpcValues[instanceId == 0UL ? name : instanceId + ":" + name] = value;
            RtpcCount++;
        }
    }
}
