using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 租约来源读取面的内核契约：TryGetLeaseSource 按句柄答取用调用点，GetLeaseSourceInfos 把带来源的在途租约快照出去。
    /// </summary>
    /// <remarks>
    /// 只带一面假 <see cref="IResourceRecordHost"/> 即可直接驱动内核，无需初始化 YooAsset。 <br />
    /// 行号不逐字钉死（随用例增删漂移），断的是「落在调用方那个文件」。
    /// </remarks>
    public sealed class ResourceRecordStoreLeaseSourceTests
    {
        private const string PackageName = "DefaultPackage";

        private Sprite _sprite;
        private Texture2D _texture;
        private StubRecordHost _host;
        private ResourceRecordStore _store;
        private bool _savedProvenanceEnabled;

        [SetUp]
        public void SetUp()
        {
            _savedProvenanceEnabled = ResourceLeaseProvenance.Enabled;
            ResourceLeaseProvenance.Enabled = false;
            _texture = new Texture2D(2, 2);
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _host = new StubRecordHost();
            _store = new ResourceRecordStore(_host, () => PackageName);
        }

        [TearDown]
        public void TearDown()
        {
            ResourceLeaseProvenance.Enabled = _savedProvenanceEnabled;
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

        /// <summary>开关开着取用：来源要答出取用那一行的调用方类型、方法与文件。</summary>
        [Test]
        public void TryGetLeaseSource_SourcedLease_AnswersTheAcquiringCallSite()
        {
            ResourceLeaseProvenance.Enabled = true;
            int assetId = CreateRecord("leaseSource/0");
            ResourceLeaseHandle lease = _store.AcquireLease(assetId, EResourceLeaseKind.Direct,
                EResourceLeaseOption.None);
            Assert.IsTrue(lease.IsValid, "前置不成立：取用该成功");

            Assert.IsTrue(_store.TryGetLeaseSource(lease, out string display, out string file, out int line),
                "开着开关取的租约必须答得出来源");
            StringAssert.Contains("ResourceRecordStoreLeaseSourceTests", display, "来源要指到调用方类型");
            StringAssert.Contains("TryGetLeaseSource_SourcedLease_AnswersTheAcquiringCallSite", display,
                "来源要指到取用那一行的调用方法");
            Assert.IsTrue(file.Replace('\\', '/').EndsWith("ResourceRecordStoreLeaseSourceTests.cs"),
                "来源文件要落在调用方这个文件");
            Assert.Greater(line, 0, "PDB 行号可用时必须带上");
        }

        /// <summary>开关没开时取的租约：读面回假，出口参数归零。</summary>
        [Test]
        public void TryGetLeaseSource_ToggleOffAtAcquire_AnswersFalseWithEmptyOutputs()
        {
            ResourceLeaseProvenance.Enabled = false;
            ResourceLeaseHandle lease = AcquireLease("leaseSource/0");

            Assert.IsFalse(_store.TryGetLeaseSource(lease, out string display, out string file, out int line),
                "没记过来源就不得凭空答");
            Assert.AreEqual(string.Empty, display, "回假时出口参数要归零");
            Assert.AreEqual(string.Empty, file, "回假时出口参数要归零");
            Assert.AreEqual(0, line, "回假时出口参数要归零");
        }

        /// <summary>归还后的句柄与从未发出的句柄都答不了来源。</summary>
        [Test]
        public void TryGetLeaseSource_ReleasedOrInvalidHandle_AnswersFalse()
        {
            ResourceLeaseProvenance.Enabled = true;
            ResourceLeaseHandle lease = AcquireLease("leaseSource/0");
            _store.Release(lease);

            Assert.IsFalse(_store.TryGetLeaseSource(lease, out _, out _, out _),
                "归还即断来源：不得把上一个租户的调用点留着");
            Assert.IsFalse(_store.TryGetLeaseSource(ResourceLeaseHandle.Invalid, out _, out _, out _),
                "无效句柄答不了任何东西");
        }

        /// <summary>两条开着开关取、一条关着取：在途与带来源分开计数，行内容能按句柄答回同一来源。</summary>
        [Test]
        public void GetLeaseSourceInfos_MixedLeases_CountsSourcedAndOutstanding()
        {
            ResourceLeaseProvenance.Enabled = true;
            AcquireLease("leaseSource/0");
            AcquireLease("leaseSource/1");
            ResourceLeaseProvenance.Enabled = false;
            AcquireLease("leaseSource/2");

            ResourceLeaseSourceInfo[] buffer = new ResourceLeaseSourceInfo[8];
            int written = _store.GetLeaseSourceInfos(buffer, 0, buffer.Length, out int outstanding, out int sourced);

            Assert.AreEqual(2, written, "只有带来源的成行");
            Assert.AreEqual(3, outstanding, "在途计全部三条");
            Assert.AreEqual(2, sourced, "带来源的计两条——差值就是开关没开时取的那批");
            Assert.AreEqual("leaseSource/0", buffer[0].Location, "行按槽位序快照，第一条带被租资产地址");
            Assert.AreEqual("Sprite", buffer[0].TypeName, "行带被租资产类型名");
            Assert.AreEqual(EResourceLeaseKind.Direct, buffer[0].Kind, "非绑定取用归一成 Direct");
            Assert.IsTrue(_store.TryGetLeaseSource(buffer[0].Handle, out string display, out _, out _),
                "快照句柄要能答回同一来源");
            StringAssert.Contains("ResourceRecordStoreLeaseSourceTests", display, "答回的来源仍指向本测试类");
        }

        /// <summary>行数截到 maxCount，计数照旧全量：读数不会被截断窗口缩小。</summary>
        [Test]
        public void GetLeaseSourceInfos_TruncatesRowsWhileCountingEverything()
        {
            ResourceLeaseProvenance.Enabled = true;
            AcquireLease("leaseSource/0");
            AcquireLease("leaseSource/1");
            ResourceLeaseProvenance.Enabled = false;
            AcquireLease("leaseSource/2");

            ResourceLeaseSourceInfo[] buffer = new ResourceLeaseSourceInfo[1];
            int written = _store.GetLeaseSourceInfos(buffer, 0, buffer.Length, out int outstanding, out int sourced);

            Assert.AreEqual(1, written, "行数被截到缓冲容量");
            Assert.AreEqual(3, outstanding, "在途计数不受行数截断影响");
            Assert.AreEqual(2, sourced, "带来源计数不受行数截断影响");
        }

        /// <summary>计数与快照都自 startIndex 起算：跳过的槽位不进任何读数。</summary>
        [Test]
        public void GetLeaseSourceInfos_ScopesReadoutToStartIndex()
        {
            ResourceLeaseProvenance.Enabled = true;
            AcquireLease("leaseSource/0");
            AcquireLease("leaseSource/1");
            ResourceLeaseProvenance.Enabled = false;
            AcquireLease("leaseSource/2");

            ResourceLeaseSourceInfo[] buffer = new ResourceLeaseSourceInfo[8];
            int written = _store.GetLeaseSourceInfos(buffer, 1, buffer.Length, out int outstanding, out int sourced);

            Assert.AreEqual(2, outstanding, "自槽位 1 起只剩两条在途");
            Assert.AreEqual(1, sourced, "其中带来源的一条");
            Assert.AreEqual(1, written, "快照也只剩这一条成行");
        }

        private int CreateRecord(string location)
        {
            int assetId = _store.GetOrCreateAssetRecord(PackageName, location, typeof(Sprite),
                EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle, _sprite, new StubHandle());
            Assert.GreaterOrEqual(assetId, 0, "记录槽分配失败：{0}", location);
            return assetId;
        }

        /// <summary>注册一条 Sprite 记录并直接取用：返回租约句柄（来源落在本辅助方法的调用点）。</summary>
        private ResourceLeaseHandle AcquireLease(string location)
        {
            ResourceLeaseHandle lease = _store.AcquireLease(CreateRecord(location), EResourceLeaseKind.Direct,
                EResourceLeaseOption.None);
            Assert.IsTrue(lease.IsValid, "前置不成立：{0} 取用失败", location);
            return lease;
        }

        /// <summary>
        /// 只回答"句柄还活着吗 / 放掉 / 三个配置读数"的假后端接缝。
        /// </summary>
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
