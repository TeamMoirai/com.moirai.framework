using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Core.SettingPersistence;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>音量设置的写入点只写键、一次都不落盘：SetSettings 与 RemoveSetting 各写完后由设置界面统一 flush。</summary>
    /// <remarks>
    /// 后端与轨级配置经 <see cref="AudioSettingsPolicy"/> 写键，滑杆与静音开关这类连续量路径连键都不写。
    /// 面板里预览过又放弃的音量不该已经写进档，所以 flush 归设置界面那一次确认。判据用只记数的
    /// <see cref="RecordingSettingHandler"/>，因此不碰 persistentDataPath；本量具只编进 Tests.EditorMode，
    /// 这两格也就一律不往 PlayMode 搬。
    /// </remarks>
    [TestFixture]
    public sealed class AudioSettingsPersistenceTests
    {
        [Test]
        public void SetSettingsWritesMasterKeysWithoutFlushing()
        {
            var listenerVolumeBefore = AudioListener.volume;
            var previousSetting = SettingUtility.Internal_PeekHandler();
            var previousAudio = AudioService.Internal_PeekHandler();
            var recording = new RecordingSettingHandler();
            var handler = new UnityAudioHandler();
            SettingUtility.Internal_UseHandler(recording);
            AudioService.Internal_UseHandler(handler);
            try
            {
                AudioService.SetSettings();

                Assert.IsTrue(recording.TryGetStored(GameConstant.Setting.AUDIO_MASTER_VOLUME, out var rawVolume),
                    "主音量键要写进配置项，等设置界面那一次 flush");
                Assert.IsTrue(float.TryParse(rawVolume, out _), "存的是可解析的线性音量值");
                Assert.IsTrue(recording.TryGetStored(GameConstant.Setting.AUDIO_MASTER_MUTED, out _));
                Assert.AreEqual(0, recording.SaveCount, "写入入口一次都不落盘：默认落盘会让预览过又放弃的改动回不去");
            }
            finally
            {
                AudioService.Internal_UseHandler(previousAudio);
                SettingUtility.Internal_UseHandler(previousSetting);
                AudioListener.volume = listenerVolumeBefore;
            }
        }

        [Test]
        public void RemoveSettingClearsMasterKeysWithoutFlushing()
        {
            var listenerVolumeBefore = AudioListener.volume;
            var previousSetting = SettingUtility.Internal_PeekHandler();
            var previousAudio = AudioService.Internal_PeekHandler();
            var recording = new RecordingSettingHandler();
            var handler = new UnityAudioHandler();
            SettingUtility.Internal_UseHandler(recording);
            AudioService.Internal_UseHandler(handler);
            try
            {
                AudioService.SetSettings();
                Assert.IsTrue(recording.TryGetStored(GameConstant.Setting.AUDIO_MASTER_VOLUME, out _),
                    "先确认写入发生过，下面的清零才是被抹掉的");

                AudioService.RemoveSetting();

                Assert.AreEqual(0, recording.SaveCount, "清空设置同样只改配置项，落盘归设置界面");
                Assert.IsFalse(recording.TryGetStored(GameConstant.Setting.AUDIO_MASTER_VOLUME, out _),
                    "移除要连键一起摘掉，下次启动才回到缺省而不是旧值");
                Assert.IsFalse(recording.TryGetStored(GameConstant.Setting.AUDIO_MASTER_MUTED, out _));
            }
            finally
            {
                AudioService.Internal_UseHandler(previousAudio);
                SettingUtility.Internal_UseHandler(previousSetting);
                AudioListener.volume = listenerVolumeBefore;
            }
        }
    }
}
