#if FMOD_INSTALLED
using System;
using System.Collections.Generic;
using UnityEngine;
using Moirai.Atropos.Audio.Middleware;

namespace Moirai.Atropos.Audio.Fmod
{
    /// <summary>
    /// 真实 FMOD.Studio 桥接。需导入 FMOD Unity 插件并定义 <c>FMOD_INSTALLED</c>。
    /// <para>能力接口：<see cref="IAudioMiddlewareBankControl"/>（Studio bank）与 <see cref="IAudioMiddlewareRtpcControl"/>（event parameter）。</para>
    /// </summary>
    /// <remarks>
    /// 本文件可能在无 FMOD SDK 的机器上审阅/合并，无法本地编译核对；整文件受 <c>FMOD_INSTALLED</c> 编译保护，
    /// 调用的标准 FMOD Unity API 为 <c>RuntimeManager.LoadBank</c> / <c>StudioSystem.loadBankFile</c> /
    /// <c>Bank.unload</c> / <c>setParameterByName</c>。
    /// </remarks>
    internal sealed class FmodBridgeNative : IAudioMiddlewareBridge, IAudioMiddlewareBankControl, IAudioMiddlewareRtpcControl
    {
        /// <summary>bankPath → 原生 Bank 句柄（键存在即已加载：幂等门兼卸载用句柄）。</summary>
        private readonly Dictionary<string, FMOD.Studio.Bank> _bankHandles =
            new Dictionary<string, FMOD.Studio.Bank>(StringComparer.Ordinal);

        public bool Initialize(Transform instanceRoot)
        {
            FMODUnity.RuntimeManager.Init();
            return true;
        }

        public void Shutdown()
        {
            // 先卸本桥跟踪到的 bank，再 flush：避免 Studio 侧悬挂已记账的库
            foreach (var kv in _bankHandles)
            {
                if (kv.Value.isValid()) kv.Value.unload();
            }

            _bankHandles.Clear();
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

        #region 声音库与实时参数 [BANK / RTPC]

        /// <inheritdoc />
        /// <remarks>
        /// 形态像文件路径时走 <c>StudioSystem.loadBankFile</c>；短名走 <c>RuntimeManager.LoadBank</c>（StreamingAssets，
        /// <c>loadSamples: true</c>——与 PlayEvent 即发即用对齐，否则 sample 未载入时首播会静默失败）。
        /// 幂等命中返回 <see cref="EAudioBankLoadResult.AlreadyLoaded"/>（本桥记账命中，或 SDK 报
        /// <c>ERR_ALREADY_LOADED</c>）；<c>ERR_ALREADY_LOADED</c> 之外的一切失败归为
        /// <see cref="EAudioBankLoadResult.Failed"/>。
        /// </remarks>
        public EAudioBankLoadResult LoadBank(string bankPath)
        {
            if (string.IsNullOrEmpty(bankPath)) return EAudioBankLoadResult.Failed;
            if (_bankHandles.ContainsKey(bankPath)) return EAudioBankLoadResult.AlreadyLoaded;

            var result = FMOD.RESULT.OK;
            FMOD.Studio.Bank bank;
            if (IsPathLike(bankPath))
            {
                result = FMODUnity.RuntimeManager.StudioSystem.loadBankFile(
                    bankPath, FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out bank);
            }
            else
            {
                // loadSamples: true 与即发即用模型对齐；旧写法 false 会在首播时因 sample 未载入而无声
                bank = FMODUnity.RuntimeManager.LoadBank(bankPath, true);
            }

            // 短名分支的 result 恒为 OK，判定等价于只看句柄；文件分支则要求返回码也干净
            if (result == FMOD.RESULT.OK && bank.isValid())
            {
                _bankHandles[bankPath] = bank;
                return EAudioBankLoadResult.Loaded;
            }

            return result == FMOD.RESULT.ERR_ALREADY_LOADED
                ? EAudioBankLoadResult.AlreadyLoaded
                : EAudioBankLoadResult.Failed;
        }

        /// <inheritdoc />
        /// <remarks>未加载或 <c>Bank.unload</c> 失败返回 false；卸载失败时保留记账以便重试。</remarks>
        public bool UnloadBank(string bankPath)
        {
            if (string.IsNullOrEmpty(bankPath) || !_bankHandles.TryGetValue(bankPath, out var bank)) return false;

            if (!bank.isValid())
            {
                _bankHandles.Remove(bankPath);
                return false;
            }

            if (bank.unload() != FMOD.RESULT.OK) return false;

            _bankHandles.Remove(bankPath);
            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// <paramref name="instanceId"/> 非 0 时作用于该 EventInstance 的 event parameter；
        /// 为 0 时写 Studio 全局参数。<paramref name="name"/> 是 FMOD 参数名（不是事件路径）。
        /// </remarks>
        public void SetRtpc(string name, float value, ulong instanceId)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (instanceId != 0UL)
            {
                var instance = new FMOD.Studio.EventInstance((IntPtr)instanceId);
                if (instance.isValid()) instance.setParameterByName(name, value);
                return;
            }

            FMODUnity.RuntimeManager.StudioSystem.setParameterByName(name, value);
        }

        /// <summary>是否为文件路径形态（含目录分隔符或 .bank 后缀）；否则按 StreamingAssets 短名处理。</summary>
        private static bool IsPathLike(string bankPath)
            => bankPath.IndexOf('/') >= 0
               || bankPath.IndexOf('\\') >= 0
               || bankPath.EndsWith(".bank", StringComparison.OrdinalIgnoreCase);

        #endregion 声音库与实时参数 [BANK / RTPC]
    }
}
#endif
