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
        public void Initialize_NormalizesDeserializedEmptyRuntimeArrays()
        {
            var handler = new YooAssetHandler();
            var type = typeof(YooAssetHandler);

            // 模拟 [SerializeReference] 反序列化残留：非 null 空数组使判空检查失效。
            var fields = new[]
            {
                "_idleBuckets", "_keepAliveBuckets", "_unusedAssetCandidates",
                "_assetSlotPages", "_leaseSlotPages", "_loadingOperationSlotPages",
            };
            foreach (var name in fields)
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(field, "field {0} not found.", name);

                var current = field.GetValue(handler);
                var empty = System.Array.CreateInstance(field.FieldType.GetElementType(), 0);
                field.SetValue(handler, empty);
                Assert.AreSame(empty, field.GetValue(handler));
            }

            // 归一化应将空数组置 null（此后各处懒分配按常量重建）。
            // 不经 Initialize()（其内部 YooAssets.Initialize 的 DontDestroyOnLoad 在 EditMode 抛异常），
            // 直接反射调用归一化方法做单点验证。
            var normalize = type.GetMethod("NormalizeDeserializedArrays",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(normalize, "NormalizeDeserializedArrays not found.");

            Assert.DoesNotThrow(() => normalize.Invoke(handler, null));

            foreach (var name in fields)
            {
                var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var value = (System.Array)field.GetValue(handler);
                Assert.IsTrue(value == null || value.Length > 0, "field {0} still empty non-null array after normalization.", name);
            }
        }
    }
}
