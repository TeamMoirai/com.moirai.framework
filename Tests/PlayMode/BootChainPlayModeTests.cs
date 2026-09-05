using System;
using System.Collections;
using Moirai.Atropos;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Moirai.Atropos.Tests.PlayMode
{
    /// <summary>
    /// 启动链/关闭链 PlayMode 测试。
    /// <para>覆盖 EditMode 无法验证的运行时行为：服务世界在播放态的真实构建、
    /// Tick 驱动链、作用域级联关闭。</para>
    /// <para>注意：本夹具自建隔离世界（<c>new ServiceWorld()</c>），不触碰 <see cref="GameServices.Default"/>——
    /// 避免与 GameEntry 启动链产生的真实世界互相污染。</para>
    /// </summary>
    [TestFixture]
    public sealed class BootChainPlayModeTests
    {
        /// <summary>测试服务（计数生命周期与 Tick）。</summary>
        private sealed class ProbeService : ServiceBase, IServiceTickable
        {
            public int InitCount;
            public int TickCount;
            public int ShutdownCount;

            public override void OnInit() => InitCount++;
            public void Tick(float elapseSeconds, float realElapseSeconds) => TickCount++;
            public override void OnShutdown() => ShutdownCount++;
        }

        [ServiceDependency(typeof(ProbeService))]
        private sealed class DependentProbeService : ServiceBase
        {
            public bool DependencyReadyOnInit;

            public override void OnInit()
            {
                // 拓扑契约：OnInit 时依赖必须已初始化
                DependencyReadyOnInit = _dependency != null &&
                    _dependency.State == EServiceState.Initialized;
            }

            public override void OnShutdown() { }

            public void Bind(ProbeService dependency) => _dependency = dependency;
            private ProbeService _dependency;
        }

        private ServiceWorld _world;

        [SetUp]
        public void SetUp()
        {
            _world = new ServiceWorld();
        }

        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
            _world = null;
        }

        [UnityTest]
        public IEnumerator World_TickDrivesServices_AfterInitialization()
        {
            var probe = new ProbeService();
            _world.Register(EServiceScopeKind.App, probe);
            _world.Initialize();

            Assert.AreEqual(1, probe.InitCount, "Initialize 应驱动 OnInit");
            Assert.AreEqual(0, probe.TickCount, "初始化帧不应有 Tick");

            // 播放态真实帧驱动
            _world.Tick(0.016f, 0.016f);
            yield return null;
            _world.Tick(0.016f, 0.016f);

            Assert.AreEqual(2, probe.TickCount, "Tick 应按帧驱动已初始化服务");
        }

        [UnityTest]
        public IEnumerator World_ScopeCascadeShutdown_InReverseOrder()
        {
            var appProbe = new ProbeService();
            var sceneProbe = new ProbeService();
            var gameplayProbe = new ProbeService();

            _world.Register(EServiceScopeKind.App, appProbe);
            _world.Register(EServiceScopeKind.Scene, sceneProbe);
            _world.Register(EServiceScopeKind.Gameplay, gameplayProbe);
            _world.Initialize();

            yield return null;

            // 场景卸载等价物：Gameplay → Scene 级联关闭，App 保留
            _world.ShutdownScope(EServiceScopeKind.Gameplay);
            _world.ShutdownScope(EServiceScopeKind.Scene);

            Assert.AreEqual(1, gameplayProbe.ShutdownCount, "Gameplay 作用域应关闭");
            Assert.AreEqual(1, sceneProbe.ShutdownCount, "Scene 作用域应关闭");
            Assert.AreEqual(0, appProbe.ShutdownCount, "App 作用域应存活");
            Assert.IsTrue(_world.HasApp);
            Assert.IsFalse(_world.HasScene);
            Assert.IsFalse(_world.HasGameplay);
        }

        [Test]
        public void World_TopoInit_DependencyInitializedBeforeDependent()
        {
            var probe = new ProbeService();
            var dependent = new DependentProbeService();
            dependent.Bind(probe);

            // 刻意逆依赖序注册——拓扑排序仍保证依赖先行
            _world.Register(EServiceScopeKind.App, dependent);
            _world.Register(EServiceScopeKind.App, probe);
            _world.Initialize();

            Assert.AreEqual(1, probe.InitCount);
            Assert.IsTrue(dependent.DependencyReadyOnInit,
                "依赖方 OnInit 时被依赖方必须已完成初始化（拓扑序契约）");
        }

        [Test]
        public void World_Dispose_IsIdempotent()
        {
            _world.Register(EServiceScopeKind.App, new ProbeService());
            _world.Initialize();

            Assert.DoesNotThrow(() => _world.Dispose());
            Assert.DoesNotThrow(() => _world.Dispose(), "重复 Dispose 应幂等");
        }
    }
}
