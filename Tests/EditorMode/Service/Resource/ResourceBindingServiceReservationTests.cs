using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 异步绑定预约位的收口用例：后端在"取用租约"那一步抛出时，预约位不得留在表里。
    /// <para>预约落地后槽位的形状是"有目标、有版本号、无租约无资源"。这条形状若靠轮转扫描回收，
    /// 前提是其目标已被销毁；目标还活着时它谁也不会来收，只在所有者释放时才走掉——
    /// 期间一直占着 <c>_bindingIndexByOwnerSlot</c> 的一条映射与一个版本号，
    /// 而同一个 (所有者, 组件, 槽位类型) 再绑就会撞上这个版本号。</para>
    /// <para>抛出错位取的是 <see cref="StubLeaseSource"/> 上那个开关，不是某座真实后端：这条窗口要的是
    /// "取用抛出"这个行为，后端哪天补上或改掉都不该把它一起带走。Addressables 后端早先正是现成的抛出源，
    /// 异步子资源绑定接通之后它就不抛了。</para>
    /// </summary>
    public sealed class ResourceBindingServiceReservationTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

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
        }

        /// <summary>
        /// 取用抛出后，预约位必须被取消：绑定表回到空，而不是留一条活着的槽位。
        /// </summary>
        [Test]
        public void BindSubSpriteAsync_WhenAcquireThrows_CancelsReservation()
        {
            var bindings = new ResourceBindingService(new StubLeaseSource { SubAssetsAcquireThrows = true });
            SpriteRenderer target = CreateTarget("reservation", out ResourceOwner owner);

            bool threw = TryBind(bindings.BindSubSpriteAsync(owner, target, AtlasKey, "icon"));

            Assert.IsTrue(threw, "前置不成立：假接缝的取用开关没生效");
            Assert.AreEqual(0, ActiveBindingCount(bindings),
                "取用抛出后预约位仍留在表里——它占着索引映射与版本号，除所有者释放外无人再收");
        }

        /// <summary>
        /// 取消后同一个键再绑一次应当从头开始（新的版本号），而不是撞进上一次的残留预约。
        /// </summary>
        [Test]
        public void BindSubSpriteAsync_AfterThrowingAcquire_LeavesNoMappedKey()
        {
            var bindings = new ResourceBindingService(new StubLeaseSource { SubAssetsAcquireThrows = true });
            SpriteRenderer target = CreateTarget("reservation-rebind", out ResourceOwner owner);

            TryBind(bindings.BindSubSpriteAsync(owner, target, AtlasKey, "icon"));

            // 再绑一次：走到同一个 (所有者, 组件, 槽位类型) 键上，若上一次的预约还在，
            // 这次就落在残留槽位上，Version 会累加而不是重新从 1 开始。
            TryBind(bindings.BindSubSpriteAsync(owner, target, AtlasKey, "icon"));

            ResourceBindingInfo[] infos = new ResourceBindingInfo[16];
            int total = bindings.GetBindingInfos(infos, 0, infos.Length);
            for (int i = 0; i < total && i < infos.Length; i++)
            {
                if (infos[i].Active)
                {
                    Assert.Fail($"残留预约未被回收，槽位 {infos[i].BindingIndex} 仍为 Active（Version={infos[i].Version}）");
                }
            }
        }

        private static ResourceKey AtlasKey => new ResourceKey("atlas/sheet", string.Empty, typeof(Sprite),
            EResourceAssetKind.Sprite);

        private SpriteRenderer CreateTarget(string name, out ResourceOwner owner)
        {
            GameObject gameObject = new GameObject(name);
            _spawned.Add(gameObject);
            owner = gameObject.AddComponent<ResourceOwner>();
            return gameObject.AddComponent<SpriteRenderer>();
        }

        /// <summary>
        /// 驱动一次异步绑定，返回它是否抛出。
        /// <para>抛出发生在首个 await 之前（假接缝的成员不是 async，调用即抛），
        /// 但外层是 async 方法，异常被收进 UniTask、在取结果时才重抛。</para>
        /// </summary>
        private static bool TryBind(UniTask<EResourceBindStatus> bind)
        {
            try
            {
                bind.GetAwaiter().GetResult();
                return false;
            }
            catch (System.Exception)
            {
                return true;
            }
        }

        private static int ActiveBindingCount(ResourceBindingService bindings)
        {
            ResourceBindingInfo[] infos = new ResourceBindingInfo[16];
            int total = bindings.GetBindingInfos(infos, 0, infos.Length);

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
