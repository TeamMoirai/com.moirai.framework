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
    public class ServiceSystemTest
    {
        // --- 测试用接口 ---

        private interface IAlphaService { }
        private interface IBetaService { }
        private interface IGammaService { }

        // --- 测试用服务基类 ---

        private abstract class TestServiceBase : Service, IServiceTickable
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
            public override ServiceScope Scope => ServiceScope.Scene;
        }

        private sealed class GameplayService : TestServiceBase, IAlphaService
        {
            public override ServiceScope Scope => ServiceScope.Gameplay;
        }

        private sealed class BetaService : TestServiceBase, IBetaService { }

        private sealed class SceneBetaService : TestServiceBase, IBetaService
        {
            public override ServiceScope Scope => ServiceScope.Scene;
        }

        [SetUp]
        public void SetUp()
        {
            ServiceSystem.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            ServiceSystem.Shutdown();
        }

        // --- 注册与获取 ---

        [Test]
        public void RegisterService_ThenGetService_ReturnsSameInstance()
        {
            var service = new AppService();
            var registered = ServiceSystem.RegisterService<IAlphaService>(service);
            var fetched = ServiceSystem.GetService<IAlphaService>();

            Assert.AreSame(service, registered);
            Assert.AreSame(service, fetched);
            Assert.AreEqual(1, service.InitCount, "OnInit 应在注册时调用一次");
        }

        [Test]
        public void RegisterService_DuplicateSameScope_ReturnsExistingInstance()
        {
            var first = new AppService();
            var second = new AppService();

            var result = ServiceSystem.RegisterService<IAlphaService>(first);
            var duplicate = ServiceSystem.RegisterService<IAlphaService>(second);

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
            ServiceSystem.RegisterService<IAlphaService>(app);
            ServiceSystem.RegisterService<IAlphaService>(scene);
            ServiceSystem.RegisterService<IAlphaService>(gameplay);

            Assert.AreSame(gameplay, ServiceSystem.GetService<IAlphaService>());

            ServiceSystem.ShutdownScope(ServiceScope.Gameplay);
            Assert.AreSame(scene, ServiceSystem.GetService<IAlphaService>(), "Gameplay 注销后应回退到 Scene");

            ServiceSystem.ShutdownScope(ServiceScope.Scene);
            Assert.AreSame(app, ServiceSystem.GetService<IAlphaService>(), "Scene 注销后应回退到 App");
        }

        [Test]
        public void RegisterService_SameInterfaceDifferentScopes_Allowed()
        {
            var app = new AppService();
            var scene = new SceneService();
            var gameplay = new GameplayService();

            ServiceSystem.RegisterService<IAlphaService>(app);
            ServiceSystem.RegisterService<IAlphaService>(scene);
            ServiceSystem.RegisterService<IAlphaService>(gameplay);

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

            ServiceSystem.RegisterService<IAlphaService>(appA);
            ServiceSystem.RegisterService<IBetaService>(betaApp);
            ServiceSystem.RegisterService<IAlphaService>(sceneA);
            ServiceSystem.RegisterService<IBetaService>(betaScene);

            ServiceSystem.ShutdownScope(ServiceScope.Scene);

            Assert.AreEqual(1, sceneA.ShutdownCount, "Scene 服务应被关闭");
            Assert.AreEqual(1, betaScene.ShutdownCount, "Scene 服务应被关闭");
            Assert.AreEqual(0, appA.ShutdownCount, "App 服务不应被关闭");
            Assert.AreEqual(0, betaApp.ShutdownCount, "App 服务不应被关闭");

            ServiceSystem.Tick(0f, 0f);
            Assert.AreEqual(1, appA.TickCount, "App 服务关闭后仍应正常轮询");
            Assert.AreEqual(1, betaApp.TickCount, "App 服务关闭后仍应正常轮询");
            Assert.AreEqual(0, sceneA.TickCount, "已注销的 Scene 服务不应被轮询");
        }

        [Test]
        public void ShutdownScope_RemovedService_NoLongerTicks()
        {
            var scene = new SceneService();
            ServiceSystem.RegisterService<IAlphaService>(scene);

            ServiceSystem.Tick(0f, 0f);
            Assert.AreEqual(1, scene.TickCount);

            ServiceSystem.ShutdownScope(ServiceScope.Scene);
            ServiceSystem.Tick(0f, 0f);

            Assert.AreEqual(1, scene.TickCount, "注销后的服务不应再被轮询");
        }

        // --- 迭代安全：注册 ---

        [Test]
        public void Update_RegisterDuringIteration_AppliedAfterFlush()
        {
            var registrar = new DeferredRegistrar();
            ServiceSystem.RegisterService<IBetaService>(registrar);

            ServiceSystem.Tick(0f, 0f);

            Assert.AreEqual(1, registrar.TickCount);
            Assert.IsNotNull(ServiceSystem.GetService<IAlphaService>(), "迭代中注册的服务应在迭代结束后生效");
            Assert.AreEqual(1, registrar.Spawned.InitCount, "延迟注册生效时应调用 OnInit");
            // 迭代内 count 已捕获，新服务本轮不 tick；下一轮开始 tick
            Assert.AreEqual(0, registrar.Spawned.TickCount);

            ServiceSystem.Tick(0f, 0f);
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
                // 首次 tick 时在迭代内注册新服务（不能在此处调用 GetService 探测：未注册会触发反射回退）
                if (!_spawned)
                {
                    _spawned = true;
                    ServiceSystem.RegisterService<IAlphaService>(Spawned);
                }
            }
        }

        // --- 迭代安全：注销 ---

        [Test]
        public void Update_UnregisterDuringIteration_AppliedAfterFlush()
        {
            var victim = new AppService();
            var killer = new UnregisterOnTick(victim);
            ServiceSystem.RegisterService<IAlphaService>(victim);
            ServiceSystem.RegisterService<IBetaService>(killer);

            ServiceSystem.Tick(0f, 0f);

            Assert.AreEqual(1, victim.ShutdownCount, "迭代中注销的服务应在迭代结束后关闭");

            ServiceSystem.Tick(0f, 0f);
            Assert.AreEqual(1, victim.TickCount, "被注销的服务不应再被轮询");
            Assert.AreEqual(2, killer.TickCount, "其余服务应继续正常轮询");
        }

        private sealed class UnregisterOnTick : TestServiceBase, IBetaService
        {
            private readonly Service _victim;

            public UnregisterOnTick(Service victim) => _victim = victim;

            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                ServiceSystem.UnregisterService(_victim);
            }
        }

        // --- 迭代安全：ShutdownScope ---

        [Test]
        public void ShutdownScope_DuringIteration_DefersRemoval()
        {
            var scene = new SceneService();
            var trigger = new ShutdownScopeOnTick();
            ServiceSystem.RegisterService<IAlphaService>(scene);
            ServiceSystem.RegisterService<IBetaService>(trigger);

            ServiceSystem.Tick(0f, 0f);

            Assert.AreEqual(1, scene.ShutdownCount, "迭代中的 ShutdownScope 应延迟到迭代结束后应用");
            // scene 先于 trigger 注册（同优先级按注册顺序），本轮已被 tick 一次后才被延迟移除
            Assert.AreEqual(1, scene.TickCount, "延迟注销的服务本轮已 tick（迭代内 count 捕获）");

            ServiceSystem.Tick(0f, 0f);
            Assert.AreEqual(1, scene.TickCount, "被注销的服务不应再被轮询");
        }

        private sealed class ShutdownScopeOnTick : TestServiceBase, IBetaService
        {
            public override void Tick(float elapseSeconds, float realElapseSeconds)
            {
                base.Tick(elapseSeconds, realElapseSeconds);
                ServiceSystem.ShutdownScope(ServiceScope.Scene);
            }
        }

        // --- 注销 API ---

        [Test]
        public void UnregisterService_ByInterface_RemovesAndShutsDown()
        {
            var service = new AppService();
            ServiceSystem.RegisterService<IAlphaService>(service);

            bool result = ServiceSystem.UnregisterService<IAlphaService>();

            Assert.IsTrue(result);
            Assert.AreEqual(1, service.ShutdownCount);
        }

        [Test]
        public void UnregisterService_ByInstance_RemovesAndShutsDown()
        {
            var service = new AppService();
            ServiceSystem.RegisterService<IAlphaService>(service);

            bool result = ServiceSystem.UnregisterService(service);

            Assert.IsTrue(result);
            Assert.AreEqual(1, service.ShutdownCount);
        }

        [Test]
        public void UnregisterService_NotRegistered_ReturnsFalse()
        {
            Assert.IsFalse(ServiceSystem.UnregisterService(new AppService()));
        }

        // --- Shutdown 健壮性 ---

        [Test]
        public void Shutdown_ServiceThrows_DoesNotAbortOtherServices()
        {
            var thrower = new ThrowingService();
            var normal = new BetaService();
            ServiceSystem.RegisterService<IAlphaService>(thrower);
            ServiceSystem.RegisterService<IBetaService>(normal);

            // 框架会记录服务关闭异常（Error 日志），声明预期
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*InvalidOperationException: test.*"));

            Assert.DoesNotThrow(() => ServiceSystem.Shutdown());
            Assert.AreEqual(1, normal.ShutdownCount, "异常服务之后的服务仍应被关闭");
        }

        private sealed class ThrowingService : TestServiceBase, IAlphaService
        {
            public override void Shutdown() => throw new System.InvalidOperationException("test");
        }

        [Test]
        public void Shutdown_ClearsAllScopes_InReverseOrder()
        {
            var app = new AppService();
            var scene = new SceneService();
            var gameplay = new GameplayService();
            ServiceSystem.RegisterService<IAlphaService>(app);
            ServiceSystem.RegisterService<IAlphaService>(scene);
            ServiceSystem.RegisterService<IAlphaService>(gameplay);

            var expected = new List<Service> { gameplay, scene, app };
            ServiceSystem.Shutdown();

            Assert.AreEqual(1, app.ShutdownCount);
            Assert.AreEqual(1, scene.ShutdownCount);
            Assert.AreEqual(1, gameplay.ShutdownCount);
            CollectionAssert.AreEquivalent(expected, new Service[] { gameplay, scene, app });
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
            ServiceSystem.RegisterService<IAlphaService>(low);
            ServiceSystem.RegisterService<IBetaService>(high);
            ServiceSystem.RegisterService<IGammaService>(mid);

            ServiceSystem.Tick(0f, 0f);

            CollectionAssert.AreEqual(new[] { "high", "mid", "low" }, order);
        }

        private sealed class OrderedService : Service, IServiceTickable, IAlphaService, IBetaService, IGammaService
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

        [Test]
        public void InitializeAsync_CallsOnInitAsync_OnAsyncInitServices()
        {
            var asyncService = new AsyncInitTestService();
            ServiceSystem.RegisterService<IAlphaService>(asyncService);

            ServiceSystem.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, asyncService.InitCount, "OnInit 同步调用");
            Assert.AreEqual(1, asyncService.AsyncInitCount, "OnInitAsync 异步调用");
        }

        [Test]
        public void InitializeAsync_SkipsNonAsyncInitServices()
        {
            var syncOnly = new AppService();
            var asyncOne = new AsyncInitTestService();
            ServiceSystem.RegisterService<IAlphaService>(syncOnly);
            ServiceSystem.RegisterService<IBetaService>(asyncOne);

            ServiceSystem.InitializeAsync().GetAwaiter().GetResult();

            Assert.AreEqual(1, syncOnly.InitCount, "同步服务 OnInit 被调用");
            Assert.AreEqual(1, asyncOne.InitCount, "异步服务同步 OnInit 也被调用");
            Assert.AreEqual(1, asyncOne.AsyncInitCount, "OnInitAsync 被调用");
        }

        private sealed class AsyncInitTestService : TestServiceBase, IAlphaService, IBetaService, IAsyncInitService
        {
            public int AsyncInitCount;
            public async UniTask OnInitAsync()
            {
                await UniTask.Yield();
                AsyncInitCount++;
            }
        }

        // --- 服务事件 [SERVICE EVENTS] ---

        [Test]
        public void ServiceRegistered_EventFires_AfterOnInit()
        {
            var registered = new List<(IService service, Type interfaceType, ServiceScope scope)>();
            ServiceSystem.ServiceRegistered += (s, t, sc) => registered.Add((s, t, sc));

            var service = new AppService();
            ServiceSystem.RegisterService<IAlphaService>(service);

            Assert.AreEqual(1, registered.Count, "事件应触发一次");
            Assert.AreSame(service, registered[0].service);
            Assert.AreEqual(typeof(IAlphaService), registered[0].interfaceType);
            Assert.AreEqual(ServiceScope.App, registered[0].scope);
            Assert.AreEqual(1, service.InitCount, "OnInit 应在事件触发前完成");
        }

        [Test]
        public void ServiceUnregistered_EventFires_AfterShutdown()
        {
            var unregistered = new List<IService>();
            ServiceSystem.ServiceUnregistered += s => unregistered.Add(s);

            var service = new AppService();
            ServiceSystem.RegisterService<IAlphaService>(service);
            ServiceSystem.UnregisterService<IAlphaService>();

            Assert.AreEqual(1, unregistered.Count, "事件应触发一次");
            Assert.AreSame(service, unregistered[0]);
            Assert.AreEqual(1, service.ShutdownCount, "Shutdown 应在事件触发前完成");
        }

        [Test]
        public void Shutdown_ClearsEventSubscriptions()
        {
            int registeredCount = 0;
            ServiceSystem.ServiceRegistered += (s, t, sc) => registeredCount++;

            var service = new AppService();
            ServiceSystem.RegisterService<IAlphaService>(service);
            Assert.AreEqual(1, registeredCount, "首次注册应触发事件");

            ServiceSystem.Shutdown(); // Clears all including events

            // Shutdown 后事件订阅应被清除：重新注册不应再触发
            int countBefore = registeredCount;
            var service2 = new AppService();
            ServiceSystem.RegisterService<IAlphaService>(service2);
            Assert.AreEqual(countBefore, registeredCount, "Shutdown 后事件订阅应被清除，不再触发");
        }
    }
}
