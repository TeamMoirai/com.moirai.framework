using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 设置项自检的判据表：每条规则各钉"越界时报"与"合法时不报"两面，外加默认值必须干净。
    /// <para>驱动方式是把资产值经 <see cref="SerializedObject"/> 写进一份**克隆**再读判据——
    /// 既反射不到任何私有字段（口径见 CLAUDE.md《测试可见性》），也绝不碰工程里那份真资产。</para>
    /// </summary>
    public sealed class ResourceServiceSettingsValidationTests
    {
        private ResourceServiceSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _settings = ScriptableObject.CreateInstance<ResourceServiceSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_settings != null)
            {
                Object.DestroyImmediate(_settings);
                _settings = null;
            }
        }

        /// <summary>
        /// 出厂默认值一条都不该报：报了就等于自检在指控自己的默认配置。
        /// </summary>
        [Test]
        public void Defaults_HaveNoIssues()
        {
            Assert.AreEqual(0, CountIssues(), "默认值被自检判成有问题，先怀疑判据而不是默认值。");
        }

        /// <summary>卸载档低于每帧档：帧驱动取较大值，卸载档永远顶不上去。</summary>
        [Test]
        public void WhenUnloadingBelowPerFrame_IsReported()
        {
            SetInt("m_ExpireProcessCountPerFrame", 32);
            SetInt("m_ExpireProcessCountWhenUnloading", 8);
            AssertReportsField("m_ExpireProcessCountWhenUnloading");

            SetInt("m_ExpireProcessCountWhenUnloading", 64);
            Assert.AreEqual(0, CountIssues(), "把关系摆正之后该清零");
        }

        /// <summary>每帧档非正数：时间轮以 expireBudget > 0 为闸门，0 就是不推进。</summary>
        [Test]
        public void PerFrameNotPositive_IsReported()
        {
            SetInt("m_ExpireProcessCountPerFrame", 0);
            AssertReportsField("m_ExpireProcessCountPerFrame");
        }

        /// <summary>销毁扫描配额非正数：被截断的 OnDestroy 留下的租约就永不回收。</summary>
        [Test]
        public void DestroySweepBudgetNotPositive_IsReported()
        {
            SetInt("m_DestroySweepBudget", 0);
            AssertReportsField("m_DestroySweepBudget");
        }

        /// <summary>空闲过期超过 256 格轮盘的一圈：记录会被跳过直到轮盘绕回。</summary>
        [Test]
        public void IdleExpireTimeBeyondWheelSpan_IsReported()
        {
            SetFloat("m_IdleAssetExpireTime", 256f);
            AssertReportsField("m_IdleAssetExpireTime");

            SetFloat("m_IdleAssetExpireTime", 255f);
            Assert.AreEqual(0, CountIssues(), "刚好一圈之内的值不该被报");
        }

        /// <summary>卸载上限非正数：调度比较恒真，卸载变成每帧一次。</summary>
        [Test]
        public void MaxUnloadIntervalNotPositive_IsReported()
        {
            SetFloat("m_MaxUnloadUnusedAssetsInterval", 0f);
            AssertReportsField("m_MaxUnloadUnusedAssetsInterval");
        }

        /// <summary>下限高于上限：预约档先被上限触发，下限形同废弃。</summary>
        [Test]
        public void MinAboveMaxUnloadInterval_IsReported()
        {
            SetFloat("m_MinUnloadUnusedAssetsInterval", 600f);
            AssertReportsField("m_MinUnloadUnusedAssetsInterval");
        }

        /// <summary>GC 节流为负：每次请求都真收。</summary>
        [Test]
        public void NegativeGCCollectInterval_IsReported()
        {
            SetFloat("m_MinGCCollectInterval", -1f);
            AssertReportsField("m_MinGCCollectInterval");
        }

        /// <summary>
        /// 缓冲区比问题数小时只少报，不抛——自检永远不该成为故障源。
        /// </summary>
        [Test]
        public void SmallBuffer_TruncatesWithoutThrowing()
        {
            // 挑四条互不牵连的：改卸载上限会顺带触发"下限高于上限"，那种级联不该混进这条判据。
            SetInt("m_DestroySweepBudget", 0);
            SetInt("m_ExpireProcessCountPerFrame", 0);
            SetFloat("m_MinGCCollectInterval", -1f);
            SetFloat("m_IdleAssetExpireTime", 999f);

            var full = new ResourceSettingsIssue[8];
            Assert.AreEqual(4, _settings.GetConfigurationIssues(full, full.Length), "前置不成立：本该报 4 条");

            var small = new ResourceSettingsIssue[2];
            Assert.AreEqual(2, _settings.GetConfigurationIssues(small, small.Length), "容量不足时只少报，不抛");
            Assert.IsNotNull(small[0].Detail);
            Assert.IsNotNull(small[1].Detail);
        }

        private void AssertReportsField(string field)
        {
            var buffer = new ResourceSettingsIssue[8];
            int count = _settings.GetConfigurationIssues(buffer, buffer.Length);
            for (int i = 0; i < count; i++)
            {
                if (buffer[i].Field == field)
                {
                    Assert.IsNotEmpty(buffer[i].Detail, "{0} 报了出来却没说清原因。", field);
                    return;
                }
            }

            Assert.Fail("期望自检报出 {0}，实际报了 {1} 条。", field, count);
        }

        private int CountIssues()
        {
            var buffer = new ResourceSettingsIssue[8];
            return _settings.GetConfigurationIssues(buffer, buffer.Length);
        }

        private void SetInt(string fieldName, int value)
        {
            var serialized = new SerializedObject(_settings);
            var property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, "设置资产里没有字段 {0}。", fieldName);
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private void SetFloat(string fieldName, float value)
        {
            var serialized = new SerializedObject(_settings);
            var property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, "设置资产里没有字段 {0}。", fieldName);
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
