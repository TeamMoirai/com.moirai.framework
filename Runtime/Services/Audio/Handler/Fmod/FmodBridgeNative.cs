#if FMOD_INSTALLED
using System;
using UnityEngine;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Fmod
{
    /// <summary>
    /// 真实 FMOD.Studio 桥接。需导入 FMOD Unity 插件并定义 <c>FMOD_INSTALLED</c>。
    /// </summary>
    public sealed class FmodBridgeNative : IAudioMiddlewareBridge
    {
        public bool Initialize(Transform instanceRoot)
        {
            FMODUnity.RuntimeManager.Init();
            return true;
        }

        public void Shutdown()
        {
            FMODUnity.RuntimeManager.StudioSystem.flushCommands();
        }

        public void Update(float unscaledDeltaTime)
        {
        }

        public ulong PlayEvent(string eventPath, float volume, float pitch, bool loop, Vector3? position3D)
        {
            if (string.IsNullOrEmpty(eventPath)) return 0UL;

            var instance = FMODUnity.RuntimeManager.CreateInstance(eventPath);
            if (!instance.isValid()) return 0UL;

            instance.setVolume(Mathf.Clamp01(volume));
            instance.setPitch(Mathf.Max(0.01f, pitch));

            if (loop)
            {
                instance.getMode(out var mode);
                instance.setMode(mode | FMOD.MODE.LOOP_NORMAL);
            }

            if (position3D.HasValue)
            {
                var p = position3D.Value;
                instance.set3DAttributes(new FMOD.VECTOR { x = p.x, y = p.y, z = p.z });
            }

            instance.start();
            return (ulong)instance.handle;
        }

        public void StopInstance(ulong instanceId, bool immediate)
        {
            var instance = new FMOD.Studio.EventInstance((IntPtr)instanceId);
            if (!instance.isValid()) return;
            instance.stop(immediate ? FMOD.STOP_MODE.IMMEDIATE : FMOD.STOP_MODE.ALLOWFADEOUT);
            instance.release();
        }

        public void SetPaused(ulong instanceId, bool paused)
        {
            var instance = new FMOD.Studio.EventInstance((IntPtr)instanceId);
            if (instance.isValid()) instance.setPaused(paused);
        }

        public void SetInstanceVolume(ulong instanceId, float volume)
        {
            var instance = new FMOD.Studio.EventInstance((IntPtr)instanceId);
            if (instance.isValid()) instance.setVolume(Mathf.Clamp01(volume));
        }

        public void SetBusVolume(string busPath, float volume)
        {
            if (FMODUnity.RuntimeManager.StudioSystem.getBus(busPath, out var bus) == FMOD.RESULT.OK)
            {
                bus.setVolume(Mathf.Clamp01(volume));
            }
        }

        public float GetBusVolume(string busPath)
        {
            if (FMODUnity.RuntimeManager.StudioSystem.getBus(busPath, out var bus) == FMOD.RESULT.OK)
            {
                bus.getVolume(out var v);
                return v;
            }

            return 0f;
        }

        public bool IsPlaying(ulong instanceId)
        {
            var instance = new FMOD.Studio.EventInstance((IntPtr)instanceId);
            if (!instance.isValid()) return false;
            instance.getPlaybackState(out var state);
            return state == FMOD.Studio.PLAYBACK_STATE.PLAYING ||
                   state == FMOD.Studio.PLAYBACK_STATE.STARTING;
        }

        public string GetEventPathFromClip(AudioClip clip)
            => clip == null || string.IsNullOrEmpty(clip.name) ? null : "event:/" + clip.name;
    }
}
#endif
