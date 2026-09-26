using Moirai.Atropos.Tasks;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Core.Tasks
{
    /// <summary>
    /// <see cref="SequenceTask"/> 编排骨架与池化引用的 EditMode 测试（不广播完成事件的那部分）。
    /// <para>空队列那条曾在 <c>TryPeek</c> 失败后直接 <c>Start()</c> 空引用；<c>Reset</c> 那条以前只
    /// <c>Clear()</c> 队列，把子任务欠 <c>Append</c> 的那次 <c>Acquire</c> 一起丢掉——子任务从此回不了池，
    /// <c>DelayTask</c> 更是连 Timer 句柄都不取消；引用下穿（0 → -1）则让那只任务永远凑不齐归还。</para>
    /// <para>要跑完子任务就得 <c>PostComplete</c> 广播，而事件宿主 <see cref="Moirai.Atropos.Events.EventManager"/>
    /// 在编辑器态拿不到（其静态入口在非 play mode 直接返回 null）——按序执行那格因此住在
    /// <c>Tests/PlayMode/Core/Tasks/</c>，这里只钉不需要广播的部分。</para>
    /// <para>观测手法统一走池的 LIFO 复用：<c>GetPooled()</c> 取回同一只实例，即说明它确实被 Dispose 并归还了。</para>
    /// </summary>
    [TestFixture]
    public sealed class SequenceTaskTests
    {
        [Test]
        public void EmptyQueue_Tick_CompletesWithoutThrow()
        {
            SequenceTask sequence = SequenceTask.GetPooled();
            sequence.Start();
            sequence.Acquire();
            try
            {
                sequence.Tick();

                Assert.AreEqual(TaskStatus.Completed, sequence.GetStatus(), "空序列应在首次 Tick 按完成收口");
            }
            finally
            {
                sequence.Dispose();
            }
        }

        [Test]
        public void Reset_ReleasesChildrenThatWereStillQueued()
        {
            StuckTask stuck = StuckTask.GetPooled();
            StuckTask tail = StuckTask.GetPooled();
            SequenceTask sequence = SequenceTask.GetPooled();
            sequence.Start();
            sequence.Append(stuck);
            sequence.Append(tail);
            sequence.Acquire();

            sequence.Tick();

            Assert.AreEqual(1, stuck.Ticks, "队列头被驱动过");
            Assert.AreEqual(0, tail.Ticks, "它后面那只还没轮到（本用例要的就是「还挂在队列里」这个状态）");
            Assert.AreEqual(TaskStatus.Running, sequence.GetStatus(), "子任务没跑完，序列不该结束");

            // 回收：交还序列自己的引用 → ReleasePooled → Reset
            sequence.Dispose();

            Assert.AreSame(tail, StuckTask.GetPooled(), "仍在队列里的子任务必须被逐一 Dispose 并回到池里");
            Assert.AreSame(stuck, StuckTask.GetPooled(), "当前运行位上的子任务也在队列里，同样要交还");
        }

        [Test]
        public void Dispose_BelowZeroReference_IsReportedAndDoesNotPoisonTheInstance()
        {
            StuckTask task = StuckTask.GetPooled();

            // 取来即 0 持有（见 GetPooled 的说明）：多还一次必须当场报，而不是把计数拖成负数
            UtfLogExpect.Error();
            task.Dispose();

            // 挡住下穿之后，正常的一借一还仍要把任务送回池里——旧写法在这里就再也取不回同一只了
            task.Acquire();
            task.Dispose();

            Assert.AreSame(task, StuckTask.GetPooled(), "多还一次不得让这只任务从此失踪");
        }

        /// <summary>Tick 后保持 Running 的子任务：既能把后续项挡在队列里，也不触发任何事件广播。</summary>
        private sealed class StuckTask : PooledTaskBase<StuckTask>
        {
            internal int Ticks;

            public override void Tick()
            {
                Ticks++;
            }
        }
    }
}
