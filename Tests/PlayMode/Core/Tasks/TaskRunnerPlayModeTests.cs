using System.Collections;
using System.Text.RegularExpressions;
using Moirai.Atropos.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Core.Tasks
{
    /// <summary>
    /// Tasks 编排骨架的 PlayMode 集成测试：需要事件广播与每帧驱动的那两条路径。
    /// <para>子任务完成会 <c>PostComplete</c> 派发事件、<see cref="TaskRunner"/> 的隔离判据只在
    /// <c>Update</c> 里成立，而事件宿主 <see cref="Moirai.Atropos.Events.EventManager"/> 的静态入口
    /// 在非 play mode 直接返回 null——这两件事在 EditMode 里测不到，故住在 PlayMode 侧；
    /// 不依赖广播的三条（空队列、Reset 交还、引用下穿）见 <c>Tests/EditorMode/Core/Tasks/SequenceTaskTests</c>。</para>
    /// <para>摘除/归还是否发生，一律用池的 LIFO 复用来观测（取回同一只实例），不去读内核私有集合。</para>
    /// </summary>
    [TestFixture]
    public sealed class TaskRunnerPlayModeTests
    {
        [UnityTest]
        public IEnumerator ThrowingTask_IsStoppedAndReaped_WithoutStarvingEarlierTasks()
        {
            HealthyTask healthy = HealthyTask.GetPooled();
            PoisonTask poison = PoisonTask.GetPooled();

            healthy.Run();
            poison.Run();

            // 首轮：毒任务抛出 → 内核先置停再记一条 Fatal（落 Error）→ 开发期照样上抛，Unity 记一条 Exception
            LogAssert.Expect(LogType.Error, new Regex(".*"));
            LogAssert.Expect(LogType.Exception, new Regex(".*"));
            yield return null;

            Assert.AreEqual(1, healthy.Ticks, "排在毒任务之前的同类必须照常推进");
            Assert.AreEqual(TaskStatus.Stopped, poison.GetStatus(), "毒任务必须当场置停，下一帧不再进 tick");

            // 次轮：Stopped 落到收尾循环被摘除并 Dispose
            yield return null;

            Assert.AreSame(poison, PoisonTask.GetPooled(), "摘除时必须把登记时 Acquire 的那次引用交还");

            healthy.Stop();
            yield return null;
            Assert.AreSame(healthy, HealthyTask.GetPooled(), "良性任务收口后同样要回到池里");
        }

        [UnityTest]
        public IEnumerator Sequence_RunsChildrenInOrder_AndReleasesEachOne()
        {
            OneTickTask first = OneTickTask.GetPooled();
            OneTickTask second = OneTickTask.GetPooled();
            SequenceTask sequence = SequenceTask.GetPooled();
            sequence.Start();
            sequence.Append(first);
            sequence.Append(second);
            sequence.Acquire();

            // 一次 Tick 会把能跑完的子任务连着跑完（跑完即续下一只，直到某只还在跑）
            sequence.Tick();
            yield return null;

            Assert.AreEqual(1, first.Ticks, "第一段应被驱动");
            Assert.AreEqual(1, second.Ticks, "第一段完成后应接着驱动第二段");
            Assert.AreEqual(TaskStatus.Completed, sequence.GetStatus(), "两段都完成即序列完成");

            sequence.Dispose();

            Assert.AreSame(second, OneTickTask.GetPooled(), "后交还的在栈顶（LIFO）");
            Assert.AreSame(first, OneTickTask.GetPooled(), "第一段也应完好地回到池里");
        }

        /// <summary>首轮 Tick 即完成并广播的子任务。</summary>
        private sealed class OneTickTask : PooledTaskBase<OneTickTask>
        {
            internal int Ticks;

            public override void Tick()
            {
                Ticks++;
                CompleteTask();
            }
        }

        /// <summary>只计数、永不自行结束的良性任务。</summary>
        private sealed class HealthyTask : PooledTaskBase<HealthyTask>
        {
            internal int Ticks;

            public override void Tick()
            {
                Ticks++;
            }
        }

        /// <summary>每次 Tick 都抛的任务，模拟"毒任务"。</summary>
        private sealed class PoisonTask : PooledTaskBase<PoisonTask>
        {
            public override void Tick()
            {
                throw new System.InvalidOperationException("poison task");
            }
        }
    }
}
