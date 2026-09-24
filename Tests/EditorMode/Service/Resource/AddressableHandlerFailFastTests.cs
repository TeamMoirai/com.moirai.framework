using System;
using System.Reflection;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using NUnit.Framework;

namespace Service.Resource
{
    /// <summary>
    /// AddressableHandler 的契约测试：实验性后端的能力缺失必须以 GameException 暴露，
    /// 禁止退回静默 no-op；已经接上的那条链（低内存回收委托）则必须真的走通。
    /// 运行时符号随 ADDRESSABLES_INSTALLED 条件编译存在，用反射定位并断言；
    /// 未安装 Addressables 的环境下整组忽略。
    /// <para>这里只钉"哪些成员仍然抛"与"哪些委托必须落地"。接通了的取用族不在此列——
    /// 它们的正解是真的返回值，拿反射断言"不抛"等于什么都不断。</para>
    /// </summary>
    public sealed class AddressableHandlerFailFastTests
    {
        private const string HandlerTypeName = "Moirai.Atropos.Resource.AddressableHandler";
        private const string MessageFragment = "not implemented";

        [Test]
        public void AcquireDirect_ThrowsGameException()
        {
            InvokeExpectingFailFast("AcquireDirect", new ResourceKey("UI/Heart"));
        }

        [Test]
        public void AcquireBinding_ThrowsGameException()
        {
            InvokeExpectingFailFast("AcquireBinding", new ResourceKey("UI/Heart"));
        }

        [Test]
        public void AcquirePrefabSourceLease_ThrowsGameException()
        {
            InvokeExpectingFailFast("AcquirePrefabSourceLease", "UI/Heart", string.Empty);
        }

        [Test]
        public void LoadGameObject_ThrowsGameException()
        {
            InvokeExpectingFailFast("LoadGameObject", "UI/Heart", null, string.Empty);
        }

        [Test]
        public void LoadLeaseByKey_ThrowsGameException()
        {
            InvokeGenericExpectingFailFast("LoadLease", method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(ResourceKey);
            }, new ResourceKey("UI/Heart"));
        }

        [Test]
        public void LoadLeaseByLocation_ThrowsGameException()
        {
            InvokeGenericExpectingFailFast("LoadLease", method =>
            {
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 2 && parameters[0].ParameterType == typeof(string);
            }, "UI/Heart", string.Empty);
        }

        [Test]
        public void GetDownloadSize_ThrowsGameException()
        {
            InvokeExpectingFailFast("GetDownloadSize", "UI/Heart", string.Empty);
        }

        [Test]
        public void RequestPackageVersion_ThrowsGameException()
        {
            InvokeExpectingFailFast("RequestPackageVersionAsync", false, 60, string.Empty);
        }

        [Test]
        public void LoadPackageManifest_ThrowsGameException()
        {
            InvokeExpectingFailFast("LoadPackageManifestAsync", "1.0.0", 60, string.Empty);
        }

        [Test]
        public void CreateResourceDownloader_ThrowsGameException()
        {
            InvokeExpectingFailFast("CreateResourceDownloader", string.Empty);
        }

        /// <summary>
        /// 低内存这条链必须是通的：登记进去的强制回收委托要真的被调用，且以 force=true 调用。
        /// <para>这一对成员原本都是空方法体，于是 <c>Application.lowMemory</c> 到了这座后端什么也不做，
        /// 而调用方看到的行为是"成功返回"——静默 no-op 里最典型的一种。</para>
        /// </summary>
        [Test]
        public void OnLowMemory_InvokesRegisteredForceUnloadAction()
        {
            object instance = CreateInstance();
            if (instance == null)
            {
                Assert.Ignore("AddressableHandler is not compiled (ADDRESSABLES_INSTALLED undefined).");
                return;
            }

            int invokedWith = -1;
            Action<bool> action = performGCCollect => invokedWith = performGCCollect ? 1 : 0;

            MethodInfo setter = FindMethod(instance.GetType(), "SetForceUnloadUnusedAssetsAction");
            MethodInfo onLowMemory = FindMethod(instance.GetType(), "OnLowMemory");
            Assert.IsNotNull(setter, "SetForceUnloadUnusedAssetsAction not found on handler.");
            Assert.IsNotNull(onLowMemory, "OnLowMemory not found on handler.");

            setter.Invoke(instance, new object[] { action });
            onLowMemory.Invoke(instance, null);

            Assert.AreEqual(1, invokedWith, "委托未被调用：这条后端把 Application.lowMemory 吞了");
        }

        private static object CreateInstance()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(HandlerTypeName, false);
                if (type != null)
                {
                    return Activator.CreateInstance(type);
                }
            }

            return null;
        }

        /// <summary>
        /// 反射调用指定成员并断言抛出带预期片段的 GameException。
        /// <para>public 与 internal 都要查：同步取用族的三个成员是 <c>internal override</c>，
        /// 只按 public 找会得到"成员不存在"，看着像后端删了它们。</para>
        /// </summary>
        /// <param name="methodName">成员名。</param>
        /// <param name="args">实参表。</param>
        /// <returns>是否实际执行了调用（false 表示后端类型不存在而忽略）。</returns>
        private static bool InvokeExpectingFailFast(string methodName, params object[] args)
        {
            object instance = CreateInstance();
            if (instance == null)
            {
                Assert.Ignore("AddressableHandler is not compiled (ADDRESSABLES_INSTALLED undefined).");
                return false;
            }

            var method = FindMethod(instance.GetType(), methodName);
            Assert.IsNotNull(method, "{0} not found on handler.", methodName);

            InvokeAndExpectFailFast(method, instance, args);
            return true;
        }

        /// <summary>
        /// 关闭泛型成员后反射调用，断言方式同 <see cref="InvokeExpectingFailFast"/>。
        /// </summary>
        private static bool InvokeGenericExpectingFailFast(string methodName, Func<MethodInfo, bool> predicate,
            params object[] args)
        {
            object instance = CreateInstance();
            if (instance == null)
            {
                Assert.Ignore("AddressableHandler is not compiled (ADDRESSABLES_INSTALLED undefined).");
                return false;
            }

            MethodInfo closedMethod = null;
            foreach (var candidate in instance.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                                    BindingFlags.NonPublic))
            {
                if (candidate.Name.Equals(methodName, StringComparison.Ordinal) && candidate.IsGenericMethod &&
                    predicate(candidate))
                {
                    closedMethod = candidate.MakeGenericMethod(typeof(UnityEngine.Object));
                    break;
                }
            }

            Assert.IsNotNull(closedMethod, "{0}<{1}> matching the predicate not found on handler.", methodName,
                nameof(UnityEngine.Object));

            InvokeAndExpectFailFast(closedMethod, instance, args);
            return true;
        }

        private static MethodInfo FindMethod(Type type, string methodName)
        {
            foreach (var candidate in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                      BindingFlags.NonPublic))
            {
                if (candidate.Name.Equals(methodName, StringComparison.Ordinal) && !candidate.IsGenericMethod)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static void InvokeAndExpectFailFast(MethodInfo method, object instance, object[] args)
        {
            var exception = Assert.Throws<TargetInvocationException>(
                () => method.Invoke(instance, args));
            Assert.IsInstanceOf<GameException>(exception.InnerException);
            StringAssert.Contains(MessageFragment, exception.InnerException.Message);
            StringAssert.Contains(method.Name, exception.InnerException.Message);
        }
    }
}
