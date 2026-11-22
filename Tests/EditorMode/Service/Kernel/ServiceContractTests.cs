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
            // 关闭默认世界——全部服务 OnShutdown 触发失效钩子清槽，保证降级路径被测
            GameServices.Shutdown();

            // 回摆后门面缓存槽是跨夹具共享的静态态（BuiltinRegistration 预热等夹具会合法地填槽）：
            // 显式清空已迁移模块的槽位（随迁移批次扩展，与 FrameworkServices 清单同步）。
            // Shutdown 已销毁槽内实例，这里只摘引用，无生命周期遗漏。
            Aud.AudioService.Internal_UseHandler(null);
            Scn.SceneService.Internal_UseHandler(null);
            Cfg.ConfigTableService.Internal_UseHandler(null);
            Dbg.DebuggerService.Internal_UseHandler(null);
            Loc.LocalizationService.Internal_UseHandler(null);
            Inp.InputService.Internal_UseHandler(null);
            Sav.SaveService.Internal_UseHandler(null);
            Res.AssetService.Internal_UseHandler(null);
            Tim.TimerService.Internal_UseHandler(null);
            Proc.ProcedureService.Internal_UseHandler(null);
            OPo.ObjectPoolService.Internal_UseHandler(null);
            UIm.UIService.Internal_ResetHandlerSlots();
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
        public void ObjectPool_FacadeDegradesGracefully()
        {
            Assert.IsFalse(OPo.ObjectPoolService.IsValid);
            Assert.IsNull(OPo.ObjectPoolService.Spawn("any"));
            Assert.IsFalse(OPo.ObjectPoolService.TrySpawn("any", null, out UnityEngine.GameObject go));
            Assert.IsNull(go);
            Assert.DoesNotThrow(() => OPo.ObjectPoolService.FlushAll());
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

        /// <summary>
        /// 框架全部 App 服务实现类型（静态分析目标集）。
        /// </summary>
        /// <remarks>
        /// 架构回摆迁移期混合态：已迁移模块记 handler（音频 = <see cref="Aud.UnityAudioHandler"/>、 <br />
        /// 场景 = <see cref="Scn.DefaultSceneHandler"/>、配置表 = <see cref="Cfg.DefaultConfigTableHandler"/>、调试器 = <see cref="Dbg.DefaultDebuggerHandler"/>， <br />
        /// 依赖声明随 handler 走），未迁移模块仍记门面；全部迁移完成后此表应全量为 handler。
        /// </remarks>
        private static readonly Type[] FrameworkServices =
        {
            typeof(Dbg.DefaultDebuggerHandler),
            typeof(Res.AssetService),
            typeof(Tim.TimerService),
            typeof(OPo.ObjectPoolService),
            typeof(Loc.LocalizationService),
            typeof(UIm.UIService),
            typeof(Scn.DefaultSceneHandler),
            typeof(Aud.UnityAudioHandler),
            typeof(Inp.InputService),
            typeof(Sav.SaveService),
            typeof(Cfg.DefaultConfigTableHandler),
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
                // 迁移期混合态：音频的调试面板注册编排已并入 UnityAudioHandler.OnInit
                [typeof(Aud.UnityAudioHandler)] = true,
                // 迁移期混合态：资源的调试面板注册编排已并入家族基座（YooAssetHandler 为默认后端）
                [typeof(Res.YooAssetHandler)] = true,
                // 迁移期混合态：计时器的调试面板注册编排已并入 DefaultTimerHandler
                [typeof(Tim.DefaultTimerHandler)] = true,
                // 迁移期混合态：本地化的调试面板注册编排已并入家族基座（ConfigTableLocalizationHandler 继承）
                [typeof(Loc.ConfigTableLocalizationHandler)] = true,
                // 迁移期混合态：流程的调试面板注册编排已并入 DefaultProcedureHandler
                [typeof(Proc.DefaultProcedureHandler)] = true,
                // 显式豁免：ConfigTable（无轮询状态）、Input/Save/Scene/ObjectPool（无调试面板）
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
            // 注册并初始化一个代表性服务（DefaultDebuggerHandler——无外部依赖，回摆后 handler 即注册实体）
            // 两阶段语义：Register 入图 + Initialize 驱动 OnInit（Handler 就绪）
            var handler = new Dbg.DefaultDebuggerHandler();
            GameServices.RegisterService(EServiceScopeKind.App, typeof(Dbg.IDebuggerService), handler);
            GameServices.Default.Initialize();
            // 预热门面缓存：容器初始化期间首次 Handler 访问经懒链回填注册结果（同实例幂等）
            Dbg.DebuggerService.WarmHandler();

            Assert.IsTrue(Dbg.DebuggerService.IsValid, "初始化后外观应可用");

            GameServices.ShutdownContainer(EServiceScopeKind.App);

            Assert.IsFalse(Dbg.DebuggerService.IsValid, "关闭后外观应失效（OnShutdown 失效钩子清槽）");

            // 重复关闭幂等——对已关闭作用域再关闭不得抛异常
            Assert.DoesNotThrow(() => GameServices.ShutdownContainer(EServiceScopeKind.App));
            Assert.DoesNotThrow(() => ((Moirai.Atropos.IServiceLifecycle)handler).Destroy());
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
            Assert.IsTrue(typeof(Dbg.IDebuggerService).IsAssignableFrom(typeof(Dbg.DefaultDebuggerHandler)),
                "DefaultDebuggerHandler 必须实现自报契约 IDebuggerService，否则容器契约绑定解析失败");
            Assert.IsTrue(typeof(Tim.ITimerService).IsAssignableFrom(typeof(Tim.DefaultTimerHandler)),
                "DefaultTimerHandler 必须实现自报契约 ITimerService，否则容器契约绑定解析失败");
        }

        [Test]
        public void ContractBinding_ResolutionReturnsServiceInstance()
        {
            // 隔离世界复现组合根的两步注册（具体类入图 + 契约附加绑定），解析必须命中同一服务实例
            var world = new ServiceWorld();

            var debugger = world.Register(EServiceScopeKind.App, new Dbg.DefaultDebuggerHandler());
            world.Register(EServiceScopeKind.App, typeof(Dbg.IDebuggerService), debugger);
            Assert.IsTrue(world.TryGetService<Dbg.IDebuggerService>(out var resolvedDebugger),
                "IDebuggerService 契约解析必须命中（未实现契约时 as T 坍缩为 null）");
            Assert.AreSame(debugger, resolvedDebugger);

            var timer = world.Register(EServiceScopeKind.App, new Tim.DefaultTimerHandler());
            world.Register(EServiceScopeKind.App, typeof(Tim.ITimerService), timer);
            Assert.IsTrue(world.TryGetService<Tim.ITimerService>(out var resolvedTimer),
                "ITimerService 契约解析必须命中（未实现契约时 as T 坍缩为 null）");
            Assert.AreSame(timer, resolvedTimer);
        }

        #endregion
    }
}
