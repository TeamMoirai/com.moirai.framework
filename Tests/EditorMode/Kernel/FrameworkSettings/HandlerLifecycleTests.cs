using System;
using Moirai.Atropos;
using Moirai.Atropos.Tests.EditorMode;
using NUnit.Framework;
using UnityEngine;

namespace Kernel.FrameworkSettings
{
    /// <summary>
    /// 生命期转换语义判据（就绪落位、复活口径、途中关停、重复关停、关停抛错、关停期锁存收尾）。
    /// </summary>
    /// <remarks>
    /// 判据唯一落点在 <see cref="LifecycleMachine"/>：机器一段直驱它的 <see cref="LifecycleMachine.IHost"/> 接缝，不经任何生产类型； <br />
    /// 宿主一段取 <see cref="ServiceMono{TScope}"/> 的复活口径，与 <see cref="FrameworkHandler"/> 的直驱入口前置及关停抛错承接。
    /// </remarks>
    [TestFixture]
    public class HandlerLifecycleTests
    {
        private sealed class Probe : LifecycleMachine.IHost
        {
            /// <summary>宿主当前生命周期状态。</summary>
            public EServiceState State { get; set; } = EServiceState.Created;

            /// <summary>OnInit 被驱动次数。</summary>
            public int InitCount { get; private set; }

            /// <summary>OnShutdown 被驱动次数。</summary>
            public int ShutdownCount { get; private set; }

            /// <summary>已关闭实例是否允许重新装配。</summary>
            public bool AllowsRevival { get; set; } = true;

            /// <summary>装配体内额外动作，用于在 OnInit 途中触发关停。</summary>
            public Action OnInitBody { get; set; }

            /// <summary>释放体内额外动作，用于模拟后端关停抛错。</summary>
            public Action OnShutdownBody { get; set; }

            public void OnInit()
            {
                InitCount++;
                OnInitBody?.Invoke();
            }

            public void OnShutdown()
            {
                ShutdownCount++;
                OnShutdownBody?.Invoke();
            }
        }

        /// <summary>
        /// 已关闭实例在允许复活的宿主上重新落位。
        /// </summary>
        [Test]
        public void Initialize_WhenDisposedAndRevivalAllowed_PlacesInitialized()
        {
            var probe = new Probe { State = EServiceState.Disposed };

            LifecycleMachine.Initialize(probe);

            Assert.AreEqual(EServiceState.Initialized, probe.State);
            Assert.AreEqual(1, probe.InitCount);
        }

        /// <summary>
        /// 已关闭实例在拒绝复活的宿主上不得重跑 OnInit。
        /// </summary>
        [Test]
        public void Initialize_WhenDisposedAndRevivalBlocked_StaysDisposed()
        {
            var probe = new Probe { State = EServiceState.Disposed, AllowsRevival = false };

            LifecycleMachine.Initialize(probe);

            Assert.AreEqual(EServiceState.Disposed, probe.State, "AllowsRevival 取假的宿主停在已关闭态，机器不得把它落回就绪");
            Assert.AreEqual(0, probe.InitCount);
        }

        /// <summary>
        /// OnInit 途中被关停时不得把已关闭态盖回就绪。
        /// </summary>
        [Test]
        public void Initialize_WhenShutdownDuringOnInit_DoesNotOverwriteClosedState()
        {
            var probe = new Probe();
            probe.OnInitBody = () => LifecycleMachine.Shutdown(probe);

            LifecycleMachine.Initialize(probe);

            Assert.AreEqual(EServiceState.Disposed, probe.State);
        }

        /// <summary>
        /// 重复关停只跑一次 OnShutdown。
        /// </summary>
        [Test]
        public void Shutdown_WhenAlreadyDisposed_IsNotDrivenTwice()
        {
            var probe = new Probe();
            LifecycleMachine.Initialize(probe);

            LifecycleMachine.Shutdown(probe);
            LifecycleMachine.Shutdown(probe);

            Assert.AreEqual(EServiceState.Disposed, probe.State);
            Assert.AreEqual(1, probe.ShutdownCount);
        }

        /// <summary>
        /// OnShutdown 抛错时照常落位已关闭，且异常不逃出关停驱动。
        /// </summary>
        [Test]
        public void Shutdown_WhenOnShutdownThrows_PlacesDisposedWithoutEscaping()
        {
            UtfLogExpect.Error();

            var probe = new Probe();
            LifecycleMachine.Initialize(probe);
            probe.OnShutdownBody = () => throw new InvalidOperationException("后端释放失败");

            Assert.DoesNotThrow(() => LifecycleMachine.Shutdown(probe));
            Assert.AreEqual(EServiceState.Disposed, probe.State);
            Assert.AreEqual(1, probe.ShutdownCount);
        }

