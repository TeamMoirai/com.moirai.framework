using Moirai.Atropos.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Service.Audio
{
    /// <summary>
    /// AudioSpatialOptions 拆分契约：默认值对齐 Unity AudioSource 声学缺省、
    /// 播放选项各工厂方法必须以声学默认初始化 Spatial（直 new 的零值结构体会把声源整形成
    /// 「无多普勒、零衰减距离、无混响」）、FromOptions 空间逐字段拷贝。
    /// </summary>
    [TestFixture]
    public sealed class AudioSpatialOptionsTests
    {
        [Test]
        public void Default_MatchesUnityAcousticDefaults()
        {
            var d = AudioSpatialOptions.Default;

            Assert.AreEqual(0f, d.PanStereo, "默认声像居中");
            Assert.AreEqual(0f, d.SpatialBlend, "默认全 2D（对齐 Unity AudioSource 缺省）");
            Assert.AreEqual(1f, d.DopplerLevel, "对齐 Unity AudioSource dopplerLevel 缺省");
            Assert.AreEqual(0, d.Spread, "对齐 Unity AudioSource spread 缺省");
            Assert.AreEqual(AudioRolloffMode.Logarithmic, d.RolloffMode, "对齐 Unity AudioSource rolloffMode 缺省");
            Assert.AreEqual(1f, d.MinDistance, "对齐 Unity AudioSource minDistance 缺省");
            Assert.AreEqual(500f, d.MaxDistance, "对齐 Unity AudioSource maxDistance 缺省");
            Assert.AreEqual(1f, d.ReverbZoneMix, "对齐 Unity AudioSource reverbZoneMix 缺省");
            Assert.IsFalse(d.BypassEffects);
            Assert.IsFalse(d.BypassListenerEffects);
            Assert.IsFalse(d.BypassReverbZones);
            Assert.IsFalse(d.UseCustomRolloffCurve);
            Assert.IsNull(d.CustomRolloffCurve);
            Assert.IsFalse(d.UseSpatialBlendCurve);
            Assert.IsFalse(d.UseReverbZoneMixCurve);
            Assert.IsFalse(d.UseSpreadCurve);
        }

        [Test]
        public void OptionsFactories_InitializeSpatialDefaults()
        {
            // 回归锁：Create 系工厂此前的对象初始化器不写空间字段——拆分后若漏置
            // Spatial=Default，播出的声源将拿到「无多普勒、零衰减距离、无混响」的零值整形
            AssertSpatialDefaults(AudioPlayOptions.Default.Spatial, "Default");
            AssertSpatialDefaults(AudioPlayOptions.Create(EAudioTrack.Sfx).Spatial, "Create");
            AssertSpatialDefaults(AudioPlayOptions.CreateLooping(EAudioTrack.Music).Spatial, "CreateLooping");
            AssertSpatialDefaults(AudioPlayOptions.CreateWithFade(EAudioTrack.Music, 0.5f).Spatial, "CreateWithFade");
        }

        [Test]
        public void FromOptions_CopiesSpatialVerbatim()
        {
            var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
            options.Spatial.SpatialBlend = 0.8f;
            options.Spatial.DopplerLevel = 2.5f;
            options.Spatial.MinDistance = 3f;
            options.Spatial.MaxDistance = 120f;
            options.Spatial.RolloffMode = AudioRolloffMode.Linear;
            options.Spatial.UseSpreadCurve = true;
            options.Spatial.SpreadCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

            AudioPlayColdParams cold = AudioPlayColdParams.FromOptions(options);
            try
            {
                Assert.AreEqual(0.8f, cold.Spatial.SpatialBlend, "空间参数经 FromOptions 必须原样进冷参");
                Assert.AreEqual(2.5f, cold.Spatial.DopplerLevel);
                Assert.AreEqual(3f, cold.Spatial.MinDistance);
                Assert.AreEqual(120f, cold.Spatial.MaxDistance);
                Assert.AreEqual(AudioRolloffMode.Linear, cold.Spatial.RolloffMode);
                Assert.IsTrue(cold.Spatial.UseSpreadCurve);
                Assert.AreSame(options.Spatial.SpreadCurve, cold.Spatial.SpreadCurve, "曲线按引用共享，不复制");
            }
            finally
            {
                AudioPlayColdParamsPool.Release(cold);
            }
        }

        [Test]
        public void ResetToDefault_RestoresAcousticDefaults()
        {
            AudioPlayColdParams cold = AudioPlayColdParamsPool.Acquire();
            try
            {
                cold.Spatial.SpatialBlend = 1f;
                cold.Spatial.MaxDistance = 42f;

                cold.ResetToDefault();

                AssertSpatialDefaults(cold.Spatial, "ResetToDefault");
            }
            finally
            {
                AudioPlayColdParamsPool.Release(cold);
            }
        }

        private static void AssertSpatialDefaults(AudioSpatialOptions spatial, string source)
        {
            Assert.AreEqual(1f, spatial.DopplerLevel, $"{source} 的空间缺省多普勒必须为 1");
            Assert.AreEqual(1f, spatial.MinDistance, $"{source} 的空间缺省最小距离必须为 1");
            Assert.AreEqual(500f, spatial.MaxDistance, $"{source} 的空间缺省最大距离必须为 500");
            Assert.AreEqual(1f, spatial.ReverbZoneMix, $"{source} 的空间缺省混响混合必须为 1");
            Assert.AreEqual(AudioRolloffMode.Logarithmic, spatial.RolloffMode, $"{source} 的空间缺省衰减必须为对数");
        }
    }
}
