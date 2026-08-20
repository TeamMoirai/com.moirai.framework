using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameTool
{
    [TestFixture]
    public class GameServicesTest
    {
        // --- 测试用接口 ---

        private interface IAlphaService { }
        private interface IBetaService { }
        private interface IGammaService { }

        // --- 测试用服务基类 ---

        private abstract class TestServiceBase : ServiceBase, IServiceTickable
        {
            public int InitCount;
            public int ShutdownCount;
            public int TickCount;

            public override void OnInit() => InitCount++;
            public override void Shutdown() => ShutdownCount++;
            public virtual void Tick(float elapseSeconds, float realElapseSeconds) => TickCount++;
        }

        private sealed class AppService : TestServiceBase, IAlphaService { }

        private sealed class SceneService : TestServiceBase, IAlphaService
        {
            public override EServiceScopeKind Scope => EServiceScopeKind.Scene;
        }

        private sealed class GameplayService : TestServiceBase, IAlphaService
        {
            public override EServiceScopeKind Scope => EServiceScopeKind.Gameplay;
        }

        private sealed class BetaService : TestServiceBase, IBetaService { }

        private sealed class SceneBetaService : TestServiceBase, IBetaService
        {
            public override EServiceScopeKind Scope => EServiceScopeKind.Scene;
        }

        [SetUp]
        public void SetUp()
        {
            GameServices.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.Shutdown();
        }

        // --- 注册与获取 ---

        [Test]
        public void RegisterService_ThenGetService_ReturnsSameInstance()
        {
            var service = new AppService();
            var registered = GameServices.RegisterService<IAlphaService>(service);
            var fetched = GameServices.GetService<IAlphaService>();

            Assert.AreSame(service, registered);
            Assert.AreSame(service, fetched);
            Assert.AreEqual(1, service.InitCount, "OnInit 应在注册时调用一次");
        }

        [Test]
        public void RegisterService_DuplicateSameScope_ReturnsExistingInstance()
        {
            var first = new AppService();
            var second = new AppService();

            var result = GameServices.RegisterService<IAlphaService>(first);
            var duplicate = GameServices.RegisterService<IAlphaService>(second);

            Assert.AreSame(first, result);
            Assert.AreSame(first, duplicate, "同作用域重复注册应返回已有实例");
            Assert.AreEqual(0, second.InitCount, "被拒绝的实例不应被初始化");
        }

        // --- 跨作用域遮蔽 ---

        [Test]
        public void GetService_CrossScopeShadowing_GameplayBeatsSceneBeatsApp()
        {
            var app = new AppService();
            var scene = new SceneService();
            var gameplay = new GameplayService();
            GameServices.RegisterService<IAlphaService>(app);
            GameServices.RegisterService<IAlphaService>(scene);
            GameServices.RegisterService<IAlphaService>(gameplay);

            Assert.AreSame(gameplay, GameServices.GetService<IAlphaService>());

            GameServices.ShutdownScope(EServiceScopeKind.Gameplay);
            Assert.AreSame(scene, GameServices.GetService<IAlphaService>(), "Gameplay 注销后应回退到 Scene");

            GameServices.ShutdownScope(EServiceScopeKind.Scene);
            Assert.AreSame(app, GameServices.GetService<IAlphaService>(), "Scene 注销后应回退到 App");
        }

        [Test]
        public void RegisterService_SameInterfaceDifferentScopes_Allowed()
        {
            var app = new AppService();
            var scene = new SceneService();
            var gameplay = new GameplayService();

            GameServices.RegisterService<IAlphaService>(app);
            GameServices.RegisterService<IAlphaService>(scene);
            GameServices.RegisterService<IAlphaService>(gameplay);

            Assert.AreEqual(1, app.InitCount);
            Assert.AreEqual(1, scene.InitCount);
            Assert.AreEqual(1, gameplay.InitCount, "不同作用域注册同一接口不应被拒绝");
        }

        // --- ShutdownScope 正确性（P0 回归：脏索引会错删其他服务的 tick 槽位） ---

        [Test]
        public void ShutdownScope_MixedRegistrations_RemainingServicesStillTick()
        {
            // 多个优先级交错的 App/Scene 服务，最大化触发 InsertSorted 移动已有元素
            var appA = new AppService();
            var betaApp = new BetaService();
            var sceneA = new SceneService();
            var betaScene = new SceneBetaService();

            GameServices.RegisterService<IAlphaService>(appA);
            GameServices.RegisterService<IBetaService>(betaApp);
            GameServices.RegisterService<IAlphaService>(sceneA);
            GameServices.RegisterService<IBetaService>(betaScene);

            GameServices.ShutdownScope(EServiceScopeKind.Scene);

            Assert.AreEqual(1, sceneA.ShutdownCount, "Scene 服务应被关闭");
            Assert.AreEqual(1, betaScene.ShutdownCount, "Scene 服务应被关闭");
            Assert.AreEqual(0, appA.ShutdownCount, "App 服务不应被关闭");
            Assert.AreEqual(0, betaApp.ShutdownCount, "App 服务不应被关闭");

            GameServices.Tick(0f, 0f);
            Assert.AreEqual(1, appA.TickCount, "App 服务关闭后仍应正常轮询");
            Assert.AreEqual(1, betaApp.TickCount, "App 服务关闭后仍应正常轮询");
            Assert.AreEqual(0, sceneA.TickCount, "已注销的 Scene 服务不应被轮询");
        }

        [Test]
        public void ShutdownScope_RemovedService_NoLongerTicks()
        {
            var scene = new SceneService();
            GameServices.RegisterService<IAlphaService>(scene);

            GameServices.Tick(0f, 0f);
            Assert.AreEqual(1, scene.TickCount);

            GameServices.ShutdownScope(EServiceScopeKind.Scene);
            GameServices.Tick(0f, 0f);

            Assert.AreEqual(1, scene.TickCount, "注销后的服务不应再被轮询");
        }

        // --- 迭代安全：注册 ---

        [Test]
        public void Update_RegisterDuringIteration_AppliedAfterFlush()
        {
            var registrar = new DeferredRegistrar();
            GameServices.RegisterService<IBetaService>(registrar);

            GameServices.Tick(0f, 0f);

            Assert.AreEqual(1, registrar.TickCount);
            Assert.IsNotNull(GameServices.GetService<IAlphaService>(), "迭代中注册的服务应在迭代结束后生效");
            Assert.AreEqual(1, registrar.Spawned.InitCount, "延迟注册生效时应调用 OnInit");
            // 迭代内 count 已捕获，新服务本轮不 tick；下一轮开始 tick
            Assert.AreEqual(0, registrar.Spawned.TickCount);

            GameServices.Tick(0f, 0f);
            Assert.AreEqual(1, registrar.Spawned.TickCount, "下一轮应开始轮询新服务");
        }

        private sealed class DeferredRegistrar : TestServiceBase, IBetaService
        {
            public AppService Spawned;
            private bool _spawned;

            public override void OnInit()
            {
                Spawned = new AppService();
            }

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                // 首次 tick 时在迭代内注册新服务（未注册的 GetService 会抛 GameException，不能在此探测）
                if (!_spawned)
                {
                    _spawned = true;
                    GameServices.RegisterService<IAlphaService>(Spawned);
                }
            }
        }

        // --- 迭代安全：注销 ---

        [Test]
        public void Update_UnregisterDuringIteration_AppliedAfterFlush()
        {
            var victim = new AppService();
            var killer = new UnregisterOnTick(victim);
            GameServices.RegisterService<IAlphaService>(victim);
            GameServices.RegisterService<IBetaService>(killer);

            GameServices.Tick(0f, 0f);

            Assert.AreEqual(1, victim.ShutdownCount, "迭代中注销的服务应在迭代结束后关闭");

            GameServices.Tick(0f, 0f);
            Assert.AreEqual(1, victim.TickCount, "被注销的服务不应再被轮询");
            Assert.AreEqual(2, killer.TickCount, "其余服务应继续正常轮询");
        }

        private sealed class UnregisterOnTick : TestServiceBase, IBetaService
        {
            private readonly ServiceBase _victim;

            public UnregisterOnTick(ServiceBase victim) => _victim = victim;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.UnregisterService(_victim);
            }
        }

        // --- 迭代安全：跨作用域 ShutdownScope ---

        [Test]
        public void ShutdownScope_DuringIteration_CrossScopeImmediateShutdown()
        {
            var scene = new SceneService();
            var trigger = new ShutdownScopeOnTick();
            GameServices.RegisterService<IAlphaService>(scene);
            GameServices.RegisterService<IBetaService>(trigger);

            GameServices.Tick(0f, 0f);

            // App scope 先于 Scene scope 被遍历：trigger 在 App scope Tick 中调用 ShutdownScope(Scene)
            // Scene scope 未在迭代中，Dispose 立即执行，scene 被关闭——本轮不会被 tick
            Assert.AreEqual(1, scene.ShutdownCount, "ShutdownScope 立即关闭作用域中的服务");
            Assert.AreEqual(0, scene.TickCount, "Scene scope 尚未 Tick 即被注销，本轮不 tick");

            GameServices.Tick(0f, 0f);
            Assert.AreEqual(0, scene.TickCount, "被注销的服务不应再被轮询");
        }

        private sealed class ShutdownScopeOnTick : TestServiceBase, IBetaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.ShutdownScope(EServiceScopeKind.Scene);
            }
        }

        // --- 迭代安全：迭代中关闭自身作用域（P0 回归：立即 Dispose 会缩短遍历列表导致越界） ---

        [Test]
        public void ShutdownScope_DuringOwnScopeIteration_DefersDisposeAndDoesNotThrow()
        {
            var trigger = new ShutdownOwnScopeOnTick();
            var other = new SceneBetaService();
            GameServices.RegisterService<IAlphaService>(trigger);
            GameServices.RegisterService<IBetaService>(other);

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            Assert.AreEqual(1, other.TickCount, "同作用域后续服务在本轮迭代中仍应被轮询（销毁延迟到迭代结束）");
            Assert.AreEqual(1, trigger.ShutdownCount, "迭代中请求销毁的作用域应在迭代结束后关闭全部服务");
            Assert.AreEqual(1, other.ShutdownCount, "作用域销毁应关闭其中全部服务");
        }

        private sealed class ShutdownOwnScopeOnTick : TestServiceBase, IAlphaService
        {
            public override EServiceScopeKind Scope => EServiceScopeKind.Scene;
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.ShutdownScope(EServiceScopeKind.Scene);
            }
        }

        // --- 注销 API ---

        [Test]
        public void UnregisterService_ByInterface_RemovesAndShutsDown()
        {
            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);

            bool result = GameServices.UnregisterService<IAlphaService>();

            Assert.IsTrue(result);
            Assert.AreEqual(1, service.ShutdownCount);
        }

        [Test]
        public void UnregisterService_ByInstance_RemovesAndShutsDown()
        {
            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);

            bool result = GameServices.UnregisterService(service);

            Assert.IsTrue(result);
            Assert.AreEqual(1, service.ShutdownCount);
        }

        [Test]
        public void UnregisterService_NotRegistered_ReturnsFalse()
        {
            Assert.IsFalse(GameServices.UnregisterService(new AppService()));
        }

        // --- Shutdown 健壮性 ---

        [Test]
        public void Shutdown_ServiceThrows_DoesNotAbortOtherServices()
        {
            var thrower = new ThrowingService();
            var normal = new BetaService();
            GameServices.RegisterService<IAlphaService>(thrower);
            GameServices.RegisterService<IBetaService>(normal);

            // 框架会记录服务关闭异常（Error 日志），声明预期
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*InvalidOperationException: test.*"));

            Assert.DoesNotThrow(() => GameServices.Shutdown());
            Assert.AreEqual(1, normal.ShutdownCount, "异常服务之后的服务仍应被关闭");
        }

        private sealed class ThrowingService : TestServiceBase, IAlphaService
        {
            public override void Shutdown() => throw new System.InvalidOperationException("test");
        }

        // --- Tick 异常隔离（P0 回归：单服务异常不应中断同轮其他服务） ---

        [Test]
        public void Update_ServiceThrowsInTick_OtherServicesStillTick()
        {
            var thrower = new ThrowingTickService();
            var normal = new BetaService();
            GameServices.RegisterService<IAlphaService>(thrower);
            GameServices.RegisterService<IBetaService>(normal);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*InvalidOperationException: tick.*"));

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));
            Assert.AreEqual(1, normal.TickCount, "抛异常服务之后的同轮服务仍应被轮询");
        }

        private sealed class ThrowingTickService : TestServiceBase, IAlphaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds)
                => throw new System.InvalidOperationException("tick");
        }

        // --- ServiceMono 合约注册（P0 回归：RegisterAs 接口应生效且 OnInit 被调用） ---

        private interface IMonoContractService { }

        private sealed class TestMonoService : ServiceMono<SceneScope>, IMonoContractService
        {
            public int InitCount;

            public override void OnInit() => InitCount++;
            public override void Shutdown() { }

            protected override System.Type RegisterAs => typeof(IMonoContractService);

            /// <summary>
            /// EditMode 下 AddComponent 不触发 Awake，暴露此方法供测试显式调用注册流程。
            /// </summary>
            public void TriggerRegistration() => Awake();
        }

        [Test]
        public void ServiceMono_WithRegisterAsInterface_RegistersUnderContractAndInitializes()
        {
            var go = new GameObject();
            var mono = go.AddComponent<TestMonoService>();

            try
            {
                // EditMode 下 Awake 不会自动触发，显式调用注册流程
                mono.TriggerRegistration();

                Assert.AreEqual(1, mono.InitCount, "注册时应调用 OnInit");
                Assert.AreSame(mono, GameServices.GetService<IMonoContractService>(), "应注册到 RegisterAs 指定的合约接口");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Shutdown_ClearsAllScopes_InReverseOrder()
        {
            var app = new AppService();
            var scene = new SceneService();
            var gameplay = new GameplayService();
            GameServices.RegisterService<IAlphaService>(app);
            GameServices.RegisterService<IAlphaService>(scene);
            GameServices.RegisterService<IAlphaService>(gameplay);

            var expected = new List<ServiceBase> { gameplay, scene, app };
            GameServices.Shutdown();

            Assert.AreEqual(1, app.ShutdownCount);
            Assert.AreEqual(1, scene.ShutdownCount);
            Assert.AreEqual(1, gameplay.ShutdownCount);
            CollectionAssert.AreEquivalent(expected, new ServiceBase[] { gameplay, scene, app });
        }

        // --- 优先级排序 ---

        [Test]
        public void Update_HigherPriorityTicksFirst()
        {
            var order = new List<string>();
            var low = new OrderedService("low", 0, order);
            var high = new OrderedService("high", 10, order);
            var mid = new OrderedService("mid", 5, order);

            // 注意：同一接口同一作用域只能注册一个实例，各服务使用不同接口
            GameServices.RegisterService<IAlphaService>(low);
            GameServices.RegisterService<IBetaService>(high);
            GameServices.RegisterService<IGammaService>(mid);

            GameServices.Tick(0f, 0f);

            CollectionAssert.AreEqual(new[] { "high", "mid", "low" }, order);
        }

        private sealed class OrderedService : ServiceBase, IServiceTickable, IAlphaService, IBetaService, IGammaService
        {
            private readonly string _name;
            private readonly List<string> _order;

            public OrderedService(string name, int priority, List<string> order)
            {
                _name = name;
                _order = order;
                Priority = priority;
            }

            public override int Priority { get; }
            public override void OnInit() { }
            public override void Shutdown() { }
            public void Tick(float elapseSeconds, float realElapseSeconds) => _order.Add(_name);
        }

        // --- 异步初始化 [ASYNC INIT] ---

        // --- 服务事件 [SERVICE EVENTS] ---

        [Test]
        public void ServiceRegistered_EventFires_AfterOnInit()
        {
            var registered = new List<(IService service, Type interfaceType, EServiceScopeKind scope)>();
            GameServices.ServiceRegistered += (s, t, sc) => registered.Add((s, t, sc));

            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);

            Assert.AreEqual(1, registered.Count, "事件应触发一次");
            Assert.AreSame(service, registered[0].service);
            Assert.AreEqual(typeof(IAlphaService), registered[0].interfaceType);
            Assert.AreEqual(EServiceScopeKind.App, registered[0].scope);
            Assert.AreEqual(1, service.InitCount, "OnInit 应在事件触发前完成");
        }

        [Test]
        public void ServiceUnregistered_EventFires_AfterShutdown()
        {
            var unregistered = new List<IService>();
            GameServices.ServiceUnregistered += s => unregistered.Add(s);

            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);
            GameServices.UnregisterService<IAlphaService>();

            Assert.AreEqual(1, unregistered.Count, "事件应触发一次");
            Assert.AreSame(service, unregistered[0]);
            Assert.AreEqual(1, service.ShutdownCount, "Shutdown 应在事件触发前完成");
        }

        [Test]
        public void Shutdown_ClearsEventSubscriptions()
        {
            int registeredCount = 0;
            GameServices.ServiceRegistered += (s, t, sc) => registeredCount++;

            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);
            Assert.AreEqual(1, registeredCount, "首次注册应触发事件");

            GameServices.Shutdown(); // Clears all including events

            // Shutdown 后事件订阅应被清除：重新注册不应再触发
            int countBefore = registeredCount;
            var service2 = new AppService();
            GameServices.RegisterService<IAlphaService>(service2);
            Assert.AreEqual(countBefore, registeredCount, "Shutdown 后事件订阅应被清除，不再触发");
        }

        // --- 依赖验证 [DEPENDENCY VALIDATION] ---

        private interface IDepTargetService { }

        private sealed class ServiceWithDependency : TestServiceBase, IAlphaService
        {
            public override Type[] Dependencies => new[] { typeof(IDepTargetService) };
        }

        [Test]
        public void RegisterService_UnmetDependency_Throws()
        {
            var service = new ServiceWithDependency();

            Assert.Throws<GameException>(() => GameServices.RegisterService<IAlphaService>(service));
        }

        [Test]
        public void RegisterService_MetDependency_Succeeds()
        {
            var target = new BetaService();
            // Register IDepTargetService using BetaService (it doesn't implement IDepTargetService,
            // so use a dedicated impl)
            var depTarget = new DepTargetServiceImpl();
            GameServices.RegisterService<IDepTargetService>(depTarget);

            var service = new ServiceWithDependency();
            GameServices.RegisterService<IAlphaService>(service);

            Assert.AreEqual(1, service.InitCount, "依赖满足时应正常初始化");
        }

        private sealed class DepTargetServiceImpl : TestServiceBase, IDepTargetService { }

        // --- 依赖拓扑序 [DEPENDENCY TOPOLOGY] ---

        private static readonly List<string> s_AsyncInitOrder = new();

        private sealed class LowPriorityAsyncService : TestServiceBase, IAlphaService, IAsyncInitService
        {
            public override int Priority => -10;

            public UniTask OnInitAsync()
            {
                s_AsyncInitOrder.Add("low");
                return UniTask.CompletedTask;
            }
        }

        private sealed class HighPriorityAsyncService : TestServiceBase, IBetaService, IAsyncInitService
        {
            public override int Priority => 10;

            public UniTask OnInitAsync()
            {
                s_AsyncInitOrder.Add("high");
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void InitializeAsync_FollowsRegistrationOrder_NotPriority()
        {
            s_AsyncInitOrder.Clear();
            var low = new LowPriorityAsyncService();
            var high = new HighPriorityAsyncService();

            // 低优先级先注册：初始化应按注册序执行，而非优先级序
            GameServices.RegisterService<IAlphaService>(low);
            GameServices.RegisterService<IBetaService>(high);

            // 全部 OnInitAsync 返回 CompletedTask，整链同步完成
            GameServices.InitializeAsync().Forget();

            CollectionAssert.AreEqual(new[] { "low", "high" }, s_AsyncInitOrder,
                "异步初始化应按注册顺序执行，而非优先级顺序");
        }

        private sealed class DependeeAsyncService : TestServiceBase, IDepTargetService, IAsyncInitService
        {
            public UniTask OnInitAsync()
            {
                s_AsyncInitOrder.Add("dependee");
                return UniTask.CompletedTask;
            }
        }

        private sealed class DependentAsyncService : TestServiceBase, IAlphaService, IAsyncInitService
        {
            // 依赖方优先级远高于被依赖方——按优先级序会先初始化依赖方，违反拓扑序
            public override int Priority => 100;

            public override Type[] Dependencies => new[] { typeof(IDepTargetService) };

            public UniTask OnInitAsync()
            {
                s_AsyncInitOrder.Add("dependent");
                return UniTask.CompletedTask;
            }
        }

        [Test]
        public void InitializeAsync_DependencyTopology_DependeeInitializesFirst()
        {
            s_AsyncInitOrder.Clear();
            var dependee = new DependeeAsyncService();
            var dependent = new DependentAsyncService();

            GameServices.RegisterService<IDepTargetService>(dependee);
            GameServices.RegisterService<IAlphaService>(dependent);

            GameServices.InitializeAsync().Forget();

            CollectionAssert.AreEqual(new[] { "dependee", "dependent" }, s_AsyncInitOrder,
                "异步初始化应按依赖拓扑序：被依赖方先初始化（即使依赖方优先级更高）");
        }

        private static readonly List<string> s_ShutdownOrder = new();

        private class ShutdownOrderService : TestServiceBase
        {
            private readonly string _name;

            public ShutdownOrderService(string name) => _name = name;

            public override void Shutdown()
            {
                s_ShutdownOrder.Add(_name);
                base.Shutdown();
            }
        }

        private sealed class ShutdownOrderDependee : ShutdownOrderService, IDepTargetService
        {
            public ShutdownOrderDependee() : base("dependee") { }
        }

        private sealed class DependentShutdownService : ShutdownOrderService, IAlphaService
        {
            public DependentShutdownService() : base("dependent") { }

            public override Type[] Dependencies => new[] { typeof(IDepTargetService) };
        }

        [Test]
        public void Shutdown_ClosesInReverseRegistrationOrder_DependentsFirst()
        {
            s_ShutdownOrder.Clear();
            var dependee = new ShutdownOrderDependee();
            var dependent = new DependentShutdownService();

            // 注册序（拓扑序）：被依赖方在前，依赖方在后
            GameServices.RegisterService<IDepTargetService>(dependee);
            GameServices.RegisterService<IAlphaService>(dependent);

            GameServices.Shutdown();

            CollectionAssert.AreEqual(new[] { "dependent", "dependee" }, s_ShutdownOrder,
                "关闭应按逆注册序（逆拓扑序）：依赖方先关闭，被依赖方后关闭");
        }

        // --- 生命周期状态 [LIFECYCLE STATE] ---

        [Test]
        public void RegisterService_SetsStateToInitialized()
        {
            var service = new AppService();
            Assert.AreEqual(EServiceState.Created, service.State, "注册前应为 Created");

            GameServices.RegisterService<IAlphaService>(service);

            Assert.AreEqual(EServiceState.Initialized, service.State, "注册后应为 Initialized");
        }

        [Test]
        public void ShutdownService_SetsStateToDisposed()
        {
            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);

            GameServices.UnregisterService<IAlphaService>();

            Assert.AreEqual(EServiceState.Disposed, service.State, "注销后应为 Disposed");
        }

        [Test]
        public void Shutdown_ServiceStateTransitions_GameplayToApp()
        {
            var app = new AppService();
            var scene = new SceneService();
            var gameplay = new GameplayService();
            GameServices.RegisterService<IAlphaService>(app);
            GameServices.RegisterService<IAlphaService>(scene);
            GameServices.RegisterService<IAlphaService>(gameplay);

            Assert.AreEqual(EServiceState.Initialized, app.State);
            Assert.AreEqual(EServiceState.Initialized, scene.State);
            Assert.AreEqual(EServiceState.Initialized, gameplay.State);

            GameServices.ShutdownScope(EServiceScopeKind.Gameplay);

            Assert.AreEqual(EServiceState.Disposed, gameplay.State, "Gameplay 应已销毁");
            Assert.AreEqual(EServiceState.Initialized, scene.State, "Scene 应仍运行");
            Assert.AreEqual(EServiceState.Initialized, app.State, "App 应仍运行");
        }

        // --- ServiceMono.OnDestroy 自动注销 [MONO ONDESTROY AUTO-UNREGISTER] ---

        [Test]
        public void ServiceMono_OnDestroy_UnregistersService()
        {
            var go = new GameObject();
            var mono = go.AddComponent<TestMonoService>();
            mono.TriggerRegistration();

            Assert.IsTrue(GameServices.UnregisterService<IMonoContractService>(), "注册后应能找到服务");

            // 模拟 OnDestroy：DestroyImmediate 会触发 OnDestroy → UnregisterService
            UnityEngine.Object.DestroyImmediate(go);

            Assert.AreEqual(1, mono.InitCount, "OnInit 应调用一次");
            // DestroyImmediate 后服务应已注销
            Assert.IsFalse(GameServices.UnregisterService<IMonoContractService>(), "OnDestroy 后服务应已注销");
        }

        // --- 双合约注册 [DUAL-CONTRACT REGISTER] ---

        private interface IDualPrimaryService { }
        private interface IDualSecondaryService { }

        private sealed class DualContractService : TestServiceBase, IDualPrimaryService, IDualSecondaryService { }

        [Test]
        public void RegisterService_DualContract_BothInterfacesResolve()
        {
            var service = new DualContractService();
            GameServices.RegisterService<IDualPrimaryService, IDualSecondaryService>(service);

            Assert.AreSame(service, GameServices.GetService<IDualPrimaryService>(), "主合约应可获取");
            Assert.AreSame(service, GameServices.GetService<IDualSecondaryService>(), "次合约应可获取");
            Assert.AreEqual(2, service.InitCount, "OnInit 应调用两次（同一实例分别注册到两个合约，各触发一次）");
        }

        // --- 偏好作用域查找 [PREFERRED SCOPE LOOKUP] ---

        [Test]
        public void GetService_PreferredScope_HitsScopeFirst()
        {
            var app = new AppService();
            var scene = new SceneService();
            GameServices.RegisterService<IAlphaService>(app);
            GameServices.RegisterService<IAlphaService>(scene);

            // 指定 App scope：应直接返回 App 实例（跳过 GetBest 的 Gameplay > Scene > App 回退）
            var result = GameServices.GetService<IAlphaService>(EServiceScopeKind.App);
            Assert.AreSame(app, result, "偏好 App scope 应直接返回 App 实例");

            // 指定 Scene scope
            result = GameServices.GetService<IAlphaService>(EServiceScopeKind.Scene);
            Assert.AreSame(scene, result, "偏好 Scene scope 应返回 Scene 实例");

            // 指定空 Gameplay scope：偏好未命中，回退到 GetBest（Scene > App）
            result = GameServices.GetService<IAlphaService>(EServiceScopeKind.Gameplay);
            Assert.AreSame(scene, result, "Gameplay scope 未命中应回退到 GetBest（Scene）");
        }

        [Test]
        public void GetService_PreferredScope_NotFound_Throws()
        {
            var app = new AppService();
            GameServices.RegisterService<IAlphaService>(app);

            // 只有 App scope 注册，查 Scene scope（未注册）应回退到 GetBest 返回 App
            var result = GameServices.GetService<IAlphaService>(EServiceScopeKind.Scene);
            Assert.AreSame(app, result, "Scene scope 未命中应回退到 GetBest 返回 App");

            // 完全未注册的接口
            Assert.Throws<GameException>(() => GameServices.GetService<IBetaService>(EServiceScopeKind.App),
                "完全未注册的接口应抛 GameException");
        }

        // --- TryResolve 不抛异常查找 [TRY RESOLVE] ---

        [Test]
        public void TryResolve_Unregistered_ReturnsFalseWithoutThrow()
        {
            bool found = GameServices.TryResolve<IBetaService>(null, out var service);

            Assert.IsFalse(found, "未注册的服务应返回 false");
            Assert.IsNull(service, "未注册的服务 out 参数应为 null");
        }

        [Test]
        public void TryResolve_Registered_ReturnsService()
        {
            var service = new AppService();
            GameServices.RegisterService<IAlphaService>(service);

            bool found = GameServices.TryResolve<IAlphaService>(null, out var resolved);

            Assert.IsTrue(found);
            Assert.AreSame(service, resolved);
        }
    }
}
