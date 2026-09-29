using System.Collections.Generic;
using Moirai.Atropos.Audio;
using NUnit.Framework;

namespace Service.Audio
{
    /// <summary>
    /// 留池视图（<c>PoolReadOnly</c> 现算投影）的结构回归：不与主表镜像同步、枚举期间卸载不抛。
    /// </summary>
    /// <remarks>分配计量见 Tests/Player 的同名基准；编辑器托管分配计数器不推进，分配断言在此无条件成立。</remarks>
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

        /// <summary>留池视图是现算投影，不是要与主表同步维护的镜像表。</summary>
        /// <remarks>
        /// 取用/归还/抬升策略不再向镜像字典记账，装箱只可能发生在枚举/取值这一次冷路径上； <br />
        /// 「视图里还在、缓存里已无」的残影因此成为结构上不可表达的状态——本用例锁的正是它。
        /// </remarks>
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

        /// <summary>枚举期间卸载条目不得抛：视图先摘快照再交出去。</summary>
        /// <remarks>交出去的是枚举快照而非包装底层字典，故「边看边清」这一调试面板与兼容入口的真实用法不会触发 <c>InvalidOperationException</c>。</remarks>
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
