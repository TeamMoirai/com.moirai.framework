using System;
using System.Threading;
using Moirai.Atropos;
using NUnit.Framework;

namespace Core.FrameworkSettings
{
    /// <summary>
    /// 探针契约：仅继承 <see cref="FrameworkHandler"/>，不关联任何生产处理器契约。
    /// </summary>
    /// <remarks>不标 <c>[Serializable]</c> 且无生产 <c>[ProviderDropdown]</c> 字段引用本类型，不会进入资产下拉扫描。</remarks>
    internal abstract class ProbeHandler : FrameworkHandler
    {
        internal Action InitAction { get; set; }
        internal Action ShutdownAction { get; set; }
        internal int InitCount { get; private set; }
        internal int ShutdownCount { get; private set; }

        /// <inheritdoc />
        protected override void OnInit()
        {
            InitCount++;
            InitAction?.Invoke();
        }

        /// <inheritdoc />
        protected override void OnShutdown()
        {
            ShutdownCount++;
            ShutdownAction?.Invoke();
        }
    }

    /// <summary>
    /// 声明默认的探针实现。
    /// </summary>
    internal sealed class ProbeDeclaredDefaultHandler : ProbeHandler
    {
    }

    /// <summary>
    /// 探针宿主：声明默认 + settings 来源回空，复刻「settings 优先 / 声明默认兜底」生产形状。
    /// </summary>
    [ProviderHandlerHost(typeof(ProbeHandler), typeof(ProbeDeclaredDefaultHandler))]
    internal static partial class ProbeHost
    {
        internal static Func<ProbeHandler> SettingsFactory { get; set; }

        private static ProbeHandler GetHandlerFromSettings() => SettingsFactory?.Invoke();

        internal static ProbeHandler RequireReadyHandler() => RequireHandler();
    }

    /// <summary>
    /// 仅 settings 来源的探针宿主，用于验证不带默认工厂的初始化路径。
    /// </summary>
    [ProviderHandlerHost(typeof(ProbeHandler))]
    internal static partial class ProbeSettingsOnlyHost
    {
        internal static Func<ProbeHandler> SettingsFactory { get; set; }

        private static ProbeHandler GetHandlerFromSettings() => SettingsFactory?.Invoke();
    }

    /// <summary>
    /// ProviderHandlerHost 声明默认处理器的行为契约：生成器生成的 <c>CreateDefaultHandler</c> 实例化声明类型，且懒加载在 settings 空档时回退到它。
    /// </summary>
    /// <remarks>探针宿主住测试程序集，生成的工厂与接缝同程序集可见，无需 InternalsVisibleTo。</remarks>
    [TestFixture]
    public sealed class ProviderHandlerHostDefaultFactoryTests
    {
        private const int WAIT_TIMEOUT_MS = 5000;

        /// <summary>
        /// 断言探针槽与工厂没有跨用例残留。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            Assert.IsNull(ProbeHost.Internal_PeekHandler(), "上个用例未还原探针槽");
            Assert.IsNull(ProbeHost.SettingsFactory, "上个用例未还原工厂");
            Assert.IsNull(ProbeSettingsOnlyHost.Internal_PeekHandler());
            Assert.IsNull(ProbeSettingsOnlyHost.SettingsFactory);
        }

