using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Resource
{
    /// <summary>
    /// ResourceKey / ResourceLeaseHandle / ResourceAssetLease 值语义测试。
    /// </summary>
    public sealed class ResourceTypesTests
    {
        #region 租约句柄 [LEASE HANDLE]

        [Test]
        public void LeaseHandle_InvalidConstant_IsNotValid()
        {
            Assert.IsFalse(ResourceLeaseHandle.Invalid.IsValid);
        }

        [Test]
        public void LeaseHandle_Constructor_FieldsRoundTrip()
        {
            var handle = new ResourceLeaseHandle(7, 42u);

            Assert.AreEqual(7, handle.Index);
            Assert.AreEqual(42u, handle.Generation);
            Assert.IsTrue(handle.IsValid);
        }

        [Test]
        public void LeaseHandle_ZeroGeneration_IsInvalid()
        {
            var handle = new ResourceLeaseHandle(3, 0u);

            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void LeaseHandle_NegativeIndex_IsInvalid()
        {
            var handle = new ResourceLeaseHandle(-2, 5u);

            Assert.IsFalse(handle.IsValid);
        }

        #endregion

        #region 资源键 [RESOURCE KEY]

        [Test]
        public void Key_AsFactory_FillsTypeKindAndLocation()
        {
            var key = ResourceKey.Asset<Texture>("UI/Heart", "Pkg");

            Assert.AreEqual("UI/Heart", key.Location);
            Assert.AreEqual("Pkg", key.PackageName);
            Assert.AreEqual(typeof(Texture), key.AssetType);
            Assert.AreEqual(EResourceAssetKind.Asset, key.AssetKind);
            Assert.IsFalse(key.HasResolvedIds);
        }

        [Test]
        public void Key_NullPackage_CoalescedToEmptyString()
        {
            var key = new ResourceKey("UI/Icon", null);

            Assert.AreEqual(string.Empty, key.PackageName);
            Assert.AreEqual("UI/Icon", key.Location);
        }

        [Test]
        public void Key_LoadKeyIdConstructor_HasResolvedIds()
        {
            var key = new ResourceKey(11, 22);

            Assert.IsTrue(key.HasResolvedIds);
            Assert.IsEmpty(key.Location);
            Assert.IsNull(key.AssetType);
        }

        #endregion

        #region 类型化租约 [TYPED LEASE]

        [Test]
        public void TypedLease_DefaultStruct_IsNotValidAndDisposeIsSafe()
        {
            var lease = default(ResourceAssetLease<Object>);

            Assert.IsFalse(lease.IsValid);

            Assert.DoesNotThrow(lease.Dispose);
            Assert.IsFalse(lease.IsValid);
        }

        [Test]
        public void TypedLease_Dispose_TwiceReleasesExactlyOnce()
        {
            GameObject owner = new GameObject("LeaseDisposeProbe");
            try
            {
                var lease = new ResourceAssetLease<Object>(null, new ResourceLeaseHandle(1, 9u), owner);

                // 注意：不能经由方法组/lambda 调用——struct 作为 receiver 会按副本捕获，
                // Dispose 将作用于副本导致测试失效。此处必须直接在本地变量上调用。
                lease.Dispose();

                // handler 为 null 时首次 Dispose 不应抛出，且清空全部状态。
                Assert.IsFalse(lease.IsValid);
                Assert.IsNull(lease.Asset);

                // 第二次 Dispose 因句柄已置 Invalid 直接短路。
                lease.Dispose();
                Assert.IsFalse(lease.IsValid);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void TypedLease_WithValidHandleAndAsset_IsValid()
        {
            GameObject owner = new GameObject("LeaseValidProbe");
            try
            {
                var lease = new ResourceAssetLease<Object>(null, new ResourceLeaseHandle(2, 7u), owner);

                Assert.IsTrue(lease.IsValid);
                Assert.AreSame(owner, lease.Asset);
                Assert.AreEqual(2, lease.Handle.Index);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        #endregion
    }
}
