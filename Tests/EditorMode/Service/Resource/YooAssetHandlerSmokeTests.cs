using System.Reflection;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Service.Resource
{
    /// <summary>
    /// YooAssetHandler 空态冒烟测试：验证未初始化后端时句柄查询与记录维护 API 的安全性行为。
    /// 全部用例仅触达纯槽位查找路径，不触碰 YooAssets 静态初始化，保证确定性。
    /// </summary>
    public sealed class YooAssetHandlerSmokeTests
    {
        [Test]
        public void Release_InvalidHandle_Twice_IsNoOp()
        {
            var handler = new YooAssetHandler();

            Assert.DoesNotThrow(() => handler.Release(ResourceLeaseHandle.Invalid));
            Assert.DoesNotThrow(() => handler.Release(ResourceLeaseHandle.Invalid));
        }

        [Test]
        public void TryGetLeaseAsset_InvalidHandle_ReturnsFalseWithNull()
        {
            var handler = new YooAssetHandler();

            bool found = handler.TryGetLeaseAsset(ResourceLeaseHandle.Invalid, out Object asset);

            Assert.IsFalse(found);
            Assert.IsNull(asset);
        }

        [Test]
        public void ProcessResourceMaintenance_EmptyState_DoesNotThrow()
        {
            var handler = new YooAssetHandler();

            Assert.DoesNotThrow(() => handler.ProcessResourceMaintenance(1234f, 16, 64));
        }

        [Test]
        public void ReleaseAllUnusedAssetRecords_EmptyState_ReturnsZero()
        {
            var handler = new YooAssetHandler();

            int released = handler.ReleaseAllUnusedAssetRecords();

            Assert.AreEqual(0, released);
        }

        [Test]
        public void WarmupResourceRecords_SmallCapacities_DoesNotThrow()
        {
            var handler = new YooAssetHandler();

            Assert.DoesNotThrow(() => handler.WarmupResourceRecords(8, 8));
            Assert.DoesNotThrow(() => handler.ForceReleaseAllAssetRecords());
        }

        [Test]
        public void RuntimeArrayFields_AreNonSerialized()
        {
            // [SerializeReference] 反序列化会把未标注的数组字段还原为非 null 空数组（Length=0），
            // 使判空守卫失效（曾导致过期轮询 IOOR 错误风暴）。修复为运行时数组全部 [NonSerialized]
            // + 使用点长度校验懒重建，NormalizeDeserializedArrays 已随之移除——本用例锁定该序列化边界契约。
            AssertNonSerialized(typeof(YooAssetHandler), "_idleBuckets");
            AssertNonSerialized(typeof(YooAssetHandler), "_keepAliveBuckets");
            AssertNonSerialized(typeof(YooAssetHandler), "_unusedAssetCandidates");
            AssertNonSerialized(typeof(YooAssetHandler), "_assetSlotPages");
            AssertNonSerialized(typeof(YooAssetHandler), "_leaseSlotPages");
            // 这座 arena 已随去重槽搬进内核，契约跟着走。
            AssertNonSerialized(typeof(ResourceRecordStore), "_loadingOperationSlotPages");
        }

        [Test]
        public void AcquireSubAssetsBinding_UninitializedPackage_FailsClosedWithoutPoisoningDedupSlot()
        {
            // 后端缺失时 GetSubAssetsHandleAsync 在 TryBeginLoading 预留去重槽之后同步抛出。
            // 修复前异常直接逃逸、去重槽停在 IsDone=false 永久中毒（同图集后续并发绑定会空转）；
            // 修复后必须按契约吞成 Invalid，且把去重槽闭环回池。
            var handler = new YooAssetHandler();

            ResourceLeaseHandle lease = RunToCompletion(
                () => handler.AcquireSubAssetsBindingAsync("atlas_key", "__moirai_missing_pkg__",
                    default(EResourceLeaseOption), default));

            Assert.IsFalse(lease.IsValid);
            Assert.AreEqual(0, handler.LoadingOperationCount,
                "去重槽必须在赢家路径抛异常后闭环，否则同图集后续并发绑定会在 WaitForLoadingAsync 里永久空转。");
        }

        [Test]
        public void AcquirePrefabSourceLease_UninitializedPackage_FailsClosedWithoutPoisoningDedupSlot()
        {
            // 主资源异步路径与子资源共用同一去重槽机制，同样锁死"异常/取消不留中毒槽"。
            var handler = new YooAssetHandler();

            ResourceLeaseHandle lease = RunToCompletion(
                () => handler.AcquirePrefabSourceLeaseAsync("prefab_key", "__moirai_missing_pkg__", default));

            Assert.IsFalse(lease.IsValid);
            Assert.AreEqual(0, handler.LoadingOperationCount);
        }

        private static ResourceLeaseHandle RunToCompletion(System.Func<UniTask<ResourceLeaseHandle>> start)
        {
            // FailLoading 会把真实异常经 LogUtility.Error 打出来；本用例不关心日志内容，屏蔽预期错误。
            // 任务必须由工厂在这里创建：async 方法在首个 await 之前同步跑完整条失败路径，
            // 写成实参就会在置位前打出 [Error]，被 Unity 记成本轮意外日志。
            bool previous = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                // 未初始化后端下整条赢家路径在任何 await 之前同步抛出并被 catch，任务同步完成，无需 PlayerLoop。
                return start().GetAwaiter().GetResult();
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previous;
            }
        }

        private static void AssertNonSerialized(System.Type owner, string name)
        {
            // 这条断的是"属性/字段上有什么标注"，除反射别无他法；读数类的探针一律走 internal 成员。
            var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "{0}.{1} 未找到——字段被改名或搬走时这条契约要跟着走。", owner.Name, name);
            Assert.IsTrue(field.IsDefined(typeof(System.NonSerializedAttribute), inherit: false),
                "{0}.{1} 必须保持 [NonSerialized]：序列化的运行时数组会被还原成非 null 空数组，判空守卫当场失效。",
                owner.Name, name);
        }
    }
}
