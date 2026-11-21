using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架静态外观：生命周期、协程、帧与 Unity 事件订阅。
    /// </summary>
    /// <remarks>
    /// 本类不含 MonoBehaviour 成员：帧逻辑订阅由 <see cref="PlayerLoopDriver"/> 静态表驱动， <br />
    /// 只在 MonoBehaviour 上派发的消息（协程 / Gizmos / ApplicationPause）由 <see cref="GameAppHost"/> 承接， <br />
    /// 对焦与退出走 Unity 的静态事件（<c>Application.focusChanged</c> / <c>Application.quitting</c>），本类直订并转成 <br />
    /// <see cref="onApplicationFocus"/> / <see cref="onApplicationQuit"/> 两个广播。
    /// </remarks>
    public partial class GameApp
    {


        #region 生命周期事件 [LIFECYCLE EVENTS]

        /// <summary>应用对焦状态变化后广播，形参是本次的真值（<c>true</c> 为回焦）。</summary>
        /// <remarks>订阅者自持生命周期：<see cref="Shutdown"/> 会把这两个广播整批摘掉（禁用域重载时上一轮订阅者不得跨会话残留），但一次 <c>+=</c> 配一次 <c>-=</c> 仍是订阅方的责任。注册与注销仅限主线程。</remarks>
        public static event Action<bool> onApplicationFocus;

        /// <summary>
        /// 由 <c>Application.focusChanged</c> 广播对焦状态，逐项隔离订户异常。
        /// </summary>
        /// <param name="hasFocus">本次对焦真值。</param>
        internal static void InvokeApplicationFocus(bool hasFocus)
        {
            InvokeQuarantined(onApplicationFocus, hasFocus, "ApplicationFocus");
        }

        /// <summary>应用退出流程开始时广播，无负载：此刻服务世界尚未关闭。</summary>
        /// <remarks>本类先把它发完再走 <see cref="Shutdown"/>，因此订阅方在这里还来得及落盘，但不得指望之后的帧。</remarks>
        public static event Action onApplicationQuit;

        /// <summary>
        /// 由 <c>Application.quitting</c> 广播退出回执，逐项隔离订户异常后再关闭框架。
        /// </summary>
        internal static void InvokeApplicationQuit()
        {
            InvokeQuarantined(onApplicationQuit, "ApplicationQuit");
            Shutdown(quitting: true);
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

#if UNITY_EDITOR
        /// <summary>
        /// 重置本类静态状态：兼容关闭 Domain Reload 的编辑器工作流。
        /// </summary>
        /// <remarks>
        /// 运行态字段在 <see cref="Initialize"/> 里由 <see cref="SeedRuntimeFromEngine"/> 重新播种，此处仅归位声明默认值， <br />
        /// 避免异常退出（未经 <see cref="Shutdown"/>）时 <see cref="IsShutdown"/> 与生命周期广播跨 Play 会话残留。 <br />
        /// 仅编辑器生效：Player 构建进程生命周期与会话一致，且不得与用户在更早钩子里写入的 <c>AutoBoot</c> 等配置竞争。
        /// </remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_FrameRate = 0;
            s_GameSpeed = 1f;
            s_RunInBackground = false;
            s_NeverSleep = false;
            s_PauseDepth = 0;
            IsShutdown = true;

            onApplicationFocus = null;
            onApplicationQuit = null;
        }
#endif

        internal static void Initialize()
        {
            if (!IsShutdown) return;

            LogUtility.Info("GameApp Active");
            IsShutdown = false;

            SeedRuntimeFromEngine();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif

            // 注意：sceneUnloaded 在场景对象销毁之后触发（Unity 无"卸载前"全局事件），
            // 因此 Scene/Gameplay 服务的 Shutdown() 不得访问场景对象。
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            // 注入 PlayerLoop 并挂接框架 Tick（静态注册表，与场景/宿主无关）
            PlayerLoopDriver.Initialize();
            RegisterBuiltinDrivers();

            // 协程 / Gizmos / ApplicationPause 宿主：主线程物化一次，不承载帧订阅
            GameAppHost.Bootstrap();

            GameTime.StartFrame();
        }

        /// <summary>
        /// 关闭游戏框架（幂等）：退掉未配对的暂停、清空订阅、释放宿主。
        /// </summary>
        /// <remarks>
        /// 编辑器退出 Play 与 <c>ApplicationQuit</c> 均走此入口。 <br />
        /// 关闭会把 <see cref="PauseGame"/> 计数归零并回放 <see cref="GameSpeed"/>，避免下一次启动从冻结实况播种出速度 0。 <br />
        /// 关闭后运行态属性（<see cref="FrameRate"/> / <see cref="GameSpeed"/> / <see cref="RunInBackground"/> / <see cref="NeverSleep"/> / <br />
        /// <see cref="IsGamePaused"/>）仍可读写并成为下一轮基线；帧订阅与协程不再被驱动。
        /// </remarks>
        /// <param name="quitting">是否处于应用退出流程；退出期跳过 <see cref="GameAppHost.Release"/> 的主动销毁。</param>
        internal static void Shutdown(bool quitting = false)
        {
            if (IsShutdown) return;

            LogUtility.Info("GameApp Shutdown");
            IsShutdown = true;

            // 放在最前：下面的清理与关闭后的续跑帧（重启场景 / 退出期异步落盘）都不该跑在冻结的时钟上
            if (s_PauseDepth > 0)
            {
                s_PauseDepth = 0;
                Time.timeScale = s_GameSpeed;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif

            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            // Destroy 订阅由 PlayerLoopDriver.Shutdown 广播并清空；随后只摘除本框架的 PlayerLoop 系统，
            // UniTask 等第三方注入保留——关闭后进程可能还要跑若干帧（重启场景 / 退出期异步落盘）
            UnregisterBuiltinDrivers();
            PlayerLoopDriver.Shutdown();

            // 归零门：整批摘掉两个生命周期广播，未配对的订阅不得跨 Play 会话残留
            onApplicationFocus = null;
            onApplicationQuit = null;

            GameServices.Shutdown();
            if (!quitting) GameAppHost.Release();

            // 释放缓存的从进程的非托管内存中分配的内存。
            MarshalUtility.FreeCachedHGlobal();
        }

        #endregion





        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 注册Destroy事件。在 <see cref="Shutdown"/> 时广播。
        /// </summary>
        /// <returns>可用于注销的句柄；<paramref name="action"/> 为 <c>null</c> 时返回 <c>null</c>。</returns>
        public static Subscription AddDestroyListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddDestroyCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveDestroyCallback(action));
        }

        /// <summary>
        /// 反注册Destroy事件。仅对能取回同一委托实例的注册有效。
        /// </summary>
        public static void RemoveDestroyListener(Action action)
        {
            PlayerLoopDriver.RemoveDestroyCallback(action);
        }

        /// <summary>
        /// 注册 OnDrawGizmos 事件（仅编辑器）。
        /// </summary>
        /// <remarks>订阅写入 <see cref="PlayerLoopDriver"/> 静态表，宿主销毁不丢失；此处仅确保派发者存在。</remarks>
        /// <returns>可用于注销的句柄；<paramref name="action"/> 为 <c>null</c> 时返回 <c>null</c>。</returns>
        public static Subscription AddOnDrawGizmosListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddDrawGizmosCallback(action);
            GameAppHost.Bootstrap();
            return new Subscription(() => PlayerLoopDriver.RemoveDrawGizmosCallback(action));
        }

        /// <summary>
        /// 反注册OnDrawGizmos事件。仅对能取回同一委托实例的注册有效。
        /// </summary>
        public static void RemoveOnDrawGizmosListener(Action action)
        {
            PlayerLoopDriver.RemoveDrawGizmosCallback(action);
        }

        /// <summary>
        /// 注册OnDrawGizmosSelected事件（仅编辑器）。语义同 <see cref="AddOnDrawGizmosListener"/>。
        /// </summary>
        public static Subscription AddOnDrawGizmosSelectedListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddDrawGizmosSelectedCallback(action);
            GameAppHost.Bootstrap();
            return new Subscription(() => PlayerLoopDriver.RemoveDrawGizmosSelectedCallback(action));
        }

        /// <summary>
        /// 反注册OnDrawGizmosSelected事件。仅对能取回同一委托实例的注册有效。
        /// </summary>
        public static void RemoveOnDrawGizmosSelectedListener(Action action)
        {
            PlayerLoopDriver.RemoveDrawGizmosSelectedCallback(action);
        }

        /// <summary>
        /// 注册 OnApplicationPause 事件。
        /// </summary>
        /// <remarks>暂停回调只能由 MonoBehaviour 消息派发，故注册时一并物化 <see cref="GameAppHost"/>。</remarks>
        /// <returns>可用于注销的句柄；<paramref name="action"/> 为 <c>null</c> 时返回 <c>null</c>。这类回调常用 lambda，注销请持本句柄。</returns>
        public static Subscription AddOnApplicationPauseListener(Action<bool> action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddApplicationPauseCallback(action);
            GameAppHost.Bootstrap();
            return new Subscription(() => PlayerLoopDriver.RemoveApplicationPauseCallback(action));
        }

        /// <summary>
        /// 反注册OnApplicationPause事件。仅对能取回同一委托实例的注册有效。
        /// </summary>
        public static void RemoveOnApplicationPauseListener(Action<bool> action)
        {
            PlayerLoopDriver.RemoveApplicationPauseCallback(action);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 从引擎实况播种运行态（默认值已由 <c>GameAppSettings.Initiation</c> 推给引擎）。
        /// </summary>
        private static void SeedRuntimeFromEngine()
        {
            s_FrameRate = Application.targetFrameRate;
            s_GameSpeed = Time.timeScale;
            s_RunInBackground = Application.runInBackground;
            s_NeverSleep = Screen.sleepTimeout == SleepTimeout.NeverSleep;
            s_PauseDepth = 0;
        }

        private static void RegisterBuiltinDrivers()
        {
            // 三段心跳走核心钩子而非用户回调表：先于全部项目订户执行，且不会被订户的连续异常熔断连带摘除
            PlayerLoopDriver.SetCoreUpdateCallback(Tick);
            PlayerLoopDriver.SetCoreFixedUpdateCallback(FixedTick);
            PlayerLoopDriver.SetCoreLateUpdateCallback(LateTick);
            PlayerLoopDriver.AddDrawGizmosCallback(DrawGizmos);

            // 对焦与退出是 Unity 的静态事件，不经 PlayerLoop 的注册表：方法组直绑，零 lambda 分配
            Application.focusChanged += InvokeApplicationFocus;
            Application.quitting += InvokeApplicationQuit;
        }

        private static void UnregisterBuiltinDrivers()
        {
            PlayerLoopDriver.SetCoreUpdateCallback(null);
            PlayerLoopDriver.SetCoreFixedUpdateCallback(null);
            PlayerLoopDriver.SetCoreLateUpdateCallback(null);
            PlayerLoopDriver.RemoveDrawGizmosCallback(DrawGizmos);

            Application.focusChanged -= InvokeApplicationFocus;
            Application.quitting -= InvokeApplicationQuit;
        }

#if UNITY_EDITOR
        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                // 编辑器退出 Play 时清理服务系统：不依赖域重载（兼容 Enter Play Mode Options 跳过域重载的场景）
                Shutdown();
            }
        }
