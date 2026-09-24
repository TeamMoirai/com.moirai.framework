using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// 同步加载去重的接力契约：同 key 在途时不得再开一次后端加载，只能读赢家已落地的那条记录。
    /// <para>修复前同步侧 <c>GetOrLoadAsset</c> 在 <c>TryBeginLoading</c> 失败后直接再发一次
    /// <c>GetHandleSync</c>——双句柄双计引用，且后完成的赢家会把先落地的句柄 Dispose 掉。
    /// 同步 API 不能 await，同栈重入又会让「等赢家完成」变成死锁，故契约是：
    /// 记录已落地则接力同一条；仍在途则 fail-fast，调用方改用异步 API。</para>
    /// <para>本组只钉内核去重槽与记录接力，不碰 YooAssets 静态表。</para>
    /// </summary>
    public sealed class ResourceRecordStoreLoadingJoinTests
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
        /// 赢家占住去重槽期间，第二个同 key 请求不得再开加载；槽计数恒为 1。
        /// </summary>
        [Test]
        public void TryBeginLoading_InFlight_SecondRequestIsRejectedWithoutNewSlot()
        {
            ulong key = LoadingKey("loc_join");

            Assert.IsTrue(_store.TryBeginLoading(key), "首个请求应成为赢家");
            Assert.IsFalse(_store.TryBeginLoading(key), "在途时第二个请求不得再开加载");
            Assert.AreEqual(1, _store.LoadingOperationCount, "去重槽只该有一座");
        }

        /// <summary>
        /// 在途且记录未落地时，接力读取必须失败——同步侧据此 fail-fast，而不是去开第二次后端加载。
        /// </summary>
        [Test]
        public void TryGetCachedAssetRecord_InFlightWithoutRecord_ReturnsFalse()
        {
            ulong key = LoadingKey("loc_join");
            Assert.IsTrue(_store.TryBeginLoading(key));

            Assert.IsFalse(TryJoinRecord("loc_join", out UObject asset), "记录未落地时不得假装接力成功");
            Assert.IsNull(asset);
        }

        /// <summary>
        /// 赢家落地记录后，接力读到的是同一条记录；去重槽闭环归池。
        /// </summary>
        [Test]
        public void TryGetCachedAssetRecord_AfterWinnerCompletes_ReadsSameRecord()
        {
            const string location = "loc_join";
            ulong loadingKey = LoadingKey(location);
            Assert.IsTrue(_store.TryBeginLoading(loadingKey));

            int assetId = CreateRecordWithSprite(location);
            _store.CompleteLoading(loadingKey);

            Assert.IsTrue(TryJoinRecord(location, out UObject asset), "记录已落地，接力应成功");
            Assert.AreSame(_sprite, asset, "接力必须读赢家那条记录，而不是另建一条");
            Assert.AreEqual(0, _store.LoadingOperationCount, "完成后去重槽必须闭环");
            Assert.IsTrue(_store.TryGetRecordId(RecordKey(location), out int joinedId));
            Assert.AreEqual(assetId, joinedId);
        }

        /// <summary>
        /// 赢家失败时去重槽必须闭环；后续请求可以重新成为赢家，不会永久中毒。
        /// </summary>
        [Test]
        public void FailLoading_ClosesSlot_SoNextRequestCanWin()
        {
            const string location = "loc_fail";
            ulong loadingKey = LoadingKey(location);
            Assert.IsTrue(_store.TryBeginLoading(loadingKey));

            _store.FailLoading(loadingKey, null);
            Assert.AreEqual(0, _store.LoadingOperationCount, "失败路径必须把去重槽还回池");

            Assert.IsTrue(_store.TryBeginLoading(loadingKey), "闭环后下一个请求应能重新成为赢家");
            _store.CompleteLoading(loadingKey);
            Assert.AreEqual(0, _store.LoadingOperationCount);
        }

        /// <summary>
        /// 赢家把记录落地、但尚未 CompleteLoading 的窗口：接力仍读得到记录（缓存优先于去重槽）。
        /// </summary>
        [Test]
        public void TryGetCachedAssetRecord_RecordLandedBeforeComplete_IsAlreadyJoinable()
        {
            const string location = "loc_window";
            ulong loadingKey = LoadingKey(location);
            Assert.IsTrue(_store.TryBeginLoading(loadingKey));

            CreateRecordWithSprite(location);
            Assert.IsTrue(TryJoinRecord(location, out _), "记录一落地就可接力，无需等 CompleteLoading");
        }

        private ulong LoadingKey(string location)
        {
            return _store.GetLoadingOperationKey(location, PackageName, typeof(Sprite), EResourceAssetKind.Sprite);
        }

        private ulong RecordKey(string location)
        {
            return _store.GetAssetRecordKey(PackageName, location, typeof(Sprite), EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle);
        }

        private int CreateRecordWithSprite(string location)
        {
            return _store.GetOrCreateAssetRecord(PackageName, location, typeof(Sprite), EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle, _sprite, new StubHandle());
        }

        /// <summary>
        /// 同步侧接力判定的等价口径：只读已落地记录，读不到即禁止再开加载。
        /// </summary>
        private bool TryJoinRecord(string location, out UObject asset)
        {
            return _store.TryGetCachedAssetRecord(PackageName, location, typeof(Sprite), EResourceAssetKind.Sprite,
                EResourceHandleKind.AssetHandle, out _, out asset);
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
