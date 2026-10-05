using System;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Resource.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UObject = UnityEngine.Object;

namespace Service.Resource
{
    /// <summary>
    /// AssetReference 弱引用的序列化形状、编辑器回退加载、释放生命周期与 GUID 解析降级口径。
    /// </summary>
    /// <remarks>
    /// 服务未就绪（handler 置 null）走 AssetDatabase 回退路径：以本测试脚本自身资源为解析目标，不依赖工程内容。
    /// </remarks>
    public sealed class AssetReferenceTests
    {
        private ResourceServiceHandler _savedHandler;

        [SetUp]
        public void SetUp()
        {
            _savedHandler = ResourceService.Internal_PeekHandler();
            ResourceService.Internal_UseHandler(null);
        }

        [TearDown]
        public void TearDown()
        {
            ResourceService.Internal_UseHandler(_savedHandler);
        }

        #region 序列化形状 [SERIALIZATION SHAPE]

        [Test]
        public void Serialization_RoundTrip_PreservesGuidAndPackageName()
        {
            ReferenceHost host = ScriptableObject.CreateInstance<ReferenceHost>();
            ReferenceHost restored = ScriptableObject.CreateInstance<ReferenceHost>();
            try
            {
                host.m_Reference.GUID = TestScriptGuid();
                host.m_Reference.PackageName = "TestPackage";
                host.m_ArrayRefs[0].GUID = TestScriptGuid();

                string json = EditorJsonUtility.ToJson(host);
                EditorJsonUtility.FromJsonOverwrite(json, restored);

                Assert.AreEqual(host.m_Reference.GUID, restored.m_Reference.GUID);
                Assert.AreEqual(host.m_Reference.PackageName, restored.m_Reference.PackageName);
                Assert.AreEqual(host.m_ArrayRefs[0].GUID, restored.m_ArrayRefs[0].GUID);
                Assert.IsFalse(restored.m_Reference.IsLoaded, "运行时缓存不得进序列化快照。");
            }
            finally
            {
                UObject.DestroyImmediate(host);
                UObject.DestroyImmediate(restored);
            }
        }

        [Test]
        public void Guid_Setter_NullNormalizesToEmpty()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript> { GUID = null, PackageName = null };

            Assert.AreEqual(string.Empty, reference.GUID);
            Assert.AreEqual(string.Empty, reference.PackageName);
        }

        #endregion

        #region 编辑器回退加载 [EDITOR FALLBACK LOADING]

        [Test]
        public void LoadAssetAsync_ServiceUnready_ResolvesViaAssetDatabase()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript> { GUID = TestScriptGuid() };

            MonoScript script = reference.LoadAssetAsync().GetAwaiter().GetResult();

            Assert.IsNotNull(script);
            Assert.IsTrue(reference.IsLoaded);
        }

        [Test]
        public void LoadAssetAsync_Twice_ReplaysCachedAsset()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript> { GUID = TestScriptGuid() };

            MonoScript first = reference.LoadAssetAsync().GetAwaiter().GetResult();
            MonoScript second = reference.LoadAssetAsync().GetAwaiter().GetResult();

            Assert.AreEqual(first, second);
        }

        [Test]
        public void LoadAssetAsync_EmptyGuid_ReturnsNull()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript>();

            MonoScript script = reference.LoadAssetAsync().GetAwaiter().GetResult();

            Assert.IsNull(script);
            Assert.IsFalse(reference.IsLoaded);
        }

        [Test]
        public void ReleaseAsset_AfterEditorLoad_ClearsCache()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript> { GUID = TestScriptGuid() };
            reference.LoadAssetAsync().GetAwaiter().GetResult();

            reference.ReleaseAsset();
            reference.ReleaseAsset(); // 重复释放必须安全

            Assert.IsFalse(reference.IsLoaded);
        }

        #endregion

        #region GUID 解析降级 [GUID RESOLUTION DEGRADE]

        [Test]
        public void TryGetLocationFromGuid_ServiceUnready_ResolvesViaAssetDatabase()
        {
            string guid = TestScriptGuid();

            bool resolved = ResourceService.TryGetLocationFromGuid(guid, out string location);

            Assert.IsTrue(resolved);
            Assert.AreEqual(AssetDatabase.GUIDToAssetPath(guid), location);
        }

        [Test]
        public void TryGetLocationFromGuid_EmptyGuid_ReturnsFalse()
        {
            bool resolved = ResourceService.TryGetLocationFromGuid(string.Empty, out string location);

            Assert.IsFalse(resolved);
            Assert.IsNull(location);
        }

        [Test]
        public void RuntimeKeyIsValid_ResolvableGuid_ReturnsTrue()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript> { GUID = TestScriptGuid() };

            Assert.IsTrue(reference.RuntimeKeyIsValid());
        }

        [Test]
        public void RuntimeKeyIsValid_EmptyGuid_ReturnsFalse()
        {
            AssetReference<MonoScript> reference = new AssetReference<MonoScript>();

            Assert.IsFalse(reference.RuntimeKeyIsValid());
        }

        #endregion

        /// <summary>
        /// 本测试脚本自身的资源 GUID：任何装了本包的工程里都存在的稳定解析目标。
        /// </summary>
        private static string TestScriptGuid()
        {
            string[] guids = AssetDatabase.FindAssets("AssetReferenceTests t:Script");
            Assert.IsNotEmpty(guids, "找不到 AssetReferenceTests 脚本资源。");
            return guids[0];
        }

        /// <summary>
        /// 序列化宿主：单引用与数组引用两个槽位。
        /// </summary>
        private sealed class ReferenceHost : ScriptableObject
        {
            [SerializeField] internal AssetReference<MonoScript> m_Reference = new AssetReference<MonoScript>();
            [SerializeField] internal AssetReference<MonoScript>[] m_ArrayRefs = { new AssetReference<MonoScript>() };
        }
    }
}
