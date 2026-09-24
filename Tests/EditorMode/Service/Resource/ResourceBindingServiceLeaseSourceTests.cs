using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 用假租约接缝驱动绑定服务：验证绑定层与后端解耦是实的，并直接断言
    /// "归还租约"这一步真的发生——此前只能在未初始化的裸后端上由"槽位被收走"间接代理。
    /// <para>能这么测的全部前提是把后端契约收成 <see cref="IResourceLeaseSource"/> 八个成员：
    /// 收之前假后端要落 74 个抽象成员，等于不可 mock。</para>
    /// </summary>
    public sealed class ResourceBindingServiceLeaseSourceTests
    {
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

            for (int i = 0; i < _assets.Count; i++)
            {
                Object asset = _assets[i];
                if (asset != null)
                {
                    Object.DestroyImmediate(asset);
                }
            }

            _spawned.Clear();
            _assets.Clear();
        }

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<Object> _assets = new List<Object>();

        /// <summary>
        /// 目标已销毁时，轮转扫描必须把那条租约真的还回去（不止是把槽位收走）。
        /// </summary>
        [Test]
        public void Sweep_ReleasesTheLease_ExactlyOnce()
        {
            var leaseSource = new StubLeaseSource();
            var bindings = new ResourceBindingService(leaseSource);

            GameObject ownerObject = new GameObject("owner");
            _spawned.Add(ownerObject);
            ResourceOwner owner = ownerObject.AddComponent<ResourceOwner>();

            GameObject targetObject = new GameObject("target");
            _spawned.Add(targetObject);
            SpriteRenderer target = targetObject.AddComponent<SpriteRenderer>();

            Sprite sprite = CreateSprite();
            Assert.AreEqual(EResourceBindStatus.Success, bindings.RegisterSpriteSource(owner, target,
                new ResourceLeaseHandle(3, 7), sprite, EResourceBindingSlotType.SpriteRendererSprite));
            Assert.AreEqual(0, leaseSource.ReleaseCalls, "登记本身不该归还任何租约");

            Object.DestroyImmediate(targetObject);
            bindings.ProcessDestroyedObjects(64);

            Assert.AreEqual(1, leaseSource.ReleaseCalls, "销毁态回收必须归还那一条租约");
            Assert.AreEqual(new ResourceLeaseHandle(3, 7), leaseSource.Released[0],
                "归还的必须正是登记时那一条句柄");
        }

        /// <summary>
        /// 重绑同一目标：新租约落地后才还旧的，且还的是旧的那一条。
        /// </summary>
        [Test]
        public void Rebind_ReleasesOnlyTheOldLease_AfterTheNewOneLands()
        {
            var leaseSource = new StubLeaseSource();
            var bindings = new ResourceBindingService(leaseSource);

            GameObject ownerObject = new GameObject("owner");
            _spawned.Add(ownerObject);
            ResourceOwner owner = ownerObject.AddComponent<ResourceOwner>();

            GameObject targetObject = new GameObject("target");
            _spawned.Add(targetObject);
            SpriteRenderer target = targetObject.AddComponent<SpriteRenderer>();

            Sprite first = CreateSprite();
            Sprite second = CreateSprite();
            Assert.AreEqual(EResourceBindStatus.Success, bindings.RegisterSpriteSource(owner, target,
                new ResourceLeaseHandle(1, 1), first, EResourceBindingSlotType.SpriteRendererSprite));
            Assert.AreEqual(EResourceBindStatus.Success, bindings.RegisterSpriteSource(owner, target,
                new ResourceLeaseHandle(2, 1), second, EResourceBindingSlotType.SpriteRendererSprite));

            Assert.AreEqual(1, leaseSource.ReleaseCalls, "重绑只该还掉上一条租约");
            Assert.AreEqual(new ResourceLeaseHandle(1, 1), leaseSource.Released[0]);
        }

        private Sprite CreateSprite()
        {
            Texture2D texture = new Texture2D(2, 2);
            _assets.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            _assets.Add(sprite);
            return sprite;
        }
    }
}
