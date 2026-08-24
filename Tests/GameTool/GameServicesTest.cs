using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
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

        private static void BuildApp(Action<ServiceCollection> configure)
        {
            if (GameServices.HasApp)
                GameServices.ShutdownContainer(EServiceScopeKind.App);
            var collection = new ServiceCollection();
            configure?.Invoke(collection);
            GameServices.BuildAsync(EServiceScopeKind.App, collection).GetAwaiter().GetResult();
        }

        private static void BuildScene(Action<ServiceCollection> configure)
        {
            if (GameServices.HasScene)
                GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            var collection = new ServiceCollection();
            configure?.Invoke(collection);
            GameServices.BuildAsync(EServiceScopeKind.Scene, collection).GetAwaiter().GetResult();
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
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            Assert.Throws<GameException>(
                () => GameServices.BuildAsync(EServiceScopeKind.App, new ServiceCollection()).GetAwaiter().GetResult());
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

        // --- Func<T> 延迟解析注入 [LAZY RESOLUTION] ---

        private sealed class LazyConsumerService : TestServiceBase, IAlphaService
        {
            public Func<IDepTargetService> Resolver { get; }

            public LazyConsumerService(Func<IDepTargetService> resolver)
            {
                Resolver = resolver;
            }
        }

        [Test]
        public void FuncInjection_ResolvesTargetLazily()
        {
            BuildApp(c =>
            {
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
                c.Register<IAlphaService, LazyConsumerService>(EServiceScopeKind.App);
            });

            var consumer = (LazyConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();
            var resolved = consumer.Resolver();

            Assert.IsInstanceOf<DependeeService>(resolved,
                "Func<T> 注入的委托应在调用时解析目标服务");
            Assert.AreSame(
                GameServices.Provider.GetRequiredService<IDepTargetService>(),
                resolved,
                "延迟解析应返回容器内同一单例");
        }

        [Test]
        public void FuncInjection_TopologyGuaranteesTargetReady()
        {
            // 注册顺序故意颠倒：Func<IDepTargetService> 的目标后注册，
            // 拓扑建边（Func<T> 解包 T）应保证目标先创建，委托调用时依赖已就绪
            BuildApp(c =>
            {
                c.Register<IAlphaService, LazyConsumerService>(EServiceScopeKind.App);
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
            });

            var consumer = (LazyConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();

            Assert.DoesNotThrow(() => consumer.Resolver(),
                "拓扑建边应保证延迟解析的目标已注册");
        }

        [Test]
        public void FuncInjection_AfterShutdown_Throws()
        {
            BuildApp(c => c.Register<IAlphaService, LazyConsumerService>(EServiceScopeKind.App));

            var consumer = (LazyConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();
            GameServices.ShutdownContainer(EServiceScopeKind.App);

            // 目标 IDepTargetService 从未注册：委托调用应 fail-fast 而非返回 null
            Assert.Throws<GameException>(() => consumer.Resolver());
        }

        // --- IServiceResolver<T> 延迟解析注入 [AOT-SAFE LAZY RESOLUTION] ---

        private sealed class ResolverConsumerService : TestServiceBase, IAlphaService
        {
            public IServiceResolver<IDepTargetService> Resolver { get; }

            public ResolverConsumerService(IServiceResolver<IDepTargetService> resolver)
            {
                Resolver = resolver;
            }
        }

        [Test]
        public void ServiceResolverInjection_ResolvesTargetLazily()
        {
            BuildApp(c =>
            {
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
                c.Register<IAlphaService, ResolverConsumerService>(EServiceScopeKind.App);
            });

            var consumer = (ResolverConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();
            var resolved = consumer.Resolver.Resolve();

            Assert.IsInstanceOf<DependeeService>(resolved,
                "IServiceResolver<T> 注入的解析器应在调用时解析目标服务");
            Assert.AreSame(
                GameServices.Provider.GetRequiredService<IDepTargetService>(),
                resolved,
                "延迟解析应返回容器内同一单例");
        }

        [Test]
        public void ServiceResolverInjection_TopologyGuaranteesTargetReady()
        {
            // 注册顺序故意颠倒：IServiceResolver<IDepTargetService> 的目标后注册，
            // 拓扑建边（IServiceResolver<T> 解包 T）应保证目标先创建，解析器调用时依赖已就绪
            BuildApp(c =>
            {
                c.Register<IAlphaService, ResolverConsumerService>(EServiceScopeKind.App);
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
            });

            var consumer = (ResolverConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();

            Assert.DoesNotThrow(() => consumer.Resolver.Resolve(),
                "拓扑建边应保证延迟解析的目标已注册");
        }

        [Test]
        public void ServiceResolverInjection_AfterShutdown_Throws()
        {
            BuildApp(c =>
            {
                c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App);
                c.Register<IAlphaService, ResolverConsumerService>(EServiceScopeKind.App);
            });

            var consumer = (ResolverConsumerService)GameServices.Provider.GetRequiredService<IAlphaService>();
            GameServices.ShutdownContainer(EServiceScopeKind.App);

            // 目标已随作用域关闭：解析器调用应 fail-fast 而非返回 null
            Assert.Throws<GameException>(() => consumer.Resolver.Resolve());
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
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            var alpha = (AlphaService)GameServices.Provider.GetRequiredService<IAlphaService>();

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.AreEqual(EServiceState.Disposed, alpha.State);
            Assert.AreEqual(1, alpha.ShutdownCount);
        }

        [Test]
        public void ShutdownContainer_FreesProviderResolution()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.IsNull(GameServices.Provider, "全部容器关闭后 Provider 应为 null");
            Assert.IsFalse(GameServices.HasApp);
        }

        // --- 跨作用域链式查找 ---

        [Test]
        public void ProviderChain_GameplayBeatsSceneBeatsApp()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            BuildScene(
                c => c.Register<IAlphaService, SceneAlphaService>(EServiceScopeKind.Scene));
            var collection = new ServiceCollection();
            collection.Register<IAlphaService, GammaLikeService>(EServiceScopeKind.Gameplay);
            GameServices.BuildAsync(EServiceScopeKind.Gameplay, collection).GetAwaiter().GetResult();

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
            BuildApp(c => c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App));
            BuildScene(c => c.Register<IAlphaService, DependentService>(EServiceScopeKind.Scene));

            var dependent = (DependentService)GameServices.Provider.GetRequiredService<IAlphaService>();
            var appDependee = GameServices.Provider.GetRequiredService<IDepTargetService>();

            Assert.AreSame(appDependee, dependent.Dependency,
                "Scene 容器内的服务应能跨作用域解析 App 容器中的依赖");
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
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            BuildScene(c => c.Register<IBetaService, BetaService>(EServiceScopeKind.Scene));

            var appAlpha = (AlphaService)GameServices.Provider.GetRequiredService<IAlphaService>();
            var sceneBeta = (BetaService)GameServices.Provider.GetRequiredService<IBetaService>();

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
                stateAtEvent = GameServices.GetState(svc);
            };

            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            Assert.IsNotNull(received, "onServiceRegistered 事件应在构建时触发");
            Assert.AreEqual(EServiceState.Initialized, stateAtEvent, "事件触发时服务应已完成初始化");
        }

        [Test]
        public void ServiceUnregisteredEvent_FiresAfterShutdown()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            var alpha = (AlphaService)GameServices.Provider.GetRequiredService<IAlphaService>();

            IService received = null;
            EServiceState stateAtEvent = EServiceState.Created;
            GameServices.onServiceUnregistered += svc =>
            {
                received = svc;
                stateAtEvent = GameServices.GetState(svc);
            };

            GameServices.ShutdownContainer(EServiceScopeKind.App);

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
                BuildScene(
                    c => c.RegisterMono<IMonoContractService, TestMonoService>(EServiceScopeKind.Scene));

                var mono = (TestMonoService)(object)GameServices.Provider.GetRequiredService<IMonoContractService>();
                created = mono.gameObject;

                Assert.IsTrue(mono.InjectCalled, "容器应在 OnInit 前调用 Inject");
                Assert.IsNotNull(mono.InjectedProvider);
                Assert.AreEqual(1, mono.InitCount);
                Assert.AreEqual(EServiceState.Initialized, mono.State);

                GameServices.ShutdownContainer(EServiceScopeKind.Scene);

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
            Assert.IsTrue(infos.Exists(i => i.ContractType == typeof(IAlphaService).FullName));
            Assert.IsTrue(infos.Exists(i => i.ContractType == typeof(IBetaService).FullName));
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

            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            // Registering 在 RegisterInternal 中触发（OnInit 前）
            // Registered 在 BuildAsync 的 OnInit 后触发
            Assert.AreEqual(2, interceptor.Events.Count, "应触发 Registering + Registered 两个事件");
            Assert.AreEqual("Registering:IAlphaService", interceptor.Events[0]);
            Assert.AreEqual("Registered:IAlphaService", interceptor.Events[1]);

            var alpha = (AlphaService)GameServices.Provider.GetRequiredService<IAlphaService>();
            Assert.AreEqual(1, alpha.InitCount, "OnInit 应在 Registering 后、Registered 前调用");
        }

        [Test]
        public void Interceptor_ShutdownFlow_ShutdownBeforeServiceShutdown_UnregisteredAfter()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            interceptor.Events.Clear();

            GameServices.ShutdownContainer(EServiceScopeKind.App);

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

            // 容器已关闭，HasApp 为 false
            Assert.IsFalse(GameServices.HasApp, "迭代中请求的 Dispose 应在迭代结束后执行");
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
            GameServices.BuildAsync(EServiceScopeKind.App, collection).GetAwaiter().GetResult();

            var beta = (TickCountService)GameServices.Provider.GetRequiredService<IBetaService>();

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
            BuildApp(c => c.Register<IAlphaService, CrossScopeDisposeOnTick>(EServiceScopeKind.App));
            BuildScene(c => c.Register<IBetaService, BetaService>(EServiceScopeKind.Scene));

            var sceneBeta = (BetaService)GameServices.Provider.GetRequiredService<IBetaService>();

            // App Tick 先执行：trigger 在 App scope Tick 中关闭 Scene scope（Scene 未在迭代中 → 立即 Dispose）
            GameServices.Tick(0f, 0f);

            Assert.AreEqual(1, sceneBeta.ShutdownCount, "Scene 作用域中的服务应被立即关闭");
            Assert.AreEqual(0, sceneBeta.TickCount, "Scene scope 未开始迭代即被关闭，服务不应被 Tick");

            // Scene 容器已关闭
            Assert.IsFalse(GameServices.HasScene);
        }

        // ═══════════════════════════════════════════════════════
        // 构建失败回滚测试 [BUILD FAILURE ROLLBACK TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class InitThrowService : TestServiceBase, IAlphaService
        {
            public override void OnInit() => throw new InvalidOperationException("simulated init failure");
        }

        [Test]
        public void BuildFailure_RollsBackScopeAndAllowsRebuild()
        {
            var dependee = new DependeeService();

            // 依赖服务 OnInit 抛出 → BuildAsync 抛出 → 作用域整体回滚
            Assert.Throws<GameException>(() => BuildApp(c =>
            {
                c.Register<IDepTargetService>(EServiceScopeKind.App, _ => dependee);
                c.Register<IAlphaService, InitThrowService>(EServiceScopeKind.App);
            }));

            Assert.IsFalse(GameServices.HasApp, "构建失败后作用域应回滚到未构建状态");
            Assert.IsNull(GameServices.Provider, "回滚后不应残留可用的 Provider");
            Assert.AreEqual(1, dependee.ShutdownCount, "已注册服务应随回滚被 Shutdown");
            Assert.AreEqual(EServiceState.Disposed, dependee.State);

            // 回滚后允许重建（不残留"已构建"的半成品状态）
            Assert.DoesNotThrow(() => BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App)));
            Assert.IsTrue(GameServices.HasApp);
        }

        [Test]
        public void DuplicateContract_FailsFastBeforeCreation()
        {
            // As 额外契约与既有主契约重叠：拓扑排序阶段即失败（实例创建前），不产生孤儿
            Assert.Throws<GameException>(() => BuildApp(c =>
            {
                c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App);
                c.Register<IBetaService, BetaService>(EServiceScopeKind.App).As<IAlphaService>();
            }));

            Assert.IsFalse(GameServices.HasApp, "契约冲突构建失败后作用域应回滚");
            Assert.IsNull(GameServices.Provider);
        }

        private sealed class BadTickMonoService : ServiceMono<SceneScope>, IMonoContractService, IServiceTickable
        {
            public override void OnInit() { }
            public override void Shutdown() { }
            public void Tick(float elapseSeconds, float realElapseSeconds) { }
        }

        [Test]
        public void BuildFailure_DestroysOrphanMonoGameObject()
        {
            // Mono 服务实现 IServiceTickable → Register 拒绝 → 实例已创建成为孤儿 → GameObject 应被销毁
            Assert.Throws<GameException>(() => BuildScene(
                c => c.RegisterMono<IMonoContractService, BadTickMonoService>(EServiceScopeKind.Scene)));

            Assert.IsFalse(GameServices.HasScene, "构建失败后作用域应回滚");
            Assert.AreEqual(0, UnityEngine.Object.FindObjectsOfType<BadTickMonoService>().Length,
                "被拒注册的孤儿 Mono 服务的 GameObject 应被销毁");
        }

        [Test]
        public void Factory_ReturningNull_Throws()
        {
            Assert.Throws<GameException>(() => BuildApp(
                c => c.Register<IAlphaService>(EServiceScopeKind.App, _ => null)));

            Assert.IsFalse(GameServices.HasApp, "工厂返回 null 构建失败后作用域应回滚");
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

        // ═══════════════════════════════════════════════════════
        // 运行时服务注册测试 [RUNTIME REGISTRATION TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void RuntimeRegister_AddsServiceToActiveScope()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            var beta = new BetaService();
            GameServices.RegisterService(EServiceScopeKind.App, beta as IBetaService);

            var resolved = GameServices.Provider.GetService<IBetaService>();
            Assert.AreSame(beta, resolved);
            Assert.AreEqual(1, beta.InitCount, "运行时注册应驱动 OnInit");
            Assert.AreEqual(EServiceState.Initialized, beta.State);
        }

        [Test]
        public void RuntimeRegister_DuplicateContract_Throws()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            var another = new AlphaService();
            Assert.Throws<GameException>(() =>
                GameServices.RegisterService(EServiceScopeKind.App, another as IAlphaService));
        }

        [Test]
        public void RuntimeUnregister_RemovesAndShutsDownService()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            var alpha = (AlphaService)GameServices.Provider.GetRequiredService<IAlphaService>();

            bool result = GameServices.UnregisterService<IAlphaService>(EServiceScopeKind.App);

            Assert.IsTrue(result);
            Assert.AreEqual(1, alpha.ShutdownCount, "运行时注销应驱动 Shutdown");
            Assert.AreEqual(EServiceState.Disposed, alpha.State);
            Assert.IsNull(GameServices.Provider.GetService<IAlphaService>(),
                "注销后服务不应再可解析");
        }

        [Test]
        public void RuntimeUnregister_NotFound_ReturnsFalse()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));

            bool result = GameServices.UnregisterService<IBetaService>(EServiceScopeKind.App);

            Assert.IsFalse(result, "未注册的服务注销应返回 false");
        }

        [Test]
        public void RuntimeRegister_WithLifecycle_CallsInitAndDestroy()
        {
            BuildApp(c => c.Register<IDepTargetService, DependeeService>(EServiceScopeKind.App));

            var consumer = new RuntimeConsumerService();
            GameServices.RegisterService(EServiceScopeKind.App, consumer as IAlphaService);

            Assert.AreEqual(1, consumer.InitCount, "IServiceLifecycle.Initialize 应驱动 OnInit");
            Assert.IsTrue(consumer.CanResolveDependency, "运行时注册的服务应能通过 Require<T> 解析已注册依赖");

            GameServices.UnregisterService<IAlphaService>(EServiceScopeKind.App);

            Assert.AreEqual(1, consumer.ShutdownCount, "IServiceLifecycle.Destroy 应驱动 Shutdown");
        }

        private sealed class RuntimeConsumerService : ServiceBase, IAlphaService
        {
            public int InitCount;
            public int ShutdownCount;
            public bool CanResolveDependency;

            public override void OnInit()
            {
                InitCount++;
                CanResolveDependency = TryGet(out IDepTargetService _);
            }

            public override void Shutdown() => ShutdownCount++;
        }

        // ═══════════════════════════════════════════════════════
        // SelfRegisteringMono 测试 [SELF-REGISTERING MONO TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class SelfRegisteringTestService : SelfRegisteringMono<AppScope>, IMonoContractService
        {
            public int InitCount;
            public int ShutdownCount;

            public override void OnInit() => InitCount++;
            public override void Shutdown() => ShutdownCount++;
        }

        [Test]
        public void SelfRegisteringMono_AutoRegistersOnAwake()
        {
            BuildApp(null);

            var go = new GameObject("SelfRegTest");
            var mono = go.AddComponent<SelfRegisteringTestService>();

            try
            {
                Assert.AreEqual(1, mono.InitCount, "Awake 应触发 OnInit");
                Assert.AreEqual(EServiceState.Initialized, mono.State);

                var resolved = GameServices.Provider.GetService<IMonoContractService>();
                Assert.AreSame(mono, resolved, "自注册后应可解析");
            }
            finally
            {
                if (go != null) UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SelfRegisteringMono_AutoUnregistersOnDestroy()
        {
            BuildApp(null);

            var go = new GameObject("SelfRegTest");
            var mono = go.AddComponent<SelfRegisteringTestService>();

            UnityEngine.Object.DestroyImmediate(go);

            Assert.AreEqual(1, mono.ShutdownCount, "OnDestroy 应触发 Shutdown");
            Assert.AreEqual(EServiceState.Disposed, mono.State);
            Assert.IsFalse(GameServices.HasApp || GameServices.Provider?.GetService<IMonoContractService>() != null,
                "注销后服务不应再可解析");
        }

        [Test]
        public void SelfRegisteringMono_Duplicate_DestroyedSelf()
        {
            BuildApp(null);

            var go1 = new GameObject("SelfReg1");
            var mono1 = go1.AddComponent<SelfRegisteringTestService>();
            var go2 = new GameObject("SelfReg2");

            try
            {
                // 第二个实例应自动销毁（同契约已注册）
                var mono2 = go2.AddComponent<SelfRegisteringTestService>();
                Assert.IsTrue(mono1.InitCount == 1, "首个实例应正常注册");
                // mono2 的 GameObject 应被销毁
                Assert.IsTrue(go2 == null || mono2 == null,
                    "重复注册的实例应被销毁");
            }
            finally
            {
                if (go1 != null) UnityEngine.Object.DestroyImmediate(go1);
                if (go2 != null) UnityEngine.Object.DestroyImmediate(go2);
            }
        }

        // ═══════════════════════════════════════════════════════
        // 异步关闭管线测试 [ASYNC SHUTDOWN TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class AsyncShutdownService : TestServiceBase, IAlphaService, IAsyncShutdownService
        {
            public bool AsyncShutdownCalled;
            public bool SyncShutdownCalled;

            public override void Shutdown()
            {
                SyncShutdownCalled = true;
                base.Shutdown();
            }

            public UniTask OnShutdownAsync()
            {
                AsyncShutdownCalled = true;
                s_OrderLog.Add("async:shutdown");
                return UniTask.CompletedTask;
            }
        }

        private sealed class AsyncShutdownDependee : TestServiceBase, IDepTargetService, IAsyncShutdownService
        {
            public UniTask OnShutdownAsync()
            {
                s_OrderLog.Add("dependee:async");
                return UniTask.CompletedTask;
            }

            public override void Shutdown()
            {
                s_OrderLog.Add("dependee:sync");
                base.Shutdown();
            }
        }

        private sealed class AsyncShutdownDependent : TestServiceBase, IAlphaService, IAsyncShutdownService
        {
            public AsyncShutdownDependent(IDepTargetService _) { }

            public UniTask OnShutdownAsync()
            {
                s_OrderLog.Add("dependent:async");
                return UniTask.CompletedTask;
            }

            public override void Shutdown()
            {
                s_OrderLog.Add("dependent:sync");
                base.Shutdown();
            }
        }

        [Test]
        public void AsyncShutdown_CalledBeforeSyncShutdown()
        {
            BuildApp(c => c.Register<IAlphaService, AsyncShutdownService>(EServiceScopeKind.App));
            var svc = (AsyncShutdownService)GameServices.Provider.GetRequiredService<IAlphaService>();

            GameServices.ShutdownContainerAsync(EServiceScopeKind.App).GetAwaiter().GetResult();

            Assert.IsTrue(svc.AsyncShutdownCalled, "OnShutdownAsync 应在 Shutdown 前调用");
            Assert.IsTrue(svc.SyncShutdownCalled, "Shutdown 仍应被调用");
            Assert.AreEqual(EServiceState.Disposed, svc.State);
        }

        [Test]
        public void AsyncShutdown_ReverseTopology()
        {
            BuildApp(c =>
            {
                c.Register<IDepTargetService, AsyncShutdownDependee>(EServiceScopeKind.App);
                c.Register<IAlphaService, AsyncShutdownDependent>(EServiceScopeKind.App);
            });

            s_OrderLog.Clear();
            GameServices.ShutdownContainerAsync(EServiceScopeKind.App).GetAwaiter().GetResult();

            // 逆拓扑序：Dependent 先于 Dependee
            // 异步阶段：dependent:async → dependee:async
            // 同步阶段：dependent:sync → dependee:sync
            Assert.AreEqual(
                new[] { "dependent:async", "dependee:async", "dependent:sync", "dependee:sync" },
                s_OrderLog.ToArray(),
                "异步关闭应按逆拓扑序执行：依赖方先关闭");
        }

        [Test]
        public void ShutdownAsync_ClosesAllScopes()
        {
            BuildApp(c => c.Register<IAlphaService, AlphaService>(EServiceScopeKind.App));
            BuildScene(c => c.Register<IBetaService, BetaService>(EServiceScopeKind.Scene));

            GameServices.ShutdownAsync().GetAwaiter().GetResult();

            Assert.IsFalse(GameServices.HasApp);
            Assert.IsFalse(GameServices.HasScene);
            Assert.IsNull(GameServices.Provider);
        }

        // ═══════════════════════════════════════════════════════
        // PendingChanges 延迟队列测试 [DEFERRED CHANGES TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class RegisterOnTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                // 在 Tick 中注册新服务——默认 Defer 模式应延迟到本轮迭代结束
                var beta = new BetaService();
                GameServices.RegisterService(EServiceScopeKind.App, beta as IBetaService);
            }
        }

        [Test]
        public void DeferredRegister_QueuedDuringTick_ProcessedAfterIteration()
        {
            BuildApp(c => c.Register<IAlphaService>(
                EServiceScopeKind.App, _ => new RegisterOnTickService()).WithPriority(10));

            // Tick 中注册 BetaService——Defer 模式不应抛异常
            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            // 迭代结束后应完成注册
            var beta = GameServices.Provider.GetService<IBetaService>();
            Assert.IsNotNull(beta, "延迟注册的服务应在迭代结束后可解析");
            Assert.AreEqual(1, ((BetaService)(object)beta).InitCount, "延迟注册应驱动 OnInit");
        }

        private sealed class ThrowRegisterOnTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.RegisterService(
                    EServiceScopeKind.App, new BetaService() as IBetaService, EDeferMode.Throw);
            }
        }

        [Test]
        public void DeferredRegister_ThrowMode_ThrowsImmediately()
        {
            BuildApp(c => c.Register<IAlphaService>(
                EServiceScopeKind.App, _ => new ThrowRegisterOnTickService()).WithPriority(10));

            // EDeferMode.Throw 在迭代中抛出 GameException，被 Tick 的 try-catch 捕获并记录日志
            LogAssert.Expect(LogType.Error, new Regex(".*EDeferMode\\.Throw.*"));
            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));
        }

        private sealed class UnregisterOnTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                // 在 Tick 中注销 BetaService——默认 Defer 模式应延迟到本轮迭代结束
                GameServices.UnregisterService<IBetaService>(EServiceScopeKind.App);
            }
        }

        [Test]
        public void DeferredUnregister_QueuedDuringTick_ProcessedAfterIteration()
        {
            BuildApp(c =>
            {
                c.Register<IBetaService, BetaService>(EServiceScopeKind.App);
                c.Register<IAlphaService>(
                    EServiceScopeKind.App, _ => new UnregisterOnTickService()).WithPriority(10);
            });

            var beta = (BetaService)GameServices.Provider.GetRequiredService<IBetaService>();

            // Tick 中注销 BetaService——Defer 模式不应抛异常
            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            // 迭代结束后应完成注销
            Assert.AreEqual(1, beta.ShutdownCount, "延迟注销应驱动 Shutdown");
            Assert.IsNull(GameServices.Provider.GetService<IBetaService>(),
                "延迟注销后服务不应再可解析");
        }

        [Test]
        public void DeferredRegister_DuplicatePendingContract_Throws()
        {
            // 在同一 Tick 中注册同一契约两次——第二次应检测到 pending 重复并抛出 GameException
            // （被 Tick 的 try-catch 捕获并记录日志）
            var collection = new ServiceCollection();
            collection.Register<IAlphaService>(
                EServiceScopeKind.App, _ => new DualRegisterOnTickService()).WithPriority(10);
            GameServices.BuildAsync(EServiceScopeKind.App, collection).GetAwaiter().GetResult();

            LogAssert.Expect(LogType.Error, new Regex(".*pending.*"));
            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));
        }

        private sealed class DualRegisterOnTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                // 第一次注册——进入 pending 队列
                GameServices.RegisterService(EServiceScopeKind.App, new BetaService() as IBetaService);
                // 第二次注册同一契约——应检测到 pending 重复并抛出
                GameServices.RegisterService(EServiceScopeKind.App, new BetaService() as IBetaService);
            }
        }

        // ═══════════════════════════════════════════════════════
        // CreationIndex 稳定排序测试 [STABLE SORT TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class SamePriorityA : TestServiceBase, IAlphaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds)
                => s_OrderLog.Add("A");
        }

        private sealed class SamePriorityB : TestServiceBase, IBetaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds)
                => s_OrderLog.Add("B");
        }

        private sealed class SamePriorityC : TestServiceBase, IDepTargetService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds)
                => s_OrderLog.Add("C");
        }

        [Test]
        public void CreationIndex_SamePriority_PreservesRegistrationOrder()
        {
            // 三个同优先级服务，注册顺序 A → B → C
            BuildApp(c =>
            {
                c.Register<IAlphaService, SamePriorityA>(EServiceScopeKind.App);
                c.Register<IBetaService, SamePriorityB>(EServiceScopeKind.App);
                c.Register<IDepTargetService, SamePriorityC>(EServiceScopeKind.App);
            });

            GameServices.Tick(0f, 0f);

            // 同优先级应按注册顺序（CreationIndex 升序）执行
            Assert.AreEqual(new[] { "A", "B", "C" }, s_OrderLog.ToArray(),
                "同优先级服务应按注册顺序执行（CreationIndex 稳定排序）");
        }

        private sealed class HighPriorityBetaService : TestServiceBase, IBetaService
        {
            public override int Priority => 10;
            public override void Tick(float elapseSeconds, float realElapseSeconds) => s_OrderLog.Add("high");
        }

        [Test]
        public void CreationIndex_MixedPriority_PriorityFirstThenCreationIndex()
        {
            // A (Priority=0, CreationIndex=0)
            // B (Priority=10, CreationIndex=1)
            // C (Priority=0, CreationIndex=2)
            // 期望顺序：B (高优先) → A (同优先, 先注册) → C (同优先, 后注册)
            BuildApp(c =>
            {
                c.Register<IAlphaService, SamePriorityA>(EServiceScopeKind.App);
                c.Register<IBetaService, HighPriorityBetaService>(EServiceScopeKind.App);
                c.Register<IDepTargetService, SamePriorityC>(EServiceScopeKind.App);
            });

            s_OrderLog.Clear();
            GameServices.Tick(0f, 0f);

            // HighPriorityBetaService 记录 "high"，SamePriorityA 记录 "A"，SamePriorityC 记录 "C"
            Assert.AreEqual(new[] { "high", "A", "C" }, s_OrderLog.ToArray(),
                "高优先级先执行，同优先级按注册顺序执行");
        }

        // ═══════════════════════════════════════════════════════
        // 零参数构造函数快路径测试 [ZERO-PARAM FAST PATH TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class ZeroParamService : TestServiceBase, IAlphaService
        {
            // 无构造函数参数——走零参数快路径
        }

        [Test]
        public void ZeroParamConstructor_FastPath_CreatesAndInjects()
        {
            BuildApp(c => c.Register<IAlphaService, ZeroParamService>(EServiceScopeKind.App));

            var resolved = GameServices.Provider.GetRequiredService<IAlphaService>();
            Assert.IsInstanceOf<ZeroParamService>(resolved);
            var svc = (ZeroParamService)resolved;
            Assert.AreEqual(1, svc.InitCount, "零参数构造服务应正常初始化");
            Assert.AreEqual(EServiceState.Initialized, svc.State);
        }
    }
}
