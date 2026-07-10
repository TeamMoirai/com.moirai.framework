using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频轨道（类别）。
    /// </summary>
    [Serializable]
    public class AudioCategory
    {
        private readonly AudioGroupConfig _audioGroupConfig;
        private int _maxChannel;

        #region 构造函数 [CONSTRUCTORS]

        /// <summary>
        /// 音频轨道构造函数。
        /// </summary>
        /// <param name="audioGroupConfig">音频轨道组配置。</param>
        public AudioCategory(AudioGroupConfig audioGroupConfig)
        {
            _audioGroupConfig = audioGroupConfig;
            _maxChannel = audioGroupConfig.MaxChannel;
            
            AudioAgents = new List<AudioAgent>(_maxChannel);
            InstanceRoot = new GameObject(StringUtility.Format("Audio Category - {0}", audioGroupConfig.AudioMixerGroup.name)).transform;
            InstanceRoot.SetParent(GameModule.Audio.InstanceRoot);
            for (int index = 0; index < _maxChannel; index++)
            {
                AudioAgent audioAgent = new AudioAgent();
                audioAgent.Init(this, index);
                AudioAgents.Add(audioAgent);
            }
        }

        #endregion
        
        #region 公共属性 [PUBLIC PROPRETIES]

        /// <summary>
        /// 对应的音轨。
        /// </summary>
        public AudioTrack AudioTrack => _audioGroupConfig.AudioTrack;
        
        /// <summary>
        /// 下属所有的音频代理。
        /// </summary>
        public List<AudioAgent> AudioAgents { get; private set; }

        /// <summary>
        /// 音频混响器。
        /// </summary>
        public AudioMixer AudioMixer => AudioMixerGroup.audioMixer;
        
        /// <summary>
        /// 音频混响器组。
        /// </summary>
        public AudioMixerGroup AudioMixerGroup => _audioGroupConfig.AudioMixerGroup;
        
        /// <summary>
        /// 实例化根节点。
        /// </summary>
        public Transform InstanceRoot { get; private set; }
        
        #endregion
        
        #region 模块方法 [MODULE METHOD]
        
        /// <summary>
        /// 音频轨道轮询。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        public void Update(float elapseSeconds)
        {
            foreach (var agent in AudioAgents)
            {
                agent?.Update(elapseSeconds);
            }
        }
        
        #endregion 模块方法 [MODULE METHOD]
        
        #region 公共方法 [PUBLIC METHODS]
        
        /// <summary>
        /// 增加音频。
        /// </summary>
        /// <param name="num"></param>
        public void AddAudio(int num)
        {
            _maxChannel += num;
            for (int i = 0; i < num; i++)
            {
                AudioAgents.Add(null);
            }
        }

        /// <summary>
        /// 播放音频。
        /// </summary>
        /// <param name="doNotAutoRecycleIfNotDonePlaying">是否不回收正在播放的音频，<c>false</c>如果池不可扩展，则会回收掉播放时间最久的 agent</param>
        /// <returns></returns>
        public AudioAgent GetAvailableAgent(bool doNotAutoRecycleIfNotDonePlaying)
        {
            int freeChannel = -1;
            float duration = -1;

            for (int i = 0; i < AudioAgents.Count; i++)
            {
                if (AudioAgents[i].IsFree)
                {
                    freeChannel = i;
                    break;
                }
                
                else if (!doNotAutoRecycleIfNotDonePlaying && AudioAgents[i].Duration > duration)
                {
                    duration = AudioAgents[i].Duration;
                    freeChannel = i;
                }
            }
            
            if (freeChannel == -1 && _audioGroupConfig.CanExpand)
            {
                AudioAgents.Add(null);
                freeChannel = _maxChannel;
                _maxChannel += 1;
            }

            if (freeChannel >= 0)
            {
                if (AudioAgents[freeChannel] == null)
                {
                    AudioAgent audioAgent = new AudioAgent();
                    audioAgent.Init(this, freeChannel);
                    
                    AudioAgents[freeChannel] = audioAgent;
                }
                
                return AudioAgents[freeChannel];
            }
            else
            {
                Log.Error($"Here is no channel to play audio {AudioMixerGroup.name}");
                return null;
            }
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
        /// 暂停当前音轨下的所有音频
        /// </summary>
        public void PauseAll()
        {
            foreach (var agent in AudioAgents)
            {
                agent?.Pause();
            }
        }

        /// <summary>
        /// 恢复当前音轨下的所有音频
        /// </summary>
        public void UnPauseAll()
        {
            foreach (var agent in AudioAgents)
            {
                agent?.UnPause();
            }
        }
        
        /// <summary>
        /// 停止当前音轨下的所有音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAll(float fadeoutDuration = 0f)
        {
            foreach (var agent in AudioAgents)
            {
                agent?.Stop(fadeoutDuration);
            }
        }
        
        /// <summary>
        /// 停止除持久性音频之外的所有音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAllButPersistent(float fadeoutDuration = 0f)
        {
            foreach (var agent in AudioAgents
                         .Where(agent => agent is { IsPersistent: false }))
            {
                agent.Stop(fadeoutDuration);
            }
        }
        
        /// <summary>
        /// 停止所有循环音频。
        /// </summary>
        /// <param name="fadeoutDuration">音频淡出持续时间。</param>
        public void StopAllLooping(float fadeoutDuration = 0f)
        {
            foreach (var agent in AudioAgents
                         .Where(agent => agent is { IsPlaying: true, IsLoop: true }))
            {
                agent.Stop(fadeoutDuration);
            }
        }
        
        #endregion 音频控制 [AUDIO CONTROLS]
        
    }
}