        /// <summary>
        /// Mono 侧的 <see cref="LifecycleMachine.IHost"/> 桥按复活口径把已关闭实例挡在装配外。
        /// </summary>
        /// <remarks>
        /// 判定面只有旗标与状态读写桥——本格直取接缝，不经 <see cref="IServiceLifecycle"/>。 <br />
        /// 容器驱动到机器的接线（<c>IServiceLifecycle.Initialize</c> → <c>LifecycleMachine.Initialize</c>）不在这里， <br />
        /// 由 <c>GameServicesTests.ServiceMono_RegistersUnderConcreteType_AndUnregistersOnDestroy</c> 钉住。
        /// </remarks>
        [Test]
        public void ServiceMono_AllowsRevival_IsBlocked()
        {
            var go = new GameObject("probe-mono");
            try
            {
                var mono = go.AddComponent<MonoLifecycleProbe>();
                var host = (LifecycleMachine.IHost)mono;

                Assert.IsFalse(host.AllowsRevival, "Mono 实例随 GameObject 销毁而亡，复活等于在拆除对象上重跑装配");

                host.State = EServiceState.Disposed;
                LifecycleMachine.Initialize(host);

                Assert.AreEqual(EServiceState.Disposed, mono.State, "复活口径由机器执行：已关闭的 Mono 宿主不落回就绪");
                Assert.AreEqual(0, mono.InitCount, "已关闭的 Mono 实例不得重跑 OnInit");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Mono 侧探针：Awake 取空实现，注册链路与本判据无关。
        /// </summary>
        private sealed class MonoLifecycleProbe : ServiceMono<AppScope>
        {
            /// <summary>OnInit 被驱动次数。</summary>
            public int InitCount { get; private set; }

            protected override void Awake()
            {
            }

            protected override void OnInit() => InitCount++;

            protected override void OnShutdown()
            {
            }
        }

        /// <summary>
        /// 直驱关停入口的前置：从未装配过的处理器不跑 OnShutdown，装配后只跑一次。
        /// </summary>
        [Test]
        public void DirectShutdown_WhenNeverInitialized_SkipsOnShutdown()
        {
            var handler = new DirectShutdownProbe();

            handler.Internal_Shutdown();

            Assert.AreEqual(0, handler.ShutdownCount, "未装配过的实例没有可释放的后端资源，直驱关停不得驱动 OnShutdown");
            Assert.AreEqual(EServiceState.Created, handler.State, "被跳过的直驱关停不写状态位，实例停在已创建态");

            handler.Internal_Init();
            handler.Internal_Shutdown();

            Assert.AreEqual(1, handler.InitCount);
            Assert.AreEqual(1, handler.ShutdownCount, "装配成功后关停必须驱动一次 OnShutdown");
            Assert.AreEqual(EServiceState.Disposed, handler.State);

            handler.Internal_Shutdown();

            Assert.AreEqual(1, handler.ShutdownCount, "重复直驱关停只跑一次 OnShutdown");
        }

        /// <summary>
        /// OnInit 途中收到的直驱关停被 Created 前置挡下：装配结果保留，不驱动释放回调。
        /// </summary>
        /// <remarks>
        /// 与容器侧的语义差：<see cref="ServiceBase"/>/<see cref="ServiceMono{TScope}"/> 无此前置，途中关停保持关停结果， <br />
        /// 由机器层格 <c>Initialize_WhenShutdownDuringOnInit_DoesNotOverwriteClosedState</c> 钉住。
        /// </remarks>
        [Test]
        public void DirectShutdown_WhenDuringOnInit_IsSkippedAndInitResultKept()
        {
            var handler = new DirectShutdownProbe();
            handler.OnInitBody = () => handler.Internal_Shutdown();

            handler.Internal_Init();

            Assert.AreEqual(EServiceState.Initialized, handler.State, "途中关停被前置挡下，装配结果保留");
            Assert.AreEqual(1, handler.InitCount);
            Assert.AreEqual(0, handler.ShutdownCount, "被挡下的直驱关停不得驱动 OnShutdown");
        }

        /// <summary>
        /// 直驱关停承接 OnShutdown 抛错：异常不逃出入口、照常落位已关闭并记错。
        /// </summary>
        /// <remarks>
        /// 记错而非上抛：异常逃出 <c>Internal_Shutdown</c> 会打断生成宿主换提供者的赋值本身（赋值在共享锁内进行）。
        /// </remarks>
        [Test]
        public void DirectShutdown_WhenOnShutdownThrows_PlacesDisposedWithoutEscaping()
        {
            UtfLogExpect.Error();

            var handler = new DirectShutdownProbe();
            handler.Internal_Init();
            handler.OnShutdownBody = () => throw new InvalidOperationException("后端释放失败");

            Assert.DoesNotThrow(() => handler.Internal_Shutdown());
            Assert.AreEqual(EServiceState.Disposed, handler.State, "抛错不截断关停落位，实例停在已关闭态");
            Assert.AreEqual(1, handler.ShutdownCount, "抛错的释放回调确实被驱动过一次");
        }

        /// <summary>
        /// 锁存闸内的编排收尾：装配过的实例在关停体里必须跑到收尾，且此时就绪判据读数为假。
        /// </summary>
        /// <remarks>
        /// 机器先落 <see cref="EServiceState.ShuttingDown"/> 才驱动 <c>OnShutdown</c>，关停体内的 <c>IsReady</c> 恒假； <br />
        /// 生产侧六处编排收尾（Timer/Asset/Save/Procedure/ObjectPool/Localization）挂 <c>IsReady</c> 即永不执行。 <br />
        /// 走 <see cref="IServiceLifecycle"/> 而非直驱入口：后者带「停在 Created 即跳过」前置，会替锁存背下第二格的功劳。
        /// </remarks>
        [Test]
        public void ShutdownTeardown_WhenInitializedBeforeShutdown_RunsGatedTail()
        {
            var probe = new LatchProbe();

            ((IServiceLifecycle)probe).Initialize();
            ((IServiceLifecycle)probe).Destroy();

            Assert.AreEqual(1, probe.ShutdownCount, "释放回调由容器侧驱动一次");
            Assert.IsTrue(probe.WasInitializedInShutdown, "机器落 Initialized 即武装锁存，关停体内必须仍读得到");
            Assert.IsFalse(probe.IsReadyInShutdown, "关停体内就绪判据恒假——这正是收尾必须挂锁存而非挂 IsReady 的理由");
            Assert.AreEqual(1, probe.TeardownCount, "装配过的后端必须跑完锁存闸内的编排收尾");
        }

        /// <summary>
        /// 锁存闸内的编排收尾：从未装配的实例只跑释放回调，不跑收尾。
        /// </summary>
        [Test]
        public void ShutdownTeardown_WhenNeverInitialized_SkipsGatedTail()
        {
            var probe = new LatchProbe();

            ((IServiceLifecycle)probe).Destroy();

            Assert.AreEqual(1, probe.ShutdownCount, "容器侧无 Created 前置，释放回调照常驱动（与直驱入口的刻意差异）");
            Assert.AreEqual(0, probe.TeardownCount, "没装配过就没有欠做的收尾：锁存未武装，闸内编排不得执行");
            Assert.AreEqual(EServiceState.Disposed, probe.State);
        }

        /// <summary>
        /// 直驱入口探针：仅继承 <see cref="FrameworkHandler"/>，不关联任何生产处理器契约。
        /// </summary>
        /// <remarks>
        /// 下拉候选按 <c>[ProviderDropdown]</c> 字段声明的类型收非抽象派生类，而本包没有一处以 <see cref="FrameworkHandler"/> 为基类 <br />
        /// （字段基座分两档：具体家族基座与服务契约接口，见 <c>Runtime/Kernel/GameApp/GameAppSettings.cs</c>）； <br />
        /// 另有 <c>ProviderCatalogIndex.CompiledCandidates</c> 按装配名排除测试装配这一层筛选项。
        /// </remarks>
        private sealed class DirectShutdownProbe : FrameworkHandler
        {
            /// <summary>OnInit 被驱动次数。</summary>
            public int InitCount { get; private set; }

            /// <summary>OnShutdown 被驱动次数。</summary>
            public int ShutdownCount { get; private set; }

            /// <summary>装配体内额外动作，用于在 OnInit 途中触发直驱关停。</summary>
            public Action OnInitBody { get; set; }

            /// <summary>释放体内额外动作，用于模拟后端抛错。</summary>
            public Action OnShutdownBody { get; set; }

            /// <inheritdoc />
            protected override void OnInit()
            {
                InitCount++;
                OnInitBody?.Invoke();
            }

            /// <inheritdoc />
            protected override void OnShutdown()
            {
                ShutdownCount++;
                OnShutdownBody?.Invoke();
            }
        }

        /// <summary>
        /// 锁存探针：继承容器侧基座，在释放体里以 <c>WasInitialized</c> 闸一段编排收尾，并留两个关停期读数。
        /// </summary>
        /// <remarks>
        /// 形状对齐生产侧六处收尾（闸内取 <c>Context</c> 解析的依赖做注销）：本探针只记次数，不接任何服务门面。
        /// </remarks>
        private sealed class LatchProbe : ServiceBase
        {
            /// <summary>释放回调被驱动次数。</summary>
            public int ShutdownCount { get; private set; }

            /// <summary>锁存闸内的编排收尾被驱动次数。</summary>
            public int TeardownCount { get; private set; }

            /// <summary>释放体内读到的锁存读数。</summary>
            public bool WasInitializedInShutdown { get; private set; }

            /// <summary>释放体内读到的就绪判据读数。</summary>
            public bool IsReadyInShutdown { get; private set; }

            /// <inheritdoc />
            protected override void OnInit()
            {
            }

            /// <inheritdoc />
            protected override void OnShutdown()
            {
                ShutdownCount++;
                WasInitializedInShutdown = WasInitialized;
                IsReadyInShutdown = IsReady;
                if (WasInitialized)
                {
                    TeardownCount++;
                }
            }
        }
    }
}
