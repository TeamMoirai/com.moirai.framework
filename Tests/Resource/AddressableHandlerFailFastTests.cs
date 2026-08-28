using System;
using System.Reflection;
using Moirai.Atropos;
using Moirai.Atropos.Resource;
using NUnit.Framework;
using UnityEngine;

namespace Resource
{
    /// <summary>
    /// AddressableHandler fail-fast 契约测试：实验性后端的能力缺失必须以 GameException 暴露，
    /// 禁止退回静默 no-op。运行时符号随 ADDRESSABLES_INSTALLED 条件编译存在，用反射定位并断言；
    /// 未安装 Addressables 的环境下整组忽略。
    /// </summary>
    public sealed class AddressableHandlerFailFastTests
    {
        private const string HandlerTypeName = "Moirai.Atropos.Resource.AddressableHandler";
        private const string MessageFragment = "not implemented";

        [Test]
        public void InitPackage_ThrowsGameException()
        {
            InvokeExpectingFailFast("InitPackage", "InitPackage", "DemoPackage", false);
        }

        [Test]
        public void AcquireDirect_ThrowsGameException()
        {
            InvokeExpectingFailFast("AcquireDirect", "AcquireDirect", new ResourceKey("UI/Heart"));
        }

        [Test]
        public void TryAcquireDirect_ThrowsGameExceptionInsteadOfSilentFalse()
        {
            // out 参数也是形参：反射 Invoke 的 args 数组必须提供等长槽位。
            object[] args = new object[] { new ResourceKey("UI/Heart"), null };

            if (InvokeExpectingFailFast("TryAcquireDirect", "TryAcquireDirect", args))
            {
                var handle = (ResourceLeaseHandle)args[1];
                Assert.IsFalse(handle.IsValid, "out handle must be invalid pre-throw assignment.");
            }
        }

        [Test]
        public void Release_ThrowsGameException()
        {
            InvokeExpectingFailFast("Release", "Release", ResourceLeaseHandle.Invalid);
        }

        [Test]
        public void LoadLease_ThrowsGameException()
        {
            object instance = CreateInstance();
            if (instance == null)
            {
                Assert.Ignore("AddressableHandler is not compiled (ADDRESSABLES_INSTALLED undefined).");
                return;
            }

            Type type = instance.GetType();
            MethodInfo closedMethod = null;
            foreach (var candidate in type.GetMethods())
            {
                if (!candidate.Name.Equals("LoadLease", StringComparison.Ordinal) || !candidate.IsGenericMethod)
                {
                    continue;
                }

                var parameters = candidate.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(string))
                {
                    closedMethod = candidate.MakeGenericMethod(typeof(UnityEngine.Object));
                    break;
                }
            }

            if (closedMethod == null)
            {
                Assert.Ignore("public generic LoadLease<T>(string, string) not found.");
                return;
            }

            var exception = Assert.Throws<TargetInvocationException>(
                () => closedMethod.Invoke(instance, new object[] { "UI/Heart", string.Empty }));
            Assert.IsInstanceOf<GameException>(exception.InnerException);
            StringAssert.Contains(MessageFragment, exception.InnerException.Message);
            StringAssert.Contains("LoadLease", exception.InnerException.Message);
        }

        [Test]
        public void LoadGameObject_ThrowsGameException()
        {
            InvokeExpectingFailFast("LoadGameObject", "LoadGameObject", "UI/Heart", null, string.Empty);
        }

        [Test]
        public void UnloadAsset_ThrowsGameException()
        {
            InvokeExpectingFailFast("UnloadAsset", "UnloadAsset", new object());
        }

        [Test]
        public void GetDownloadSize_ThrowsGameException()
        {
            InvokeExpectingFailFast("GetDownloadSize", "GetDownloadSize", "UI/Heart", string.Empty);
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
        /// </summary>
        /// <returns>是否实际执行了调用（false 表示后端类型不存在而忽略）。</returns>
        private static bool InvokeExpectingFailFast(string methodName, string expectedMessageFragment, params object[] args)
        {
            object instance = CreateInstance();
            if (instance == null)
            {
                Assert.Ignore("AddressableHandler is not compiled (ADDRESSABLES_INSTALLED undefined).");
                return false;
            }

            var method = instance.GetType().GetMethod(methodName);
            Assert.IsNotNull(method, "{0} not found on handler.", methodName);

            var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
                () => method.Invoke(instance, args));
            Assert.IsInstanceOf<GameException>(exception.InnerException);
            StringAssert.Contains(MessageFragment, exception.InnerException.Message);
            StringAssert.Contains(expectedMessageFragment, exception.InnerException.Message);
            return true;
        }
    }
}
