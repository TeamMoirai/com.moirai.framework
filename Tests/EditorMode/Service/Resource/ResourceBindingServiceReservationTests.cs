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
    /// <para>抛出错位取的是 Addressables 后端：它对子资源绑定无条件抛
    /// （fail-fast 契约，见 <c>AddressableHandlerFailFastTests</c>），因此这条窗口是现成可复现的。
    /// 该后端类型按名字跨程序集发现，测试程序集不直接引用它——它在测试侧的 <c>versionDefines</c>
    /// 里根本没有定义，直接引用会让整份用例在没装 Addressables 的工程里静默编译为空。</para>
    /// </summary>
    public sealed class ResourceBindingServiceReservationTests
    {
        private const string AddressableHandlerTypeName = "Moirai.Atropos.Resource.AddressableHandler";

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
            ResourceServiceHandler backend = CreateAddressableHandler();
            if (backend == null)
            {
                Assert.Ignore("Addressables 未安装，没有会抛出的后端可用。");
            }

            var bindings = new ResourceBindingService(backend);
            GameObject gameObject = new GameObject("reservation");
            _spawned.Add(gameObject);
            ResourceOwner owner = gameObject.AddComponent<ResourceOwner>();
            SpriteRenderer target = gameObject.AddComponent<SpriteRenderer>();

            ResourceKey atlasKey = new ResourceKey("atlas/sheet", string.Empty, typeof(Sprite),
                EResourceAssetKind.Sprite);

            bool threw = TryBind(bindings.BindSubSpriteAsync(owner, target, atlasKey, "icon"));

            Assert.IsTrue(threw, "前置不成立：该后端应在取用子资源绑定时抛出");
            Assert.AreEqual(0, ActiveBindingCount(bindings),
                "取用抛出后预约位仍留在表里——它占着索引映射与版本号，除所有者释放外无人再收");
        }

        /// <summary>
        /// 取消后同一个键再绑一次应当从头开始（新的版本号），而不是撞进上一次的残留预约。
        /// </summary>
        [Test]
        public void BindSubSpriteAsync_AfterThrowingAcquire_LeavesNoMappedKey()
        {
            ResourceServiceHandler backend = CreateAddressableHandler();
            if (backend == null)
            {
                Assert.Ignore("Addressables 未安装，没有会抛出的后端可用。");
            }

            var bindings = new ResourceBindingService(backend);
            GameObject gameObject = new GameObject("reservation-rebind");
            _spawned.Add(gameObject);
            ResourceOwner owner = gameObject.AddComponent<ResourceOwner>();
            SpriteRenderer target = gameObject.AddComponent<SpriteRenderer>();

            ResourceKey atlasKey = new ResourceKey("atlas/sheet", string.Empty, typeof(Sprite),
                EResourceAssetKind.Sprite);

            TryBind(bindings.BindSubSpriteAsync(owner, target, atlasKey, "icon"));

            // 再绑一次：走到同一个 (所有者, 组件, 槽位类型) 键上，若上一次的预约还在，
            // 这次就落在残留槽位上，Version 会累加而不是重新从 1 开始。
            TryBind(bindings.BindSubSpriteAsync(owner, target, atlasKey, "icon"));

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

        /// <summary>
        /// 驱动一次异步绑定，返回它是否抛出。
        /// <para>抛出发生在首个 await 之前（该后端的成员不是 async，调用即抛），
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

        private static ResourceServiceHandler CreateAddressableHandler()
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                System.Type type = assembly.GetType(AddressableHandlerTypeName, false);
                if (type != null && !type.IsAbstract)
                {
                    return (ResourceServiceHandler)System.Activator.CreateInstance(type);
                }
            }

            return null;
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
