using Moirai.Atropos;
using Moirai.Atropos.Audio;
using Core.SettingPersistence;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>音量设置的落盘只在两个确认入口：SetSettings 与 RemoveSetting 各恰一次 flush，逐条键不各刷一次。</summary>
    /// <remarks>
    /// 后端与轨级配置经 <see cref="AudioSettingsPolicy"/> 写键，此前没人补 <c>SettingUtility.Save()</c>，
    /// 文件后端下玩家调好的音量重启即丢。判据用只记数的 <see cref="RecordingSettingHandler"/>，
    /// 因此不碰 persistentDataPath；本量具只编进 Tests.EditorMode，这两格也就一律不往 PlayMode 搬。
    /// </remarks>
    [TestFixture]
    public sealed class AudioSettingsPersistenceTests
    {
        [Test]
        public void SetSettingsWritesMasterKeysAndFlushesOnce()
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
                    "主音量键必须在 flush 之前写进去");
                Assert.IsTrue(float.TryParse(rawVolume, out _), "存的是可解析的线性音量值");
                Assert.IsTrue(recording.TryGetStored(GameConstant.Setting.AUDIO_MASTER_MUTED, out _));
                Assert.AreEqual(1, recording.SaveCount, "确认入口恰好吃一次 flush");
            }
            finally
            {
                AudioService.Internal_UseHandler(previousAudio);
                SettingUtility.Internal_UseHandler(previousSetting);
                AudioListener.volume = listenerVolumeBefore;
            }
        }

        [Test]
        public void RemoveSettingClearsMasterKeysAndFlushesOnce()
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
                Assert.AreEqual(1, recording.SaveCount, "先确认写入发生过，下面的清零才是被抹掉的");
                recording.SaveCount = 0;

                AudioService.RemoveSetting();

                Assert.AreEqual(1, recording.SaveCount, "清空设置也是一次确认，不是逐条键各一次");
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
