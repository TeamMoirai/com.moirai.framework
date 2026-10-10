using System;
using Moirai.Atropos.ObjectPool;
using NUnit.Framework;
using Testing;

namespace Service.ObjectPool
{
    /// <summary>
    /// 取池地址归一化的 0-GC 验收：备忘表命中后稳态不得再产生托管分配。
    /// </summary>
    /// <remarks>
    /// 量具与 UI/Timer/Audio/Resource 的 0-GC 格同一把：<see cref="AllocationCapture"/>（<c>GC.Alloc</c> 采样事件数）。
    /// 本刀把这两格从 <c>Tests/EditorMode</c> 的字节前后差迁过来——那把尺在编辑器 Mono／Mono 玩家／IL2CPP 玩家实测恒 0
    /// （见 <c>Documentation~/zh/Testing.md</c>《0-GC 验收》，同文并明令「绝不要写前后差计量」），
    /// 因此旧断言 <c>AreEqual(0, after - before)</c> 是必真的绿。 <br />
    /// 稳态之外还各带一条结构档：归一化结果必须仍是同一个字符串实例——量具哪天坏了，这条先替它喊。 <br />
    /// 同夹具里剩下的那格 <c>HotPath_ZeroGcAlloc_WarmPoolRoundtrip</c> 依赖 uGUI 池夹具机器（假装载器/注册表/调度器/根节点），
    /// 尚未迁；它同样在用那把恒 0 的尺，见任务台账。
    /// </remarks>
    [TestFixture]
    [Category("Performance")]
    public sealed class PoolLocationAllocationTests
    {
        /// <summary>测量窗内的调用次数：够摊平首次建表，又不至于让一格跑几秒。</summary>
        private const int ITERATIONS = 256;

        /// <summary>测量台自身的牙：采样推进时一次必然分配必须被计到，否则 0 事件断言全是假绿。</summary>
        [Test]
        public void MeasureManaged_DetectsKnownAllocation()
        {
            AllocationCapture.CalibrateKnownAllocation();
        }

        /// <summary>带扩展名的地址：剥扩展名那次必然产字符串，之后每次都要命中备忘表——稳态 0 事件。</summary>
        [Test]
        public void NormalizeLocation_RawPathWithExtension_SteadyStateAllocatesZero()
        {
            DefaultGameObjectPoolHandler handler = new DefaultGameObjectPoolHandler();
            const string raw = "Assets/UI/Hero.prefab";

            Assert.AreEqual("Assets/UI/Hero", handler.NormalizeLocationCached(raw),
                "量具前提坏了：首次归一化要把扩展名剥掉，之后的稳态才有得谈");

            string later = null;
            int events = AllocationCapture.MeasureManaged("pool-normalize-raw", ITERATIONS,
                () => later = handler.NormalizeLocationCached(raw),
                b => Assert.AreEqual(0, b, "取池热路径每次都要归一化地址：扩展名剥离只能发生一次，之后必须复用备忘表"));

            Assert.AreEqual("Assets/UI/Hero", later);
        }

        /// <summary>本来就无需改写的地址：原样返回，既不产新字符串也不进取池备忘表——稳态 0 事件。</summary>
        [Test]
        public void NormalizeLocation_IdentityResult_SteadyStateAllocatesZero()
        {
            DefaultGameObjectPoolHandler handler = new DefaultGameObjectPoolHandler();
            const string clean = "Assets/UI/Hero";

            string same = null;
            AllocationCapture.MeasureManaged("pool-normalize-identity", ITERATIONS,
                () => same = handler.NormalizeLocationCached(clean),
                b => Assert.AreEqual(0, b, "零分配的结果不进取池备忘表，免得白白留住字符串"));

            Assert.AreSame(clean, same, "无需改写时原样返回，不该产生新字符串");
        }
    }
}
