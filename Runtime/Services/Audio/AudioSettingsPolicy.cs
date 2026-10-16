using UnityEngine;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 音频设置政策：设置键名的拼装、默认值与音量值域钳制在此定义一次，后端与轨级配置都从这里取。
    /// </summary>
    /// <remarks>
    /// 后端只负责把设置落到自己的引擎对象上，不再自己拼键、也不再各自决定缺省值与钳制口径；
    /// 轨级音量缺省按调用方传入（<see cref="AudioGroupConfig"/> 用序列化的 <c>m_DefaultVolume</c>，
    /// 后端装载全部轨时用 <see cref="DefaultVolume"/>），避免把可配的每组缺省抹平成常量。
    /// </remarks>
    internal static class AudioSettingsPolicy
    {
        /// <summary>未写入过时的音量缺省。</summary>
        internal const float DefaultVolume = 1f;

        /// <summary>未写入过时的静音缺省。</summary>
        internal const bool DefaultMuted = false;

        /// <summary>音量值域钳制：存量值可能写于上限还是 10 的年代，读回来一律落回契约值域。</summary>
        internal static float NormalizeVolume(float volume) => Mathf.Clamp01(volume);

        /// <summary>主音量键。</summary>
        internal static string MasterVolumeKey => GameConstant.Setting.AUDIO_MASTER_VOLUME;

        /// <summary>主音量静音键。</summary>
        internal static string MasterMutedKey => GameConstant.Setting.AUDIO_MASTER_MUTED;

        /// <summary>指定音轨的音量键。</summary>
        /// <param name="track">音轨。</param>
        internal static string TrackVolumeKey(EAudioTrack track) =>
            StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_VOLUME, track);

        /// <summary>指定音轨的静音键。</summary>
        /// <param name="track">音轨。</param>
        internal static string TrackMutedKey(EAudioTrack track) =>
            StringUtility.Format(GameConstant.Setting.AUDIO_GROUP_MUTED, track);

        /// <summary>写音量（自动钳到契约值域）。</summary>
        /// <param name="key">设置键（取本类的键成员）。</param>
        /// <param name="volume">音量。</param>
        internal static void SaveVolume(string key, float volume) => SettingUtility.SetFloat(key, NormalizeVolume(volume));

        /// <summary>读音量并按 <paramref name="defaultVolume"/> 回落。</summary>
        /// <param name="key">设置键（取本类的键成员）。</param>
        /// <param name="defaultVolume">未写入过时的缺省音量。</param>
        internal static float LoadVolume(string key, float defaultVolume) =>
            NormalizeVolume(SettingUtility.GetFloat(key, defaultVolume));

        /// <summary>写静音标记。</summary>
        /// <param name="key">设置键（取本类的键成员）。</param>
        /// <param name="muted">是否静音。</param>
        internal static void SaveMuted(string key, bool muted) => SettingUtility.SetBool(key, muted);

        /// <summary>读静音标记，未写入过时为不静音。</summary>
        /// <param name="key">设置键（取本类的键成员）。</param>
        internal static bool LoadMuted(string key) => SettingUtility.GetBool(key, DefaultMuted);

        /// <summary>移除一对设置（静音与音量），移除即回到各自的缺省。</summary>
        /// <param name="mutedKey">静音设置键。</param>
        /// <param name="volumeKey">音量设置键。</param>
        internal static void RemovePair(string mutedKey, string volumeKey)
        {
            SettingUtility.RemoveSetting(mutedKey);
            SettingUtility.RemoveSetting(volumeKey);
        }
    }
}
