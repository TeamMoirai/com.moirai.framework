using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using IServiceProvider = Moirai.Atropos.IServiceProvider;

namespace GameTool
{
    [TestFixture]
    public class GameServicesTest
    {
        // --- 测试用契约（必须继承 IService 以满足注册约束） ---

        private interface IAlphaService : IService { }
        private interface IBetaService : IService { }
        private interface IDepTargetService : IService { }
        private interface IMonoContractService : IService { }

        // --- 顺序记录（静态，SetUp 清空） ---

        private static readonly List<string> s_OrderLog = new List<string>();

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

        // --- 简单实现 ---

        private sealed class AlphaService : TestServiceBase, IAlphaService { }
        private sealed class BetaService : TestServiceBase, IBetaService { }
        private sealed class DependeeService : TestServiceBase, IDepTargetService { }

        // --- 构造注入实现 ---

        private sealed class DependentService : TestServiceBase, IAlphaService
        {
            public IDepTargetService Dependency { get; }

            public DependentService(IDepTargetService dependency)
            {
                Dependency = dependency;
                s_OrderLog.Add("Dependent:ctor");
            }
        }

        private sealed class ProviderConsumerService : TestServiceBase, IAlphaService
        {
            public IServiceProvider Provider { get; }

            public ProviderConsumerService(IServiceProvider provider)
            {
                Provider = provider;
            }
        }

        // --- 循环依赖实现 ---

        private sealed class CycleServiceA : TestServiceBase, IAlphaService
        {
            public CycleServiceA(IBetaService beta) { }
        }

        private sealed class CycleServiceB : TestServiceBase, IBetaService
        {
            public CycleServiceB(IAlphaService alpha) { }
        }

        // --- 优先级实现 ---

        private sealed class HighPriorityService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;
            public override void Tick(float elapseSeconds, float realElapseSeconds) => s_OrderLog.Add("high");
        }

        private sealed class LowPriorityService : TestServiceBase, IBetaService
        {
            public override int Priority => -10;
            public override void Tick(float elapseSeconds, float realElapseSeconds) => s_OrderLog.Add("low");
        }

        // --- 异步初始化实现 ---

        private sealed class DependeeAsyncService : TestServiceBase, IDepTargetService, IAsyncInitService
        {
            public UniTask OnInitAsync()
            {
                s_OrderLog.Add("Dependee:async");
                return UniTask.CompletedTask;
            }
        }

        private sealed class DependentAsyncService : TestServiceBase, IAlphaService, IAsyncInitService
        {
            private readonly IDepTargetService _dependency;

            public DependentAsyncService(IDepTargetService dependency)
            {
                _dependency = dependency;
                Assert.IsNotNull(dependency, "构造注入的依赖在容器构建期必须已就位");
            }

            public UniTask OnInitAsync()
            {
                s_OrderLog.Add("Dependent:async");
                return UniTask.CompletedTask;
            }
        }

        // --- MonoBehaviour 服务实现 ---

        private sealed class TestMonoService : ServiceMono<SceneScope>, IMonoContractService
        {
            public int InitCount;
            public int ShutdownCount;
            public bool InjectCalled;
            public IServiceProvider InjectedProvider;

            protected internal override void Inject(IServiceProvider provider)
            {
                InjectCalled = true;
                InjectedProvider = provider;
            }

            public override void OnInit() => InitCount++;
            public override void Shutdown() => ShutdownCount++;
        }

        // --- 生命周期 ---

        [SetUp]
        public void SetUp()
        {
            s_OrderLog.Clear();
            GameServices.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            GameServices.Shutdown();
        }

        // --- 辅助 ---

        private static ServiceContainer BuildApp(Action<ServiceCollection> configure)
        {
            var collection = new ServiceCollection();
            configure?.Invoke(collection);
            var container = GameServices.BuildContainer(EServiceScopeKind.App, collection);
            container.BuildAsync().GetAwaiter().GetResult();
            return container;
        }

        private static ServiceContainer BuildScene(
            ServiceContainer parent, Action<ServiceCollection> configure)
        {
            var collection = new ServiceCollection();
            configure?.Invoke(collection);
            var container = GameServices.BuildContainer(EServiceScopeKind.Scene, collection, parent);
            container.BuildAsync().GetAwaiter().GetResult();
            return container;
        }

