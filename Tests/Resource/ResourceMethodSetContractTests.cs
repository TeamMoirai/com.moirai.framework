using System;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Resource
{
    /// <summary>
    /// ResourceService 方法集契约测试：锁定外观公开面，防止后续重构悄然漂移。
    /// 含 legacy 族 Obsolete 特性、运行时配置属性读写、InitPackageAsync 签名、
    /// HasAsset 三值语义四类断言。
    /// </summary>
    public sealed class ResourceMethodSetContractTests
    {
        private const BindingFlags StaticPublic = BindingFlags.Public | BindingFlags.Static;

        #region 遗留 API [LEGACY API]

        [Test]
        public void Legacy_LoadAsset_GenericClass_HasObsoleteAttribute()
        {
            MethodInfo method = GetFacadeMethod("LoadAsset", m => m.IsGenericMethod &&
                m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));

            Assert.IsNotNull(method, "Facade LoadAsset<T>(string, string) missing.");
            AssertHasObsolete(method);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual("packageName", parameters[1].Name);
            Assert.IsTrue(parameters[1].HasDefaultValue, "packageName must be optional.");
        }

        [Test]
        public void Legacy_LoadAssetWithCallback_GenericClass_HasObsoleteAttribute()
        {
            MethodInfo method = GetFacadeMethod("LoadAsset", m => m.IsGenericMethod &&
                m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(string));

            Assert.IsNotNull(method, "Facade LoadAsset<T>(string, Action<T>, string) missing.");
            AssertHasObsolete(method);
            Assert.AreEqual(typeof(Action<>).MakeGenericType(method.GetGenericArguments()[0]),
                method.GetParameters()[1].ParameterType);
        }

        [Test]
        public void Legacy_LoadAssetAsync_GenericClass_HasObsoleteAttribute()
        {
            MethodInfo method = GetFacadeMethod("LoadAssetAsync", m => m.IsGenericMethod);

            Assert.IsNotNull(method, "Facade LoadAssetAsync<T>(string, CancellationToken, string) missing.");
            AssertHasObsolete(method);
            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.AreEqual("cancellationToken", parameters[1].Name);
        }

        [Test]
        public void Legacy_LoadAssetAsyncCallbackFamily_BothOverloads_HasObsoleteAttribute()
        {
            MethodInfo withAssetType = GetFacadeMethod("LoadAssetAsync", m => !m.IsGenericMethod &&
                m.GetParameters().Length == 6 && m.GetParameters()[1].ParameterType == typeof(Type));

            Assert.IsNotNull(withAssetType, "Facade LoadAssetAsync(location, Type, priority, callbacks, userData, packageName) missing.");
            AssertHasObsolete(withAssetType);

            MethodInfo withoutAssetType = GetFacadeMethod("LoadAssetAsync", m => !m.IsGenericMethod &&
                m.GetParameters().Length == 5);

            Assert.IsNotNull(withoutAssetType, "Facade LoadAssetAsync(location, priority, callbacks, userData, packageName) missing.");
            AssertHasObsolete(withoutAssetType);

            foreach (var method in new[] { withAssetType, withoutAssetType })
            {
                Assert.AreEqual(typeof(LoadAssetCallbacks), method.GetParameters().First(p => p.ParameterType == typeof(LoadAssetCallbacks)).ParameterType);
            }
        }

        [Test]
        public void Legacy_UnloadAsset_HasObsoleteAttribute()
        {
            MethodInfo method = typeof(ResourceService).GetMethod("UnloadAsset", StaticPublic);

            Assert.IsNotNull(method, "Facade UnloadAsset(object) missing.");
            AssertHasObsolete(method);
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
        public void InitPackageAsync_Signature()
        {
            MethodInfo method = typeof(ResourceService).GetMethod("InitPackageAsync", StaticPublic);

            Assert.IsNotNull(method, "Facade InitPackageAsync missing.");
            Assert.AreEqual(typeof(UniTask<bool>), method.ReturnType, "return type must be UniTask<bool>.");

            ParameterInfo[] parameters = method.GetParameters();
            Assert.AreEqual(3, parameters.Length);
            Assert.IsTrue(parameters.All(p => p.ParameterType == typeof(string)), "all parameters must be string.");
            Assert.IsTrue(parameters.All(p => p.HasDefaultValue), "all parameters must be optional.");
            CollectionAssert.AreEqual(new[] { "packageName", "hostServerURL", "fallbackHostServerURL" },
                parameters.Select(p => p.Name).ToArray());
        }

        [Test]
        public void InitPackageAsync_HandlerAbstract_Exists()
        {
            MethodInfo method = typeof(ResourceServiceHandler).GetMethod("InitPackageAsync");

            Assert.IsNotNull(method, "Handler abstract InitPackageAsync missing.");
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

        private static void AssertHasObsolete(MethodInfo method)
        {
            var attribute = method.GetCustomAttribute<ObsoleteAttribute>();
            Assert.IsNotNull(attribute, "{0} must be marked [Obsolete].", method.Name);
            StringAssert.Contains("Lease", attribute.Message);
        }

        #endregion
    }
}
