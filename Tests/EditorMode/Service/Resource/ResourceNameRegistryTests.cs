using System;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// ResourceNameRegistry 的行为契约：三条 packed key 名称轴共用一份实现，
    /// 所以这份实现必须逐条对上原来三份拷贝的语义，尤其是两处"看着一样其实不同"的分岔。
    /// </summary>
    public sealed class ResourceNameRegistryTests
    {
        private const int Max = 8;

        [Test]
        public void GetOrAdd_Deduplicates_AndIsStablePerValue()
        {
            var registry = new ResourceNameRegistry<string>(Max, string.Empty);

            int a = registry.GetOrAdd("alpha");
            int b = registry.GetOrAdd("beta");
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(a, registry.GetOrAdd("alpha"), "同名必须回同一个 id，否则同一条资源会编出两个键");
        }

        [Test]
        public void TryGetId_DoesNotIntern()
        {
            var registry = new ResourceNameRegistry<string>(Max, string.Empty);
            registry.GetOrAdd("known");

            Assert.IsTrue(registry.TryGetId("known", out int knownId));
            Assert.IsFalse(registry.TryGetId("unknown", out _),
                "非驻留取键路径不得登记新 id——它会顺手撑大 id 空间");


            // 关键判据：三次落空不该烧掉任何 id——若 TryGetId 顺手登记，
            // 之后第一次 GetOrAdd 拿到的就不是 1 号了。
            registry.TryGetId("ghost-1", out _);
            registry.TryGetId("ghost-2", out _);
            registry.TryGetId("ghost-3", out _);
            Assert.AreEqual(2, registry.GetOrAdd("fresh"),
                "非驻留查询不得占用 id：位宽有限，烧掉的 id 会让键空间提前耗尽");
        }

        [Test]
        public void GetValue_RespectsPerAxisMissValue()
        {
            var strings = new ResourceNameRegistry<string>(Max, string.Empty);
            var types = new ResourceNameRegistry<Type>(Max, null);

            Assert.AreEqual(string.Empty, strings.GetValue(0), "id 0 是保留位，必须回 miss 值");
            Assert.AreEqual(string.Empty, strings.GetValue(999));
            Assert.IsNull(types.GetValue(3), "类型轴的 miss 是 null，不是 string.Empty");

            int id = strings.GetOrAdd("x");
            Assert.AreEqual("x", strings.GetValue(id));
            strings.Retain(id);
            strings.Release(id);
            Assert.AreEqual(string.Empty, strings.GetValue(id), "回收后槽位必须清空");
        }

        [Test]
        public void Release_ReclaimsIdOnlyAtZero()
        {
            var registry = new ResourceNameRegistry<string>(Max, string.Empty);
            int id = registry.GetOrAdd("shared");
            int nextFresh = registry.GetOrAdd("other");

            registry.Retain(id);
            registry.Retain(id);
            registry.Release(id);
            Assert.AreEqual("shared", registry.GetValue(id), "计数未归零不得回收");

            registry.Release(id);
            Assert.AreEqual(string.Empty, registry.GetValue(id), "归零即回收名字与 id");
            Assert.AreEqual(id, registry.GetOrAdd("shared"), "回收的 id 必须被复用，否则位宽会被白耗");
            Assert.AreNotEqual(nextFresh, id);
        }

        [Test]
        public void DecrementOnly_DiffersFromRelease()
        {
            // 这是原实现里两条分开的路：整表清空只减数，不摘字典也不回收 id。
            // 合并成一份实现时最容易被顺手并掉，并掉之后 bulk clear 会反向改动字典。
            var registry = new ResourceNameRegistry<string>(Max, string.Empty);
            int id = registry.GetOrAdd("bulk");
            registry.Retain(id);

            registry.DecrementOnly(id);

            Assert.IsTrue(registry.TryGetId("bulk", out int stillThere));
            Assert.AreEqual(id, stillThere, "DecrementOnly 不该把名字从字典摘掉");
            Assert.AreEqual(id, registry.GetValue(id) == "bulk" ? id : -1, "槽位也必须留着");
        }

        [Test]
        public void Retain_Release_OutOfRangeIdsAreSilentlyIgnored()
        {
            var registry = new ResourceNameRegistry<string>(Max, string.Empty);

            Assert.DoesNotThrow(() =>
            {
                registry.Retain(0);
                registry.Retain(-1);
                registry.Retain(4242);
                registry.Release(0);
                registry.Release(-5);
                registry.Release(9999);
                registry.DecrementOnly(0);
                registry.DecrementOnly(7777);
            });
        }

        [Test]
        public void ExceedingAxisWidth_Throws_InsteadOfTruncating()
        {
            var registry = new ResourceNameRegistry<string>(Max, string.Empty);
            for (int i = 0; i < Max; i++)
            {
                registry.GetOrAdd("name-" + i);
            }

            Assert.Throws<GameException>(() => registry.GetOrAdd("one-too-many"),
                "超出该轴位宽必须抛：截断会让两条资源编出同一个键");
        }

    }
}
