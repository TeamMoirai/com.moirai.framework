using System.Collections.Generic;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 后端能力判据的门禁：能力缺失且该能力被用到才报，缺失但字段停在默认值不报，多条命中逐条列全。
    /// </summary>
    /// <remarks>
    /// 只验 <see cref="AudioBackendCapabilityPolicy"/> 这份纯判据（输入为能力快照 + 选项值 + 标识串）， <br />
    /// 不枚举项目里的播放选项资产、也不构造后端实例——资产内容与后端装配是配置层结论，归构建期校验器。 <br />
    /// 「能力缺失」按最保守口径断：判据不得替未知后端自称支持。
    /// </remarks>
    [TestFixture]
    public sealed class AudioBackendCapabilityPolicyTests
    {
        private const string TEST_ASSET_PATH = "Assets/Game/Audio/Hit.asset";

        private static AudioBackendCapabilities Capabilities(bool delayedStart, bool playbackTrim, bool soloControl)
        {
            return new AudioBackendCapabilities
            {
                SupportsDelayedStart = delayedStart,
                SupportsPlaybackTrim = playbackTrim,
                SupportsSoloControl = soloControl,
                BackendName = "TestBackend",
            };
        }

        /// <summary>
        /// 只把一项能力用到非默认值，其余字段保持默认。
        /// </summary>
        private static AudioPlayOptions OptionsWith(float initialDelay, float playbackDuration, EAudioSoloMode soloMode)
        {
            AudioPlayOptions options = AudioPlayOptions.Default;
            options.InitialDelay = initialDelay;
            options.PlaybackDuration = playbackDuration;
            options.SoloMode = soloMode;
            return options;
        }

        [Test]
        public void AllSupported_DelayedTrimAndSoloUsed_ReportNothing()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(true, true, true),
                OptionsWith(0.25f, 1.5f, EAudioSoloMode.SingleTrack),
                TEST_ASSET_PATH, problems);

            Assert.IsEmpty(problems, "三项全支持时任何配置都不该被报出来");
        }

        [Test]
        public void DelayedStartMissing_InitialDelayUsed_ReportInitialDelayField()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(false, true, true),
                OptionsWith(0.25f, 0f, EAudioSoloMode.None),
                TEST_ASSET_PATH, problems);

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(nameof(AudioPlayOptions.InitialDelay), problems[0].Field);
            Assert.AreEqual(TEST_ASSET_PATH, problems[0].AssetPath);
            StringAssert.Contains("InitialDelay", problems[0].Detail);
        }

        [Test]
        public void PlaybackTrimMissing_DurationUsed_ReportPlaybackDurationField()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(true, false, true),
                OptionsWith(0f, 1.5f, EAudioSoloMode.None),
                TEST_ASSET_PATH, problems);

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(nameof(AudioPlayOptions.PlaybackDuration), problems[0].Field);
            Assert.AreEqual(TEST_ASSET_PATH, problems[0].AssetPath);
            StringAssert.Contains("PlaybackDuration", problems[0].Detail);
        }

        [Test]
        public void SoloMissing_SoloModeUsed_ReportSoloModeField()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(true, true, false),
                OptionsWith(0f, 0f, EAudioSoloMode.AllTracks),
                TEST_ASSET_PATH, problems);

            Assert.AreEqual(1, problems.Count);
            Assert.AreEqual(nameof(AudioPlayOptions.SoloMode), problems[0].Field);
            Assert.AreEqual(TEST_ASSET_PATH, problems[0].AssetPath);
            StringAssert.Contains("SoloMode", problems[0].Detail);
        }

        [Test]
        public void AllMissing_DelayTrimAndSoloUsed_ReportThreeIssues()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(false, false, false),
                OptionsWith(0.25f, 1.5f, EAudioSoloMode.SingleTrack),
                TEST_ASSET_PATH, problems);

            Assert.AreEqual(3, problems.Count, "多条同时命中必须逐条列全，不能合并成一条");
            Assert.AreEqual(nameof(AudioPlayOptions.InitialDelay), problems[0].Field);
            Assert.AreEqual(nameof(AudioPlayOptions.PlaybackDuration), problems[1].Field);
            Assert.AreEqual(nameof(AudioPlayOptions.SoloMode), problems[2].Field);
            for (int i = 0; i < problems.Count; i++)
            {
                Assert.AreEqual(TEST_ASSET_PATH, problems[i].AssetPath);
                Assert.AreNotEqual(0, problems[i].Detail.Length);
            }
        }

        [Test]
        public void AllMissing_DelayTrimAndSoloAtDefault_ReportNothing()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(false, false, false),
                OptionsWith(0f, 0f, EAudioSoloMode.None),
                TEST_ASSET_PATH, problems);

            Assert.IsEmpty(problems, "能力缺失但对应字段停在默认值时语义没有分叉，不该打扰操作者");
        }

        [Test]
        public void SoloMissing_AutoUnSoloWithoutSoloMode_ReportNothing()
        {
            // 取消静音只在 Solo 分支里被读：SoloMode 为 None 时这个开关落不到任何行为上，不构成语义分叉
            AudioPlayOptions options = OptionsWith(0f, 0f, EAudioSoloMode.None);
            options.AutoUnSoloOnEnd = true;

            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(Capabilities(true, true, false), options, TEST_ASSET_PATH, problems);

            Assert.IsEmpty(problems);
        }

        [Test]
        public void PlaybackTrimMissing_NonPositiveDuration_ReportNothing()
        {
            // 运行期的裁剪闸门是「严格大于 0」，非正值走的是默认长度那条路，报出来是假警
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(true, false, true),
                OptionsWith(0f, -1f, EAudioSoloMode.None),
                TEST_ASSET_PATH, problems);

            Assert.IsEmpty(problems);
        }

        [Test]
        public void Snapshot_NullHandler_AllCapabilitiesFalse()
        {
            AudioBackendCapabilities capabilities = AudioBackendCapabilities.From(null);

            Assert.IsFalse(capabilities.SupportsDelayedStart, "未知后端不得自称支持延迟起播");
            Assert.IsFalse(capabilities.SupportsPlaybackTrim, "未知后端不得自称支持播放裁剪");
            Assert.IsFalse(capabilities.SupportsSoloControl, "未知后端不得自称支持独奏");
        }

        [Test]
        public void PlaybackTrimMissing_PlaybackTimeUsed_ReportPlaybackTimeField()
        {
            AudioPlayOptions options = AudioPlayOptions.Default;
            options.PlaybackTime = 1.5f;

            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(true, false, true), options, TEST_ASSET_PATH, problems);

            Assert.AreEqual(1, problems.Count, "起点偏移被忽略也是语义分叉，一条不能漏");
            Assert.AreEqual(nameof(AudioPlayOptions.PlaybackTime), problems[0].Field);
            Assert.AreEqual(TEST_ASSET_PATH, problems[0].AssetPath);
        }

        [Test]
        public void PlaybackTrimMissing_ZeroPlaybackTime_ReportNothing()
        {
            var problems = new List<AudioCapabilityIssue>();
            AudioBackendCapabilityPolicy.CollectIssues(
                Capabilities(true, false, true), AudioPlayOptions.Default, TEST_ASSET_PATH, problems);

            Assert.IsEmpty(problems, "起点偏移停在默认值时语义没有分叉，不得报假警");
        }
    }
}
