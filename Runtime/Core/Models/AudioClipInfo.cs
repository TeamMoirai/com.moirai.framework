using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 提供音频剪辑的 JSON 兼容引用
    /// </summary>
    [Serializable]
    public class AudioClipInfo : IMatchComparable<AudioClipInfo>
    {
        [Tooltip("资源路径")]
        [SerializeField] private string m_Path;
        [Tooltip("资源GUID")]
        [SerializeField] private string m_Guid;

        public string Path => m_Path;

        /// <summary>
        /// 克隆此对象
        /// </summary>
        /// <returns></returns>
        public AudioClipInfo Clone()
        {
            AudioClipInfo result = new AudioClipInfo();

            result.m_Path = m_Path;
            result.m_Guid = m_Guid;

            return result;
        }
        
        public bool Matches(AudioClipInfo other)
        {
            return m_Path == other?.m_Path
                   && m_Guid == other?.m_Guid
                ;
        }
    }
}