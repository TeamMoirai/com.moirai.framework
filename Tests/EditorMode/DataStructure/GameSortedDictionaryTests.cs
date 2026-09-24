using Moirai.Atropos;
using NUnit.Framework;

namespace DataStructure
{
    /// <summary>
    /// 验证 <see cref="GameSortedDictionary{TKey, TValue}"/> 插入后按键自动排序以及按下标按排序序访问的行为。
    /// </summary>
    public class GameSortedDictionaryTests
    {
        [Test]
        public void Add_KeysAreSortedAfterInsertion()
        {
            var dict = new GameSortedDictionary<string, int>();
            dict.Add("c", 3);
            dict.Add("a", 1);
            dict.Add("b", 2);

            Assert.AreEqual("a", dict.Keys[0]);
            Assert.AreEqual("b", dict.Keys[1]);
            Assert.AreEqual("c", dict.Keys[2]);
        }

        [Test]
        public void Add_IntKeys_SortedNumerically()
        {
            var dict = new GameSortedDictionary<int, string>();
            dict.Add(30, "thirty");
            dict.Add(10, "ten");
            dict.Add(20, "twenty");

            Assert.AreEqual(10, dict.Keys[0]);
            Assert.AreEqual(20, dict.Keys[1]);
            Assert.AreEqual(30, dict.Keys[2]);
        }

        [Test]
        public void GetValueByIndex_ReturnsSortedOrder()
        {
            var dict = new GameSortedDictionary<int, string>();
            dict.Add(3, "c");
            dict.Add(1, "a");
            dict.Add(2, "b");

            Assert.AreEqual("a", dict.GetValueByIndex(0));
            Assert.AreEqual("b", dict.GetValueByIndex(1));
            Assert.AreEqual("c", dict.GetValueByIndex(2));
        }
    }
}
