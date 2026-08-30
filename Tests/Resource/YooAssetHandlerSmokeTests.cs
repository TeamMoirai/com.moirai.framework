using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Resource
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
        public void ProcessKeepAlive_EmptyState_DoesNotThrow()
        {
            var handler = new YooAssetHandler();

            Assert.DoesNotThrow(() => handler.ProcessKeepAlive(1234f, 16));
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

            Assert.DoesNotThrow(() => handler.WarmupResourceRecords(8, 8, 8));
            Assert.DoesNotThrow(() => handler.ForceReleaseAllAssetRecords());
        }

        [Test]
        public void RuntimeArrayFields_AreNonSerialized()
        {
            // [SerializeReference] 反序列化会把未标注的数组字段还原为非 null 空数组（Length=0），
            // 使判空守卫失效（曾导致过期轮询 IOOR 错误风暴）。修复为运行时数组全部 [NonSerialized]
            // + 使用点长度校验懒重建，NormalizeDeserializedArrays 已随之移除——本用例锁定该序列化边界契约。
            var type = typeof(YooAssetHandler);
            var fields = new[]
            {
                "_idleBuckets", "_keepAliveBuckets", "_unusedAssetCandidates",
                "_assetSlotPages", "_leaseSlotPages", "_loadingOperationSlotPages",
            };
            foreach (var name in fields)
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(field, "field {0} not found.", name);
                Assert.IsTrue(field.IsDefined(typeof(System.NonSerializedAttribute), inherit: false),
                    "field {0} must stay [NonSerialized]; serialized runtime arrays deserialize as non-null empty arrays.", name);
            }
        }
    }
}
