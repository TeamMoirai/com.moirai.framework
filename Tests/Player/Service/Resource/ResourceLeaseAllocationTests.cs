using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using Testing;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// 租约热路径的 0-GC 验收：稳态取用 / 归还、按 key 直查缓存、打包 key 往返，三趟都不得分配。
    /// </summary>
    /// <remarks>
    /// 分配观测走 <c>GC.Alloc</c> 采样事件数；Unity 内不存在字节口径的 GC 计数 API，事件口径是唯一通道。
    /// 与 <c>ResourceBindingAllocationTests</c> 同住 <c>Moirai.Atropos.Tests.Player</c>（<c>UNITY_INCLUDE_TESTS</c>）： 采样探不到的运行时整组 <br />
    /// Ignore，验收以 L3 玩家运行收到的采样为准。
    /// 量的是记录内核（分页槽位 + <see cref="ResourceUlongIntMap"/>）自己那几趟，不掺真后端的原生调用。
    /// 名称轴解析已收成「打包一次、按 key 直查」，这几格把「热路径不再走三条字典往返」钉成门禁。
    /// </remarks>
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
            // NUnit 断言自身分配（Constraint 链每格 5~9 个 GC.Alloc 事件，2026-09-28 实测）——
            // 断言留在测量窗外：窗内只取值，窗外判。
            ResourceLeaseHandle lastHandle = default;
            AllocationCapture.MeasureManaged("Lease.AcquireRelease", Iterations, () =>
            {
                lastHandle = _store.AcquireLease(_assetId, EResourceLeaseKind.Direct,
                    EResourceLeaseOption.None);
                _store.Release(lastHandle);
            }, bytes => Assert.AreEqual(0, bytes, "稳态取用/归还出现了分配"));
            Assert.IsTrue(lastHandle.IsValid, "末轮取到的租约应当有效");
        }

        /// <summary>
        /// 按已打包 key 直查缓存记录：开地址表命中不得分配。
        /// </summary>
        [Test]
        public void TryGetCachedAssetRecordByKey_ZeroAlloc()
        {
            // 断言外移（NUnit 断言自身分配，见上）：窗内只取值，窗外判。
            bool found = false;
            int assetId = 0;
            UObject asset = null;
            AllocationCapture.MeasureManaged("Lease.TryGetByKey", Iterations,
                () => found = _store.TryGetCachedAssetRecordByKey(_recordKey, out assetId, out asset),
                bytes => Assert.AreEqual(0, bytes, "按 key 直查出现了分配"));
            Assert.IsTrue(found, "驻留 key 应当命中");
            Assert.AreEqual(_assetId, assetId, "命中的记录 id 应一致");
            Assert.AreSame(_sprite, asset, "命中的资产应当是同一实例");
        }

        /// <summary>
        /// 名称轴已登记后的打包 key 往返：GetOrAdd 命中路径不得分配。
        /// </summary>
        [Test]
        public void GetAssetRecordKey_WhenNamesResident_ZeroAlloc()
        {
            // 断言外移（NUnit 断言自身分配）：窗内只取值，窗外判。
            ulong key = 0;
            AllocationCapture.MeasureManaged("Lease.PackKey", Iterations,
                () => key = _store.GetAssetRecordKey(PackageName, "UI/Heart", typeof(Sprite),
                    EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle),
                bytes => Assert.AreEqual(0, bytes, "驻留名称的打包 key 出现了分配"));
            Assert.AreEqual(_recordKey, key, "驻留名称打包出的 key 应当稳定一致");
        }

        /// <summary>
        /// 取租约 → 读资产 → 归还，整条业务往返不得分配。
        /// </summary>
        [Test]
        public void Acquire_TryGetLeaseAsset_Release_ZeroAlloc()
        {
            // 断言外移（NUnit 断言自身分配）：窗内只取值，窗外判。
            bool assetRead = false;
            UObject asset = null;
            AllocationCapture.MeasureManaged("Lease.FullRoundTrip", Iterations, () =>
            {
                ResourceLeaseHandle handle = _store.AcquireLease(_assetId, EResourceLeaseKind.Direct,
                    EResourceLeaseOption.None);
                assetRead = _store.TryGetLeaseAsset(handle, out asset);
                _store.Release(handle);
            }, bytes => Assert.AreEqual(0, bytes, "完整租约往返出现了分配"));
            Assert.IsTrue(assetRead, "末轮应当读到资产");
            Assert.AreSame(_sprite, asset, "读到的应当是同一实例");
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
