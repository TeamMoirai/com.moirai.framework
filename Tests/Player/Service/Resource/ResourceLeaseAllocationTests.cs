using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using Testing;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// 租约热路径的 0-GC 验收：稳态取用/归还、按 key 直查缓存、打包 key 往返，三趟都不得分配。
    /// <para><b>真机计量</b>：测量走 <c>GC.Alloc</c> 采样事件数（UTF 官方 AllocatingGCMemory 同机制；
    /// 字节口径 GC 计数 API 在 Unity 内无实现），故与 <c>ResourceBindingAllocationTests</c> 同住
    /// <c>Moirai.Atropos.Tests.Player</c>——采样探不到的运行时整组 Ignore，验收以 L3 玩家运行收到的采样为准。</para>
    /// <para>量的是记录内核（分页槽位 + <see cref="ResourceUlongIntMap"/>）自己那几趟，
    /// 不掺真后端的原生调用——那只会把噪声计进来。名称轴解析已收成「打包一次、按 key 直查」，
    /// 这几格就是把「热路径不再走三条字典往返」钉成门禁。</para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class ResourceLeaseAllocationTests
    {
        private const int Iterations = 1024;
        private const string PackageName = "DefaultPackage";

        private Sprite _sprite;
        private Texture2D _texture;
        private StubRecordHost _host;
        private ResourceRecordStore _store;
        private ulong _recordKey;
        private int _assetId;

        [SetUp]
        public void SetUp()
        {
            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _host = new StubRecordHost();
            _store = new ResourceRecordStore(_host, () => PackageName);
            _store.EnsureRecordCapacity(64);
            _store.EnsureLoadingOperationCapacity(64);

            _recordKey = _store.GetAssetRecordKey(PackageName, "UI/Heart", typeof(Sprite),
                EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle);
            _assetId = _store.GetOrCreateAssetRecordByKey(_recordKey, EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle, _sprite, new StubHandle());
        }

        [TearDown]
        public void TearDown()
        {
            if (_sprite != null)
            {
                Object.Destroy(_sprite);
            }

            if (_texture != null)
            {
                Object.Destroy(_texture);
            }

            _store = null;
            _host = null;
        }

        /// <summary>
        /// 稳态 AcquireLease + Release：槽位借还与引用计数不得分配。
        /// </summary>
        [Test]
        public void AcquireLease_Release_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Lease.AcquireRelease", Iterations, () =>
            {
                ResourceLeaseHandle handle = _store.AcquireLease(_assetId, EResourceLeaseKind.Direct,
                    EResourceLeaseOption.None);
                Assert.IsTrue(handle.IsValid);
                _store.Release(handle);
            }, bytes => Assert.AreEqual(0, bytes, "稳态取用/归还出现了分配"));
        }

        /// <summary>
        /// 按已打包 key 直查缓存记录：开地址表命中不得分配。
        /// </summary>
        [Test]
        public void TryGetCachedAssetRecordByKey_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Lease.TryGetByKey", Iterations,
                () =>
                {
                    bool found = _store.TryGetCachedAssetRecordByKey(_recordKey, out int assetId, out UObject asset);
                    Assert.IsTrue(found);
                    Assert.AreEqual(_assetId, assetId);
                    Assert.AreSame(_sprite, asset);
                },
                bytes => Assert.AreEqual(0, bytes, "按 key 直查出现了分配"));
        }

        /// <summary>
        /// 名称轴已登记后的打包 key 往返：GetOrAdd 命中路径不得分配。
        /// </summary>
        [Test]
        public void GetAssetRecordKey_WhenNamesResident_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Lease.PackKey", Iterations,
                () =>
                {
                    ulong key = _store.GetAssetRecordKey(PackageName, "UI/Heart", typeof(Sprite),
                        EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle);
                    Assert.AreEqual(_recordKey, key);
                },
                bytes => Assert.AreEqual(0, bytes, "驻留名称的打包 key 出现了分配"));
        }

        /// <summary>
        /// 取租约 → 读资产 → 归还，整条业务往返不得分配。
        /// </summary>
        [Test]
        public void Acquire_TryGetLeaseAsset_Release_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Lease.FullRoundTrip", Iterations, () =>
            {
                ResourceLeaseHandle handle = _store.AcquireLease(_assetId, EResourceLeaseKind.Direct,
                    EResourceLeaseOption.None);
                Assert.IsTrue(_store.TryGetLeaseAsset(handle, out UObject asset));
                Assert.AreSame(_sprite, asset);
                _store.Release(handle);
            }, bytes => Assert.AreEqual(0, bytes, "完整租约往返出现了分配"));
        }

        private sealed class StubRecordHost : IResourceRecordHost
        {
            public bool IsHandleValid(object handle) => handle is StubHandle stub && !stub.Released;

            public void DisposeHandle(object handle)
            {
                if (handle is StubHandle stub)
                {
                    stub.Released = true;
                }
            }

            public Sprite GetSubSprite(object handle, string spriteName) => null;

            public int IdleAssetCapacity => 256;

            public float IdleAssetExpireTime => 60f;

            public int AssetRecordCapacity => 64;
        }

        private sealed class StubHandle
        {
            internal bool Released;
        }
    }
}
