using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// 卸载账与记录内核的接线：只有真被回收的资产才入账，没收口的东西一条都不记。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="ResourceLeaseProvenanceTests"/> 的分工：那组只喂 <c>AddUnload</c> 断账本自身的环形与清空，
    /// 本组经假 <see cref="IResourceRecordHost"/> 驱动真淘汰路径，断的是"记账点挂在真回收上、且地址与类型取自被卸的那一条"。
    /// 账里的 UTC 时刻不断值，只断条数与读取序。
    /// </remarks>
    [TestFixture]
    public sealed class ResourceRecordStoreUnloadLedgerTests
    {
        private const string PACKAGE_NAME = "DefaultPackage";

        private Texture2D _texture;
        private Sprite _sprite;
        private StubRecordHost _host;
        private ResourceRecordStore _store;
        private bool _savedEnabled;

        [SetUp]
        public void SetUp()
        {
            _savedEnabled = ResourceLeaseProvenance.Enabled;
            ResourceLeaseProvenance.Enabled = false;
            ResourceLeaseProvenance.ClearUnload();
            Assert.AreEqual(0, ResourceLeaseProvenance.UnloadCount, "进门时账面必须是空的");

            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _host = new StubRecordHost();
            _store = new ResourceRecordStore(_host, () => PACKAGE_NAME);
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLeaseProvenance.Enabled = _savedEnabled;
            ResourceLeaseProvenance.ClearUnload();
            _store = null;
            _host = null;
            if (_sprite != null)
            {
                UObject.DestroyImmediate(_sprite);
            }

            if (_texture != null)
            {
                UObject.DestroyImmediate(_texture);
            }
        }

        /// <summary>真被回收的资产逐条入账，最新优先的读取序就是淘汰序。</summary>
        [Test]
        public void UnloadLedger_RecordsEveryRealRelease_InEvictionOrder()
        {
            // 容量先于入队调零：超限请求位就是入队那一下置起来的
            _host.IdleCapacity = 0;
            for (int i = 0; i < 3; i++)
            {
                EnterIdleRecord($"unloadLoc{i}", 100f - i * 10f);
            }

            _store.TrimIdleAssetCapacity(8);

            Assert.AreEqual(3, ResourceLeaseProvenance.UnloadCount, "三条真回收都该入账");
            Assert.AreEqual("unloadLoc0", ResourceLeaseProvenance.GetNewestUnload(0).Location,
                "最后出局的那条排最新——账要跟着实际淘汰序走");
            Assert.AreEqual("unloadLoc2", ResourceLeaseProvenance.GetNewestUnload(2).Location, "最早出局（最长空闲）的排最末");
            Assert.AreEqual("Sprite", ResourceLeaseProvenance.GetNewestUnload(0).TypeName, "类型名取自被卸的那一条");
        }

        /// <summary>没有一次真回收：账必须空着——还挂在租约上的记录不参与淘汰，也就不该留下任何痕迹。</summary>
        [Test]
        public void UnloadLedger_StaysSilentWhenNothingIsReleased()
        {
            int assetId = CreateRecord("busy/0");
            ResourceLeaseHandle lease = _store.AcquireLease(assetId, EResourceLeaseKind.Direct,
                EResourceLeaseOption.None);
            Assert.IsTrue(lease.IsValid, "前置不成立：取用该成功");

            _host.IdleCapacity = 0;
            _store.TrimIdleAssetCapacity(8);

            Assert.AreEqual(0, ResourceLeaseProvenance.UnloadCount, "还被租约握着的记录不该进账");

            _store.Release(lease);
            Assert.AreEqual(0, ResourceLeaseProvenance.UnloadCount, "归还本身不是回收，不该记账");
        }

        /// <summary>入账不依赖来源采集开关：关掉采集也要看得见谁被卸了。</summary>
        [Test]
        public void UnloadLedger_DoesNotDependOnSourceCapture()
        {
            ResourceLeaseProvenance.Enabled = false;
            _host.IdleCapacity = 0;
            EnterIdleRecord("toggleFree/0", 100f);

            _store.TrimIdleAssetCapacity(8);

            Assert.AreEqual(1, ResourceLeaseProvenance.UnloadCount, "采集关着也要记这条卸载");
            Assert.AreEqual("toggleFree/0", ResourceLeaseProvenance.GetNewestUnload(0).Location, "记的是被卸掉的那条地址");
        }

        /// <summary>造一条记录并取还一次，让它带着指定的空闲到期刻度进入空闲集合。</summary>
        private void EnterIdleRecord(string location, float idleExpireSeconds)
        {
            int assetId = CreateRecord(location);
            ResourceLeaseHandle lease = _store.AcquireLease(assetId, EResourceLeaseKind.Direct,
                EResourceLeaseOption.None);
            Assert.IsTrue(lease.IsValid, "前置不成立：{0} 取用失败", location);

            // 内核按活读取这个读数，所以每条记录拿到不同的空闲过期刻度
            _host.IdleExpireTime = idleExpireSeconds;
            _store.Release(lease);
        }

        /// <summary>建一条 Sprite 记录并回它的记录号。</summary>
        private int CreateRecord(string location)
        {
            StubHandle handle = new StubHandle();
            int assetId = _store.GetOrCreateAssetRecord(PACKAGE_NAME, location, typeof(Sprite),
                EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle, _sprite, handle);
            Assert.GreaterOrEqual(assetId, 0, "记录槽分配失败：{0}", location);
            return assetId;
        }

        /// <summary>只回答"句柄还活着吗 / 放掉 / 三个配置读数"的假后端接缝。</summary>
        private sealed class StubRecordHost : IResourceRecordHost
        {
            internal int IdleCapacity = 256;

            internal float IdleExpireTime = 60f;

            internal int RecordCapacity = 64;

            public bool IsHandleValid(object handle) => handle is StubHandle stub && !stub.Released;

            public void DisposeHandle(object handle)
            {
                if (handle is StubHandle stub)
                {
                    stub.Released = true;
                }
            }

            public Sprite GetSubSprite(object handle, string spriteName) => null;

            public int IdleAssetCapacity => IdleCapacity;

            public float IdleAssetExpireTime => IdleExpireTime;

            public int AssetRecordCapacity => RecordCapacity;
        }

        private sealed class StubHandle
        {
            internal bool Released;
        }
    }
}
