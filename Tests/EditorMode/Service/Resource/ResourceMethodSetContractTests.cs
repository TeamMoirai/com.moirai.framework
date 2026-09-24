using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Service.Resource
{
    /// <summary>
    /// ResourceService 方法集契约测试：锁定外观公开面，防止后续重构悄然漂移。
    /// 含 legacy 族 Obsolete 特性、运行时配置属性读写、InitializePackageAsync/TryInitializePackageAsync 签名、
    /// HasAsset 三值语义四类断言。
    /// </summary>
    public sealed class ResourceMethodSetContractTests
    {
        private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;

        #region 租约 API 与已删成员 [LEASE API AND REMOVED MEMBERS]

        /// <summary>
        /// 租约一族必须都在，且不带 [Obsolete]——它们是遗留加载族的唯一替代。
        /// </summary>
        [Test]
        public void LeaseApi_Families_PresentAndNotObsolete()
        {
            string[] families = {
                "LoadLease", "LoadLeaseAsync", "AcquireDirect", "AcquireDirectAsync",
                "Release", "TryGetLeaseAsset", "LoadGameObject", "LoadGameObjectAsync",
            };
            MethodInfo[] methods = typeof(ResourceService).GetMethods(StaticPublic);

            foreach (string name in families)
            {
                int found = 0;
                foreach (MethodInfo method in methods)
                {
                    if (method.Name != name)
                    {
                        continue;
                    }

                    found++;
                    Assert.IsNull(method.GetCustomAttribute<ObsoleteAttribute>(),
                        "{0} 是现行 API，不得挂 [Obsolete]。");
                }

                Assert.IsTrue(found > 0, "Facade {0} 缺失。", name);
            }
        }

        /// <summary>
        /// 遗留加载族已删除，缺席本身要被钉住——否则一次误加回来就再没人知道它是死的。
        /// </summary>
        [Test]
        public void Removed_LegacyLoadFamily_IsAbsent()
        {
            foreach (string name in new[] { "LoadAsset", "LoadAssetAsync", "UnloadAsset" })
            {
                Assert.IsNull(typeof(ResourceService).GetMethod(name, StaticPublic),
                    "Facade {0} 应已删除（改用 LoadLease/LoadLeaseAsync + 租约）。", name);
                Assert.IsNull(typeof(ResourceServiceHandler).GetMethod(name),
                    "Handler {0} 应已删除。", name);
            }

            Assert.IsNull(typeof(ResourceService).GetMethod("TryAcquireDirect", StaticPublic),
                "Facade TryAcquireDirect 应已删除（AcquireDirect 失败本就返回 Invalid）。");
            MethodInfo warmup = typeof(ResourceService).GetMethod("WarmupResourceRecords", StaticPublic);
            Assert.IsNotNull(warmup, "Facade WarmupResourceRecords 缺失。");
            Assert.AreEqual(2, warmup.GetParameters().Length,
                "第三个参数 unityObjectIndexCapacity 随 by-UnityObject 索引一起删除了。");
        }

        /// <summary>
        /// 随遗留族一起消失的类型不得复活。
        /// </summary>
        [Test]
        public void Removed_CallbackTypes_AreAbsent()
        {
            foreach (string name in new[] { "LoadAssetCallbacks", "ELoadResourceStatus", "LoadAssetUpdateCallback" })
            {
                Assert.IsNull(typeof(ResourceService).Assembly.GetType("Moirai.Atropos.Resource." + name),
                    "类型 {0} 应随遗留加载族一并删除。", name);
            }
        }

        /// <summary>
        /// 绑定状态枚举形状：值不得重编号，取消与加载失败必须分家。
        /// </summary>
        [Test]
        public void BindStatusEnum_Shape_NoRenumberAndCancelSeparated()
        {
            CollectionAssert.AreEqual(
                new[] { "Success", "InvalidKey", "MissingOwner", "MissingTarget", "StaleOwner",
                        "Cancelled", "LoadFailed", "ApplyFailed", "ServiceShutdown" },
                Enum.GetNames(typeof(EResourceBindStatus)));
            Assert.AreEqual(5, (int)EResourceBindStatus.Cancelled, "Cancelled 必须占 4/6 之间的空位，不得重编号。");
            Assert.AreEqual(6, (int)EResourceBindStatus.LoadFailed);
        }

        #endregion

        #region 运行时配置 [RUNTIME CONFIGURATION]

        [Test]
        public void RuntimeConfig_AllFourProperties_ReadableAndWritable()
        {
            foreach (string propertyName in new[] { "AutoUnloadBundleWhenUnused", "DownloadingMaxNum", "FailedTryAgain", "Milliseconds" })
            {
                PropertyInfo property = typeof(ResourceService).GetProperty(propertyName, StaticPublic);
                Assert.IsNotNull(property, "Facade property {0} missing.", propertyName);
                Assert.IsNotNull(property.GetGetMethod(), "{0} must be readable.", propertyName);
                Assert.IsNotNull(property.GetSetMethod(), "{0} must be writable.", propertyName);

                Type expectedType = propertyName == "Milliseconds" ? typeof(long)
                    : propertyName == "AutoUnloadBundleWhenUnused" ? typeof(bool)
                    : typeof(int);
                Assert.AreEqual(expectedType, property.PropertyType);
            }
        }

        [Test]
        public void Handler_RuntimeConfig_AbstractContract_ReadableAndWritable()
        {
            foreach (string propertyName in new[] { "AutoUnloadBundleWhenUnused", "DownloadingMaxNum", "FailedTryAgain", "Milliseconds" })
            {
                PropertyInfo property = typeof(ResourceServiceHandler).GetProperty(propertyName);
                Assert.IsNotNull(property, "Handler abstract property {0} missing.", propertyName);
                Assert.IsTrue(property.GetGetMethod().IsAbstract || property.DeclaringType == typeof(ResourceServiceHandler),
                    "{0} must be declared on the abstract handler.", propertyName);
                Assert.IsNotNull(property.GetSetMethod(), "{0} must be writable.", propertyName);
            }
        }

        #endregion

        #region 包初始化 [PACKAGE INITIALIZATION]

        [Test]
        public void InitializePackageAsync_Signature()
        {
            MethodInfo method = typeof(ResourceService).GetMethod("InitializePackageAsync", StaticPublic);

            Assert.IsNotNull(method, "Facade InitializePackageAsync missing.");
            Assert.AreEqual(typeof(UniTask<bool>), method.ReturnType, "return type must be UniTask<bool>.");

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.IsTrue(parameters.All(p => p.ParameterType == typeof(string)), "all parameters must be string.");
            Assert.IsTrue(parameters.All(p => p.HasDefaultValue), "all parameters must be optional.");
            CollectionAssert.AreEqual(new[] { "packageName", "hostServerURL", "fallbackHostServerURL" },
                parameters.Select(p => p.Name).ToArray());
        }

        [Test]
        public void InitializePackageAsync_HandlerAbstract_Exists()
        {
            MethodInfo method = typeof(ResourceServiceHandler).GetMethod("InitializePackageAsync");

            Assert.IsNotNull(method, "Handler abstract InitializePackageAsync missing.");
            Assert.IsTrue(method.IsAbstract);
        }

        #endregion

        #region HasAsset 语义 [HAS ASSET SEMANTICS]

        [Test]
        public void HasAssetEnum_ThreeValues_Semantics()
        {
            Array values = Enum.GetValues(typeof(EResourceHasAssetResult));

            CollectionAssert.AreEqual(new[] { "NotExist", "AssetOnline", "AssetOnDisk" },
                Enum.GetNames(typeof(EResourceHasAssetResult)));
            CollectionAssert.AreEqual(new byte[] { 0, 1, 2 }, values.Cast<byte>().ToArray());
        }

        [Test]
        public void HasAsset_FacadeAndHandler_SignatureAligned()
        {
            MethodInfo facade = typeof(ResourceService).GetMethod("HasAsset", StaticPublic);
            MethodInfo handler = typeof(ResourceServiceHandler).GetMethod("HasAsset");

            Assert.IsNotNull(facade, "Facade HasAsset missing.");
            Assert.IsNotNull(handler, "Handler HasAsset missing.");
            Assert.AreEqual(typeof(EResourceHasAssetResult), facade.ReturnType);
            Assert.AreEqual(typeof(EResourceHasAssetResult), handler.ReturnType);
        }

        #endregion

        #region 辅助方法 [HELPERS]

        private static MethodInfo GetFacadeMethod(string name, Func<MethodInfo, bool> predicate)
        {
            return typeof(ResourceService).GetMethods(StaticPublic)
                .FirstOrDefault(m => m.Name == name && predicate(m));
        }

        #endregion
    }
}
