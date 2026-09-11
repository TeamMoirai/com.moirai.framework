using Moirai.Atropos.ObjectPool;
using NUnit.Framework;

namespace Service.ObjectPool
{
    /// <summary>
    /// SlotArrayPool 回归测试：零长度、长度向上取整、同长度复用、清零归还、超大数组丢弃。
    /// <para>池为静态共享——用例尽量使用独立长度档位，避免互相污染。</para>
    /// </summary>
    public sealed class SlotArrayPoolTests
    {
        #region 空与长度 [EMPTY & LENGTH]

        [Test]
        public void Rent_ZeroOrNegative_ReturnsSharedEmpty()
        {
            Assert.AreSame(System.Array.Empty<int>(), SlotArrayPool<int>.Rent(0));
            Assert.AreSame(System.Array.Empty<int>(), SlotArrayPool<int>.Rent(-1));
            Assert.AreEqual(0, SlotArrayPool<int>.Rent(0).Length);
        }

        [Test]
        public void Rent_PositiveCount_LengthAtLeastRequest()
        {
            int[] small = SlotArrayPool<int>.Rent(1);
            int[] mid = SlotArrayPool<int>.Rent(20);
            int[] exact = SlotArrayPool<int>.Rent(64);

            try
            {
                Assert.GreaterOrEqual(small.Length, 1);
                Assert.GreaterOrEqual(mid.Length, 20);
                Assert.GreaterOrEqual(exact.Length, 64);
            }
            finally
            {
                SlotArrayPool<int>.Return(small, true);
                SlotArrayPool<int>.Return(mid, true);
                SlotArrayPool<int>.Return(exact, true);
            }
        }

        #endregion

        #region 复用与清零 [REUSE & CLEAR]

        [Test]
        public void Return_SameLength_RentReusesInstance()
        {
            // 使用偏大档位，降低与其它用例争用同一桶的概率。
            const int count = 1 << 10;
            int[] first = SlotArrayPool<int>.Rent(count);

            SlotArrayPool<int>.Return(first, clearArray: true);
            int[] second = SlotArrayPool<int>.Rent(count);

            try
            {
                Assert.AreSame(first, second, "same-bucket rent must pop the just-returned array");
            }
            finally
            {
                SlotArrayPool<int>.Return(second, true);
            }
        }

        [Test]
        public void Return_ClearArray_True_ZerosContents()
        {
            const int count = 1 << 11;
            int[] rented = SlotArrayPool<int>.Rent(count);
            for (int i = 0; i < rented.Length; i++)
            {
                rented[i] = i + 1;
            }

            SlotArrayPool<int>.Return(rented, clearArray: true);
            int[] again = SlotArrayPool<int>.Rent(count);

            try
            {
                Assert.AreSame(rented, again);
                for (int i = 0; i < again.Length; i++)
                {
                    Assert.AreEqual(0, again[i], "clearArray=true must wipe slot " + i);
                }
            }
            finally
            {
                SlotArrayPool<int>.Return(again, true);
            }
        }

        [Test]
        public void Return_ClearArray_False_KeepsContents()
        {
            const int count = 1 << 12;
            int[] rented = SlotArrayPool<int>.Rent(count);
            rented[0] = 123;
            rented[rented.Length - 1] = 456;

            SlotArrayPool<int>.Return(rented, clearArray: false);
            int[] again = SlotArrayPool<int>.Rent(count);

            try
            {
                Assert.AreSame(rented, again);
                Assert.AreEqual(123, again[0], "clearArray=false keeps data for caller reuse");
                Assert.AreEqual(456, again[again.Length - 1]);
            }
            finally
            {
                // 以清零方式归还，避免脏数据泄漏到后续用例。
                SlotArrayPool<int>.Return(again, true);
            }
        }

        [Test]
        public void Return_NullOrEmpty_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => SlotArrayPool<int>.Return(null, true));
            Assert.DoesNotThrow(() => SlotArrayPool<int>.Return(System.Array.Empty<int>(), true));
        }

        #endregion

        #region 超大数组 [OVERSIZED]

        [Test]
        public void Rent_BeyondMaxBucket_AllocatesExactLengthAndIsNotPooled()
        {
            // 最大桶为 MIN_BUCKET_SIZE(16) << (MAX_BUCKETS-1) = 2^21；再大必须精确分配且不入池。
            const int oversized = (1 << 21) + 1;
            byte[] first = SlotArrayPool<byte>.Rent(oversized);

            try
            {
                Assert.AreEqual(oversized, first.Length, "oversized rent must be exact-length, not clamped to max bucket");
            }
            finally
            {
                SlotArrayPool<byte>.Return(first, clearArray: true);
            }

            byte[] second = SlotArrayPool<byte>.Rent(oversized);
            try
            {
                Assert.AreEqual(oversized, second.Length);
                Assert.AreNotSame(first, second, "oversized arrays must be discarded on Return, not recycled");
            }
            finally
            {
                SlotArrayPool<byte>.Return(second, clearArray: true);
            }
        }

        #endregion
    }
}
