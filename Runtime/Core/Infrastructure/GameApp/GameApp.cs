using System;
using System.Collections;
using Moirai.Atropos.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架静态外观：生命周期、协程、帧与 Unity 事件订阅。
    /// </summary>
    /// <remarks>
    /// 本类不含 MonoBehaviour 成员：帧逻辑订阅由 <see cref="PlayerLoopDriver"/> 静态表驱动，
    /// 只在 MonoBehaviour 上派发的消息（协程 / Gizmos / ApplicationPause）由 <see cref="GameAppHost"/> 承接。
    /// </remarks>
    public partial class GameApp
    {
        #region 订阅句柄 [SUBSCRIPTION]

        /// <summary>
        /// <see cref="GameApp"/> 各类订阅的可注销句柄。
        /// </summary>
        /// <remarks>
        /// 注册表按委托相等比较注销，lambda 每次求值都是新实例、摘不掉；句柄在注册时攥住确切实例，lambda 也能干净注销。 <br />
        /// 非线程安全，只在主线程创建与释放；框架 <see cref="Shutdown"/> 后 Dispose 是空操作。
        /// </remarks>
        public sealed class Subscription : IDisposable
        {
            private Action _disposeAction;

            internal Subscription(Action disposeAction)
            {
                _disposeAction = disposeAction;
            }

            /// <summary>是否仍处于订阅状态（未 Dispose 过）。</summary>
            public bool IsSubscribed => _disposeAction != null;

            /// <summary>
            /// 注销订阅。幂等——重复调用安全。
            /// </summary>
            public void Dispose()
            {
                Action dispose = _disposeAction;
                if (dispose == null) return;

                _disposeAction = null;
                dispose();
            }
        }

        #endregion

        #region 属性 [PROPERTIES]

        // 引擎状态的门面而非框架存活状态：由 Initialize 从配置播种，运行期写入即刻作用于引擎；
        // IsShutdown 之后仍可读写不抛，下次 Initialize 重新播种为配置基线。
        private static int s_FrameRate;
        private static float s_GameSpeed = 1f;
        private static bool s_RunInBackground;
        private static bool s_NeverSleep;
        private static int s_PauseDepth;

        /// <summary>获取游戏是否已关闭。</summary>
        public static bool IsShutdown { get; internal set; } = true;

        /// <summary>获取或设置游戏帧率。</summary>
        public static int FrameRate
        {
            get => s_FrameRate;
            set => Application.targetFrameRate = s_FrameRate = value;
        }

        /// <summary>获取或设置期望的游戏速度（映射到 <c>Time.timeScale</c>）。</summary>
        /// <remarks>
        /// 暂停期间写入只更新解除暂停后的目标值，<c>Time.timeScale</c> 保持 0（暂停优先）。 <br />
        /// 判断时间是否真正冻结请读 <c>GameSpeed &lt;= 0</c> 或 <c>Time.timeScale</c>，不要读 <see cref="IsGamePaused"/>。
        /// </remarks>
        public static float GameSpeed
        {
            get => s_GameSpeed;
            set
            {
                s_GameSpeed = value >= 0f ? value : 0f;

                // 暂停中只记下期望值，等 ResumeGame 把计数降到 0 时再生效
                if (s_PauseDepth == 0) Time.timeScale = s_GameSpeed;
            }
        }

        /// <summary>获取游戏是否被暂停，即 <see cref="PauseGame"/> 的引用计数是否非零。</summary>
        /// <remarks>与速度解耦：把速度调到 0 不算暂停。</remarks>
        public static bool IsGamePaused => s_PauseDepth > 0;

        /// <summary>获取当前的暂停请求层数（<see cref="PauseGame"/> 加一、<see cref="ResumeGame"/> 减一）。</summary>
        /// <remarks>仅供调试定位未配对 <see cref="ResumeGame"/> 的层；判暂停请读 <see cref="IsGamePaused"/>。</remarks>
        internal static int PauseDepth => s_PauseDepth;

        /// <summary>获取是否正常游戏速度（期望值约等于 1，容差 0.01）。暂停不影响本判定。</summary>
        public static bool IsNormalGameSpeed => Mathf.Abs(s_GameSpeed - 1f) < 0.01f;

        /// <summary>获取或设置是否允许后台运行。</summary>
        public static bool RunInBackground
        {
            get => s_RunInBackground;
            set => Application.runInBackground = s_RunInBackground = value;
        }

        /// <summary>获取或设置是否禁止休眠。</summary>
        public static bool NeverSleep
        {
            get => s_NeverSleep;
            set
            {
                s_NeverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

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

            GameServices.Shutdown();
            if (!quitting) GameAppHost.Release();

            // 释放缓存的从进程的非托管内存中分配的内存。
            MarshalUtility.FreeCachedHGlobal();
        }

        #endregion

        #region 公共 API [PUBLIC API]

        /// <summary>
        /// 暂停游戏：引用计数式，各来源须各自 <see cref="ResumeGame"/> 才真正恢复。
        /// </summary>
        /// <remarks>计数 0→1 时把 <c>Time.timeScale</c> 压到 0，已在暂停中只加计数；暂停期间的 <see cref="GameSpeed"/> 写入只更新恢复目标。</remarks>
        public static void PauseGame()
        {
            if (s_PauseDepth == 0) Time.timeScale = 0f;

            s_PauseDepth++;
        }

        /// <summary>
        /// 恢复游戏，<see cref="PauseGame"/> 的逆操作；计数归零才真正回速。
        /// </summary>
        /// <remarks>计数已为 0 时是空操作，不会把 <c>Time.timeScale</c> 拉回陈旧值。</remarks>
        public static void ResumeGame()
        {
            if (s_PauseDepth == 0) return;

            if (--s_PauseDepth == 0) Time.timeScale = s_GameSpeed;
        }

        /// <summary>
        /// 重置为正常游戏速度。暂停中调用只更新恢复目标，不会顺手解除暂停。
        /// </summary>
        public static void ResetGameSpeed()
        {
            if (IsNormalGameSpeed) return;

            GameSpeed = 1f;
        }

        #endregion

        #region 控制协程 [COROUTINE CONTROL]

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        /// <param name="routine">协程枚举器；<c>null</c> 时静默返回 <c>null</c>。</param>
        /// <returns>协程句柄；宿主不可用（框架已关闭，或处于退出窗口）时为 <c>null</c> 并告警。</returns>
        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;

            GameAppHost host = GameAppHost.Instance;
            if (host == null)
            {
                // 与「参数为空」区分开：宿主取不到意味着协程根本没跑，而调用方拿到的只是无声的 null
                LogUtility.Warning(
                    "GameApp.StartCoroutine: {0} 未启动——GameAppHost 不可用（框架已 Shutdown 或处于退出窗口）。",
                    routine.GetType().FullName);
                return null;
            }

            return host.StartCoroutine(routine);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(IEnumerator routine)
        {
            if (routine == null) return;

            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopCoroutine(routine);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(Coroutine routine)
        {
            if (routine == null) return;

            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopCoroutine(routine);
        }

        /// <summary>
        /// 停止所有全局协程。
        /// </summary>
        public static void StopAllCoroutines()
        {
            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopAllCoroutines();
        }

        #endregion

        #region 注入 Unity Update [INJECT UNITY UPDATE]

        /// <summary>
        /// 添加帧更新事件。订阅写入 <see cref="PlayerLoopDriver"/> 静态表，宿主销毁不丢失。
        /// </summary>
        /// <param name="action">帧回调；<c>null</c> 时不注册并返回 <c>null</c>。</param>
        /// <returns>可用于注销的句柄（同参数重复注册会被驱动去重，任一持有句柄 Dispose 即注销该唯一登记）。
        /// 用 lambda 注册时<b>只能</b>靠本句柄注销——<see cref="RemoveUpdateListener"/> 按委托相等比较，
        /// 再写一个同样体的 lambda 是新实例，摘不掉。</returns>
        public static Subscription AddUpdateListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddUpdateCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveUpdateCallback(action));
        }

        /// <summary>
        /// 添加物理帧更新事件。语义同 <see cref="AddUpdateListener"/>。
        /// </summary>
        public static Subscription AddFixedUpdateListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddFixedUpdateCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveFixedUpdateCallback(action));
        }

        /// <summary>
        /// 添加Late帧更新事件。语义同 <see cref="AddUpdateListener"/>。
        /// </summary>
        public static Subscription AddLateUpdateListener(Action action)
        {
            if (action == null) return null;

            PlayerLoopDriver.AddLateUpdateCallback(action);
            return new Subscription(() => PlayerLoopDriver.RemoveLateUpdateCallback(action));
        }

        /// <summary>
        /// 移除帧更新事件。仅对能取回同一委托实例的注册有效（方法组、缓存于字段的委托）。
        /// </summary>
        public static void RemoveUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveUpdateCallback(action);
        }

        /// <summary>
        /// 移除物理帧更新事件。有效性同 <see cref="RemoveUpdateListener"/>。
        /// </summary>
        public static void RemoveFixedUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveFixedUpdateCallback(action);
        }

        /// <summary>
        /// 移除Late帧更新事件。有效性同 <see cref="RemoveUpdateListener"/>。
        /// </summary>
        public static void RemoveLateUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveLateUpdateCallback(action);
        }

        #endregion

        #region 注入帧逻辑 Handler [INJECT FRAME HANDLER]

        /// <summary>
        /// 订阅帧逻辑到 Update 阶段（接口式）。
        /// </summary>
        /// <remarks>
        /// Action 适合无状态挂钩，Handler 适合携带状态、需要阶段内顺序的系统（实现 <see cref="IPlayerLoopPriority"/> 控制先后，数值小者先跑）。 <br />
        /// 同一实例重复注册被忽略，驱动中调用延迟到本阶段结束后提交；注册表持强引用，注销必须成对，热路径禁止堆分配。
        /// </remarks>
        public static void AddUpdateHandler(IUpdateHandler handler)
        {
            PlayerLoopDriver.Register(handler);
        }

        /// <summary>
        /// 反注册 Update 阶段 Handler。
        /// </summary>
        public static void RemoveUpdateHandler(IUpdateHandler handler)
        {
            PlayerLoopDriver.Unregister(handler);
        }

        /// <summary>
        /// 订阅帧逻辑到 FixedUpdate 阶段。语义同 <see cref="AddUpdateHandler"/>。
        /// </summary>
        public static void AddFixedUpdateHandler(IFixedUpdateHandler handler)
        {
            PlayerLoopDriver.Register(handler);
        }

        /// <summary>
        /// 反注册 FixedUpdate 阶段 Handler。
        /// </summary>
        public static void RemoveFixedUpdateHandler(IFixedUpdateHandler handler)
        {
            PlayerLoopDriver.Unregister(handler);
        }

        /// <summary>
        /// 订阅帧逻辑到 LateUpdate 阶段（晚于 <c>MonoBehaviour.LateUpdate</c>）。
        /// </summary>
        public static void AddLateUpdateHandler(ILateUpdateHandler handler)
        {
            PlayerLoopDriver.Register(handler);
        }

        /// <summary>
        /// 反注册 LateUpdate 阶段 Handler。
        /// </summary>
        public static void RemoveLateUpdateHandler(ILateUpdateHandler handler)
        {
            PlayerLoopDriver.Unregister(handler);
        }

        /// <summary>
        /// 把对象注册到它实现的每一个帧阶段（Update / FixedUpdate / LateUpdate）。
        /// </summary>
        public static void AddFrameHandler(object handler)
        {
            PlayerLoopDriver.RegisterAll(handler);
        }

        /// <summary>
        /// 从其曾注册的全部帧阶段注销。语义同 <see cref="AddFrameHandler"/> 的逆。
        /// </summary>
        public static void RemoveFrameHandler(object handler)
        {
            PlayerLoopDriver.UnregisterAll(handler);
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
            PlayerLoopDriver.AddApplicationFocusCallback(ApplicationFocus);
            PlayerLoopDriver.AddApplicationQuitCallback(ApplicationQuit);
        }

        private static void UnregisterBuiltinDrivers()
        {
            PlayerLoopDriver.SetCoreUpdateCallback(null);
            PlayerLoopDriver.SetCoreFixedUpdateCallback(null);
            PlayerLoopDriver.SetCoreLateUpdateCallback(null);
            PlayerLoopDriver.RemoveDrawGizmosCallback(DrawGizmos);
            PlayerLoopDriver.RemoveApplicationFocusCallback(ApplicationFocus);
            PlayerLoopDriver.RemoveApplicationQuitCallback(ApplicationQuit);
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

        private static void ApplicationFocus(bool hasFocus)
        {
            if (hasFocus) GameAppMessageEvent.ApplicationFocus();
            else GameAppMessageEvent.NotApplicationFocus();
        }

        private static void ApplicationQuit()
        {
            GameAppMessageEvent.ApplicationQuit();
            Shutdown(quitting: true);
        }

        private static void DrawGizmos()
        {
            GameServices.DrawGizmos();
        }

        #endregion
    }
}