        // --- 构建与解析 ---

        [Test]
        public void BuildAsync_CreatesInitializesAndResolves()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            var resolved = GameServices.Provider.GetRequiredService<IAlphaService>();

            Assert.IsInstanceOf<AlphaService>(resolved);
            var alpha = (AlphaService)resolved;
            Assert.AreEqual(1, alpha.InitCount, "OnInit 应在构建时调用一次");
            Assert.AreEqual(EServiceState.Initialized, alpha.State);
            Assert.AreEqual(0, alpha.ShutdownCount);
            Assert.AreSame(resolved, GameServices.Provider.GetService<IAlphaService>(), "重复解析应返回同一单例");
        }

        [Test]
        public void GetRequiredService_Unregistered_Throws()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            Assert.Throws<GameException>(
                () => GameServices.Provider.GetRequiredService<IBetaService>());
        }

        [Test]
        public void GetService_Unregistered_ReturnsNull_TryGetReturnsFalse()
        {
            BuildApp(null);

            Assert.IsNull(GameServices.Provider.GetService<IBetaService>());
            Assert.IsFalse(GameServices.Provider.TryGetService<IBetaService>(out var service));
            Assert.IsNull(service);
        }

        [Test]
        public void FactoryRegistration_ReturnsFactoryInstance()
        {
            var instance = new AlphaService();
            BuildApp(c => c.Register<IAlphaService>(EServiceScopeKind.App, _ => instance));

            Assert.AreSame(instance, GameServices.Provider.GetRequiredService<IAlphaService>());
            Assert.AreEqual(1, instance.InitCount, "工厂实例同样由容器驱动 OnInit");
        }

        [Test]
        public void BuildAsync_Twice_Throws()
        {
            var container = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            Assert.Throws<GameException>(
                () => container.BuildAsync().GetAwaiter().GetResult());
        }

        // --- 构造注入与拓扑排序 ---

        [Test]
        public void ConstructorInjection_DependencyInjected()
        {
            BuildApp(c =>
            {
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
                c.Register<IAlphaService, DependentService>(EServiceScopeKind.App);
            });

            var dependent = (DependentService)GameServices.Provider.GetRequiredService<IAlphaService>();
            var dependee = GameServices.Provider.GetRequiredService<IDepTargetService>();

            Assert.AreSame(dependee, dependent.Dependency, "构造函数依赖应注入容器内已注册实例");
        }

        [Test]
        public void Topology_DependeeCreatedAndInitializedFirst()
        {
            // 注册顺序故意颠倒：依赖方先注册，拓扑排序应保证被依赖方先创建、先初始化
            BuildApp(c =>
            {
                c.Register<IAlphaService, DependentAsyncService>(EServiceScopeKind.App);
                c.Register<IDepTargetService, DependeeAsyncService>(EServiceScopeKind.App);
            });

            Assert.AreEqual(
                new[] { "Dependee:async", "Dependent:async" },
                s_OrderLog.ToArray(),
                "异步初始化按拓扑序执行：被依赖方（Dependee）先于依赖方（Dependent）");
        }

        [Test]
        public void CircularDependency_Throws()
        {
            Assert.Throws<GameException>(() => BuildApp(c =>
            {
                c.Register<IAlphaService, CycleServiceA>(EServiceScopeKind.App);
                c.Register<IBetaService, CycleServiceB>(EServiceScopeKind.App);
            }));
        }

        [Test]
        public void IServiceProvider_InjectableViaConstructor()
        {
            BuildApp(c =>
            {
                c.Register<IAlphaService, ProviderConsumerService>(EServiceScopeKind.App);
                c.Register<IBetaService, BetaService>(EServiceScopeKind.App);
            });

            var consumer = (ProviderConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();

            Assert.IsNotNull(consumer.Provider);
            Assert.IsNotNull(consumer.Provider.GetService<IBetaService>(), "注入的 Provider 应能解析同作用域服务");
        }

        // --- 关闭顺序与状态 ---

        private abstract class ShutdownOrderService : TestServiceBase
        {
            public abstract string Name { get; }

            public override void Shutdown()
            {
                s_OrderLog.Add(Name + ":shutdown");
                base.Shutdown();
            }
        }

        private sealed class ShutdownDependee : ShutdownOrderService, IDepTargetService
        {
            public override string Name => "dependee";
        }

        private sealed class ShutdownDependent : ShutdownOrderService, IAlphaService
        {
            public IDepTargetService Dependency { get; }

            public ShutdownDependent(IDepTargetService dependency)
            {
                Dependency = dependency;
            }

            public override string Name => "dependent";
        }

        [Test]
        public void Shutdown_DependentsCloseFirst_ReverseTopology()
        {
            // 拓扑序：Dependee → Dependent（先注册先初始化）；关闭序应为 Dependent → Dependee
            BuildApp(c =>
            {
                c.Register<IDepTargetService, ShutdownDependee>(EServiceScopeKind.App);
                c.Register<IAlphaService, ShutdownDependent>(EServiceScopeKind.App);
            });

            s_OrderLog.Clear();
            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.AreEqual(
                new[] { "dependent:shutdown", "dependee:shutdown" },
                s_OrderLog.ToArray(),
                "关闭应按逆拓扑序执行：依赖方先关闭，被依赖方后关闭");
        }

        [Test]
        public void Shutdown_TransitionsStateToDisposed()
        {
            var container = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            var alpha = (AlphaService)container.ServiceProvider.GetRequiredService<IAlphaService>();

            container.Dispose();

            Assert.AreEqual(EServiceState.Disposed, alpha.State);
            Assert.AreEqual(1, alpha.ShutdownCount);
        }

        [Test]
        public void ShutdownContainer_FreesProviderResolution()
        {
            var container = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.IsNull(GameServices.Provider, "全部容器关闭后 Provider 应为 null");
            Assert.IsNull(GameServices.AppContainer);
        }

        // --- 跨作用域链式查找 ---

        [Test]
        public void ProviderChain_GameplayBeatsSceneBeatsApp()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            BuildScene(GameServices.AppContainer,
                c => c.Register<IAlphaService, SceneAlphaService>(EServiceScopeKind.Scene));
            var collection = new ServiceCollection();
            collection.Register<IAlphaService, GammaLikeService>(EServiceScopeKind.Gameplay);
            var gameplay = GameServices.BuildContainer(
                EServiceScopeKind.Gameplay, collection, GameServices.SceneContainer);
            gameplay.BuildAsync().GetAwaiter().GetResult();

            Assert.IsInstanceOf<GammaLikeService>(GameServices.Provider.GetRequiredService<IAlphaService>(),
                "Gameplay 遮蔽 Scene 与 App");

            GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
            Assert.IsInstanceOf<SceneAlphaService>(GameServices.Provider.GetRequiredService<IAlphaService>(),
                "Gameplay 关闭后回退到 Scene");

            GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            Assert.IsInstanceOf<AlphaService>(GameServices.Provider.GetRequiredService<IAlphaService>(),
                "Scene 关闭后回退到 App");
        }

        private sealed class GammaLikeService : TestServiceBase, IAlphaService { }

        private sealed class SceneAlphaService : TestServiceBase, IAlphaService { }

        [Test]
        public void CrossScopeResolution_SceneServiceResolvesAppDependency()
        {
            var app = BuildApp(c => c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App));
            BuildScene(app, c => c.Register<IAlphaService, DependentService>(EServiceScopeKind.Scene));

            var dependent = (DependentService)GameServices.Provider.GetRequiredService<IAlphaService>();
            var appDependee = app.ServiceProvider.GetRequiredService<IDepTargetService>();

            Assert.AreSame(appDependee, dependent.Dependency,
                "Scene 容器内的服务应能通过父链解析 App 容器中的依赖");
        }

        // --- 轮询 ---

        [Test]
        public void Tick_HigherPriorityFirst()
        {
            BuildApp(c =>
            {
                c.Register<IAlphaService, HighPriorityService>(EServiceScopeKind.App);
                c.Register<IBetaService, LowPriorityService>(EServiceScopeKind.App);
            });

            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(new[] { "high", "low" }, s_OrderLog.ToArray(),
                "高优先级服务应先于低优先级服务轮询");
        }

        [Test]
        public void Tick_DrivesAllActiveContainers()
        {
            var app = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            var scene = BuildScene(app, c => c.Register<IBetaService, BetaService>(EServiceScopeKind.Scene));

            var appAlpha = (AlphaService)app.ServiceProvider.GetRequiredService<IAlphaService>();
            var sceneBeta = (BetaService)scene.ServiceProvider.GetRequiredService<IBetaService>();

            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(1, appAlpha.TickCount, "App 容器服务应被轮询");
            Assert.AreEqual(1, sceneBeta.TickCount, "Scene 容器服务应被轮询");

            GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(2, appAlpha.TickCount, "App 容器服务应继续被轮询");
            Assert.AreEqual(1, sceneBeta.TickCount, "已关闭容器的服务不应再被轮询");
        }

        // --- 事件 ---

        [Test]
        public void ServiceRegisteredEvent_FiresAfterOnInit()
        {
            IService received = null;
            EServiceState stateAtEvent = EServiceState.Created;
            GameServices.onServiceRegistered += (svc, type, scope) =>
            {
                received = svc;
                stateAtEvent = svc.State;
            };

            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            Assert.IsNotNull(received, "onServiceRegistered 事件应在构建时触发");
            Assert.AreEqual(EServiceState.Initialized, stateAtEvent, "事件触发时服务应已完成初始化");
        }

        [Test]
        public void ServiceUnregisteredEvent_FiresAfterShutdown()
        {
            var container = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            var alpha = (AlphaService)container.ServiceProvider.GetRequiredService<IAlphaService>();

            IService received = null;
            EServiceState stateAtEvent = EServiceState.Created;
            GameServices.onServiceUnregistered += svc =>
            {
                received = svc;
                stateAtEvent = svc.State;
            };

            container.Dispose();

            Assert.AreSame(alpha, received, "onServiceUnregistered 事件应在关闭时触发");
            Assert.AreEqual(EServiceState.Disposed, stateAtEvent);
        }

        // --- MonoBehaviour 服务 ---

        [Test]
        public void RegisterMono_CreatesInjectsAndInitializes()
        {
            GameObject created = null;
            try
            {
                var scene = BuildScene(null,
                    c => c.RegisterMono<IMonoContractService, TestMonoService>(EServiceScopeKind.Scene));

                var mono = (TestMonoService)(object)scene.ServiceProvider.GetRequiredService<IMonoContractService>();
                created = mono.gameObject;

                Assert.IsTrue(mono.InjectCalled, "容器应在 OnInit 前调用 Inject");
                Assert.IsNotNull(mono.InjectedProvider);
                Assert.AreEqual(1, mono.InitCount);
                Assert.AreEqual(EServiceState.Initialized, mono.State);

                scene.Dispose();

                Assert.AreEqual(1, mono.ShutdownCount, "容器关闭应驱动 Mono 服务 Shutdown");
                Assert.AreEqual(EServiceState.Disposed, mono.State);
            }
            finally
            {
                if (created != null) UnityEngine.Object.DestroyImmediate(created);
            }
        }

        // --- 诊断 [DIAGNOSTICS] ---

        [Test]
        public void GetDiagnosticInfo_ReportsRegisteredServices()
        {
            BuildApp(c =>
            {
                c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App);
                c.Register<IBetaService, BetaService>(EServiceScopeKind.App);
            });

            var infos = GameServices.GetDiagnosticInfo();

            Assert.GreaterOrEqual(infos.Count, 2);
            Assert.IsTrue(infos.Exists(i => i.InterfaceType == typeof(IAlphaService).FullName));
            Assert.IsTrue(infos.Exists(i => i.InterfaceType == typeof(IBetaService).FullName));
        }

        // ═══════════════════════════════════════════════════════
        // 拦截器测试 [INTERCEPTOR TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class TestInterceptor : IServiceInterceptor
        {
            public int Priority { get; set; }
            public List<string> Events { get; } = new();

            public void OnServiceRegistering(IService service, Type interfaceType, EServiceScopeKind scope)
                => Events.Add("Registering:" + interfaceType.Name);

            public void OnServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope)
                => Events.Add("Registered:" + interfaceType.Name);

            public void OnServiceUnregistered(IService service)
                => Events.Add("Unregistered:" + service.GetType().Name);

            public void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds)
                => Events.Add("Tick:" + service.GetType().Name);

            public void OnServiceShutdown(IService service)
                => Events.Add("Shutdown:" + service.GetType().Name);
        }

        [Test]
        public void Interceptor_RegisterFlow_RegisteringBeforeOnInit_RegisteredAfterOnInit()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            var container = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            // Registering 在 RegisterInternal 中触发（OnInit 前）
            // Registered 在 BuildAsync 的 OnInit 后触发
            Assert.AreEqual(2, interceptor.Events.Count, "应触发 Registering + Registered 两个事件");
            Assert.AreEqual("Registering:IAlphaService", interceptor.Events[0]);
            Assert.AreEqual("Registered:IAlphaService", interceptor.Events[1]);

            var alpha = (AlphaService)container.ServiceProvider.GetRequiredService<IAlphaService>();
            Assert.AreEqual(1, alpha.InitCount, "OnInit 应在 Registering 后、Registered 前调用");
        }

        [Test]
        public void Interceptor_ShutdownFlow_ShutdownBeforeServiceShutdown_UnregisteredAfter()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            var container = BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            interceptor.Events.Clear();

            container.Dispose();

            // Shutdown 在 service.Shutdown() 调用前触发
            // Unregistered 在服务从注册表移除后触发
            Assert.IsTrue(interceptor.Events.Contains("Shutdown:AlphaService"), "应触发 Shutdown 拦截");
            Assert.IsTrue(interceptor.Events.Contains("Unregistered:AlphaService"), "应触发 Unregistered 拦截");
            Assert.Less(
                interceptor.Events.IndexOf("Shutdown:AlphaService"),
                interceptor.Events.IndexOf("Unregistered:AlphaService"),
                "Shutdown 应在 Unregistered 之前");
        }

        [Test]
        public void Interceptor_Tick_TriggersBeforeEachService()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            interceptor.Events.Clear();

            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(1, interceptor.Events.Count, "应触发一次 Tick 拦截");
            Assert.AreEqual("Tick:AlphaService", interceptor.Events[0]);
        }

        [Test]
        public void Interceptor_MultipleInterceptors_ExecuteByPriorityDescending()
        {
            var high = new TestInterceptor { Priority = 10 };
            var low = new TestInterceptor { Priority = 0 };
            GameServices.AddInterceptor(low);
            GameServices.AddInterceptor(high);

            Assert.AreSame(high, GameServices.Interceptors[0], "高优先级应排在前面");
            Assert.AreSame(low, GameServices.Interceptors[1], "低优先级应排在后面");
        }

        [Test]
        public void Interceptor_RemoveInterceptor_StopsReceivingEvents()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            Assert.IsTrue(interceptor.Events.Count > 0, "添加后应收到事件");

            interceptor.Events.Clear();
            GameServices.RemoveInterceptor(interceptor);

            BuildApp(c => c.Register<IBetaService, BetaService>(EServiceScopeKind.App));
            Assert.AreEqual(0, interceptor.Events.Count, "移除后不应再收到事件");
        }

        // ═══════════════════════════════════════════════════════
        // 迭代安全测试 [ITERATION SAFETY TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class DisposeScopeOnTick : TestServiceBase, IAlphaService
        {
            private readonly EServiceScopeKind _scopeToDispose;

            public DisposeScopeOnTick(EServiceScopeKind scopeToDispose)
            {
                _scopeToDispose = scopeToDispose;
            }

            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.ShutdownContainer(_scopeToDispose);
            }
        }

        [Test]
        public void Dispose_DuringTick_DefersAndDoesNotThrow()
        {
            // 同作用域内一个服务的 Tick 触发自身作用域 Dispose——应延迟到迭代结束
            BuildApp(c => c.Register<IAlphaService>(
                EServiceScopeKind.App,
                _ => new DisposeScopeOnTick(EServiceScopeKind.App)).WithPriority(10));

            // 不应抛异常（迭代中 Dispose 被延迟）
            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            // 容器已关闭，AppContainer 为 null
            Assert.IsNull(GameServices.AppContainer, "迭代中请求的 Dispose 应在迭代结束后执行");
        }

        private sealed class TickCountService : TestServiceBase, IBetaService
        {
            // 记录自身 Tick 被调用的次数
        }

        [Test]
        public void Dispose_DuringTick_OtherServicesStillTickInSameFrame()
        {
            // 高优先级服务在 Tick 中触发 Dispose，低优先级服务应在本轮迭代中仍被 Tick
            // 注册顺序：trigger (Priority=10) 先 Tick，other (Priority=0) 后 Tick
            var collection = new ServiceCollection();
            collection.Register<IAlphaService>(EServiceScopeKind.App, _ => new DisposeScopeOnTick(EServiceScopeKind.App))
                       .WithPriority(10);
            collection.Register<IBetaService, TickCountService>(EServiceScopeKind.App)
                       .WithPriority(0);
            var container = GameServices.BuildContainer(EServiceScopeKind.App, collection);
            container.BuildAsync().GetAwaiter().GetResult();

            var beta = (TickCountService)container.ServiceProvider.GetRequiredService<IBetaService>();

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));
            Assert.AreEqual(1, beta.TickCount, "同作用域后续服务在本轮迭代中仍应被轮询（销毁延迟到迭代结束）");
            Assert.AreEqual(1, beta.ShutdownCount, "迭代结束后作用域销毁应关闭全部服务");
        }

        private sealed class CrossScopeDisposeOnTick : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                // 在 App 作用域 Tick 中关闭 Scene 作用域——Scene 未在迭代中，Dispose 立即执行
                GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            }
        }

        [Test]
        public void CrossScopeDispose_DuringTick_ImmediateShutdown()
        {
            var app = BuildApp(c => c.Register<IAlphaService, CrossScopeDisposeOnTick>(EServiceScopeKind.App));
            var scene = BuildScene(app, c => c.Register<IBetaService, BetaService>(EServiceScopeKind.Scene));

            var sceneBeta = (BetaService)scene.ServiceProvider.GetRequiredService<IBetaService>();

            // App Tick 先执行：trigger 在 App scope Tick 中关闭 Scene scope（Scene 未在迭代中 → 立即 Dispose）
            GameServices.Tick(0f, 0f);

            Assert.AreEqual(1, sceneBeta.ShutdownCount, "Scene 作用域中的服务应被立即关闭");
            Assert.AreEqual(0, sceneBeta.TickCount, "Scene scope 未开始迭代即被关闭，服务不应被 Tick");

            // Scene 容器已关闭
            Assert.IsNull(GameServices.SceneContainer);
        }

        // ═══════════════════════════════════════════════════════
        // 构造函数选择测试 [CONSTRUCTOR SELECTION TESTS]
        // ═══════════════════════════════════════════════════════

        private interface IMultiCtorService : IService { }

        private sealed class MultiCtorService : TestServiceBase, IMultiCtorService
        {
            public string UsedCtor { get; private set; }
            public IDepTargetService Dependency { get; private set; }

            public MultiCtorService()
            {
                UsedCtor = "parameterless";
            }

            [ServiceConstructor]
            public MultiCtorService(IDepTargetService dependency)
            {
                UsedCtor = "attributed";
                Dependency = dependency;
            }

            public MultiCtorService(IDepTargetService dependency, IBetaService beta)
            {
                UsedCtor = "most-params";
                Dependency = dependency;
            }
        }

        [Test]
        public void ServiceConstructorAttribute_PreferredOverMostParameters()
        {
            BuildApp(c =>
            {
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
                c.Register<IBetaService, BetaService>(EServiceScopeKind.App);
                c.Register<IMultiCtorService, MultiCtorService>(EServiceScopeKind.App);
            });

            var svc = (MultiCtorService)GameServices.Provider.GetRequiredService<IMultiCtorService>();

            Assert.AreEqual("attributed", svc.UsedCtor, "[ServiceConstructor] 标记的构造函数应被优先选择");
            Assert.IsNotNull(svc.Dependency, "标记构造函数的依赖应被注入");
        }
    }
}
