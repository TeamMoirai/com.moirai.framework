using Moirai.Atropos;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 音频设置政策的门禁：键名、缺省与值域钳制只在这里定义一次。
    /// </summary>
    /// <remarks>
    /// 只断读写结果，不断存储形态（默认后端是文件、也可换 PlayerPrefs，都不该被用例钉住）。
    /// 轨级音量的缺省由调用方传入：每组配置的 <c>m_DefaultVolume</c> 可序列化改值，政策不得把它抹平成常量。
    /// 设置存储是跨会话的真实存档（<c>persistentDataPath</c> 下的 Setting.dat 里就有音频这些键），
    /// 「没写过所以走缺省」这类判据必须自己把键清出来才成立，否则读到的是别人留下的存量。
    /// </remarks>
    [TestFixture]
    public sealed class AudioSettingsPolicyTests
    {
        [SetUp]
        public void SetUp() => ClearOwnedKeys();

        [TearDown]
        public void TearDown() => ClearOwnedKeys();

        private static void ClearOwnedKeys()
        {
            AudioSettingsPolicy.RemovePair(AudioSettingsPolicy.MasterMutedKey, AudioSettingsPolicy.MasterVolumeKey);
            foreach (var track in (EAudioTrack[])System.Enum.GetValues(typeof(EAudioTrack)))
            {
                AudioSettingsPolicy.RemovePair(AudioSettingsPolicy.TrackMutedKey(track), AudioSettingsPolicy.TrackVolumeKey(track));
            }
        }

        [Test]
        public void NormalizeVolume_ClampsToContractRange()
        {
            Assert.AreEqual(1f, AudioSettingsPolicy.NormalizeVolume(2f), "存量值可能写于上限还是 10 的年代");
            Assert.AreEqual(0f, AudioSettingsPolicy.NormalizeVolume(-1f));
            Assert.AreEqual(0.4f, AudioSettingsPolicy.NormalizeVolume(0.4f), 1e-6f, "值域内原样");
        }

        [Test]
        public void MasterRoundTrip_KeepsWrittenValues()
        {
            AudioSettingsPolicy.SaveVolume(AudioSettingsPolicy.MasterVolumeKey, 0.25f);
            AudioSettingsPolicy.SaveMuted(AudioSettingsPolicy.MasterMutedKey, true);

            Assert.AreEqual(0.25f, AudioSettingsPolicy.LoadVolume(AudioSettingsPolicy.MasterVolumeKey,
                AudioSettingsPolicy.DefaultVolume), 1e-6f);
            Assert.IsTrue(AudioSettingsPolicy.LoadMuted(AudioSettingsPolicy.MasterMutedKey));
        }

        [Test]
        public void Defaults_AppearWhenNothingStored()
        {
            Assert.AreEqual(1f, AudioSettingsPolicy.LoadVolume(AudioSettingsPolicy.MasterVolumeKey,
                AudioSettingsPolicy.DefaultVolume), 1e-6f, "未写入过即满音量");
            Assert.IsFalse(AudioSettingsPolicy.LoadMuted(AudioSettingsPolicy.MasterMutedKey), "未写入过即不静音");
        }

        [Test]
        public void CallerSuppliedDefault_IsHonouredPerTrack()
        {
            // AudioGroupConfig 每组有可序列化的 m_DefaultVolume：政策必须按调用方给的缺省回落，而不是统一常量
            Assert.AreEqual(0.42f, AudioSettingsPolicy.LoadVolume(AudioSettingsPolicy.TrackVolumeKey(EAudioTrack.Voice), 0.42f),
                1e-6f, "该轨没写过设置时用传入的缺省值");
        }

        [Test]
        public void TrackKeys_IsolatePerTrack()
        {
            AudioSettingsPolicy.SaveVolume(AudioSettingsPolicy.TrackVolumeKey(EAudioTrack.Sfx), 0.5f);

            Assert.AreEqual(0.5f, AudioSettingsPolicy.LoadVolume(AudioSettingsPolicy.TrackVolumeKey(EAudioTrack.Sfx), 1f), 1e-6f);
            Assert.AreEqual(1f, AudioSettingsPolicy.LoadVolume(AudioSettingsPolicy.TrackVolumeKey(EAudioTrack.Music), 1f), 1e-6f,
                "一条轨的设置不得读到另一条轨上");
        }

        [Test]
        public void LegacyOutOfRangeStoredValue_IsClampedOnRead()
        {
            // 直接写一个越界值绕开政策的写入侧：读回来必须落回契约值域（Unity 后端此前不钳，这一格钉住统一后的口径）
            SettingUtility.SetFloat(AudioSettingsPolicy.MasterVolumeKey, 10f);

            Assert.AreEqual(1f, AudioSettingsPolicy.LoadVolume(AudioSettingsPolicy.MasterVolumeKey, 1f), 1e-6f,
                "越界存量一律钳回 0..1");
        }
    }
}
