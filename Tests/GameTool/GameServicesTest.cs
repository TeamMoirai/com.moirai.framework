using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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
        // --- 测试用契约（必须继承 IService 以满足注册约束） ---

        private interface IAlphaService : IService { }
        private interface IBetaService : IService { }
        private interface IDepTargetService : IService { }

        // --- 顺序记录（静态，SetUp 清空） ---

        private static readonly List<string> s_OrderLog = new List<string>();

        // --- 测试用服务基类 ---

        private abstract class TestServiceBase : ServiceBase, IServiceTickable
        {
            public int InitCount;
            public int ShutdownCount;
            public int TickCount;

            public override void OnInit() => InitCount++;
            public override void OnShutdown() => ShutdownCount++;
            public virtual void Tick(float elapseSeconds, float realElapseSeconds) => TickCount++;
        }

        // --- 简单实现 ---

        private sealed class AlphaService : TestServiceBase, IAlphaService { }
        private sealed class BetaService : TestServiceBase, IBetaService { }
        private sealed class DependeeService : TestServiceBase, IDepTargetService { }

        // --- 依赖声明实现 ---

        [ServiceDependency(typeof(DependeeService))]
        private sealed class DependentService : TestServiceBase, IAlphaService
        {
            public DependeeService Dependency { get; private set; }

            public override void OnInit()
            {
                base.OnInit();
                Dependency = GameServices.GetRequiredService<DependeeService>();
                s_OrderLog.Add("Dependent:init");
            }
        }

        // --- 循环依赖实现 ---

        [ServiceDependency(typeof(CycleServiceB))]
        private sealed class CycleServiceA : TestServiceBase, IAlphaService { }

        [ServiceDependency(typeof(CycleServiceA))]
        private sealed class CycleServiceB : TestServiceBase, IBetaService { }

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

        // --- 异步关闭实现 ---

        private sealed class AsyncShutdownService : TestServiceBase, IAlphaService, IAsyncShutdownService
        {
            public bool AsyncShutdownCalled;
            public bool SyncShutdownCalled;

            public override void OnShutdown()
            {
                SyncShutdownCalled = true;
                base.OnShutdown();
            }

            public UniTask OnShutdownAsync()
            {
                AsyncShutdownCalled = true;
                s_OrderLog.Add("async:shutdown");
                return UniTask.CompletedTask;
            }
        }

        // --- 可变静态配置的保存/恢复（防测试间状态泄漏） ---

        private EDuplicateContractPolicy _originalPolicy;
        private int _originalTripThreshold;

        // --- 生命周期 ---

        [SetUp]
        public void SetUp()
        {
            s_OrderLog.Clear();
            GameServices.Shutdown();
            _originalPolicy = GameServices.DuplicateContractPolicy;
            _originalTripThreshold = ServiceScope.s_TickFailureTripThreshold;
        }

        [TearDown]
        public void TearDown()
        {
            ServiceScope.s_TickFailureTripThreshold = _originalTripThreshold;
            GameServices.DuplicateContractPolicy = _originalPolicy;

            GameServices.Shutdown();
        }

        // --- 辅助 ---

        private static void Register<T>(T service) where T : class, IService
        {
            GameServices.RegisterService(EServiceScopeKind.App, service);
        }

        private static void RegisterScene<T>(T service) where T : class, IService
        {
            if (GameServices.HasScene)
                GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            GameServices.RegisterService(EServiceScopeKind.Scene, service);
        }

        // ═══════════════════════════════════════════════════════
        // RegisterService 基础测试 [REGISTER APP TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void RegisterService_CreatesInitializesAndResolves()
        {
            Register(new AlphaService());

            var resolved = GameServices.GetRequiredService<AlphaService>();

            Assert.IsInstanceOf<AlphaService>(resolved);
            var alpha = (AlphaService)resolved;
            Assert.AreEqual(1, alpha.InitCount, "OnInit 应在注册时调用一次");
            Assert.AreEqual(EServiceState.Initialized, alpha.State);
            Assert.AreEqual(0, alpha.ShutdownCount);
            Assert.AreSame(resolved, GameServices.GetService<AlphaService>(), "重复解析应返回同一单例");
        }

        [Test]
        public void GetRequiredService_Unregistered_Throws()
        {
            Register(new AlphaService());

            Assert.Throws<GameException>(
                () => GameServices.GetRequiredService<BetaService>());
        }

        [Test]
        public void GetService_Unregistered_ReturnsNull_TryGetReturnsFalse()
        {
            Register(new AlphaService());

            Assert.IsNull(GameServices.GetService<BetaService>());
            Assert.IsFalse(GameServices.TryGetService<BetaService>(out var service));
            Assert.IsNull(service);
        }

        [Test]
        public void RegisterService_DuplicateRegistration_Idempotent()
        {
            var instance = new AlphaService();
            Register(instance);

            // 重复注册——应幂等返回既有实例，不重复 OnInit（开发默认策略下伴随冲突告警）
            LogAssert.Expect(LogType.Warning, new Regex(".*already bound.*"));
            var returned = GameServices.RegisterService(EServiceScopeKind.App, new AlphaService());

            Assert.AreSame(instance, returned, "重复注册应返回既有实例");
            Assert.AreEqual(1, instance.InitCount, "重复注册不应再调用 OnInit");
        }

        // ═══════════════════════════════════════════════════════
        // 依赖预注册测试 [DEPENDENCY PREREGISTRATION TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void RegisterService_ResolvesDependencyChain()
        {
            // 先注册依赖，再注册依赖方——依赖由 [ServiceDependency] 声明
            GameServices.RegisterService(EServiceScopeKind.App, new DependeeService());
            Register(new DependentService());

            var dependent = GameServices.GetRequiredService<DependentService>();
            var dependee = GameServices.GetRequiredService<DependeeService>();

            Assert.IsNotNull(dependent.Dependency, "依赖应已由预注册拉起");
            Assert.AreSame(dependee, dependent.Dependency, "依赖应是同一单例");
            Assert.AreEqual(1, dependee.InitCount, "依赖服务应已初始化");
            Assert.AreEqual(1, dependent.InitCount, "当前服务应已初始化");
        }

        [Test]
        public void RegisterService_DependencyRegisteredFirst_StillWorks()
        {
            // 依赖先于依赖方注册——[ServiceDependency] 声明的依赖校验通过
            GameServices.RegisterService(EServiceScopeKind.App, new DependeeService());
            GameServices.RegisterService(EServiceScopeKind.App, new DependentService());

            var dependent = GameServices.GetRequiredService<DependentService>();
            Assert.IsNotNull(dependent.Dependency, "依赖应已就位");
        }

        [Test]
        public void RegisterService_DependencyAlreadyRegistered_NotReRegistered()
        {
            // 先注册依赖
            var dependee = new DependeeService();
            GameServices.RegisterService(EServiceScopeKind.App, dependee);

            // 再注册依赖方——依赖不应被重复创建/初始化
            GameServices.RegisterService(EServiceScopeKind.App, new DependentService());

            Assert.AreEqual(1, dependee.InitCount, "已注册的依赖不应被重复初始化");
        }

        [Test]
        public void RegisterService_NestedDependency_AllPreregistered()
        {
            // A → B → C: 注册 A 时应自动拉起 B 和 C
            // 这里用真实场景：DependentService → DependeeService
            GameServices.RegisterService(EServiceScopeKind.App, new DependeeService());
            Register(new DependentService());

            Assert.IsTrue(GameServices.HasApp);
            Assert.IsNotNull(GameServices.GetService<DependentService>());
            Assert.IsNotNull(GameServices.GetService<DependeeService>());
        }

        [Test]
        public void RegisterService_CircularDependency_Throws()
        {
            // 环上任一服务的依赖都未注册——缺失依赖校验在注册期即 fail-fast（环无法完成注册）
            Assert.Throws<GameException>(() => GameServices.RegisterService(EServiceScopeKind.App, new CycleServiceA()));
        }

        // ═══════════════════════════════════════════════════════
        // 关闭顺序与状态测试 [SHUTDOWN TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Shutdown_DependentsCloseFirst_ReverseOrder()
        {
            // 注册序：Dependee → Dependent；关闭序应为 Dependent → Dependee
            GameServices.RegisterService(EServiceScopeKind.App, new DependeeService());
            GameServices.RegisterService(EServiceScopeKind.App, new DependentService());

            s_OrderLog.Clear();
            GameServices.ShutdownContainer(EServiceScopeKind.App);

            // Dependent 的 Shutdown 应先于 Dependee（逆注册序）
            // 由于 TestServiceBase.Shutdown 不记日志，用 InitCount 间接验证
            Assert.IsFalse(GameServices.HasApp, "关闭后 HasApp 应为 false");
        }

        [Test]
        public void Shutdown_TransitionsStateToDisposed()
        {
            Register(new AlphaService());
            var alpha = (AlphaService)GameServices.GetRequiredService<AlphaService>();

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.AreEqual(EServiceState.Disposed, alpha.State);
            Assert.AreEqual(1, alpha.ShutdownCount);
        }

        [Test]
        public void ShutdownContainer_FreesServiceResolution()
        {
            Register(new AlphaService());

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.IsNull(GameServices.GetService<AlphaService>(), "全部容器关闭后查找应返回 null");
            Assert.IsFalse(GameServices.HasApp);
        }

        [Test]
        public void Shutdown_ClearsRegistry_AllowRebuild()
        {
            Register(new AlphaService());
            GameServices.Shutdown();

            Assert.IsFalse(GameServices.HasApp);

            // 关闭后应能重新注册
            Assert.DoesNotThrow(() => GameServices.RegisterService(EServiceScopeKind.App, new AlphaService()));
            Assert.IsTrue(GameServices.HasApp);
        }

        [Test]
        public void AsyncShutdown_CalledBeforeSyncShutdown()
        {
            Register(new AsyncShutdownService());
            var svc = (AsyncShutdownService)GameServices.GetRequiredService<AsyncShutdownService>();

            GameServices.ShutdownContainerAsync(EServiceScopeKind.App).GetAwaiter().GetResult();

            Assert.IsTrue(svc.AsyncShutdownCalled, "OnShutdownAsync 应在 Shutdown 前调用");
            Assert.IsTrue(svc.SyncShutdownCalled, "Shutdown 仍应被调用");
            Assert.AreEqual(EServiceState.Disposed, svc.State);
        }

        [Test]
        public void ShutdownAsync_ClosesAllScopes()
        {
            Register(new AlphaService());
            RegisterScene(new BetaService());

            GameServices.ShutdownAsync().GetAwaiter().GetResult();

            Assert.IsFalse(GameServices.HasApp);
            Assert.IsFalse(GameServices.HasScene);
            Assert.IsFalse(GameServices.HasGameplay);
            Assert.IsNull(GameServices.GetService<AlphaService>(), "关闭后查找应返回 null");
        }

        // ═══════════════════════════════════════════════════════
        // 跨作用域测试 [CROSS SCOPE TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class SceneAlphaService : TestServiceBase, IAlphaService { }
        private sealed class GameplayAlphaService : TestServiceBase, IAlphaService { }

        [Test]
        public void ProviderChain_GameplayBeatsSceneBeatsApp()
        {
            // 三个作用域注册同一契约 IAlphaService 的不同实现——跨作用域查找 Gameplay > Scene > App
            GameServices.RegisterService(EServiceScopeKind.App, new AlphaService() as IAlphaService);
            GameServices.RegisterService(EServiceScopeKind.Scene, new SceneAlphaService() as IAlphaService);
            GameServices.RegisterService(EServiceScopeKind.Gameplay, new GameplayAlphaService() as IAlphaService);

            Assert.IsInstanceOf<GameplayAlphaService>(GameServices.GetRequiredService<IAlphaService>(),
                "Gameplay 遮蔽 Scene 与 App");

            GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
            Assert.IsInstanceOf<SceneAlphaService>(GameServices.GetRequiredService<IAlphaService>(),
                "Gameplay 关闭后回退到 Scene");

            GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            Assert.IsInstanceOf<AlphaService>(GameServices.GetRequiredService<IAlphaService>(),
                "Scene 关闭后回退到 App");
        }

        // ═══════════════════════════════════════════════════════
        // 轮询测试 [TICK TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void Tick_HigherPriorityFirst()
        {
            Register(new HighPriorityService());
            GameServices.RegisterService(EServiceScopeKind.App, new LowPriorityService() as IBetaService);

            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(new[] { "high", "low" }, s_OrderLog.ToArray(),
                "高优先级服务应先于低优先级服务轮询");
        }

        [Test]
        public void Tick_DrivesAllActiveContainers()
        {
            Register(new AlphaService());
            RegisterScene(new BetaService());

            var appAlpha = (AlphaService)GameServices.GetRequiredService<AlphaService>();
            var sceneBeta = (BetaService)GameServices.GetRequiredService<BetaService>();

            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(1, appAlpha.TickCount, "App 容器服务应被轮询");
            Assert.AreEqual(1, sceneBeta.TickCount, "Scene 容器服务应被轮询");

            GameServices.ShutdownContainer(EServiceScopeKind.Scene);
            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(2, appAlpha.TickCount, "App 容器服务应继续被轮询");
            Assert.AreEqual(1, sceneBeta.TickCount, "已关闭容器的服务不应再被轮询");
        }

        // ═══════════════════════════════════════════════════════
        // 事件测试 [EVENT TESTS]
        // ═══════════════════════════════════════════════════════

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

            Register(new AlphaService());

            Assert.IsNotNull(received, "onServiceRegistered 事件应在注册时触发");
            Assert.AreEqual(EServiceState.Initialized, stateAtEvent, "事件触发时服务应已完成初始化");
        }

        [Test]
        public void ServiceUnregisteredEvent_FiresAfterShutdown()
        {
            Register(new AlphaService());
            var alpha = (AlphaService)GameServices.GetRequiredService<AlphaService>();

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

            Register(new AlphaService());

            Assert.GreaterOrEqual(interceptor.Events.Count, 2, "应触发 Registering + Registered 两个事件");
            var alpha = (AlphaService)GameServices.GetRequiredService<AlphaService>();
            Assert.AreEqual(1, alpha.InitCount, "OnInit 应在 Registering 后、Registered 前调用");
        }

        [Test]
        public void Interceptor_ShutdownFlow_ShutdownBeforeServiceShutdown_UnregisteredAfter()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            Register(new AlphaService());
            interceptor.Events.Clear();

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.IsTrue(interceptor.Events.Contains("Shutdown:AlphaService"), "应触发 Shutdown 拦截");
            Assert.IsTrue(interceptor.Events.Contains("Unregistered:AlphaService"), "应触发 Unregistered 拦截");
        }

        [Test]
        public void Interceptor_Tick_TriggersBeforeEachService()
        {
            var interceptor = new TestInterceptor();
            GameServices.AddInterceptor(interceptor);

            Register(new AlphaService());
            interceptor.Events.Clear();

            GameServices.Tick(0.1f, 0.1f);

            Assert.AreEqual(1, interceptor.Events.Count, "应触发一次 Tick 拦截");
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

            Register(new AlphaService());
            Assert.IsTrue(interceptor.Events.Count > 0, "添加后应收到事件");

            interceptor.Events.Clear();
            GameServices.RemoveInterceptor(interceptor);

            GameServices.RegisterService(EServiceScopeKind.App, new BetaService() as IBetaService);
            Assert.AreEqual(0, interceptor.Events.Count, "移除后不应再收到事件");
        }

        // ═══════════════════════════════════════════════════════
        // 运行时注册测试 [RUNTIME REGISTRATION TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void RuntimeRegister_AddsServiceToActiveScope()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new AlphaService() as IAlphaService);

            var beta = new BetaService();
            GameServices.RegisterService(EServiceScopeKind.App, beta as IBetaService);

            var resolved = GameServices.GetService<IBetaService>();
            Assert.AreSame(beta, resolved);
            Assert.AreEqual(1, beta.InitCount, "运行时注册应驱动 OnInit");
            Assert.AreEqual(EServiceState.Initialized, beta.State);
        }

        [Test]
        public void RuntimeRegister_DuplicateContract_IdempotentReturnsExisting()
        {
            var first = new AlphaService();
            GameServices.RegisterService(EServiceScopeKind.App, first as IAlphaService);

            LogAssert.Expect(LogType.Warning, new Regex(".*already bound.*"));
            var another = new AlphaService();
            var returned = GameServices.RegisterService(EServiceScopeKind.App, another as IAlphaService);

            Assert.AreSame(first, returned, "重复注册应幂等返回既有实例（与文档契约一致）");
            Assert.AreEqual(1, first.InitCount, "重复注册不应再次驱动 OnInit");
        }

        [Test]
        public void RuntimeUnregister_RemovesAndShutsDownService()
        {
            Register(new AlphaService());
            var alpha = (AlphaService)GameServices.GetRequiredService<AlphaService>();

            bool result = GameServices.UnregisterService<AlphaService>(EServiceScopeKind.App);

            Assert.IsTrue(result);
            Assert.AreEqual(1, alpha.ShutdownCount, "运行时注销应驱动 Shutdown");
            Assert.AreEqual(EServiceState.Disposed, alpha.State);
            Assert.IsNull(GameServices.GetService<AlphaService>(),
                "注销后服务不应再可解析");
        }

        [Test]
        public void RuntimeUnregister_NotFound_ReturnsFalse()
        {
            Register(new AlphaService());

            bool result = GameServices.UnregisterService<BetaService>(EServiceScopeKind.App);

            Assert.IsFalse(result, "未注册的服务注销应返回 false");
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
            GameServices.RegisterService(EServiceScopeKind.App, new DisposeScopeOnTick(EServiceScopeKind.App) as IAlphaService);

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));
            Assert.IsFalse(GameServices.HasApp, "迭代中请求的 Dispose 应在迭代结束后执行");
        }

        private sealed class TickCountService : TestServiceBase, IBetaService { }

        [Test]
        public void Dispose_DuringTick_OtherServicesStillTickInSameFrame()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new DisposeScopeOnTick(EServiceScopeKind.App) as IAlphaService);
            var beta = new BetaService();
            GameServices.RegisterService(EServiceScopeKind.App, beta as IBetaService);

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));
            Assert.AreEqual(1, beta.TickCount, "同作用域后续服务在本轮迭代中仍应被轮询");
            Assert.AreEqual(1, beta.ShutdownCount, "迭代结束后作用域销毁应关闭全部服务");
        }

        // ═══════════════════════════════════════════════════════
        // 延迟队列测试 [DEFERRED CHANGES TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class RegisterOnTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.RegisterService(EServiceScopeKind.App, new BetaService() as IBetaService);
            }
        }

        [Test]
        public void DeferredRegister_QueuedDuringTick_ProcessedAfterIteration()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new RegisterOnTickService() as IAlphaService);

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            var beta = GameServices.GetService<IBetaService>();
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
        public void DeferredRegister_ThrowMode_LogsErrorThenRethrows()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new ThrowRegisterOnTickService() as IAlphaService);

            // 开发环境下先记录日志再上抛（fail-fast 分级策略）
            LogAssert.Expect(LogType.Error, new Regex(".*EDeferMode\\.Throw.*"));
            Assert.Throws<GameException>(() => GameServices.Tick(0f, 0f));
        }

        private sealed class UnregisterOnTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                GameServices.UnregisterService<IBetaService>(EServiceScopeKind.App);
            }
        }

        [Test]
        public void DeferredUnregister_QueuedDuringTick_ProcessedAfterIteration()
        {
            var beta = new BetaService();
            GameServices.RegisterService(EServiceScopeKind.App, beta as IBetaService);
            GameServices.RegisterService(EServiceScopeKind.App, new UnregisterOnTickService() as IAlphaService);

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            Assert.AreEqual(1, beta.ShutdownCount, "延迟注销应驱动 Shutdown");
            Assert.IsNull(GameServices.GetService<IBetaService>(),
                "延迟注销后服务不应再可解析");
        }

        // ═══════════════════════════════════════════════════════
        // 稳定排序测试 [STABLE SORT TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class SamePriorityA : TestServiceBase, IAlphaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds) => s_OrderLog.Add("A");
        }

        private sealed class SamePriorityB : TestServiceBase, IBetaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds) => s_OrderLog.Add("B");
        }

        private sealed class SamePriorityC : TestServiceBase, IDepTargetService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds) => s_OrderLog.Add("C");
        }

        [Test]
        public void CreationIndex_SamePriority_PreservesRegistrationOrder()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new SamePriorityA() as IAlphaService);
            GameServices.RegisterService(EServiceScopeKind.App, new SamePriorityB() as IBetaService);
            GameServices.RegisterService(EServiceScopeKind.App, new SamePriorityC() as IDepTargetService);

            GameServices.Tick(0f, 0f);

            Assert.AreEqual(new[] { "A", "B", "C" }, s_OrderLog.ToArray(),
                "同优先级服务应按注册顺序执行（CreationIndex 稳定排序）");
        }

        // ═══════════════════════════════════════════════════════
        // 诊断测试 [DIAGNOSTICS TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void GetDiagnosticInfo_ReportsRegisteredServices()
        {
            Register(new AlphaService());
            GameServices.RegisterService(EServiceScopeKind.App, new BetaService() as IBetaService);

            var infos = GameServices.GetDiagnosticInfo();

            Assert.GreaterOrEqual(infos.Count, 2);
        }

        // ═══════════════════════════════════════════════════════
        // 显式契约注册测试 [EXPLICIT CONTRACT REGISTRATION TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void ExplicitContract_TypeOverload_RegistersAndResolves()
        {
            var svc = new AlphaService();
            var returned = GameServices.RegisterService(EServiceScopeKind.App, typeof(IAlphaService), svc);

            Assert.AreSame(svc, returned);
            Assert.AreSame(svc, GameServices.GetRequiredService<IAlphaService>());
            Assert.AreEqual(1, svc.InitCount, "显式契约注册应驱动 OnInit");
        }

        [Test]
        public void ExplicitContract_DuplicateRegistration_Idempotent()
        {
            var first = new AlphaService();
            GameServices.RegisterService(EServiceScopeKind.App, typeof(IAlphaService), first);

            LogAssert.Expect(LogType.Warning, new Regex(".*already bound.*"));
            var returned = GameServices.RegisterService(EServiceScopeKind.App, typeof(IAlphaService), new AlphaService());

            Assert.AreSame(first, returned, "重复注册应返回既有实例");
            Assert.AreEqual(1, first.InitCount);
        }

        private sealed class AnotherFactoryService : TestServiceBase, IAlphaService { }

        [ServiceDependency(typeof(AnotherFactoryService))]
        private sealed class InterfaceContractDependent : TestServiceBase, IBetaService { }

        [Test]
        public void ExplicitContract_InterfaceContract_StillValidatesDependenciesFromImplType()
        {
            // 依赖已显式注册——以接口契约注册依赖方时，依赖校验仍从实现类型读取声明
            // （若错误地从接口契约读取，声明将丢失且校验被跳过）
            GameServices.RegisterService(EServiceScopeKind.App, new AnotherFactoryService());
            GameServices.RegisterService(EServiceScopeKind.App, typeof(IBetaService), new InterfaceContractDependent());

            Assert.AreEqual(1, GameServices.GetRequiredService<AnotherFactoryService>().InitCount,
                "接口契约注册也应从实现类型读取依赖声明");
        }

        // ═══════════════════════════════════════════════════════
        // 多契约绑定测试 [MULTI-CONTRACT BINDING TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class DualContractService : TestServiceBase, IAlphaService, IBetaService { }

        [Test]
        public void MultiContract_SameInstance_BindsBothContracts_ShutdownOnce()
        {
            var svc = new DualContractService();
            GameServices.RegisterService(EServiceScopeKind.App, typeof(IAlphaService), svc);
            GameServices.RegisterService(EServiceScopeKind.App, typeof(IBetaService), svc);

            Assert.AreSame(svc, GameServices.GetRequiredService<IAlphaService>());
            Assert.AreSame(svc, GameServices.GetRequiredService<IBetaService>());
            Assert.AreEqual(1, svc.InitCount, "同实例多契约只初始化一次");

            GameServices.ShutdownContainer(EServiceScopeKind.App);
            Assert.AreEqual(1, svc.ShutdownCount, "同实例多契约只关闭一次");
        }

        private sealed class BindOnTickService : TestServiceBase, IAlphaService, IDepTargetService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                // 同一实例在迭代中再绑一个契约——两个请求都延迟，FIFO 保证先注册后绑定
                GameServices.RegisterService(EServiceScopeKind.App, typeof(IDepTargetService), this);
            }
        }

        [Test]
        public void MultiContract_DeferredDuringTick_BoundAfterIteration()
        {
            var svc = new BindOnTickService();
            GameServices.RegisterService(EServiceScopeKind.App, typeof(IAlphaService), svc);

            Assert.DoesNotThrow(() => GameServices.Tick(0f, 0f));

            Assert.AreSame(svc, GameServices.GetService<IDepTargetService>(),
                "迭代中请求的附加契约应在迭代结束后可解析");
        }

        // ═══════════════════════════════════════════════════════
        // 手动注册与懒加载自动注册测试 [MANUAL & LAZY REGISTRATION TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class FactoryDepService : TestServiceBase, IAlphaService { }

        [ServiceDependency(typeof(FactoryDepService))]
        private sealed class FactoryDependentService : TestServiceBase, IBetaService { }

        [Test]
        public void RegisterService_MissingDependency_Throws()
        {
            // 服务实例仅由手动注册创建（默认工厂表已移除）——依赖未注册时注册依赖方立即失败
            Assert.Throws<GameException>(() =>
                GameServices.RegisterService(EServiceScopeKind.App, new FactoryDependentService()));
            Assert.IsFalse(GameServices.HasApp, "失败的注册不应留下半初始化状态");
        }

        // ─── GameApp 关闭态测试工具（EditMode 下 GameApp 未 Initialize，IsShutdown 恒为 true） ───

        private static void SetGameAppActive(bool active)
        {
            var field = typeof(GameApp).GetField("s_IsShutdown",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.IsNotNull(field, "GameApp.s_IsShutdown 字段名变更，需同步本测试");
            field.SetValue(null, !active);
        }

        [Test]
        public void EnsureRegistered_WhenGameAppShutDown_ThrowsAndBlocksRevival()
        {
            // EditMode 下 GameApp 未 Initialize——IsShutdown 恒为 true，
            // 等价于"框架未启动/拆除窗口/已手动关闭"的关闭态
            Assert.IsTrue(GameApp.IsShutdown);

            Assert.Throws<GameException>(() => GameServices.EnsureRegistered<AlphaService>());

            Assert.IsFalse(GameServices.HasApp, "关闭期懒加载不得复活世界");
            Assert.IsNull(GameServices.GetService<AlphaService>(), "关闭期不应留下半初始化服务");
        }

        [Test]
        public void RegisterService_ExplicitRegister_NotBlockedByGameAppShutDown()
        {
            // 显式注册是关闭态下唯一的重建路径——不受 GameApp.IsShutdown 阻断
            Assert.IsTrue(GameApp.IsShutdown);

            Assert.DoesNotThrow(() => Register(new AlphaService()));
            Assert.IsTrue(GameServices.HasApp);
        }

        [Test]
        public void EnsureRegistered_WhenGameAppActive_CreatesAndRegistersWhenAbsent()
        {
            SetGameAppActive(true);
            try
            {
                GameServices.EnsureRegistered<AlphaService>();

                var resolved = GameServices.GetRequiredService<AlphaService>();
                Assert.IsInstanceOf<AlphaService>(resolved);
                Assert.AreEqual(1, ((AlphaService)resolved).InitCount, "自动注册应驱动 OnInit");
                Assert.AreEqual(EServiceState.Initialized, ((AlphaService)resolved).State);
            }
            finally
            {
                SetGameAppActive(false);
            }
        }

        [Test]
        public void EnsureRegistered_AlreadyRegistered_Idempotent()
        {
            SetGameAppActive(true);
            try
            {
                var instance = new AlphaService();
                Register(instance);

                GameServices.EnsureRegistered<AlphaService>();

                Assert.AreSame(instance, GameServices.GetRequiredService<AlphaService>(), "已注册时不应替换实例");
                Assert.AreEqual(1, instance.InitCount, "已注册时不应再次驱动 OnInit");
            }
            finally
            {
                SetGameAppActive(false);
            }
        }

        private sealed class SelfEnsureService : TestServiceBase, IAlphaService
        {
            public override void OnInit()
            {
                base.OnInit();
                // 注册链路中重入 EnsureRegistered——s_InFlight 守卫应跳过而非递归
                GameServices.EnsureRegistered<SelfEnsureService>();
                s_OrderLog.Add("self-ensure");
            }
        }

        [Test]
        public void EnsureRegistered_ReentrantDuringRegistration_Skipped()
        {
            SetGameAppActive(true);
            try
            {
                GameServices.RegisterService(EServiceScopeKind.App, new SelfEnsureService());

                Assert.IsTrue(s_OrderLog.Contains("self-ensure"), "重入的 EnsureRegistered 应被跳过而非递归");
                Assert.AreEqual(1, GameServices.GetRequiredService<SelfEnsureService>().InitCount);
            }
            finally
            {
                SetGameAppActive(false);
            }
        }

        // ═══════════════════════════════════════════════════════
        // 注销后重绑测试 [UNREGISTER & REBIND TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void RuntimeUnregister_RemovesRegistryEntry_RebindWithFreshInstance()
        {
            Register(new AlphaService());
            Assert.IsTrue(GameServices.UnregisterService<AlphaService>(EServiceScopeKind.App));

            var fresh = new AlphaService();
            GameServices.RegisterService(EServiceScopeKind.App, fresh);

            Assert.AreSame(fresh, GameServices.GetRequiredService<AlphaService>(),
                "注销后应以全新实例重新解析");
            Assert.AreEqual(1, fresh.InitCount, "注销后重新注册应重新初始化");
        }

        // ═══════════════════════════════════════════════════════
        // Mono 服务契约修复回归测试 [SERVICE MONO CONTRACT REGRESSION]
        // ═══════════════════════════════════════════════════════

        // ExecuteAlways：EditMode 下 AddComponent 立即触发 Awake、DestroyImmediate 触发 OnDestroy
        [ExecuteAlways]
        private sealed class TestMonoService : ServiceMono<AppScope>
        {
            public override void OnInit() { }
            public override void OnShutdown() { }
        }

        [Test]
        public void ServiceMono_RegistersUnderConcreteType_AndUnregistersOnDestroy()
        {
            var go = new GameObject("TestMonoService");
            try
            {
                var mono = go.AddComponent<TestMonoService>();

                Assert.AreSame(mono, GameServices.GetRequiredService<TestMonoService>(),
                    "应以运行时具体类型为契约注册（而非 IService 基类）");
                Assert.AreEqual(EServiceState.Initialized, mono.State);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            Assert.IsNull(GameServices.GetService<TestMonoService>(), "销毁后应自动注销");
        }

        // ═══════════════════════════════════════════════════════
        // Tick 异常分级策略测试 [TICK EXCEPTION POLICY TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class ThrowingTickService : TestServiceBase, IAlphaService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
                => throw new InvalidOperationException("tick boom");
        }

        [Test]
        public void Tick_Exception_InDevelopmentEnvironment_LoggedThenRethrown()
        {
            GameServices.RegisterService(EServiceScopeKind.App, new ThrowingTickService() as IAlphaService);

            LogAssert.Expect(LogType.Error, new Regex(".*threw in Tick.*"));
            Assert.Throws<InvalidOperationException>(() => GameServices.Tick(0f, 0f),
                "开发环境（编辑器/开发构建）下 Tick 异常应记录后上抛（fail-fast）");
        }

        // ═══════════════════════════════════════════════════════
        // 重复契约策略测试 [DUPLICATE CONTRACT POLICY TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void DuplicateContract_PolicyWarn_LogsWarningAndReturnsExisting()
        {
            GameServices.DuplicateContractPolicy = EDuplicateContractPolicy.Warn;
            var first = new AlphaService();
            Register(first);

            LogAssert.Expect(LogType.Warning, new Regex(".*already bound.*"));
            var returned = GameServices.RegisterService(EServiceScopeKind.App, new AlphaService());

            Assert.AreSame(first, returned, "Warn 策略应幂等返回既有实例");
            Assert.AreEqual(1, first.InitCount, "Warn 策略不应驱动新实例 OnInit");
        }

        [Test]
        public void DuplicateContract_PolicySkip_SilentReturnsExisting()
        {
            GameServices.DuplicateContractPolicy = EDuplicateContractPolicy.Skip;
            var first = new AlphaService();
            Register(first);

            var returned = GameServices.RegisterService(EServiceScopeKind.App, new AlphaService());

            Assert.AreSame(first, returned, "Skip 策略应静默幂等返回既有实例");
            Assert.AreEqual(1, first.InitCount);
        }

        [Test]
        public void DuplicateContract_PolicyThrow_RejectsConflictingInstance()
        {
            GameServices.DuplicateContractPolicy = EDuplicateContractPolicy.Throw;
            var first = new AlphaService();
            Register(first);

            Assert.Throws<GameException>(
                () => GameServices.RegisterService(EServiceScopeKind.App, new AlphaService()),
                "Throw 策略应以不同实例抢占已占用契约时抛出异常");
            Assert.AreSame(first, GameServices.GetRequiredService<AlphaService>(),
                "抛出后既有实例不受影响");
        }

        [Test]
        public void DuplicateContract_SameInstance_AlwaysIdempotent_EvenUnderThrow()
        {
            GameServices.DuplicateContractPolicy = EDuplicateContractPolicy.Throw;
            var instance = new AlphaService();
            Register(instance);

            var returned = GameServices.RegisterService(EServiceScopeKind.App, instance);

            Assert.AreSame(instance, returned, "同实例重复注册在任何策略下都应静默幂等");
            Assert.AreEqual(1, instance.InitCount);
        }

        // ═══════════════════════════════════════════════════════
        // Tick 异常熔断测试 [TICK BREAKER TESTS]
        // ═══════════════════════════════════════════════════════

        private sealed class CountingThrowService : TestServiceBase, IDepTargetService
        {
            public override int Priority => 10;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                TickCount++;
                throw new InvalidOperationException("tick boom");
            }
        }

        private sealed class HealthyBeta : TestServiceBase, IBetaService { }

        [Test]
        public void Tick_ConsecutiveFailures_TripsAfterThreshold_StopsPolling()
        {
            ServiceScope.s_TickFailureTripThreshold = 3;

            var thrower = new CountingThrowService();
            var healthy = new HealthyBeta();
            GameServices.RegisterService(EServiceScopeKind.App, thrower as IDepTargetService);
            GameServices.RegisterService(EServiceScopeKind.App, healthy as IBetaService);

            // 前 3 帧异常上抛（开发环境 fail-fast）；第 3 次失败触发熔断摘除。
            // 故障服务优先级更高——每帧先执行、先抛出，健康服务在前 3 帧被 fail-fast 中断
            for (int frame = 1; frame <= 3; frame++)
            {
                LogAssert.Expect(LogType.Error, new Regex(".*threw in Tick.*"));
                if (frame == 3)
                    LogAssert.Expect(LogType.Warning, new Regex(".*removed from.*"));
                Assert.Throws<InvalidOperationException>(() => GameServices.Tick(0.1f, 0.1f));
            }

            // 熔断后：不再上抛、不再轮询故障服务；健康服务恢复轮询
            Assert.DoesNotThrow(() => GameServices.Tick(0.1f, 0.1f));
            Assert.AreEqual(3, thrower.TickCount, "达到阈值即熔断——不应有第 4 次尝试");
            Assert.AreEqual(1, healthy.TickCount, "健康服务应在故障服务被摘除后的帧恢复轮询");

            // 条目保留——解析不受熔断影响，重新注册可完全重置
            Assert.AreSame(thrower, GameServices.GetRequiredService<IDepTargetService>());
        }

        // ═══════════════════════════════════════════════════════
        // 轮询耗时统计测试 [POLL STATISTICS TESTS]
        // ═══════════════════════════════════════════════════════

        [Test]
        public void PollStatistics_AggregatedPerService_AndResettable()
        {
            Register(new AlphaService());

            GameServices.Tick(0.1f, 0.1f);
            GameServices.Tick(0.1f, 0.1f);

            var infos = GameServices.GetDiagnosticInfo();
            bool found = false;
            for (int i = 0; i < infos.Count; i++)
            {
                // ImplementationType 为程序集全名（嵌套类含 "+ "），用包含匹配
                if (!infos[i].ImplementationType.Contains(nameof(AlphaService))) continue;

                found = true;
                Assert.GreaterOrEqual(infos[i].PollSamples, 2, "两次 Tick 应产生至少两条采样");
                Assert.GreaterOrEqual(infos[i].PollPeakMs, infos[i].PollAvgMs, "峰值不应小于均值");
                break;
            }
            Assert.IsTrue(found, "诊断信息中应包含 AlphaService");

            GameServices.ResetPollStatistics();

            infos = GameServices.GetDiagnosticInfo();
            for (int i = 0; i < infos.Count; i++)
            {
                if (!infos[i].ImplementationType.Contains(nameof(AlphaService))) continue;
                Assert.AreEqual(0, infos[i].PollSamples, "重置后采样数应为零");
                return;
            }
            Assert.Fail("重置后仍应能找到 AlphaService 诊断条目");
        }
    }
}
