using System.Collections.Generic;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// Shuffle 洗牌袋契约：一轮不重复、顺序收尾不误判、耗尽可重洗。
    /// </summary>
    public sealed class ShuffleIndexBagTests
    {
        [Test]
        public void Next_ReturnsValidIndices_WithinRange()
        {
            var bag = new ShuffleIndexBag();
            for (int i = 0; i < 20; i++)
            {
                int pick = bag.Next(5);
                Assert.GreaterOrEqual(pick, 0);
                Assert.Less(pick, 5);
            }
        }

        [Test]
        public void FirstRound_CoversAllTracksOnce_WithoutRepeat()
        {
            var bag = new ShuffleIndexBag();
            var seen = new HashSet<int>();
            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(seen.Add(bag.Next(5)), "第一轮不应重复下标");
            }

            Assert.AreEqual(0, bag.Remaining, "第一轮正好耗尽");
        }

        [Test]
        public void ExhaustedBag_NoneLoop_ConsumerStopsAfterFullRound()
        {
            // 复现回归：随机袋一轮 3 首播完 → Remaining==0，调用方应停；
            // 旧实现用顺序 _index+1 回绕，随机落到末位下标会在第一首后就收尾。
            var bag = new ShuffleIndexBag();
            const int trackCount = 3;

            bag.Next(trackCount);
            Assert.AreEqual(trackCount - 1, bag.Remaining);

            bag.Next(trackCount);
            bag.Next(trackCount);
            Assert.AreEqual(0, bag.Remaining, "一轮正好播完全部曲目");

            // None 收尾判据：Remaining==0 即停，不看下标是否“末位”
            Assert.IsTrue(bag.Remaining == 0);
        }

        [Test]
        public void ExhaustedBag_LoopList_RefillsAndContinues()
        {
            var bag = new ShuffleIndexBag();
            for (int i = 0; i < 3; i++) bag.Next(3);

            Assert.AreEqual(0, bag.Remaining);

            // 重洗时排除上一首 → 袋内 2 个；弹出 1 个后剩 1
            int next = bag.Next(3);
            Assert.GreaterOrEqual(next, 0);
            Assert.Less(next, 3);
            Assert.AreEqual(1, bag.Remaining, "重洗（排除上一首）并弹出后剩 n-2");
        }

        [Test]
        public void Reset_ClearsBagAndLastPlayed()
        {
            var bag = new ShuffleIndexBag();
            bag.Next(4);
            bag.Next(4);
            bag.Reset();

            Assert.AreEqual(0, bag.Remaining);

            // Reset 后完整重开一轮：再次全覆盖且不重复
            var seen = new HashSet<int>();
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(seen.Add(bag.Next(4)), "Reset 后的新一轮不应重复下标");
            }

            Assert.AreEqual(0, bag.Remaining);
        }

        [Test]
        public void SingleTrack_AlwaysZero()
        {
            var bag = new ShuffleIndexBag();
            Assert.AreEqual(0, bag.Next(1));
            Assert.AreEqual(0, bag.Next(1));
        }

        [Test]
        public void TrackCountChange_ResetsRound()
        {
            var bag = new ShuffleIndexBag();
            bag.Next(5);
            bag.Next(5);

            int pick = bag.Next(3);
            Assert.Less(pick, 3, "曲目数变化后不得沿用旧袋下标");
            Assert.AreEqual(2, bag.Remaining, "按新曲目数重洗一轮");
        }
    }
}
