using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// 热路径 key 直查契约：打包一次之后按 key 建/查记录，与字符串轴入口结果一致且不再走名称字典。
    /// <para>0-GC 数字在玩家侧（<c>ResourceLeaseAllocationTests</c>）；这里钉的是语义——
    /// 两条入口必须指向同一条记录，否则「打包 key 一次解析」会把缓存命中打成旁路。</para>
    /// </summary>
    public sealed class ResourceRecordStoreKeyHotPathTests
    {
        private const string PackageName = "DefaultPackage";

        private Sprite _sprite;
        private Texture2D _texture;
        private StubRecordHost _host;
        private ResourceRecordStore _store;

        [SetUp]
        public void SetUp()
        {
            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _host = new StubRecordHost();
            _store = new ResourceRecordStore(_host, () => PackageName);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sprite != null)
            {
                Object.DestroyImmediate(_sprite);
            }

            if (_texture != null)
            {
                Object.DestroyImmediate(_texture);
            }

            _store = null;
            _host = null;
        }

        /// <summary>
        /// 同一坐标反复打包 key 必须得到同一个 ulong——否则按 key 直查会永远打不中。
        /// </summary>
        [Test]
        public void GetAssetRecordKey_SameCoordinates_StableAcrossCalls()
        {
            ulong first = Pack();
            ulong second = Pack();
            Assert.AreEqual(first, second);
        }

        /// <summary>
        /// 按 key 建的记录，字符串轴入口必须查得到同一条。
        /// </summary>
        [Test]
        public void CreateByKey_IsVisibleToNameBasedLookup()
        {
            ulong key = Pack();
            int assetId = _store.GetOrCreateAssetRecordByKey(key, EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle, _sprite, new StubHandle());

            Assert.IsTrue(_store.TryGetCachedAssetRecord(PackageName, "UI/Heart", typeof(Sprite),
                EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle, out int byNameId, out UObject byName));
            Assert.AreEqual(assetId, byNameId);
            Assert.AreSame(_sprite, byName);
        }

        /// <summary>
        /// 字符串轴入口建的记录，按 key 直查必须命中同一条。
        /// </summary>
        [Test]
        public void CreateByName_IsVisibleToKeyLookup()
        {
            _store.GetOrCreateAssetRecord(PackageName, "UI/Heart", typeof(Sprite), EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle, _sprite, new StubHandle());

            Assert.IsTrue(_store.TryGetCachedAssetRecordByKey(Pack(), out _, out UObject asset));
            Assert.AreSame(_sprite, asset);
        }

        /// <summary>
        /// loading key 与 AssetHandle 口径的 record key 同值——缓存命中 / 去重 / 建记录共用一次打包。
        /// </summary>
        [Test]
        public void LoadingKey_MatchesAssetHandleRecordKey()
        {
            ulong loading = _store.GetLoadingOperationKey("UI/Heart", PackageName, typeof(Sprite),
                EResourceAssetKind.Sprite);
            ulong record = Pack();
            Assert.AreEqual(record, loading);
        }

        private ulong Pack()
        {
            return _store.GetAssetRecordKey(PackageName, "UI/Heart", typeof(Sprite), EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle);
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