        /// <summary>
        /// 关闭探针并无条件还原静态槽与工厂。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            try
            {
                try
                {
                    ProbeHost.Internal_PeekHandler()?.Internal_Shutdown();
                }
                finally
                {
                    ProbeSettingsOnlyHost.Internal_PeekHandler()?.Internal_Shutdown();
                }
            }
            finally
            {
                ProbeHost.Internal_UseHandler(null);
                ProbeHost.SettingsFactory = null;
                ProbeSettingsOnlyHost.Internal_UseHandler(null);
                ProbeSettingsOnlyHost.SettingsFactory = null;
            }
        }

        /// <summary>
        /// 生成的兜底工厂必须实例化声明默认类型。
        /// </summary>
        [Test]
        public void CreateDefaultHandler_DeclaredDefault_InstantiatesDeclaredType()
        {
            ProbeHandler created = ProbeHost.CreateDefaultHandler();

            Assert.IsInstanceOf<ProbeDeclaredDefaultHandler>(created, "兜底工厂应实例化 [ProviderHandlerHost] 声明的默认处理器");
            Assert.IsFalse(created.IsInitialized, "工厂只创建不初始化，初始化由懒加载链路负责");
        }

        /// <summary>
        /// settings 来源返回 null 时懒加载回退到声明默认，初始化成功后就位。
        /// </summary>
        [Test]
        public void Handler_SettingsSourceNull_FallsBackToDeclaredDefault()
        {
            ProbeHandler resolved = ProbeHost.Handler;

            Assert.IsInstanceOf<ProbeDeclaredDefaultHandler>(resolved, "settings 空档应回退到声明默认");
            Assert.IsTrue(ProbeHost.IsValid);
            Assert.IsTrue(resolved.IsInitialized, "懒加载返回时应完成 Internal_Init");
            Assert.AreSame(resolved, ProbeHost.Handler, "已就位处理器应稳定复用，不重复实例化");
        }

        /// <summary>
        /// 初始化失败不发布实例，下一次访问可重试同一对象。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Handler_InitThrows_LeavesEmptySlotAndRetries(bool settingsOnly)
        {
            var handler = new ProbeDeclaredDefaultHandler();
            handler.InitAction = () => throw new InvalidOperationException("probe init failure");
            ProbeHost.SettingsFactory = () => handler;
            ProbeSettingsOnlyHost.SettingsFactory = () => handler;
            Func<ProbeHandler> get = settingsOnly
                ? () => ProbeSettingsOnlyHost.Handler
                : () => ProbeHost.Handler;

            AssertFailure<InvalidOperationException>(() => { _ = get(); }, "probe init failure");
            Assert.IsNull(ProbeHost.Internal_PeekHandler());
            Assert.IsNull(ProbeSettingsOnlyHost.Internal_PeekHandler());
            Assert.IsFalse(handler.IsInitialized);

            handler.InitAction = null;
            Assert.AreSame(handler, get());
            Assert.IsTrue(handler.IsInitialized);
            Assert.AreEqual(2, handler.InitCount);
        }

        /// <summary>
        /// 初始化回调内只读就绪入口不可见半初始化对象。
        /// </summary>
        [Test]
        public void Handler_DuringOnInit_ReadinessRemainsEmpty()
        {
            var handler = new ProbeDeclaredDefaultHandler();
            handler.InitAction = () =>
            {
                Assert.IsFalse(ProbeHost.IsValid);
                Assert.IsNull(ProbeHost.Internal_PeekHandler());
                AssertFailure<GameException>(() => { _ = ProbeHost.RequireReadyHandler(); }, "not ready");
            };
            ProbeHost.SettingsFactory = () => handler;

            Assert.AreSame(handler, ProbeHost.Handler);
            Assert.AreSame(handler, ProbeHost.RequireReadyHandler());
        }

        /// <summary>
        /// 同宿主初始化回调递归读取或赋值应明确失败，且不阻断后续重试。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Handler_OnInitReenters_RejectsRecursionAndRetries(bool useSetter)
        {
            var handler = new ProbeDeclaredDefaultHandler();
            handler.InitAction = () =>
            {
                // 限深让缺少递归保护的实现表现为可诊断失败，而不是压垮测试进程。
                if (handler.InitCount > 1)
                    throw new ApplicationException("probe recursion depth exceeded");
                if (useSetter)
                    ProbeHost.Handler = handler;
                else
                    _ = ProbeHost.Handler;
            };
            ProbeHost.SettingsFactory = () => handler;

            AssertFailure<InvalidOperationException>(() => { _ = ProbeHost.Handler; }, "ProbeHost");
            Assert.IsNull(ProbeHost.Internal_PeekHandler());
            Assert.AreEqual(1, handler.InitCount);
            handler.InitAction = null;
            Assert.AreSame(handler, ProbeHost.Handler);
            Assert.IsTrue(handler.IsInitialized);
        }

        /// <summary>
        /// 服务注册工厂允许先登记再重入，并保留内层成功装配的对象。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Handler_ServiceFactoryReenters_ReusesInnerPublication(bool settingsOnly)
        {
            var inner = new ProbeDeclaredDefaultHandler();
            var outer = new ProbeDeclaredDefaultHandler();
            var registered = false;
            var calls = 0;
            Func<ProbeHandler> get = settingsOnly
                ? () => ProbeSettingsOnlyHost.Handler
                : () => ProbeHost.Handler;
            Func<ProbeHandler> factory = () =>
            {
                calls++;
                if (registered) return inner;
                registered = true;
                Assert.AreSame(inner, get()); // EnsureRegistered → service.OnInit → Handler
                return outer;
            };
            ProbeHost.SettingsFactory = factory;
            ProbeSettingsOnlyHost.SettingsFactory = factory;

            Assert.AreSame(inner, get());
            Assert.AreEqual(2, calls);
            Assert.AreEqual(1, inner.InitCount);
            Assert.AreEqual(0, outer.InitCount, "外层不能初始化第二个对象");
        }

        /// <summary>
        /// 工厂重入已装配后即使外层返回 null，也不应触发空来源异常。
        /// </summary>
        [Test]
        public void Handler_ReentrantFactoryReturnsNull_UsesPublishedHandler()
        {
            var inner = new ProbeDeclaredDefaultHandler();
            var registered = false;
            ProbeSettingsOnlyHost.SettingsFactory = () =>
            {
                if (registered) return inner;
                registered = true;
                _ = ProbeSettingsOnlyHost.Handler;
                return null;
            };

            Assert.AreSame(inner, ProbeSettingsOnlyHost.Handler);
            Assert.AreEqual(1, inner.InitCount);
        }

        /// <summary>
        /// 初始化可同步解析其他宿主；跨宿主依赖环在回到未就绪宿主时失败。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Handler_CrossHostDependency_CompletesOrRejectsCycle(bool cyclic)
        {
            var first = new ProbeDeclaredDefaultHandler();
            var second = new ProbeDeclaredDefaultHandler();
            ProbeHost.SettingsFactory = () => first;
            ProbeSettingsOnlyHost.SettingsFactory = () => second;
            first.InitAction = () => { _ = ProbeSettingsOnlyHost.Handler; };
            if (cyclic)
                second.InitAction = () => { _ = ProbeHost.Handler; };

            if (cyclic)
            {
                AssertFailure<InvalidOperationException>(() => { _ = ProbeHost.Handler; }, "ProbeHost");
                Assert.IsNull(ProbeHost.Internal_PeekHandler());
                Assert.IsNull(ProbeSettingsOnlyHost.Internal_PeekHandler());
                second.InitAction = null;
            }
            Assert.AreSame(first, ProbeHost.Handler);
            Assert.IsTrue(first.IsInitialized);
            Assert.IsTrue(second.IsInitialized);
        }

        /// <summary>
        /// 并发冷读串行经过工厂和初始化，第二个读者不能取得未就绪对象。
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void Handler_ConcurrentColdReaders_ReturnOnlyInitializedInstance(bool blockFactory)
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var readerStarted = new ManualResetEventSlim();
            using var readerReturned = new ManualResetEventSlim();
            var handler = new ProbeDeclaredDefaultHandler();
            var calls = 0;
            Action block = () =>
            {
                entered.Set();
                if (!release.Wait(WAIT_TIMEOUT_MS)) throw new TimeoutException("probe release timeout");
            };
            if (!blockFactory) handler.InitAction = block;
            ProbeHost.SettingsFactory = () =>
            {
                Interlocked.Increment(ref calls);
                if (blockFactory) block();
                return handler;
            };
            Exception writerError = null;
            Exception readerError = null;
            ProbeHandler first = null;
            ProbeHandler second = null;
            var readerSawReady = false;
            var writer = new Thread(() =>
            {
                try { first = ProbeHost.Handler; }
                catch (Exception exception) { writerError = exception; }
            }) { IsBackground = true };
            var reader = new Thread(() =>
            {
                try
                {
                    readerStarted.Set();
                    second = ProbeHost.Handler;
                    readerSawReady = second.IsInitialized;
                }
                catch (Exception exception) { readerError = exception; }
                finally { readerReturned.Set(); }
            }) { IsBackground = true };
            var readerRunning = false;
            writer.Start();
            try
            {
                Assert.IsTrue(entered.Wait(WAIT_TIMEOUT_MS), "写线程未到达初始化屏障");
                reader.Start();
                readerRunning = true;
                Assert.IsTrue(readerStarted.Wait(WAIT_TIMEOUT_MS));
                // 等待确定的线程状态/完成信号，而不是依赖固定延时推测它是否进入 getter。
                Assert.IsTrue(SpinWait.SpinUntil(() => readerReturned.IsSet ||
                    (reader.ThreadState & ThreadState.WaitSleepJoin) != 0, WAIT_TIMEOUT_MS));
                Assert.IsFalse(readerReturned.IsSet, "初始化未完成，读者却已返回");
                Assert.IsFalse(ProbeHost.IsValid);
                Assert.IsNull(ProbeHost.Internal_PeekHandler());
                Assert.AreEqual(1, Volatile.Read(ref calls), "冷工厂调用必须串行");
            }
            finally
            {
                release.Set();
                bool writerJoined = writer.Join(WAIT_TIMEOUT_MS);
                bool readerJoined = !readerRunning || reader.Join(WAIT_TIMEOUT_MS);
                Assert.IsTrue(writerJoined && readerJoined, "探针线程未在截止时间内退出");
            }
            Assert.IsNull(writerError);
            Assert.IsNull(readerError);
            Assert.AreSame(handler, first);
            Assert.AreSame(first, second);
            Assert.IsTrue(readerSawReady);
            Assert.AreEqual(1, handler.InitCount);
            Assert.AreEqual(1, calls);
        }

        /// <summary>
        /// setter 初始化失败保留旧对象，成功替换只关闭旧对象一次，同实例赋值幂等。
        /// </summary>
        [Test]
        public void Handler_SetterFailsThenReplaces_PreservesOldAndShutsDownOnce()
        {
            var previous = new ProbeDeclaredDefaultHandler();
            ProbeHost.Handler = previous;
            var next = new ProbeDeclaredDefaultHandler();
            next.InitAction = () =>
            {
                Assert.AreSame(previous, ProbeHost.Handler);
                Assert.AreSame(previous, ProbeHost.Internal_PeekHandler());
                throw new InvalidOperationException("replacement failure");
            };

            AssertFailure<InvalidOperationException>(() => ProbeHost.Handler = next, "replacement failure");
            Assert.AreSame(previous, ProbeHost.Handler);
            Assert.IsTrue(previous.IsInitialized);
            Assert.AreEqual(0, previous.ShutdownCount);
            Assert.IsFalse(next.IsInitialized);
            next.InitAction = () => Assert.AreSame(previous, ProbeHost.Internal_PeekHandler());
            previous.ShutdownAction = () => Assert.AreSame(next, ProbeHost.Handler);

            ProbeHost.Handler = next;
            ProbeHost.Handler = next;
            Assert.AreSame(next, ProbeHost.Handler);
            Assert.IsTrue(next.IsInitialized);
            Assert.AreEqual(2, next.InitCount);
            Assert.AreEqual(1, previous.ShutdownCount);
            Assert.IsFalse(previous.IsInitialized);
        }

        /// <summary>
        /// 替换的 OnInit 不能再装配第三个实例，否则失败会改变旧槽。
        /// </summary>
        [Test]
        public void Handler_SetterReenters_RejectsNestedReplacement()
        {
            var previous = new ProbeDeclaredDefaultHandler();
            var next = new ProbeDeclaredDefaultHandler();
            var nested = new ProbeDeclaredDefaultHandler();
            ProbeHost.Handler = previous;
            next.InitAction = () => ProbeHost.Handler = nested;

            AssertFailure<InvalidOperationException>(() => ProbeHost.Handler = next, "ProbeHost");
            Assert.AreSame(previous, ProbeHost.Handler);
            Assert.AreEqual(0, previous.ShutdownCount);
            Assert.AreEqual(0, nested.InitCount);
        }

        private static void AssertFailure<T>(TestDelegate action, string message) where T : Exception
        {
            Exception caught = Assert.Catch(action);
            while (caught != null)
            {
                if (caught is T && caught.Message.Contains(message)) return;
                caught = caught.InnerException;
            }
            Assert.Fail($"应抛出包含 '{message}' 的 {typeof(T).Name}");
        }
    }
}
