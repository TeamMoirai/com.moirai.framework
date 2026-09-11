using System;
using Moirai.Atropos.Pool;
using NUnit.Framework;

namespace Service.ObjectPool
{
    /// <summary>
    /// 内部简单对象池（_ObjectPool&lt;T&gt;）契约测试：双重释放 fail-fast、容量上限、基本收发。
    /// </summary>
    public sealed class InternalObjectPoolTests
    {
        private sealed class PooledItem
        {
            public int Value;
        }

        [Test]
        public void Release_DuplicateRelease_ThrowsAndPoolNotCorrupted()
        {
            var pool = new Internal_ObjectPool<PooledItem>(() => new PooledItem(), maxSize: 4);

            PooledItem item = pool.Get();
            pool.Release(item);

            // 双重释放：开发期 fail-fast（拒绝二次压栈，阻断"两次 Get 返回同一引用"的池污染）
            Assert.Throws<InvalidOperationException>(() => pool.Release(item));
            Assert.AreEqual(1, pool.Size(), "重复释放被拒绝后池中应只有一份实例");

            PooledItem first = pool.Get();
            PooledItem second = pool.Get();
            Assert.AreSame(item, first, "取出应得到已归还的实例");
            Assert.AreNotSame(first, second, "池中无重复引用——两次 Get 不得返回同一实例");
        }

        [Test]
        public void Get_EmptyPool_CreatesViaFactory()
        {
            var pool = new Internal_ObjectPool<PooledItem>(() => new PooledItem { Value = 42 }, maxSize: 2);

            PooledItem item = pool.Get();

            Assert.IsNotNull(item);
            Assert.AreEqual(42, item.Value, "空池应经工厂创建新实例");
        }

        [Test]
        public void Release_RespectsMaxSize()
        {
            var pool = new Internal_ObjectPool<PooledItem>(() => new PooledItem(), maxSize: 2);

            pool.Release(new PooledItem());
            pool.Release(new PooledItem());
            pool.Release(new PooledItem()); // 超出容量——丢弃不入池

            Assert.AreEqual(2, pool.Size(), "池大小不应超过 MaxSize");
        }

        [Test]
        public void MaxSize_Shrink_PopsExcess()
        {
            var pool = new Internal_ObjectPool<PooledItem>(() => new PooledItem(), maxSize: 4);
            pool.Release(new PooledItem());
            pool.Release(new PooledItem());
            pool.Release(new PooledItem());

            pool.MaxSize = 1;

            Assert.AreEqual(1, pool.Size(), "收缩 MaxSize 应弹出超量实例");
        }
    }
}
