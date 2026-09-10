using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频轨道（类别）。管理一组可复用的 <see cref="AudioAgent"/> 通道。
    /// <para>取通道顺序：空闲 → 硬上限内扩展 → 按优先级 Voice Stealing。</para>
    /// <para>Unity Priority 语义：0 最高，255 最低；仅抢占「不更重要」的非持久音。</para>
    /// </summary>
    [Serializable]
    public class AudioCategory
    {
        private readonly AudioServiceHandler _handler;
        private readonly AudioGroupConfig _audioGroupConfig;
        private int _maxChannel;

        /// <summary>
        /// 扩展硬上限，防止 CanExpand 时通道无界增长。
        /// </summary>
        public const int HARD_CHANNEL_CAP = 32;

        /// <summary>
        /// 所属音频处理器（Agent 绑定用，勿用全局 AudioService.Handler）。
        /// </summary>
        internal AudioServiceHandler Handler => _handler;

        #region 构造函数 [CONSTRUCTORS]

        /// <summary>
        /// 音频轨道构造函数。
        /// </summary>
        /// <param name="handler">音频处理器</param>
        /// <param name="audioGroupConfig">音频轨道组配置。</param>
        public AudioCategory(AudioServiceHandler handler, AudioGroupConfig audioGroupConfig)
        {
            _handler = handler;
            _audioGroupConfig = audioGroupConfig;
            _maxChannel = audioGroupConfig.MaxChannel;

            AudioAgents = new List<AudioAgent>(_maxChannel);
            InstanceRoot = new GameObject(StringUtility.Format("Audio Category - {0}", audioGroupConfig.AudioMixerGroup.name)).transform;
            InstanceRoot.SetParent(handler.InstanceRoot);
            for (int index = 0; index < _maxChannel; index++)
            {
                AudioAgent audioAgent = new AudioAgent();
                audioAgent.Init(this, index);
                AudioAgents.Add(audioAgent);
            }
        }

        #endregion

        #region 公共属性 [PUBLIC PROPERTIES]

        /// <summary>
        /// 对应的音轨。
        /// </summary>
        public EAudioTrack AudioTrack => _audioGroupConfig.AudioTrack;

        /// <summary>
        /// 下属所有的音频代理。
        /// </summary>
        public List<AudioAgent> AudioAgents { get; private set; }

        /// <summary>
        /// 音频混响器。
        /// </summary>
        public AudioMixer AudioMixer => AudioMixerGroup != null ? AudioMixerGroup.audioMixer : null;

        /// <summary>
        /// 音频混响器组。
        /// </summary>
        public AudioMixerGroup AudioMixerGroup => _audioGroupConfig.AudioMixerGroup;

        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot { get; private set; }

        /// <summary>
        /// 当前通道数。
        /// </summary>
        public int ChannelCount => AudioAgents.Count;

        #endregion

        #region 服务方法 [SERVICE METHOD]

        /// <summary>
        /// 音频轨道轮询——仅遍历非空闲代理。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        public void Update(float elapseSeconds)
        {
            var agents = AudioAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.IsFree) continue;
                agent.Update(elapseSeconds);
            }
        }

        #endregion 服务方法 [SERVICE METHOD]

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 增加音频通道槽位（惰性创建 Agent）。
        /// </summary>
        /// <param name="num">增量。</param>
        public void AddAudio(int num)
        {
            _maxChannel += num;
            for (int i = 0; i < num; i++)
            {
                AudioAgents.Add(null);
            }
        }

        /// <summary>
        /// 获取可用音频代理。
        /// </summary>
        /// <param name="doNotAutoRecycleIfNotDonePlaying">是否不抢占正在播放的音频。</param>
        /// <param name="priority">请求优先级（0 最高，255 最低）。</param>
        /// <param name="persistent">请求是否为持久音（持久音不被非持久请求抢占）。</param>
        /// <returns>可用代理；无法分配时返回 null。</returns>
        public AudioAgent GetAvailableAgent(bool doNotAutoRecycleIfNotDonePlaying, int priority = 128, bool persistent = false)
        {
            var agents = AudioAgents;
            int freeChannel = -1;
            int stealChannel = -1;
            float stealDuration = -1f;
            int stealPriority = int.MinValue;

            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.IsFree)
                {
                    freeChannel = i;
                    break;
                }

                if (doNotAutoRecycleIfNotDonePlaying) continue;

                // Voice Stealing：不抢更优先（数值更小）或持久音
                if (agent.IsPersistent && !persistent) continue;
                if (agent.Priority < priority) continue;

                // 同优先级取最久；更高优先级数字（更不重要）优先于更短但更重要的
                bool better =
                    agent.Priority > stealPriority ||
                    (agent.Priority == stealPriority && agent.Duration > stealDuration);

                if (better)
                {
                    stealPriority = agent.Priority;
                    stealDuration = agent.Duration;
                    stealChannel = i;
                }
            }

            int selected = freeChannel >= 0 ? freeChannel : stealChannel;

            if (selected < 0 && _audioGroupConfig.CanExpand && agents.Count < HARD_CHANNEL_CAP)
            {
                selected = agents.Count;
                agents.Add(null);
                _maxChannel = agents.Count;
            }

            if (selected < 0)
            {
                LogUtility.Error("Here is no channel to play audio {0}", AudioMixerGroup != null ? AudioMixerGroup.name : AudioTrack.ToString());
                return null;
            }

            var result = agents[selected];
            if (result == null)
            {
                result = new AudioAgent();
                result.Init(this, selected);
                agents[selected] = result;
            }
            else if (selected != freeChannel)
            {
                // 抢占：立即停掉被选中的在播音，确保句柄释放
                result.Stop(0f);
            }

            return result;
        }

        /// <summary>
        /// 写入配置。
        /// </summary>
        public void SetSettings() => _audioGroupConfig.SetSettings();

        /// <summary>
        /// 加载配置。
        /// </summary>
        public void LoadSettings() => _audioGroupConfig.LoadSettings();

        /// <summary>
        /// 移除设置。
        /// </summary>
        public void RemoveSetting() => _audioGroupConfig.RemoveSetting();

        #endregion

        #region 音频控制 [AUDIO CONTROLS]

        /// <summary>
        /// 暂停当前音轨下的所有音频。
        /// </summary>
        public void PauseAll()
        {
            var agents = AudioAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                agents[i]?.Pause();
            }
        }

        /// <summary>
        /// 恢复当前音轨下的所有音频。
        /// </summary>
        public void UnpauseAll()
        {
            var agents = AudioAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                agents[i]?.Unpause();
            }
        }

        /// <summary>
        /// 停止当前音轨下的所有音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAll(float fadeoutDuration = 0f)
        {
            var agents = AudioAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                agents[i]?.Stop(fadeoutDuration);
            }
        }

        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAllButPersistent(float fadeoutDuration = 0f)
        {
            var agents = AudioAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && !agent.IsPersistent)
                {
                    agent.Stop(fadeoutDuration);
                }
            }
        }

        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAllLooping(float fadeoutDuration = 0f)
        {
            var agents = AudioAgents;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent != null && agent.IsPlaying && agent.IsLoop)
                {
                    agent.Stop(fadeoutDuration);
                }
            }
        }

        #endregion 音频控制 [AUDIO CONTROLS]
    }
}
