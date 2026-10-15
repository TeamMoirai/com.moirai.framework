using System;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Service.Resource
{
    /// <summary>
    /// 租约来源采集与卸载账的契约：开关关掉一律不采，来源要跳过资源内核自身帧，卸载账有界且按最新优先可读。
    /// </summary>
    /// <remarks>
    /// 只断行为不断实现：来源断的是"落在调用方那一个文件"，不逐字钉行号（行号会随用例增删漂移）。
    /// 卸载账里记的是 UTC 时刻，本组用例不断时间值，只断条数、丢弃计数与读取顺序。
    /// </remarks>
    [TestFixture]
    public sealed class ResourceLeaseProvenanceTests
    {
        private bool _savedEnabled;

        [SetUp]
        public void SetUp()
        {
            _savedEnabled = ResourceLeaseProvenance.Enabled;
            ResourceLeaseProvenance.Enabled = false;
            ResourceLeaseProvenance.ClearUnload();
            Assert.AreEqual(0, ResourceLeaseProvenance.UnloadCount, "进门时卸载账必须是空的（上个用例没清）");
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLeaseProvenance.Enabled = _savedEnabled;
            ResourceLeaseProvenance.ClearUnload();
        }

        /// <summary>开关关掉：来源交回空值，取用路径不建调用栈也不建字符串。</summary>
        [Test]
        public void Capture_ToggleOff_ReturnsEmptySource()
        {
            ResourceLeaseProvenance.LeaseSource source = ResourceLeaseProvenance.Capture();

            Assert.IsTrue(string.IsNullOrEmpty(source.Display), "关掉开关不该采到任何调用点");
            Assert.IsTrue(string.IsNullOrEmpty(source.File), "关掉开关不该建路径字符串");
            Assert.AreEqual(0, source.Line, "关掉开关行号留在 0");
        }

        /// <summary>开关打开：来源落在测试自身这一帧——资源内核自己的帧必须被跳过。</summary>
        [Test]
        public void Capture_ToggleOn_ReportsCallerOutsideTheResourceKernel()
        {
            ResourceLeaseProvenance.Enabled = true;

            ResourceLeaseProvenance.LeaseSource source = ResourceLeaseProvenance.Capture();

            StringAssert.Contains(nameof(ResourceLeaseProvenanceTests), source.Display,
                "来源应当指到调用方类型，而不是内核自己");
            StringAssert.EndsWith(nameof(ResourceLeaseProvenanceTests) + ".cs", source.File.Replace('\\', '/'),
                "来源文件要是用例所在文件（路径统一成正斜杠再比）");
            Assert.Greater(source.Line, 0, "行号要真采到，0 说明没带文件行信息");
        }

        /// <summary>卸载账按最新优先读出，读取序与写入序相反。</summary>
        [Test]
        public void UnloadRing_ReadsNewestFirst()
        {
            ResourceLeaseProvenance.AddUnload("a/first", "Texture2D");
            ResourceLeaseProvenance.AddUnload("b/second", "Sprite");
            ResourceLeaseProvenance.AddUnload("c/third", "AudioClip");

            Assert.AreEqual(3, ResourceLeaseProvenance.UnloadCount, "三条都该留住");
            Assert.AreEqual("c/third", ResourceLeaseProvenance.GetNewestUnload(0).Location, "第 0 条是最新的");
            Assert.AreEqual("b/second", ResourceLeaseProvenance.GetNewestUnload(1).Location, "第 1 条次新");
            Assert.AreEqual("a/first", ResourceLeaseProvenance.GetNewestUnload(2).Location, "第 2 条最旧");
            Assert.AreEqual("AudioClip", ResourceLeaseProvenance.GetNewestUnload(0).TypeName, "类型名跟着地址一起记");
        }

        /// <summary>超出容量覆盖最旧并计入丢弃数，留住的仍是最近一整窗。</summary>
        [Test]
        public void UnloadRing_IsBoundedAndCountsDropped()
        {
            int total = ResourceLeaseProvenance.UNLOAD_CAPACITY + 5;
            for (int i = 0; i < total; i++)
            {
                ResourceLeaseProvenance.AddUnload("asset/" + i, "Texture2D");
            }

            Assert.AreEqual(ResourceLeaseProvenance.UNLOAD_CAPACITY, ResourceLeaseProvenance.UnloadCount,
                "条数钉在容量上，不随写入次数无界增长");
            Assert.AreEqual(5, ResourceLeaseProvenance.UnloadDropped, "被覆盖掉的条数要如实报出来");
            Assert.AreEqual("asset/" + (total - 1), ResourceLeaseProvenance.GetNewestUnload(0).Location,
                "覆盖之后最新的仍是最后写入那条");
            Assert.AreEqual("asset/5", ResourceLeaseProvenance.GetNewestUnload(ResourceLeaseProvenance.UNLOAD_CAPACITY - 1).Location,
                "最旧留住的那条应该是第 5 条——前 5 条已被覆盖");
        }

        /// <summary>清空连计数与丢弃数一起归零，越界读当场抬错而不是静默回旧值。</summary>
        [Test]
        public void ClearUnload_ResetsBothCounters_AndOutOfRangeThrows()
        {
            ResourceLeaseProvenance.AddUnload("a", "Sprite");
            Assert.Catch<ArgumentOutOfRangeException>(() => ResourceLeaseProvenance.GetNewestUnload(1));

            ResourceLeaseProvenance.ClearUnload();

            Assert.AreEqual(0, ResourceLeaseProvenance.UnloadCount, "清空后条数归零");
            Assert.AreEqual(0, ResourceLeaseProvenance.UnloadDropped, "丢弃计数也归零（它是这一窗的读数）");
            Assert.Catch<ArgumentOutOfRangeException>(() => ResourceLeaseProvenance.GetNewestUnload(0));
        }

        /// <summary>地址与类型名为 null 时收空串：账面不能因为内核某侧没名字而整条丢掉。</summary>
        [Test]
        public void AddUnload_NormalizesNullNames_KeepsTheRecord()
        {
            ResourceLeaseProvenance.AddUnload(null, null);

            Assert.AreEqual(1, ResourceLeaseProvenance.UnloadCount, "空名也要留住这条卸载事实");
            Assert.AreEqual(string.Empty, ResourceLeaseProvenance.GetNewestUnload(0).Location, "null 地址收成空串");
            Assert.AreEqual(string.Empty, ResourceLeaseProvenance.GetNewestUnload(0).TypeName, "null 类型收成空串");
        }
    }
}
