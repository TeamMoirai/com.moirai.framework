using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;

namespace DataStructure
{
    /// <summary>
    /// 验证 <see cref="ShuffleBag{T}"/> 的轮次契约：一轮按权重表正好覆盖一次、换手不连点、
    /// 中途 Add 不倒拨本轮、空袋不抛、Reset 重开一轮。
    /// </summary>
    public class ShuffleBagTests
    {
        [Test]
        public void Pick_FirstRound_CoversEveryItemOnce()
        {
            var bag = new ShuffleBag<int>(4);
            for (int i = 0; i < 4; i++) bag.Add(i, 1);

            var seen = new HashSet<int>();
            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(seen.Add(bag.Pick()), "一轮之内不应重复");
            }

            Assert.AreEqual(0, bag.Remaining, "4 手正好抽空一轮");
            Assert.AreEqual(4, bag.Size);
        }

        [Test]
        public void Pick_AcrossRounds_NeverRepeatsBackToBack()
        {
            // 回归锚点：旧实现换手时把刚取出的项留在可取区，n 项袋子每轮换手有 1/(n-1) 概率连点
            var bag = new ShuffleBag<int>(3);
            for (int i = 0; i < 3; i++) bag.Add(i, 1);

            int prev = bag.Pick();
            for (int i = 1; i < 3000; i++)
            {
                int current = bag.Pick();
                Assert.AreNotEqual(prev, current, $"第 {i} 手与上一手相同（换手连点）");
                prev = current;
            }
        }

        [Test]
        public void Pick_TwoItems_AlternateStrictly()
        {
            var bag = new ShuffleBag<int>(2);
            bag.Add(1, 1);
            bag.Add(2, 1);

            int prev = bag.Pick();
            for (int i = 0; i < 200; i++)
            {
                int current = bag.Pick();
                Assert.AreNotEqual(prev, current, "两项时只能交替");
                prev = current;
            }
        }

        [Test]
        public void Pick_AcrossRounds_HeadAndTailStayFree()
        {
            // 「把上一手沉到袋底」这类改法会让同一项永远占据袋尾：轮首与轮尾都仍要是随机项。
            // 200 轮 x 3 项下某项一次都没轮到过头/尾的概率约 (2/3)^200，不是抖动来源。
            var bag = new ShuffleBag<int>(3);
            for (int i = 0; i < 3; i++) bag.Add(i, 1);

            var heads = new HashSet<int>();
            var tails = new HashSet<int>();
            for (int round = 0; round < 200; round++)
            {
                heads.Add(bag.Pick());
                bag.Pick();
                tails.Add(bag.Pick());
            }

            Assert.AreEqual(3, heads.Count, "每项都该当过轮首");
            Assert.AreEqual(3, tails.Count, "每项都该当过轮尾");
        }

        [Test]
        public void Pick_Weighted_EveryRoundMatchesTemplateExactly()
        {
            var bag = new ShuffleBag<string>(8);
            bag.Add("a", 3);
            bag.Add("b", 2);
            bag.Add("c", 1);
            Assert.AreEqual(6, bag.Size, "Size 为权重之和");

            for (int round = 0; round < 200; round++)
            {
                var counts = new Dictionary<string, int>();
                for (int i = 0; i < 6; i++)
                {
                    string item = bag.Pick();
                    counts.TryGetValue(item, out int count);
                    counts[item] = count + 1;
                }

                Assert.AreEqual(3, CountsOf(counts, "a"), $"第 {round} 轮 a 的出现数");
                Assert.AreEqual(2, CountsOf(counts, "b"), $"第 {round} 轮 b 的出现数");
                Assert.AreEqual(1, CountsOf(counts, "c"), $"第 {round} 轮 c 的出现数");
            }
        }

        [Test]
        public void Add_MidRound_KeepsCurrentRoundAndAppliesNextRound()
        {
            var bag = new ShuffleBag<int>(4);
            bag.Add(1, 1);
            bag.Add(2, 1);
            bag.Add(3, 1);

            int first = bag.Pick();
            int second = bag.Pick();
            Assert.AreEqual(1, bag.Remaining);

            // 旧实现在这里把游标拨回袋尾，本轮已取出的两项重新可取
            bag.Add(4, 1);
            Assert.AreEqual(1, bag.Remaining, "Add 不得改动本轮剩余");

            int third = bag.Pick();
            Assert.AreNotEqual(first, third);
            Assert.AreNotEqual(second, third, "本轮仍是原来三项");
            Assert.AreEqual(0, bag.Remaining);

            var nextRound = new HashSet<int> { bag.Pick(), bag.Pick(), bag.Pick(), bag.Pick() };
            Assert.AreEqual(4, nextRound.Count, "新项自下一轮起生效，且四项各一次");
        }

        [Test]
        public void Pick_EmptyBag_ReturnsDefaultInsteadOfThrowing()
        {
            var numbers = new ShuffleBag<int>(4);
            Assert.AreEqual(0, numbers.Size);
            Assert.AreEqual(0, numbers.Pick());
            Assert.AreEqual(0, numbers.Remaining);

            var texts = new ShuffleBag<string>(2);
            Assert.IsNull(texts.Pick());
        }

        [Test]
        public void Reset_StartsFreshRoundAndForgetsLastPick()
        {
            var bag = new ShuffleBag<int>(3);
            bag.Add(1, 1);
            bag.Add(2, 1);
            bag.Add(3, 1);
            bag.Pick();
            bag.Pick();

            bag.Reset();
            Assert.AreEqual(0, bag.Remaining);
            Assert.AreEqual(3, bag.Size, "Reset 只丢本轮，权重表保留");
            Assert.AreEqual(0, bag.CurrentItem);

            var seen = new HashSet<int>();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(seen.Add(bag.Pick()), "Reset 后重开一轮应再次全覆盖");
            }
        }

        [Test]
        public void Clear_EmptiesTemplateToo()
        {
            var bag = new ShuffleBag<int>(3);
            bag.Add(7, 2);
            bag.Pick();

            bag.Clear();
            Assert.AreEqual(0, bag.Size);
            Assert.AreEqual(0, bag.Remaining);
            Assert.AreEqual(0, bag.Pick());
        }

        [Test]
        public void CurrentItem_TracksLastPickAndSingleItemBagRepeatsIt()
        {
            var bag = new ShuffleBag<string>(1);
            bag.Add("solo", 1);

            Assert.AreEqual("solo", bag.Pick());
            Assert.AreEqual("solo", bag.CurrentItem);
            Assert.AreEqual("solo", bag.Pick(), "只有一项时无可避免地回到它");
        }

        private static int CountsOf(Dictionary<string, int> counts, string key)
        {
            counts.TryGetValue(key, out int count);
            return count;
        }
    }
}
