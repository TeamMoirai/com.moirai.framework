using System.Collections.Generic;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// ResourceBindingService 的关停/重置分界与销毁态兜底回收用例。
    /// <para>全部只走槽位层：用未初始化的裸 <see cref="YooAssetHandler"/> 构造绑定服务，
    /// 不触达 YooAssets 静态初始化，也不落任何租约，保证编辑模式下的确定性。</para>
    /// </summary>
    public sealed class ResourceBindingServiceLifecycleTests
    {
        private ResourceBindingService _bindings;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            _bindings = new ResourceBindingService(new YooAssetHandler());
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

            _spawned.Clear();
            _bindings = null;
        }

        [Test]
        public void Shutdown_DrainsOwnersAndKeepsServiceClosed()
        {
            ResourceOwner owner = AddOwner("owner");
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(owner));
            Assert.AreEqual(1, ActiveOwnerCount());

            _bindings.Shutdown();

            Assert.AreEqual(0, ActiveOwnerCount());

            // 关停是终态：槽位页已整体释放，此后不得再放行注册
            ResourceOwner late = AddOwner("late");
            Assert.AreEqual(EResourceBindStatus.ServiceShutdown, _bindings.RegisterOwner(late));
            Assert.AreEqual(EResourceBindStatus.ServiceShutdown, _bindings.ReleaseOwner(late.OwnerId, late.Generation));
        }

        [Test]
        public void Reset_DrainsOwnersAndReopensService()
        {
            ResourceOwner owner = AddOwner("owner");
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(owner));

            _bindings.Reset();

            Assert.AreEqual(0, ActiveOwnerCount());

            // 强制回收全部资源走的是 Reset：同一实例要继续可用
            ResourceOwner reopened = AddOwner("reopened");
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(reopened));
            Assert.AreEqual(1, ActiveOwnerCount());
        }

        [Test]
        public void ProcessDestroyedObjects_ReclaimsOwnerSlotWhoseComponentIsGone()
        {
            ResourceOwner owner = AddOwner("owner");
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(owner));

            // 组件销毁后 OnDestroy 经静态门面取绑定服务，取不到就是把账落在原处：槽位仍占着
            Object.DestroyImmediate(owner);
            Assert.AreEqual(1, ActiveOwnerCount());

            _bindings.ProcessDestroyedObjects(64);

            Assert.AreEqual(0, ActiveOwnerCount());
        }

        [Test]
        public void ProcessDestroyedObjects_KeepsLiveOwnersUntouched()
        {
            ResourceOwner owner = AddOwner("owner");
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(owner));

            _bindings.ProcessDestroyedObjects(64);
            _bindings.ProcessDestroyedObjects(64);

            Assert.AreEqual(1, ActiveOwnerCount());
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.ReleaseOwner(owner.OwnerId, owner.Generation));
        }

        [Test]
        public void ProcessDestroyedObjects_BudgetRotatesCursorAcrossCalls()
        {
            for (int i = 0; i < 3; i++)
            {
                ResourceOwner owner = AddOwner($"owner-{i}");
                Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(owner));
            }

            for (int i = 0; i < _spawned.Count; i++)
            {
                ResourceOwner owner = _spawned[i].GetComponent<ResourceOwner>();
                if (owner != null)
                {
                    Object.DestroyImmediate(owner);
                }
            }

            Assert.AreEqual(3, ActiveOwnerCount());

            // 每帧只查验配额内的槽位，游标跨调用轮转：三轮走完三个销毁态所有者
            _bindings.ProcessDestroyedObjects(1);
            Assert.AreEqual(2, ActiveOwnerCount());

            _bindings.ProcessDestroyedObjects(1);
            Assert.AreEqual(1, ActiveOwnerCount());

            _bindings.ProcessDestroyedObjects(1);
            Assert.AreEqual(0, ActiveOwnerCount());
        }

        [Test]
        public void ProcessDestroyedObjects_AfterTerminalShutdown_IsNoOp()
        {
            ResourceOwner owner = AddOwner("owner");
            Assert.AreEqual(EResourceBindStatus.Success, _bindings.RegisterOwner(owner));

            _bindings.Shutdown();

            Assert.DoesNotThrow(() => _bindings.ProcessDestroyedObjects(64));
        }

        private ResourceOwner AddOwner(string name)
        {
            GameObject gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            return gameObject.AddComponent<ResourceOwner>();
        }

        private int ActiveOwnerCount()
        {
            ResourceOwnerInfo[] infos = new ResourceOwnerInfo[32];
            int total = _bindings.GetOwnerInfos(infos, 0, infos.Length);

            int active = 0;
            for (int i = 0; i < total && i < infos.Length; i++)
            {
                if (infos[i].Active)
                {
                    active++;
                }
            }

            return active;
        }
    }
}