#endif

        private static void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            // 场景卸载时销毁 Gameplay 和 Scene 容器
            // ShutdownContainer 内部按逆拓扑序关闭服务
            GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
            GameServices.ShutdownContainer(EServiceScopeKind.Scene);
        }

        // 帧时钟由 PlayerLoopDriver 在各阶段入口采样，此处直接读取本帧快照
        private static void Tick()
        {
            if (IsShutdown) return;

            GameServices.Tick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private static void FixedTick()
        {
            if (IsShutdown) return;
            
            GameServices.FixedTick(GameTime.fixedDeltaTime, GameTime.unscaledDeltaTime);
        }

        private static void LateTick()
        {
            if (IsShutdown) return;

            GameServices.LateTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        /// <summary>
        /// 逐项调用广播，单项异常不截断其余项（低频生命周期事件专用）。
        /// </summary>
        /// <remarks>
        /// 关闭与失焦回执的职责就是清理与联动，截断等于静默漏掉后续每一项的响应，故开发构建也不上抛，异常按 Error 级带栈记录。 <br />
        /// <c>GetInvocationList</c> 每次调用有分配，不得用于帧热路径。
        /// </remarks>
        private static void InvokeQuarantined(Action handler, string stageName)
        {
            if (handler == null) return;

            Delegate[] invocations = handler.GetInvocationList();
            for (int i = 0; i < invocations.Length; i++)
            {
                Action item = (Action)invocations[i];
                try
                {
                    item();
                }
                catch (Exception exception)
                {
                    LogUtility.Error("GameApp.{0} subscriber '{1}.{2}' threw: {3}",
                        stageName, item.Method.DeclaringType, item.Method.Name, exception);
                }
            }
        }

        /// <summary>
        /// 带负载的 <see cref="InvokeQuarantined(Action, string)"/> 重载（对焦回执）。
        /// </summary>
        private static void InvokeQuarantined<T>(Action<T> handler, T argument, string stageName)
        {
            if (handler == null) return;

            Delegate[] invocations = handler.GetInvocationList();
            for (int i = 0; i < invocations.Length; i++)
            {
                Action<T> item = (Action<T>)invocations[i];
                try
                {
                    item(argument);
                }
                catch (Exception exception)
                {
                    LogUtility.Error("GameApp.{0} subscriber '{1}.{2}' threw: {3}",
                        stageName, item.Method.DeclaringType, item.Method.Name, exception);
                }
            }
        }

        private static void DrawGizmos()
        {
            GameServices.DrawGizmos();
        }

        #endregion
    }
}
