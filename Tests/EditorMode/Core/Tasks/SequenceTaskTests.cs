using Moirai.Atropos.Tasks;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;

namespace Core.Tasks
{
    /// <summary>
    /// <see cref="SequenceTask"/> 编排骨架与池化引用的 EditMode 测试（不含需要广播完成事件的部分）。
    /// </summary>
    /// <remarks>
    /// 覆盖缺陷形态：空队列越过 <c>TryPeek</c> 失败直接 <c>Start()</c> 的空引用； <br />
    /// <c>Reset</c> 只 <c>Clear()</c> 队列而丢掉子任务欠 <c>Append</c> 的那次 <c>Acquire</c>（子任务回不了池、<c>DelayTask</c> 的 Timer 句柄不取消）； <br />
    /// 引用下穿（0 → -1）使任务永不凑齐归还。
    /// 跑完子任务需要 <c>PostComplete</c> 广播， <br />
    /// 而事件宿主 <see cref="Moirai.Atropos.Events.EventManager"/> 在编辑器态拿不到（静态入口在非 play mode 返回 null）， <br />
    /// 故按序执行那格住在 <c>Tests/PlayMode/Core/Tasks/</c>。
    /// 观测手法统一走池的 LIFO 复用：<c>GetPooled()</c> 取回同一只实例即说明它确实被 Dispose 并归还。
    /// </remarks>
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
