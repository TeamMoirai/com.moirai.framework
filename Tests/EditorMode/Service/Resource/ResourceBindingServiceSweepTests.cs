using System.Collections.Generic;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 销毁态轮转扫描的回收完整性契约：无条件项（归还租约、摘槽位）必须完成，不因尽力而为项抛出而中断。
    /// </summary>
    /// <remarks>
    /// 轮转扫描对绑定槽位做两件事：清组件槽位（尽力而为）与归还租约、摘槽位（无条件）。
    /// 前者要在"引擎已销毁但托管引用仍在"的组件上读原生属性，会抛 <c>MissingReferenceException</c>；
    /// 两件事同居一个 <c>try</c> 时该抛出会把无条件项一并截断——槽位永不回收、租约永不归还，
    /// 而游标在判定之前已推进，下一圈仍撞回同一槽位。
    /// 全部只走槽位层：未初始化的裸 <see cref="YooAssetHandler"/> 加手工构造的租约句柄，
    /// 不触达 YooAssets 静态初始化，也不改真实计数；裸后端无真实引用计数，故此处不直接断言"租约已归还"。
    /// </remarks>
    public sealed class ResourceBindingServiceSweepTests
    {
        // 轮转圈数：修复前每圈都抛，5 圈足以把"重复抛出"与"只抛一次"分开
        private const int RevolutionProbes = 5;

        private ResourceBindingService _bindings;
        private ResourceBindingInfo[] _bindingInfos;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private readonly List<Sprite> _sprites = new List<Sprite>();

        [SetUp]
        public void SetUp()
        {
            _bindings = new ResourceBindingService(new YooAssetHandler());
            _bindingInfos = new ResourceBindingInfo[32];
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                GameObject gameObject = _spawned[i];
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            for (int i = 0; i < _sprites.Count; i++)
            {
                Sprite sprite = _sprites[i];
                if (sprite != null)
                {
                    Object.DestroyImmediate(sprite);
                }
            }

            for (int i = 0; i < _textures.Count; i++)
            {
                Texture2D texture = _textures[i];
                if (texture != null)
                {
                    Object.DestroyImmediate(texture);
                }
            }

            _spawned.Clear();
            _sprites.Clear();
            _textures.Clear();
            _bindingInfos = null;
            _bindings = null;
        }

        /// <summary>
        /// 目标已销毁、绑定仍占着槽位时，轮转扫描必须既不抛出、又把槽位收走。
        /// </summary>
        [Test]
        public void Sweep_ReclaimsDestroyedBindingSlot_WhenComponentReadThrows()
        {
            PlantBindingOnDestroyedTarget();

            // 前置：销毁派发经静态门面取绑定服务，取不到就是把账落在原处
            Assert.AreEqual(1, ActiveBindingCount(),
                "前置不成立：销毁后槽位应仍占着，否则本用例测不到回收路径");

            Assert.DoesNotThrow(() => _bindings.ProcessDestroyedObjects(64));

            Assert.AreEqual(0, ActiveBindingCount(),
                "销毁态绑定槽位必须被轮转扫描回收，而不是留在表里等下一圈");
        }

        /// <summary>
        /// 抛出不能截断回收：游标照常推进，后续轮次不得再撞回同一槽位反复抛出。
        /// </summary>
        [Test]
        public void Sweep_DoesNotKeepThrowingOnLaterRevolutions()
        {
            PlantBindingOnDestroyedTarget();

            for (int i = 0; i < RevolutionProbes; i++)
            {
                int attempt = i + 1;
                Assert.DoesNotThrow(() => _bindings.ProcessDestroyedObjects(1),
                    "第 " + attempt + " 圈轮转仍在抛出，说明槽位没被收走、下一圈还会撞回同一处");
            }

            Assert.AreEqual(0, ActiveBindingCount());
        }

        /// <summary>
        /// 同一个销毁态绑定，经"所有者释放"与"轮转回收"两条路径必须落到同一终态。
        /// </summary>
        /// <remarks>
        /// 判据是"什么必须无条件完成"在两条路径上答案一致：所有者路径的 <c>try</c> 只包住组件清理，
        /// 摘槽位与移除映射在其外；轮转路径同样不得让抛出把它们一并跳过。
        /// </remarks>
        [Test]
        public void ReleaseOwner_And_Sweep_AgreeOnSlotReclamation()
        {
            ResourceOwner viaReleaseOwner = PlantBindingOnDestroyedTargetOwnedBy(out int firstBindingCount);
            Assert.AreEqual(1, firstBindingCount);

            Assert.DoesNotThrow(
                () => _bindings.ReleaseOwner(viaReleaseOwner.OwnerId, viaReleaseOwner.Generation),
                "所有者释放不该把组件清理的抛出透出来");
            Assert.AreEqual(0, ActiveBindingCount(), "所有者释放路径未回收槽位");

            PlantBindingOnDestroyedTarget();
            Assert.DoesNotThrow(() => _bindings.ProcessDestroyedObjects(64),
                "轮转回收路径不该把组件清理的抛出透出来");
            Assert.AreEqual(0, ActiveBindingCount(), "轮转回收路径未回收槽位");
        }

        /// <summary>
        /// 造一个"目标已销毁、绑定仍占位"的现场，返回其所有者。
        /// </summary>
        /// <remarks>
        /// 所有者与目标刻意分在两个 GameObject：同一个物体上销毁会把所有者一并带走，
        /// 那样先触发的是所有者回收分支，测不到绑定槽位这一条轮转路径。
        /// </remarks>
        private ResourceOwner PlantBindingOnDestroyedTarget()
        {
            return PlantBindingOnDestroyedTargetOwnedBy(out _);
        }

        private ResourceOwner PlantBindingOnDestroyedTargetOwnedBy(out int bindingCountAfterPlanting)
        {
            GameObject ownerObject = new GameObject("sweep-owner");
            _spawned.Add(ownerObject);
            ResourceOwner owner = ownerObject.AddComponent<ResourceOwner>();

            GameObject targetObject = new GameObject("sweep-target");
            _spawned.Add(targetObject);
            SpriteRenderer target = targetObject.AddComponent<SpriteRenderer>();

            EResourceBindStatus status = _bindings.RegisterSpriteSource(owner, target,
                new ResourceLeaseHandle(0, 1), CreateSprite(), EResourceBindingSlotType.SpriteRendererSprite);
            Assert.AreEqual(EResourceBindStatus.Success, status, "造前置数据失败：绑定未落地");

            bindingCountAfterPlanting = ActiveBindingCount();

            Object.DestroyImmediate(targetObject);

            return owner;
        }

        private Sprite CreateSprite()
        {
            Texture2D texture = new Texture2D(2, 2);
            _textures.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _sprites.Add(sprite);
            return sprite;
        }

        private int ActiveBindingCount()
        {
            int total = _bindings.GetBindingInfos(_bindingInfos, 0, _bindingInfos.Length);

            int active = 0;
            for (int i = 0; i < total && i < _bindingInfos.Length; i++)
            {
                if (_bindingInfos[i].Active)
                {
                    active++;
                }
            }

            return active;
        }
    }
}
