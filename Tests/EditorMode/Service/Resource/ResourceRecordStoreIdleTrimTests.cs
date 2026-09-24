using System.Collections.Generic;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 记录内核的空闲容量淘汰特征化测试：钉住"受害者按空闲过期刻度从旧到新挑、每趟不超预算、
    /// 预算用尽时把请求位留回下一帧"这三条现状。
    /// <para>这是内核抽出来之后第一次能只带一面假 <see cref="IResourceRecordHost"/> 直接驱动它——
    /// 之前这套账全在 <c>YooAssetHandler</c> 里，不初始化 YooAsset 就进不去。</para>
    /// <para>刻意不测"怎么挑更快"：这几格是要把现状钉成基线，好让后面把线性最小值扫描换成
    /// 轮盘取桶时，改变选择顺序会立刻显形，而不是悄悄换掉一批被淘汰的资源。</para>
    /// </summary>
    public sealed class ResourceRecordStoreIdleTrimTests
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
        /// 预算之内先摘最长空闲的那条：过期刻度越早（= 越早该被回收）越先出局。
        /// </summary>
        [Test]
        public void Trim_PicksTheLongestIdleFirst()
        {
            // 6 条记录，空闲过期时长各差 10 秒：loc5 最早到期，因此最长空闲。
            for (int i = 0; i < 6; i++)
            {
                EnterIdleRecord($"loc{i}", 100f - i * 10f);
            }

            Assert.AreEqual(6, SnapshotLocations().Count, "前置不成立：六条记录没能都留在表里");

            _host.IdleCapacity = 2;
            _store.TrimIdleAssetCapacity(2);

            var survivors = SnapshotLocations();
            Assert.AreEqual(4, survivors.Count, "每趟只该摘掉预算内的 2 条");
            CollectionAssert.DoesNotContain(survivors, "loc5");
            CollectionAssert.DoesNotContain(survivors, "loc4");
            CollectionAssert.Contains(survivors, "loc0", "最短空闲的那条不该这一趟出局");
        }

        /// <summary>
        /// 超限未摘完时请求位必须留回：否则下一帧不再有人接着摘，容量会静默停在超限状态。
        /// </summary>
        [Test]
        public void Trim_KeepsTheRequestPendingWhenBudgetRunsOut()
        {
            // 容量先于入队调小：请求位就是"入队时发现自己超限"这条路径置起来的
            // （另一条是设置项 setter 直接 RequestIdleCapacityTrim，走不到这里）。
            _host.IdleCapacity = 2;
            for (int i = 0; i < 6; i++)
            {
                EnterIdleRecord($"loc{i}", 100f - i * 10f);
            }

            Assert.IsTrue(_store.IdleCapacityTrimPending, "入队时就该请求一次淘汰");

            _store.TrimIdleAssetCapacity(2);
            Assert.IsTrue(_store.IdleCapacityTrimPending, "还超限：请求位必须留回下一帧");

            _store.TrimIdleAssetCapacity(8);
            Assert.AreEqual(2, SnapshotLocations().Count, "预算够时就该一路压回容量上限");
            Assert.IsFalse(_store.IdleCapacityTrimPending, "已回到容量之内：请求位该清掉");
        }

        /// <summary>
        /// 还被租约握着的记录不参与淘汰，即使它是"最长空闲"的那一条。
        /// </summary>
        [Test]
        public void Trim_SkipsRecordsStillHeldByALease()
        {
            for (int i = 0; i < 6; i++)
            {
                EnterIdleRecord($"loc{i}", 100f - i * 10f);
            }

            // loc5 最早到期，但重新取用一次让它离开空闲集合。
            ResourceLeaseHandle held = _store.AcquireLease(RecordIdOf("loc5"), EResourceLeaseKind.Direct,
                EResourceLeaseOption.None);
            Assert.IsTrue(held.IsValid, "前置不成立：取用该成功");

            _host.IdleCapacity = 2;
            _store.TrimIdleAssetCapacity(2);

            var survivors = SnapshotLocations();
            CollectionAssert.Contains(survivors, "loc5", "有活跃租约的记录不该被淘汰");
            CollectionAssert.DoesNotContain(survivors, "loc4");
            CollectionAssert.DoesNotContain(survivors, "loc3");
        }

        /// <summary>
        /// 造一条已进入空闲集合的记录，并按 <paramref name="idleExpireSeconds"/> 决定它的空闲过期刻度。
        /// </summary>
        private void EnterIdleRecord(string location, float idleExpireSeconds)
        {
            StubHandle handle = new StubHandle();
            int assetId = _store.GetOrCreateAssetRecord(PackageName, location, typeof(Sprite),
                EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle, _sprite, handle);
            Assert.GreaterOrEqual(assetId, 0, "记录槽分配失败：{0}", location);

            ResourceLeaseHandle lease = _store.AcquireLease(assetId, EResourceLeaseKind.Direct,
                EResourceLeaseOption.None);
            Assert.IsTrue(lease.IsValid, "前置不成立：{0} 取用失败", location);

            // 内核按活读取这个读数，所以每条记录拿到不同的空闲过期刻度。
            _host.IdleExpireTime = idleExpireSeconds;
            _store.Release(lease);
        }

        private int RecordIdOf(string location)
        {
            ulong key = _store.GetAssetRecordKey(PackageName, location, typeof(Sprite),
                EResourceAssetKind.Sprite, EResourceHandleKind.AssetHandle);
            Assert.IsTrue(_store.TryGetRecordId(key, out int assetId), "记录 {0} 不在了", location);
            return assetId;
        }

        private List<string> SnapshotLocations()
        {
            ResourceAssetInfo[] infos = new ResourceAssetInfo[64];
            int count = _store.GetAssetInfos(infos, 0, infos.Length);
            List<string> locations = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                locations.Add(infos[i].Location);
            }

            return locations;
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
