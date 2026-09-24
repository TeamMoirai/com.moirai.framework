using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using Service.Audio;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// 绑定热路径的 0-GC 验收：稳态重绑、空闲轮转扫描、诊断读表三条路径每次调用都不得分配。
    /// <para><b>玩家专用</b>：托管分配计数器在 Unity 编辑器 Mono 下不推进（本机实测一次 64MB 主线程分配仍报 0），
    /// 零分配断言在编辑器里无条件成立，所以这几格住在 <c>Moirai.Atropos.Tests.Player</c>
    /// （<c>UNITY_INCLUDE_TESTS</c> + <c>!UNITY_EDITOR</c>），只随玩家构建的测试运行执行。
    /// 编辑器侧要判的是结构（版本号不变、租约同值、目标引用相等），不是字节数。</para>
    /// <para>测量口径与计时台共用 <see cref="AllocationCapture"/>：先做一次必然分配探测计数器能力，
    /// 探不到就整组 Ignore——"测不出分配"绝不写成"没有分配"。</para>
    /// <para>后端用 <see cref="CountingLeaseSource"/> 而不是真后端：这几格量的是绑定层自己那三趟（打包键、
    /// 索引表、槽位写入），掺进真后端的原生调用只会把噪声计进来。</para>
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    public sealed class ResourceBindingAllocationTests
    {
        private const int Iterations = 1024;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<UObject> _assets = new List<UObject>();

        private ResourceBindingService _bindings;
        private CountingLeaseSource _leaseSource;
        private ResourceOwner _owner;
        private SpriteRenderer _target;
        private Sprite _sprite;
        private ResourceKey _key;

        [SetUp]
        public void SetUp()
        {
            _leaseSource = new CountingLeaseSource();
            _bindings = new ResourceBindingService(_leaseSource);

            GameObject root = new GameObject("[ResourceBindingAllocTest]");
            _spawned.Add(root);
            _owner = root.AddComponent<ResourceOwner>();
            _target = root.AddComponent<SpriteRenderer>();

            Texture2D texture = new Texture2D(2, 2);
            _assets.Add(texture);
            _sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _assets.Add(_sprite);
            _leaseSource.Asset = _sprite;

            _key = new ResourceKey("UI/Heart", string.Empty, typeof(Sprite), EResourceAssetKind.Sprite);

            AllocationCapture.CalibrateKnownAllocation();
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.BindSprite(_owner, _target, _key));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject gameObject = _spawned[i];
                if (gameObject != null)
                {
                    UObject.Destroy(gameObject);
                }
            }

            for (int i = 0; i < _assets.Count; i++)
            {
                UObject asset = _assets[i];
                if (asset != null)
                {
                    UObject.Destroy(asset);
                }
            }

            _spawned.Clear();
            _assets.Clear();
            _bindings = null;
            _leaseSource = null;
        }

        /// <summary>
        /// 同一目标同一键反复重绑：租约换手的稳态路径每轮零分配。
        /// </summary>
        [Test]
        public void BindSprite_SameTargetSameKey_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Binding.BindSprite", Iterations,
                () => Assert.AreEqual(EResourceBindStatus.Success, _bindings.BindSprite(_owner, _target, _key)),
                bytes => Assert.AreEqual(0L, bytes, "稳态重绑出现了分配"));
        }

        /// <summary>
        /// 没有销毁态目标时的一轮轮转扫描：预算判定本身不该造出任何临时对象。
        /// </summary>
        [Test]
        public void ProcessDestroyedObjects_NothingDestroyed_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Binding.ProcessDestroyedObjects", Iterations,
                () => _bindings.ProcessDestroyedObjects(64),
                bytes => Assert.AreEqual(0L, bytes, "空闲轮转扫描出现了分配"));
        }

        /// <summary>
        /// 诊断读表：数组由调用方持有，读一屏信息不得再分配。
        /// </summary>
        [Test]
        public void GetBindingInfos_IntoCallerArray_ZeroAlloc()
        {
            ResourceBindingInfo[] results = new ResourceBindingInfo[Iterations];
            AllocationCapture.MeasureManaged("Binding.GetBindingInfos", Iterations,
                () => _bindings.GetBindingInfos(results, 0, 16),
                bytes => Assert.AreEqual(0L, bytes, "诊断读表出现了分配"));
        }

        /// <summary>
        /// 所有者整体注销再登记：一条绑定从登记到放手，全程零分配。
        /// </summary>
        [Test]
        public void ReleaseOwner_ZeroAlloc()
        {
            AllocationCapture.MeasureManaged("Binding.ReleaseOwner", Iterations, () =>
            {
                Assert.AreEqual(EResourceBindStatus.Success, _bindings.ReleaseOwner(_owner));
                Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(_owner));
                Assert.AreEqual(EResourceBindStatus.Success, _bindings.BindSprite(_owner, _target, _key));
            }, bytes => Assert.AreEqual(0L, bytes, "注销 + 重登记这条往返出现了分配"));

            Assert.Greater(_leaseSource.ReleaseCount, 0, "这条往返应当真的还掉过租约");
        }

        /// <summary>
        /// 只记账的假接缝：八个成员一个都不许造出临时对象，故连集合都不用。
        /// </summary>
        private sealed class CountingLeaseSource : IResourceLeaseSource
        {
            // 外层夹具要读写这两个位置，故 internal 而不是 private。
            internal UObject Asset;

            internal int ReleaseCount;

            public ResourceLeaseHandle AcquireBinding(ResourceKey key) => new ResourceLeaseHandle(1, 1);

            public UniTask<ResourceLeaseHandle> AcquireBindingAsync(ResourceKey key,
                CancellationToken cancellationToken) => UniTask.FromResult(new ResourceLeaseHandle(1, 1));

            public UniTask<ResourceLeaseHandle> AcquireSubAssetsBindingAsync(string location, string packageName,
                EResourceLeaseOption options, CancellationToken cancellationToken) =>
                UniTask.FromResult(new ResourceLeaseHandle(1, 1));

            public bool TryGetSubSpriteAsset(ResourceLeaseHandle handle, string spriteName, out Sprite sprite)
            {
                sprite = Asset as Sprite;
                return sprite != null;
            }

            public bool TryGetLeaseAsset(ResourceLeaseHandle handle, out UObject asset)
            {
                asset = Asset;
                return asset != null;
            }

            public bool TryGetLeaseAssetId(ResourceLeaseHandle handle, out int assetId)
            {
                assetId = 1;
                return true;
            }

            public void SetLeaseOptions(ResourceLeaseHandle handle, EResourceLeaseOption options)
            {
            }

            public void Release(ResourceLeaseHandle handle) => ReleaseCount++;
        }
    }
}
