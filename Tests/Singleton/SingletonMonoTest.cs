using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Singleton
{
    /// <summary>
    /// <see cref="SingletonMono{T}"/> 的 EditMode 单元测试。
    /// 编辑模式下 MonoBehaviour 回调不会自动触发，故以反射模拟 Awake/OnDestroy 生命周期，
    /// 覆盖：实例物化、退出窗口、多实例消解与销毁清理。
    /// </summary>
    [TestFixture]
    public class SingletonMonoTest
    {
        /// <summary>带生命周期计数的测试单例（每个用例独立清理静态状态）。</summary>
        private class TestSingletonMono : SingletonMono<TestSingletonMono>
        {
            /// <summary>OnInit 调用次数。</summary>
            public static int InitCount;

            /// <summary>OnShutdown 调用次数。</summary>
            public static int ShutdownCount;

            protected override void OnInit() => InitCount++;

            protected override void OnShutdown() => ShutdownCount++;
        }

        /// <summary>专用于后台线程 fail-fast 探测的独立类型（避免与其他用例共享静态状态）。</summary>
        private class BackgroundProbeSingleton : SingletonMono<BackgroundProbeSingleton> { }

        private static readonly FieldInfo InstanceField = typeof(SingletonMono<TestSingletonMono>)
            .GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly FieldInfo ShuttingDownField = typeof(SingletonMono<TestSingletonMono>)
            .GetField("s_ShuttingDown", BindingFlags.NonPublic | BindingFlags.Static);

        private static readonly MethodInfo AwakeMethod = typeof(SingletonMono<TestSingletonMono>)
            .GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo OnDestroyMethod = typeof(SingletonMono<TestSingletonMono>)
            .GetMethod("OnDestroy", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _createdObjects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            ResetStaticState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetStaticState();
            foreach (GameObject go in _createdObjects)
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
            _createdObjects.Clear();
        }

        /// <summary>重置静态状态并销毁测试期间创建的对象（跨用例隔离）。</summary>
        private static void ResetStaticState()
        {
            InstanceField.SetValue(null, null);
            ShuttingDownField.SetValue(null, false);
            TestSingletonMono.InitCount = 0;
            TestSingletonMono.ShutdownCount = 0;

            typeof(SingletonMono<BackgroundProbeSingleton>)
                .GetField("s_Instance", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);
        }

        /// <summary>创建挂载测试单例的 GameObject（编辑模式下回调不自动触发）。</summary>
        private TestSingletonMono CreateSingletonGameObject()
        {
            var go = new GameObject("SingletonMonoTest_Object");
            _createdObjects.Add(go);
            return go.AddComponent<TestSingletonMono>();
        }

        #region 实例访问 [Instance Access]

        [Test]
        public void Instance_EditModeWithoutInstance_ReturnsNullWithoutCreating()
        {
            Assert.IsNull(TestSingletonMono.Instance, "编辑模式未找到实例时应返回 null 而非自动创建");
            Assert.IsFalse(TestSingletonMono.IsValid);
            Assert.IsNull(TestSingletonMono.TryGetInstance());
            Assert.IsNull(TestSingletonMono.Current);
        }

        [Test]
        public void Instance_EditMode_FindsExistingSceneInstance()
        {
            TestSingletonMono existing = CreateSingletonGameObject();

            // 静态状态为空但场景中已有组件：应查找到既有实例而非返回 null
            TestSingletonMono found = TestSingletonMono.Instance;

            Assert.IsNotNull(found);
            Assert.AreSame(existing, found);
        }

        [Test]
        public void Instance_DuringShutdownWindow_ReturnsNull()
        {
            TestSingletonMono instance = CreateSingletonGameObject();
            InvokeAwake(instance);
            Assert.IsTrue(TestSingletonMono.IsValid);

            ShuttingDownField.SetValue(null, true);

            Assert.IsNull(TestSingletonMono.Instance, "退出窗口期 Instance 应返回 null");
            Assert.IsFalse(TestSingletonMono.IsValid, "退出窗口期 IsValid 应为 false");
            Assert.IsNull(TestSingletonMono.TryGetInstance());
        }

        [Test]
        public void Instance_FromBackgroundThreadBeforeMaterialization_ThrowsGameException()
        {
            Exception captured = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = BackgroundProbeSingleton.Instance;
                }
                catch (Exception e)
                {
                    captured = e;
                }
            });
            thread.Start();
            thread.Join();

            Assert.IsInstanceOf<GameException>(captured,
                "物化前从后台线程访问应 fail-fast 抛出 GameException，而非越线程调用 Unity API");
        }

        #endregion

        #region 生命周期 [Lifecycle]

        [Test]
        public void Awake_AssignsInstanceAndCallsOnInit()
        {
            TestSingletonMono instance = CreateSingletonGameObject();

            InvokeAwake(instance);

            Assert.IsTrue(TestSingletonMono.IsValid);
            Assert.AreSame(instance, TestSingletonMono.Instance);
            Assert.AreEqual(1, TestSingletonMono.InitCount, "胜出实例的 Awake 应回调 OnInit");
        }

        [Test]
        public void Awake_Duplicate_KeepsExistingInstanceWhenNotReplaceable()
        {
            TestSingletonMono first = CreateSingletonGameObject();
            InvokeAwake(first);

            // 编辑模式下 Destroy 被拒绝并记录错误，此处为预期行为（重复实例的自毁尝试）
            LogAssert.Expect(LogType.Error, new Regex(".*"));
            TestSingletonMono duplicate = CreateSingletonGameObject();
            InvokeAwake(duplicate);

            Assert.AreSame(first, TestSingletonMono.Instance, "非替换模式下重复实例不应顶替原实例");
            Assert.AreEqual(1, TestSingletonMono.InitCount, "重复实例不应触发 OnInit");
        }

        [Test]
        public void Awake_Replaceable_Instance_SwitchesToLatest()
        {
            TestSingletonMono first = CreateSingletonGameObject();
            InvokeAwake(first);
            // 仲裁依赖时间戳：固定为 MaxValue 确保新实例必然胜出（与 Time.time 的编辑模式取值无关）
            typeof(SingletonMono<TestSingletonMono>)
                .GetField("_initializationTime", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(first, float.MaxValue);

            TestSingletonMono second = CreateSingletonGameObject();
            SetReplaceable(second, true);

            // 编辑模式下 Destroy 被拒绝并记录错误，此处为预期行为（旧实例的销毁尝试）
            LogAssert.Expect(LogType.Error, new Regex(".*"));
            InvokeAwake(second);

            Assert.AreSame(second, TestSingletonMono.Instance, "替换模式下最新实例应胜出");
            Assert.AreEqual(2, TestSingletonMono.InitCount, "新实例应回调 OnInit");
        }

        [Test]
        public void OnDestroy_ClearsInstance_CallsOnShutdown_KeepsShutdownFlagInEditMode()
        {
            TestSingletonMono instance = CreateSingletonGameObject();
            InvokeAwake(instance);

            InvokeOnDestroy(instance);

            Assert.IsFalse(TestSingletonMono.IsValid, "销毁后实例应被清空");
            Assert.AreEqual(1, TestSingletonMono.ShutdownCount);
            Assert.IsTrue((bool)ShuttingDownField.GetValue(null),
                "编辑模式销毁后退出标记应保持 true（无播放会话复位它）");
            Assert.IsNull(TestSingletonMono.Instance, "退出窗口应阻止重新物化");
        }

        [Test]
        public void Awake_DuringShutdownWindow_IsIgnored()
        {
            ShuttingDownField.SetValue(null, true);
            TestSingletonMono instance = CreateSingletonGameObject();

            InvokeAwake(instance);

            Assert.IsFalse(TestSingletonMono.IsValid, "退出窗口期的 Awake 应被忽略");
            Assert.AreEqual(0, TestSingletonMono.InitCount);
        }

        [Test]
        public void Awake_AfterShutdownFlagReset_RevivesInstance()
        {
            TestSingletonMono instance = CreateSingletonGameObject();
            InvokeAwake(instance);
            InvokeOnDestroy(instance);
            Assert.IsTrue((bool)ShuttingDownField.GetValue(null));

            // 复活前提是退出标记先被复位（域重载自动清空静态 / 参照 MainThreadDispatcher.ResetStatics 钩子）
            ShuttingDownField.SetValue(null, false);
            TestSingletonMono revived = CreateSingletonGameObject();
            InvokeAwake(revived);

            Assert.AreSame(revived, TestSingletonMono.Instance);
            Assert.AreEqual(2, TestSingletonMono.InitCount, "标记复位后新实例应正常初始化");
        }

        #endregion

        #region 辅助方法 [Helpers]

        /// <summary>反射调用受保护的 Awake（编辑模式下不会自动触发）。</summary>
        private static void InvokeAwake(TestSingletonMono instance) => AwakeMethod.Invoke(instance, null);

        /// <summary>反射调用受保护的 OnDestroy。</summary>
        private static void InvokeOnDestroy(TestSingletonMono instance) => OnDestroyMethod.Invoke(instance, null);

        /// <summary>反射设置 m_Replaceable 序列化字段。</summary>
        private static void SetReplaceable(TestSingletonMono instance, bool value)
        {
            typeof(SingletonMono<TestSingletonMono>)
                .GetField("m_Replaceable", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(instance, value);
        }

        #endregion
    }
}
