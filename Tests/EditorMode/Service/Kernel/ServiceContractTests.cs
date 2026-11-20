using System;
using System.Collections.Generic;
using Moirai.Atropos;
using NUnit.Framework;
using Aud = Moirai.Atropos.Audio;
using Cfg = Moirai.Atropos.ConfigTable;
using Dbg = Moirai.Atropos.Debugger;
using Inp = Moirai.Atropos.Input;
using Loc = Moirai.Atropos.Localization;
using OPo = Moirai.Atropos.ObjectPool;
using Proc = Moirai.Atropos.Procedure;
using Res = Moirai.Atropos.Asset;
using Sav = Moirai.Atropos.Save;
using Scn = Moirai.Atropos.Scene;
using Tim = Moirai.Atropos.Timer;
using UIm = Moirai.Atropos.UI;

namespace Service.Kernel
{
    /// <summary>
    /// 服务层架构契约测试（三件套）：外观 null 降级契约、<c>[ServiceDependency]</c> 声明完整性静态分析、Handler 生命周期对称。
    /// </summary>
    /// <remarks>
    /// ① 外观在 Handler 未就绪时全部 API 静默降级为安全默认值，不抛异常； <br />
    /// ② <c>[ServiceDependency]</c> 声明的类型必须实现 IService、依赖图无环、OnInit 注册调试面板的服务必须声明 DebuggerService； <br />
    /// ③ 外观 OnShutdown 后 IsValid 为 false 且重复 OnShutdown 幂等。全部为 EditMode 纯静态契约验证，不启动服务世界。
    /// </remarks>
    [TestFixture]
    public sealed class ServiceContractTests
    {
        #region ① 外观 null 降级契约 [FACADE DEGRADATION]

        [SetUp]
        [TearDown]
        public void ResetWorld()
        {
            // 关闭默认世界——全部服务 OnShutdown 触发外观 s_Handler=null，保证降级路径被测
            GameServices.Shutdown();
        }

