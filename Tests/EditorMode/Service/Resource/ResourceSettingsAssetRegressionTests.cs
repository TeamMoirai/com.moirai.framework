using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Service.Resource
{
    /// <summary>
    /// 真实设置资产的回归门禁：读的是工程里那份 <c>ResourceServiceSettings.asset</c>，不是测试自造的实例。
    /// <para><c>[SerializeReference]</c> 的后端引用一旦静默失效（改名、挪程序集、类型下线），Unity 不报错，
    /// 只把那条引用读成 null——服务照样初始化成功，之后每一次取用都打在空后端上。这是它唯一的自动防线。</para>
    /// <para>值比对走 <see cref="SerializedObject"/> 的原生字段对公开属性：属性层加过夹取、换过默认值、
    /// 或忘了转发，都会在这里露出来（内核抽取那一轮真的动过这几个读数）。</para>
    /// </summary>
    public sealed class ResourceSettingsAssetRegressionTests
    {
        [Test]
        public void SettingsAsset_DeserializesAndMatchesTheSingleton()
        {
            ResourceServiceSettings settings = LoadProjectSettings();
            if (settings == null)
            {
                return;
            }

            Assert.AreSame(settings, ResourceServiceSettings.Instance,
                "按资产路径读到的那份与 Instance 不是同一个对象：加载路径与 Resources.Load 已经分叉");
        }

        [Test]
        public void SettingsAsset_HandlerReferenceResolvesToConcreteBackend()
        {
            if (LoadProjectSettings() == null)
            {
                return;
            }

            ResourceServiceHandler handler = ResourceServiceSettings.ResourceServiceHandler;
            Assert.IsNotNull(handler,
                "后端引用读成了 null——[SerializeReference] 记录的类型名在当前程序集里已经找不到，" +
                "而 Unity 不会为这件事报任何错");
            Assert.IsInstanceOf<YooAssetHandler>(handler,
                "入库资产配的是 YooAsset 后端；换后端要连这份断言一起改，不要让它替改动放行");
            Assert.False(string.IsNullOrEmpty(handler.DefaultPackageName),
                "默认包名为空：取用会全部落到一个不存在的包上");

            // 未配置的可选引用必须读回 null，而不是"非 null 的零值对象"——后者会让所有判空守卫失效。
            Assert.IsNull(((YooAssetHandler)handler).EncryptorHandler);
        }

        /// <summary>
        /// 每个公开读数都必须等于资产里那个字段：夹取、重定向、漏转发都在这里现形。
        /// </summary>
        [Test]
        public void SettingsAsset_PublicReadingsMatchSerializedFields()
        {
            ResourceServiceSettings settings = LoadProjectSettings();
            if (settings == null)
            {
                return;
            }

            var serialized = new SerializedObject(settings);

            AssertRawInt(serialized, "m_AssetRecordCapacity", ResourceServiceSettings.AssetRecordCapacity);
            AssertRawInt(serialized, "m_AssetLeaseCapacity", ResourceServiceSettings.AssetLeaseCapacity);
            AssertRawInt(serialized, "m_BindingOwnerCapacity", ResourceServiceSettings.BindingOwnerCapacity);
            AssertRawInt(serialized, "m_BindingSlotCapacity", ResourceServiceSettings.BindingSlotCapacity);
            AssertRawInt(serialized, "m_ExpireProcessCountPerFrame", ResourceServiceSettings.ExpireProcessCountPerFrame);
            AssertRawInt(serialized, "m_ExpireProcessCountWhenUnloading",
                ResourceServiceSettings.ExpireProcessCountWhenUnloading);
            AssertRawInt(serialized, "m_DestroySweepBudget", ResourceServiceSettings.DestroySweepBudget);
            AssertRawInt(serialized, "m_IdleAssetCapacity", ResourceServiceSettings.IdleAssetCapacity);
            AssertRawFloat(serialized, "m_IdleAssetExpireTime", ResourceServiceSettings.IdleAssetExpireTime);
            AssertRawFloat(serialized, "m_MinUnloadUnusedAssetsInterval",
                ResourceServiceSettings.MinUnloadUnusedAssetsInterval);
            AssertRawFloat(serialized, "m_MaxUnloadUnusedAssetsInterval",
                ResourceServiceSettings.MaxUnloadUnusedAssetsInterval);
            AssertRawFloat(serialized, "m_MinGCCollectInterval", ResourceServiceSettings.MinGCCollectInterval);
            Assert.AreEqual((EResourcePlayMode)serialized.FindProperty("m_PlayMode").enumValueIndex,
                ResourceServiceSettings.PlayMode, "运行模式的读取结果与资产里的配置值不一致");
        }

        private static void AssertRawInt(SerializedObject serialized, string fieldName, int reading)
        {
            var property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, "资产里没有 {0} 这个字段。", fieldName);
            Assert.AreEqual(property.intValue, reading, "{0} 的公开读数与资产值不一致。", fieldName);
        }

        private static void AssertRawFloat(SerializedObject serialized, string fieldName, float reading)
        {
            var property = serialized.FindProperty(fieldName);
            Assert.IsNotNull(property, "资产里没有 {0} 这个字段。", fieldName);
            Assert.AreEqual(property.floatValue, reading, "{0} 的公开读数与资产值不一致。", fieldName);
        }

        /// <summary>
        /// 取工程里那份设置资产；没有就整组忽略（包不能要求宿主工程一定有这份资产）。
        /// <para>多于一份时直接判失败而不是忽略：加载方按类型名搜资产，那时"打包后取到哪一份"已经不确定，
        /// 这是会在真机上随机发作的一类配置错误。</para>
        /// </summary>
        private static ResourceServiceSettings LoadProjectSettings()
        {
            string[] guids = AssetDatabase.FindAssets("t:ResourceServiceSettings");
            if (guids.Length == 0)
            {
                Assert.Ignore("本工程没有 ResourceServiceSettings.asset，整套配置真值门禁无从校验。");
                return null;
            }

            Assert.AreEqual(1, guids.Length, "ResourceServiceSettings 资产有多份，Resources.Load 取到哪一份不确定。");
            return AssetDatabase.LoadAssetAtPath<ResourceServiceSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
