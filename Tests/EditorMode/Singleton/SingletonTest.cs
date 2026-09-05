using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Singleton
{
    /// <summary>
    /// <see cref="Singleton{T}"/> 纯 C# 单例的 EditMode 单元测试。
    /// 覆盖：惰性创建、初始化契约、线程安全、Dispose 幂等性与编辑器构造守卫。
    /// </summary>
    [TestFixture]
    public class SingletonTest
    {
        /// <summary>带生命周期计数的测试单例。</summary>
        private class CountingSingleton : Singleton<CountingSingleton>
        {
            /// <summary>OnInit 调用次数。</summary>
            public static int InitCount;

            /// <summary>OnShutdown 调用次数。</summary>
            public static int ShutdownCount;

            /// <summary>OnInit 中递归取回的实例（验证先发布后初始化契约）。</summary>
            public static CountingSingleton RecursiveSelf;

            protected override void OnInit()
            {
                InitCount++;
                RecursiveSelf = Instance; // 递归访问：应取回正在初始化中的同一实例
            }

            protected override void OnShutdown()
            {
                ShutdownCount++;
            }
        }

        /// <summary>无自定义行为的最小测试单例。</summary>
        private class PlainSingleton : Singleton<PlainSingleton> { }

        [SetUp]
        public void SetUp()
        {
            ResetStaticState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetStaticState();
        }

        /// <summary>反射清空静态实例并重置计数（跨用例隔离）。</summary>
        private static void ResetStaticState()
        {
            typeof(Singleton<CountingSingleton>)
                .GetField("s_Instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.SetValue(null, null);
            typeof(Singleton<PlainSingleton>)
                .GetField("s_Instance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.SetValue(null, null);
            CountingSingleton.InitCount = 0;
            CountingSingleton.ShutdownCount = 0;
            CountingSingleton.RecursiveSelf = null;
        }

        #region 惰性创建与初始化契约 [Lazy Creation & Init Contract]

        [Test]
        public void Instance_LazyCreates_AndCallsOnInitExactlyOnce()
        {
            Assert.IsFalse(CountingSingleton.IsValid, "访问前不应存在实例");

            CountingSingleton first = CountingSingleton.Instance;
            CountingSingleton second = CountingSingleton.Instance;

            Assert.IsNotNull(first);
            Assert.AreSame(first, second, "重复访问应返回同一实例");
            Assert.IsTrue(CountingSingleton.IsValid);
            Assert.AreEqual(1, CountingSingleton.InitCount, "OnInit 只应回调一次");
        }

        [Test]
        public void OnInit_RecursiveInstanceAccess_ReturnsSameInstance()
        {
            CountingSingleton instance = CountingSingleton.Instance;

            Assert.AreSame(instance, CountingSingleton.RecursiveSelf,
                "OnInit 内递归访问 Instance 应取回正在初始化中的同一实例（先发布后初始化契约）");
        }

        #endregion

        #region 生命周期 [Lifecycle]

        [Test]
        public void Dispose_ClearsInstance_AndCallsOnShutdownOnce()
        {
            CountingSingleton instance = CountingSingleton.Instance;

            instance.Dispose();

            Assert.IsFalse(CountingSingleton.IsValid, "Dispose 后实例应被清空");
            Assert.AreEqual(1, CountingSingleton.ShutdownCount);
        }

        [Test]
        public void Dispose_IsIdempotent_OnRepeatedCalls()
        {
            CountingSingleton instance = CountingSingleton.Instance;

            instance.Dispose();
            instance.Dispose();

            Assert.AreEqual(1, CountingSingleton.ShutdownCount, "重复 Dispose 不应重复回调 OnShutdown");
        }

        [Test]
        public void Dispose_OnStaleInstance_DoesNotKillLiveInstance()
        {
            // 先创建一个实例后释放，再造一个新实例（活动实例）
            CountingSingleton stale = CountingSingleton.Instance;
            stale.Dispose();
            CountingSingleton live = CountingSingleton.Instance;
            Assert.AreNotSame(stale, live);

            // 陈旧实例调用 Dispose 不应误杀当前活动实例
            stale.Dispose();

            Assert.IsTrue(CountingSingleton.IsValid, "活动实例不应被陈旧实例的 Dispose 误杀");
            Assert.AreSame(live, CountingSingleton.Instance);
        }

        [Test]
        public void Instance_AfterDispose_CreatesNewInstanceAndReinitializes()
        {
            CountingSingleton first = CountingSingleton.Instance;
            first.Dispose();

            CountingSingleton second = CountingSingleton.Instance;

            Assert.AreNotSame(first, second, "Dispose 后应创建新实例");
            Assert.AreEqual(2, CountingSingleton.InitCount, "新实例应再次回调 OnInit");
        }

        [Test]
        public void Dispose_ImplementsIDisposable()
        {
            Assert.IsTrue(typeof(IDisposable).IsAssignableFrom(typeof(Singleton<PlainSingleton>)),
                "Singleton<T> 应实现 IDisposable（using 语句与统一释放语义）");
        }

        #endregion

        #region 线程安全 [Thread Safety]

        [Test]
        public void Instance_ConcurrentAccessFromMultipleThreads_ReturnsSingleInstance()
        {
            const int THREAD_COUNT = 8;
            const int LOOP_COUNT = 64;
            var results = new CountingSingleton[THREAD_COUNT * LOOP_COUNT];
            var barrier = new Barrier(THREAD_COUNT);
            var threads = new List<Thread>(THREAD_COUNT);

            for (int i = 0; i < THREAD_COUNT; i++)
            {
                int index = i;
                var thread = new Thread(() =>
                {
                    barrier.SignalAndWait(); // 全员就绪后同时冲击 Instance
                    for (int n = 0; n < LOOP_COUNT; n++)
                    {
                        results[index * LOOP_COUNT + n] = CountingSingleton.Instance;
                    }
                });
                threads.Add(thread);
                thread.Start();
            }

            foreach (Thread thread in threads) thread.Join();

            CountingSingleton first = results[0];
            Assert.IsNotNull(first);
            Assert.IsTrue(results.All(r => ReferenceEquals(r, first)), "所有线程应取回同一实例");
            Assert.AreEqual(1, CountingSingleton.InitCount, "并发首次访问下 OnInit 只应回调一次");
        }

        #endregion

        #region 编辑器守卫 [Editor Validation]

        [Test]
        public void DirectConstruction_LogsErrorInEditor()
        {
            LogAssert.Expect(LogType.Error, new Regex(".*"));

            var rogue = new PlainSingleton();

            Assert.IsNotNull(rogue);
            // rogue 未经 Instance 登记：IsValid 不受影响（游离实例不污染单例状态）
            Assert.IsFalse(PlainSingleton.IsValid);
        }

        [Test]
        public void InstanceConstruction_ViaGetter_DoesNotLogError()
        {
            LogAssert.NoUnexpectedReceived();

            _ = PlainSingleton.Instance;

            // 无错误日志即通过（守卫不应误报合法的 Instance 物化）
        }

        #endregion
    }
}