        [Test]
        public void Scene_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Scn.SceneService.IsValid);
            Assert.IsFalse(Scn.SceneService.IsContainScene("any"));
            Assert.IsFalse(Scn.SceneService.ActivateScene("any"));
            Assert.IsFalse(Scn.SceneService.IsMainScene("any"));
            Assert.IsNull(Scn.SceneService.CurrentMainSceneName);
        }

        [Test]
        public void Timer_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Tim.TimerService.IsValid);
            Assert.IsFalse(Tim.TimerService.IsRunning(123UL));
            Assert.AreEqual(0f, Tim.TimerService.GetLeftTime(123UL));
            Assert.DoesNotThrow(() => Tim.TimerService.Pause(123UL));
            Assert.DoesNotThrow(() => Tim.TimerService.Cancel(123UL));
        }

        [Test]
        public void Save_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Sav.SaveService.IsValid);
            Assert.IsFalse(Sav.SaveService.FileExists("any"));
            Assert.IsNull(Sav.SaveService.DetermineSavePath());
            Assert.DoesNotThrow(() => Sav.SaveService.DeleteAllSaveFiles());
            Assert.AreEqual(0, Sav.SaveService.GetSaveFiles("any").Length, "降级时应返回空数组");
            Assert.AreEqual(Sav.SaveError.HandlerNotReady, Sav.SaveService.TryLoad<object>("any").Error,
                "同步判别加载降级时应返回 HandlerNotReady 失败结果");
            Assert.Throws<GameException>(() => Sav.SaveService.Save("any", "any"), "写路径 fail-fast：处理器未就绪应抛 GameException");
            Assert.IsNull(Sav.SaveService.Load<object>("any"), "同步加载降级时应返回 default");
        }

        [Test]
        public void Input_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Inp.InputService.IsValid);
            Assert.IsFalse(Inp.InputService.Enabled);
            Assert.IsFalse(Inp.InputService.GetBool("any"));
            Assert.AreEqual(0f, Inp.InputService.GetFloat("any"));
            Assert.AreEqual(UnityEngine.Vector2.zero, Inp.InputService.GetVector2("any"));
            Assert.AreEqual(UnityEngine.Vector2.zero, Inp.InputService.GetMousePosition());
        }

        [Test]
        public void Localization_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Loc.LocalizationService.IsValid);
            Assert.IsFalse(Loc.LocalizationService.Has("any"));
            // 降级返回 id 原文——UI 显示键名而非空白
            Assert.AreEqual("some_key", Loc.LocalizationService.GetTextFromId("some_key"));
            Assert.AreEqual(Loc.Language.Unspecified.Name, Loc.LocalizationService.CurrentLanguage.Name);
        }

        [Test]
        public void GameObjectPool_FacadeDegradesGracefully()
        {
            Assert.IsFalse(OPo.GameObjectPoolService.IsValid);
            Assert.IsNull(OPo.GameObjectPoolService.Spawn("any"));
            Assert.IsFalse(OPo.GameObjectPoolService.TrySpawn("any", null, out UnityEngine.GameObject go));
            Assert.IsNull(go);
            Assert.DoesNotThrow(() => OPo.GameObjectPoolService.FlushAll());
        }

        [Test]
        public void Procedure_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Proc.ProcedureService.IsValid);
            Assert.IsNull(Proc.ProcedureService.CurrentProcedure);
            Assert.AreEqual(0f, Proc.ProcedureService.CurrentProcedureTime);
        }

        [Test]
        public void Resource_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Res.AssetService.IsValid);
            Assert.DoesNotThrow(() => Res.AssetService.OnLowMemory());
        }

        [Test]
        public void Audio_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Aud.AudioService.IsValid);
            Assert.AreEqual(0UL, Aud.AudioService.Play((UnityEngine.AudioClip)null, new Aud.AudioPlayOptions()));
            Assert.DoesNotThrow(() => Aud.AudioService.StopAll());
        }

        [Test]
        public void UI_FacadeDegradesGracefully()
        {
            Assert.IsFalse(UIm.UIService.IsValid);
            Assert.IsNull(UIm.UIService.CurrentModal);
        }

        [Test]
        public void ConfigTable_FacadeDegradesGracefully()
        {
            // 注：s_Handler 为跨用例静态状态——同域内更早的会话可能已懒加载本处理器
            //（配置表数据非 null 属合法历史状态）。降级契约断言"不抛异常"而非"必须为 null"。
            Assert.DoesNotThrow(() => Cfg.ConfigTableService.GetAllLocalizedStrings());
            Assert.DoesNotThrow(() => Cfg.ConfigTableService.GetUIWindowLocation("any"));
        }

        [Test]
        public void Debugger_FacadeDegradesGracefully()
        {
            Assert.IsFalse(Dbg.DebuggerService.IsValid);
            Assert.IsNull(Dbg.DebuggerService.WindowRegistry);
            Assert.IsNull(Dbg.DebuggerService.GetDebuggerWindow("any/path"));
        }

        #endregion

        #region ② ServiceDependency 声明完整性静态分析 [DEPENDENCY STATIC ANALYSIS]

        /// <summary>框架全部 App 服务实现类型（组合根 12 项——静态分析目标集）。</summary>
        private static readonly Type[] FrameworkServices =
        {
            typeof(Dbg.DebuggerService),
            typeof(Res.AssetService),
            typeof(Tim.TimerService),
            typeof(OPo.GameObjectPoolService),
            typeof(Loc.LocalizationService),
            typeof(UIm.UIService),
            typeof(Scn.SceneService),
            typeof(Aud.AudioService),
            typeof(Inp.InputService),
            typeof(Sav.SaveService),
            typeof(Cfg.ConfigTableService),
            typeof(Proc.ProcedureService),
        };

        [Test]
        public void ServiceDependency_AllDeclaredTypes_AreIService()
        {
            foreach (Type serviceType in FrameworkServices)
            {
                object[] attrs = serviceType.GetCustomAttributes(typeof(ServiceDependencyAttribute), false);
                foreach (ServiceDependencyAttribute attr in attrs)
                {
                    foreach (Type dep in attr.DependencyTypes)
                    {
                        Assert.IsTrue(typeof(IService).IsAssignableFrom(dep) || dep.IsInterface,
                            $"{serviceType.Name} 声明的依赖 {dep.Name} 必须实现 IService 或是 [ProviderHandlerHost] 自报的契约接口（Phase 2 注入互调）");
                    }
                }
            }
        }

        [Test]
        public void ServiceDependency_GraphIsAcyclic()
        {
            // 声明图 DFS 三色标记检测环（与运行时拓扑排序同契约——环必须编译期可见）
            var edges = new Dictionary<Type, Type[]>();
            foreach (Type serviceType in FrameworkServices)
            {
                var deps = new List<Type>();
                foreach (ServiceDependencyAttribute attr in
                    serviceType.GetCustomAttributes(typeof(ServiceDependencyAttribute), false))
                    deps.AddRange(attr.DependencyTypes);
                edges[serviceType] = deps.ToArray();
            }

            var state = new Dictionary<Type, int>(); // 0=未访问 1=访问中 2=完成
            var stack = new List<Type>();

            foreach (Type root in FrameworkServices)
            {
                Assert.IsTrue(DfsAcyclic(root, edges, state, stack),
                    $"依赖图存在环（路径: {string.Join(" -> ", stack)}）");
            }
        }

        private static bool DfsAcyclic(Type node, Dictionary<Type, Type[]> edges, Dictionary<Type, int> state, List<Type> stack)
        {
            if (!state.TryGetValue(node, out int s)) s = 0;
            if (s == 2) return true;
            if (s == 1) { stack.Add(node); return false; }

            state[node] = 1;
            stack.Add(node);
            if (edges.TryGetValue(node, out Type[] deps))
            {
                foreach (Type dep in deps)
                {
                    if (!DfsAcyclic(dep, edges, state, stack)) return false;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[node] = 2;
            return true;
        }

        [Test]
        public void ServiceDependency_DebugWindowRegistrants_DeclareDebuggerService()
        {
            // OnInit 注册调试面板的服务必须声明 DebuggerService（拓扑序保证 Debugger 先行就绪）
            var mustDeclare = new Dictionary<Type, bool>
            {
                [typeof(Aud.AudioService)] = true,
                [typeof(Res.AssetService)] = true,
                [typeof(Tim.TimerService)] = true,
                [typeof(Loc.LocalizationService)] = true,
                [typeof(Proc.ProcedureService)] = true,
                // 显式豁免：ConfigTable（无轮询状态）、Input/Save/Scene/GameObjectPool（无调试面板）
            };

            foreach (var pair in mustDeclare)
            {
                Assert.IsTrue(DeclaresDependency(pair.Key, typeof(Dbg.IDebuggerService)),
                    $"{pair.Key.Name} OnInit 注册调试面板，必须声明 [ServiceDependency(typeof(IDebuggerService))]");
            }
        }

        private static bool DeclaresDependency(Type serviceType, Type dependencyType)
        {
            foreach (ServiceDependencyAttribute attr in
                serviceType.GetCustomAttributes(typeof(ServiceDependencyAttribute), false))
            {
                foreach (Type dep in attr.DependencyTypes)
                {
                    if (dep == dependencyType) return true;
                }
            }
            return false;
        }

        #endregion

        #region ③ Handler 生命周期对称 [LIFECYCLE SYMMETRY]

        [Test]
        public void FacadeShutdown_IsIdempotent_AndInvalidates()
        {
            // 注册并初始化一个代表性服务（DebuggerService——无外部依赖）
            // 两阶段语义：Register 入图 + Initialize 驱动 OnInit（Handler 就绪）
            var service = new Dbg.DebuggerService();
            GameServices.RegisterService(EServiceScopeKind.App, service);
            GameServices.Default.Initialize();

            Assert.IsTrue(Dbg.DebuggerService.IsValid, "初始化后外观应可用");

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.IsFalse(Dbg.DebuggerService.IsValid, "关闭后外观应失效（s_Handler 置空）");

            // 重复关闭幂等——对已关闭作用域再关闭不得抛异常
            Assert.DoesNotThrow(() => GameServices.ShutdownContainer(EServiceScopeKind.App));
            Assert.DoesNotThrow(() => service.OnShutdown());
        }

        #endregion

        #region ④ 契约绑定 [CONTRACT BINDING]

        /// <summary>
        /// <c>[ProviderHandlerHost]</c> 自报的契约接口必须由服务门面自身实现（boot 已消费的契约全量断言）。
        /// </summary>
        /// <remarks>
        /// 容器把服务实例绑定在自报契约接口下（Phase 2 注入互调）；服务不实现契约时，解析在 <c>as T</c> 处坍缩为 null， <br />
        /// <c>GetRequiredService</c> 报 "not found in any active scope" 中断 boot——PlayMode 全 UI 失败的根因。
        /// </remarks>
        [Test]
        public void ProviderHandlerHost_SelfReportedContract_IsImplementedByService()
        {
            Assert.IsTrue(typeof(Dbg.IDebuggerService).IsAssignableFrom(typeof(Dbg.DebuggerService)),
                "DebuggerService 必须实现自报契约 IDebuggerService，否则容器契约绑定解析失败");
            Assert.IsTrue(typeof(Tim.ITimerService).IsAssignableFrom(typeof(Tim.TimerService)),
                "TimerService 必须实现自报契约 ITimerService，否则容器契约绑定解析失败");
        }

        [Test]
        public void ContractBinding_ResolutionReturnsServiceInstance()
        {
            // 隔离世界复现组合根的两步注册（具体类入图 + 契约附加绑定），解析必须命中同一服务实例
            var world = new ServiceWorld();

            var debugger = world.Register(EServiceScopeKind.App, new Dbg.DebuggerService());
            world.Register(EServiceScopeKind.App, typeof(Dbg.IDebuggerService), debugger);
            Assert.IsTrue(world.TryGetService<Dbg.IDebuggerService>(out var resolvedDebugger),
                "IDebuggerService 契约解析必须命中（未实现契约时 as T 坍缩为 null）");
            Assert.AreSame(debugger, resolvedDebugger);

            var timer = world.Register(EServiceScopeKind.App, new Tim.TimerService());
            world.Register(EServiceScopeKind.App, typeof(Tim.ITimerService), timer);
            Assert.IsTrue(world.TryGetService<Tim.ITimerService>(out var resolvedTimer),
                "ITimerService 契约解析必须命中（未实现契约时 as T 坍缩为 null）");
            Assert.AreSame(timer, resolvedTimer);
        }

        #endregion
    }
}
