using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameTool
{
    [TestFixture]
    public class ModuleSystemTest
    {
        // --- 测试用接口 ---

        private interface IAlphaModule { }
        private interface IBetaModule { }
        private interface IGammaModule { }

        // --- 测试用模块基类 ---

        private abstract class TestModuleBase : Module, IUpdateModule
        {
            public int InitCount;
            public int ShutdownCount;
            public int TickCount;

            public override void OnInit() => InitCount++;
            public override void Shutdown() => ShutdownCount++;
            public virtual void Update(float elapseSeconds, float realElapseSeconds) => TickCount++;
        }

        private sealed class AppModule : TestModuleBase, IAlphaModule { }

        private sealed class SceneModule : TestModuleBase, IAlphaModule
        {
            public override ModuleScope Scope => ModuleScope.Scene;
        }

        private sealed class GameplayModule : TestModuleBase, IAlphaModule
        {
            public override ModuleScope Scope => ModuleScope.Gameplay;
        }

        private sealed class BetaModule : TestModuleBase, IBetaModule { }

        private sealed class SceneBetaModule : TestModuleBase, IBetaModule
        {
            public override ModuleScope Scope => ModuleScope.Scene;
        }

        [SetUp]
        public void SetUp()
        {
            ModuleSystem.Shutdown();
        }

        [TearDown]
        public void TearDown()
        {
            ModuleSystem.Shutdown();
        }

        // --- 注册与获取 ---

        [Test]
        public void RegisterModule_ThenGetModule_ReturnsSameInstance()
        {
            var module = new AppModule();
            var registered = ModuleSystem.RegisterModule<IAlphaModule>(module);
            var fetched = ModuleSystem.GetModule<IAlphaModule>();

            Assert.AreSame(module, registered);
            Assert.AreSame(module, fetched);
            Assert.AreEqual(1, module.InitCount, "OnInit 应在注册时调用一次");
        }

        [Test]
        public void RegisterModule_DuplicateSameScope_ReturnsExistingInstance()
        {
            var first = new AppModule();
            var second = new AppModule();

            var result = ModuleSystem.RegisterModule<IAlphaModule>(first);
            var duplicate = ModuleSystem.RegisterModule<IAlphaModule>(second);

            Assert.AreSame(first, result);
            Assert.AreSame(first, duplicate, "同作用域重复注册应返回已有实例");
            Assert.AreEqual(0, second.InitCount, "被拒绝的实例不应被初始化");
        }

        // --- 跨作用域遮蔽 ---

        [Test]
        public void GetModule_CrossScopeShadowing_GameplayBeatsSceneBeatsApp()
        {
            var app = new AppModule();
            var scene = new SceneModule();
            var gameplay = new GameplayModule();
            ModuleSystem.RegisterModule<IAlphaModule>(app);
            ModuleSystem.RegisterModule<IAlphaModule>(scene);
            ModuleSystem.RegisterModule<IAlphaModule>(gameplay);

            Assert.AreSame(gameplay, ModuleSystem.GetModule<IAlphaModule>());

            ModuleSystem.ShutdownScope(ModuleScope.Gameplay);
            Assert.AreSame(scene, ModuleSystem.GetModule<IAlphaModule>(), "Gameplay 注销后应回退到 Scene");

            ModuleSystem.ShutdownScope(ModuleScope.Scene);
            Assert.AreSame(app, ModuleSystem.GetModule<IAlphaModule>(), "Scene 注销后应回退到 App");
        }

        [Test]
        public void RegisterModule_SameInterfaceDifferentScopes_Allowed()
        {
            var app = new AppModule();
            var scene = new SceneModule();
            var gameplay = new GameplayModule();

            ModuleSystem.RegisterModule<IAlphaModule>(app);
            ModuleSystem.RegisterModule<IAlphaModule>(scene);
            ModuleSystem.RegisterModule<IAlphaModule>(gameplay);

            Assert.AreEqual(1, app.InitCount);
            Assert.AreEqual(1, scene.InitCount);
            Assert.AreEqual(1, gameplay.InitCount, "不同作用域注册同一接口不应被拒绝");
        }

        // --- ShutdownScope 正确性（P0 回归：脏索引会错删其他模块的 tick 槽位） ---

        [Test]
        public void ShutdownScope_MixedRegistrations_RemainingModulesStillTick()
        {
            // 多个优先级交错的 App/Scene 模块，最大化触发 InsertSorted 移动已有元素
            var appA = new AppModule();
            var betaApp = new BetaModule();
            var sceneA = new SceneModule();
            var betaScene = new SceneBetaModule();

            ModuleSystem.RegisterModule<IAlphaModule>(appA);
            ModuleSystem.RegisterModule<IBetaModule>(betaApp);
            ModuleSystem.RegisterModule<IAlphaModule>(sceneA);
            ModuleSystem.RegisterModule<IBetaModule>(betaScene);

            ModuleSystem.ShutdownScope(ModuleScope.Scene);

            Assert.AreEqual(1, sceneA.ShutdownCount, "Scene 模块应被关闭");
            Assert.AreEqual(1, betaScene.ShutdownCount, "Scene 模块应被关闭");
            Assert.AreEqual(0, appA.ShutdownCount, "App 模块不应被关闭");
            Assert.AreEqual(0, betaApp.ShutdownCount, "App 模块不应被关闭");

            ModuleSystem.Update(0f, 0f);
            Assert.AreEqual(1, appA.TickCount, "App 模块关闭后仍应正常轮询");
            Assert.AreEqual(1, betaApp.TickCount, "App 模块关闭后仍应正常轮询");
            Assert.AreEqual(0, sceneA.TickCount, "已注销的 Scene 模块不应被轮询");
        }

        [Test]
        public void ShutdownScope_RemovedModule_NoLongerTicks()
        {
            var scene = new SceneModule();
            ModuleSystem.RegisterModule<IAlphaModule>(scene);

            ModuleSystem.Update(0f, 0f);
            Assert.AreEqual(1, scene.TickCount);

            ModuleSystem.ShutdownScope(ModuleScope.Scene);
            ModuleSystem.Update(0f, 0f);

            Assert.AreEqual(1, scene.TickCount, "注销后的模块不应再被轮询");
        }

        // --- 迭代安全：注册 ---

        [Test]
        public void Update_RegisterDuringIteration_AppliedAfterFlush()
        {
            var registrar = new DeferredRegistrar();
            ModuleSystem.RegisterModule<IBetaModule>(registrar);

            ModuleSystem.Update(0f, 0f);

            Assert.AreEqual(1, registrar.TickCount);
            Assert.IsNotNull(ModuleSystem.GetModule<IAlphaModule>(), "迭代中注册的模块应在迭代结束后生效");
            Assert.AreEqual(1, registrar.Spawned.InitCount, "延迟注册生效时应调用 OnInit");
            // 迭代内 count 已捕获，新模块本轮不 tick；下一轮开始 tick
            Assert.AreEqual(0, registrar.Spawned.TickCount);

            ModuleSystem.Update(0f, 0f);
            Assert.AreEqual(1, registrar.Spawned.TickCount, "下一轮应开始轮询新模块");
        }

        private sealed class DeferredRegistrar : TestModuleBase, IBetaModule
        {
            public AppModule Spawned;
            private bool _spawned;

            public override void OnInit()
            {
                Spawned = new AppModule();
            }

            public override void Update(float elapseSeconds, float realElapseSeconds)
            {
                base.Update(elapseSeconds, realElapseSeconds);
                // 首次 tick 时在迭代内注册新模块（不能在此处调用 GetModule 探测：未注册会触发反射回退）
                if (!_spawned)
                {
                    _spawned = true;
                    ModuleSystem.RegisterModule<IAlphaModule>(Spawned);
                }
            }
        }

        // --- 迭代安全：注销 ---

        [Test]
        public void Update_UnregisterDuringIteration_AppliedAfterFlush()
        {
            var victim = new AppModule();
            var killer = new UnregisterOnTick(victim);
            ModuleSystem.RegisterModule<IAlphaModule>(victim);
            ModuleSystem.RegisterModule<IBetaModule>(killer);

            ModuleSystem.Update(0f, 0f);

            Assert.AreEqual(1, victim.ShutdownCount, "迭代中注销的模块应在迭代结束后关闭");

            ModuleSystem.Update(0f, 0f);
            Assert.AreEqual(1, victim.TickCount, "被注销的模块不应再被轮询");
            Assert.AreEqual(2, killer.TickCount, "其余模块应继续正常轮询");
        }

        private sealed class UnregisterOnTick : TestModuleBase, IBetaModule
        {
            private readonly Module _victim;

            public UnregisterOnTick(Module victim) => _victim = victim;

            public override void Update(float elapseSeconds, float realElapseSeconds)
            {
                base.Update(elapseSeconds, realElapseSeconds);
                ModuleSystem.UnregisterModule(_victim);
            }
        }

        // --- 迭代安全：ShutdownScope ---

        [Test]
        public void ShutdownScope_DuringIteration_DefersRemoval()
        {
            var scene = new SceneModule();
            var trigger = new ShutdownScopeOnTick();
            ModuleSystem.RegisterModule<IAlphaModule>(scene);
            ModuleSystem.RegisterModule<IBetaModule>(trigger);

            ModuleSystem.Update(0f, 0f);

            Assert.AreEqual(1, scene.ShutdownCount, "迭代中的 ShutdownScope 应延迟到迭代结束后应用");
            // scene 先于 trigger 注册（同优先级按注册顺序），本轮已被 tick 一次后才被延迟移除
            Assert.AreEqual(1, scene.TickCount, "延迟注销的模块本轮已 tick（迭代内 count 捕获）");

            ModuleSystem.Update(0f, 0f);
            Assert.AreEqual(1, scene.TickCount, "被注销的模块不应再被轮询");
        }

        private sealed class ShutdownScopeOnTick : TestModuleBase, IBetaModule
        {
            public override void Update(float elapseSeconds, float realElapseSeconds)
            {
                base.Update(elapseSeconds, realElapseSeconds);
                ModuleSystem.ShutdownScope(ModuleScope.Scene);
            }
        }

        // --- 注销 API ---

        [Test]
        public void UnregisterModule_ByInterface_RemovesAndShutsDown()
        {
            var module = new AppModule();
            ModuleSystem.RegisterModule<IAlphaModule>(module);

            bool result = ModuleSystem.UnregisterModule<IAlphaModule>();

            Assert.IsTrue(result);
            Assert.AreEqual(1, module.ShutdownCount);
        }

        [Test]
        public void UnregisterModule_ByInstance_RemovesAndShutsDown()
        {
            var module = new AppModule();
            ModuleSystem.RegisterModule<IAlphaModule>(module);

            bool result = ModuleSystem.UnregisterModule(module);

            Assert.IsTrue(result);
            Assert.AreEqual(1, module.ShutdownCount);
        }

        [Test]
        public void UnregisterModule_NotRegistered_ReturnsFalse()
        {
            Assert.IsFalse(ModuleSystem.UnregisterModule(new AppModule()));
        }

        // --- Shutdown 健壮性 ---

        [Test]
        public void Shutdown_ModuleThrows_DoesNotAbortOtherModules()
        {
            var thrower = new ThrowingModule();
            var normal = new BetaModule();
            ModuleSystem.RegisterModule<IAlphaModule>(thrower);
            ModuleSystem.RegisterModule<IBetaModule>(normal);

            // 框架会记录模块关闭异常（Error 日志），声明预期
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*InvalidOperationException: test.*"));

            Assert.DoesNotThrow(() => ModuleSystem.Shutdown());
            Assert.AreEqual(1, normal.ShutdownCount, "异常模块之后的模块仍应被关闭");
        }

        private sealed class ThrowingModule : TestModuleBase, IAlphaModule
        {
            public override void Shutdown() => throw new System.InvalidOperationException("test");
        }

        [Test]
        public void Shutdown_ClearsAllScopes_InReverseOrder()
        {
            var app = new AppModule();
            var scene = new SceneModule();
            var gameplay = new GameplayModule();
            ModuleSystem.RegisterModule<IAlphaModule>(app);
            ModuleSystem.RegisterModule<IAlphaModule>(scene);
            ModuleSystem.RegisterModule<IAlphaModule>(gameplay);

            var expected = new List<Module> { gameplay, scene, app };
            ModuleSystem.Shutdown();

            Assert.AreEqual(1, app.ShutdownCount);
            Assert.AreEqual(1, scene.ShutdownCount);
            Assert.AreEqual(1, gameplay.ShutdownCount);
            CollectionAssert.AreEquivalent(expected, new Module[] { gameplay, scene, app });
        }

        // --- 优先级排序 ---

        [Test]
        public void Update_HigherPriorityTicksFirst()
        {
            var order = new List<string>();
            var low = new OrderedModule("low", 0, order);
            var high = new OrderedModule("high", 10, order);
            var mid = new OrderedModule("mid", 5, order);

            // 注意：同一接口同一作用域只能注册一个实例，各模块使用不同接口
            ModuleSystem.RegisterModule<IAlphaModule>(low);
            ModuleSystem.RegisterModule<IBetaModule>(high);
            ModuleSystem.RegisterModule<IGammaModule>(mid);

            ModuleSystem.Update(0f, 0f);

            CollectionAssert.AreEqual(new[] { "high", "mid", "low" }, order);
        }

        private sealed class OrderedModule : Module, IUpdateModule, IAlphaModule, IBetaModule, IGammaModule
        {
            private readonly string _name;
            private readonly List<string> _order;

            public OrderedModule(string name, int priority, List<string> order)
            {
                _name = name;
                _order = order;
                Priority = priority;
            }

            public override int Priority { get; }
            public override void OnInit() { }
            public override void Shutdown() { }
            public void Update(float elapseSeconds, float realElapseSeconds) => _order.Add(_name);
        }
    }
}
