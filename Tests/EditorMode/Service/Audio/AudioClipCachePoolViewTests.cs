using System.Collections.Generic;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 留池视图（<c>PoolReadOnly</c> 现算投影）的结构回归：视图不得与主表镜像同步、枚举期间卸载不得抛。
    /// <para>分配计量部分已迁至 Tests/Player 的同名基准（编辑器托管分配计数器不推进，
    /// 分配断言在编辑器内无条件成立——测不出分配不等于没有分配）。</para>
    /// </summary>
    [TestFixture]
    public class AudioClipCachePoolViewTests
    {
        private const string A = "Audio/Sfx/Confirm";
        private const string B = "Audio/Sfx/LevelUp";

        private AudioCacheTestSupport _fixture;

        [SetUp]
        public void SetUp()
        {
            _fixture = new AudioCacheTestSupport(capacity: 8, ttl: 30f);
        }

        [TearDown]
        public void TearDown()
        {
            var fixture = _fixture;
            _fixture = null;
            fixture?.Dispose();
        }

        /// <summary>
        /// 留池视图是**现算投影**，不是一份要与主表同步维护的镜像表。
        /// <para>旧实现每次取用/归还/抬升策略都往镜像字典写一笔（为此还要在条目上挂一只装箱副本防止重复装箱），
        /// 漏掉一处同步就留下"视图里还在、缓存里已无"的残影。改成投影后，装箱只可能发生在枚举/取值这一次
        /// 冷路径上，而残影成为结构上不可表达的状态——这里锁的正是它。</para>
        /// </summary>
        [Test]
        public void PoolView_IsComputedProjection_LeavesNoStaleEntry()
        {
            Assert.IsTrue(_fixture.Cache.Preload(A, EAudioCachePolicy.Ttl));
            Assert.IsTrue(_fixture.Cache.PoolReadOnly.ContainsKey(A));
            Assert.AreEqual(1, _fixture.Cache.PoolReadOnly.Count);

            var entry = _fixture.Entry(A);

            // 三个曾经各要刷一次镜像表的热点：命中取用、停播归还、策略抬升
            Assert.IsTrue(_fixture.Cache.Preload(A, EAudioCachePolicy.Ttl));
            _fixture.Cache.Retain(entry);
            _fixture.Cache.Release(entry);
            Assert.IsTrue(_fixture.Cache.Preload(A, EAudioCachePolicy.Pin));
            Assert.AreEqual(1, _fixture.Cache.PoolReadOnly.Count, "热点上不该改变视图规模");

            Assert.IsTrue(_fixture.Cache.PoolReadOnly.TryGetValue(A, out object value));
            Assert.IsTrue(((AudioClipLease)value).IsValid, "视图取到的必须是仍持租约的条目");

            Assert.IsTrue(_fixture.Cache.Unload(A, force: true));
            Assert.IsFalse(_fixture.Cache.PoolReadOnly.ContainsKey(A), "摘除后视图必须立刻失明——镜像表忘删就是这里出残影");
            Assert.AreEqual(0, _fixture.Cache.PoolReadOnly.Count);
        }

        /// <summary>
        /// 枚举期间卸载条目不得炸：视图必须先摘快照再交出去。
        /// <para>旧实现返回的是 <c>ReadOnlyDictionary</c> 包装，枚举底层字典时任何写操作都会
        /// <c>InvalidOperationException</c>；而"边看边清"恰恰是调试面板与兼容入口的真实用法。</para>
        /// </summary>
        [Test]
        public void PoolView_EnumerateWhileUnloading_DoesNotThrow()
        {
            Assert.IsTrue(_fixture.Cache.Preload(A, EAudioCachePolicy.Pin));
            Assert.IsTrue(_fixture.Cache.Preload(B, EAudioCachePolicy.Pin));

            var seen = new List<string>();
            foreach (var kv in _fixture.Cache.PoolReadOnly)
            {
                seen.Add(kv.Key);
                // 在枚举体内摘掉另一条：现算快照下这一步只会让本轮多报一条，绝不抛
                _fixture.Cache.Unload(B, force: true);
            }

            Assert.AreEqual(2, seen.Count, "两条 Pin 条目都应在本轮快照里被看到");
        }
    }
}
