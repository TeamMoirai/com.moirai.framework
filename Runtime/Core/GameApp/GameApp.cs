using System;
using System.Collections;
using Moirai.Atropos.Events;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架静态外观：生命周期、协程、帧与 Unity 事件订阅。
    /// <para>本类不含任何 MonoBehaviour 成员：帧逻辑订阅由 <see cref="PlayerLoopDriver"/> 的静态注册表驱动，
    /// Unity 只在 MonoBehaviour 上派发的消息（协程 / Gizmos / ApplicationPause）由 <see cref="GameAppHost"/> 承接。</para>
    /// </summary>
    public partial class GameApp
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取游戏是否已关闭。
        /// </summary>
        public static bool IsShutdown { get; private set; } = true;

        /// <summary>
        /// 获取或设置游戏帧率。
        /// </summary>
        public static int FrameRate
        {
            get => GameAppSettings.Instance.m_FrameRate;
            set => Application.targetFrameRate = GameAppSettings.Instance.m_FrameRate = value;
        }

        /// <summary>
        /// 获取或设置游戏速度。
        /// </summary>
        public static float GameSpeed
        {
            get => GameAppSettings.Instance.m_GameSpeed;
            set => Time.timeScale = GameAppSettings.Instance.m_GameSpeed = value >= 0f ? value : 0f;
        }

        /// <summary>
        /// 获取游戏是否暂停。
        /// </summary>
        public static bool IsGamePaused => GameAppSettings.Instance.m_GameSpeed <= 0f;

        /// <summary>
        /// 获取是否正常游戏速度。
        /// </summary>
        public static bool IsNormalGameSpeed => Math.Abs(GameAppSettings.Instance.m_GameSpeed - 1f) < 0.01f;

        /// <summary>
        /// 获取或设置是否允许后台运行。
        /// </summary>
        public static bool RunInBackground
        {
            get => GameAppSettings.Instance.m_RunInBackground;
            set => Application.runInBackground = GameAppSettings.Instance.m_RunInBackground = value;
        }

        /// <summary>
        /// 获取或设置是否禁止休眠。
        /// </summary>
        public static bool NeverSleep
        {
            get => GameAppSettings.Instance.m_NeverSleep;
            set
            {
                GameAppSettings.Instance.m_NeverSleep = value;
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
        /// 关闭游戏框架。幂等——重复调用安全。
        /// 统一入口：编辑器退出 Play 模式与 ApplicationQuit 均通过此方法清理。
        /// </summary>
        internal static void Shutdown()
        {
            if (IsShutdown) return;

            LogUtility.Info("GameApp Shutdown");
            IsShutdown = true;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif

            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            // Destroy 订阅由 PlayerLoopDriver.Shutdown 广播并清空；随后恢复默认 PlayerLoop
            UnregisterBuiltinDrivers();
            PlayerLoopDriver.Shutdown();

            GameServices.Shutdown();
            GameAppHost.Release();

            // 释放缓存的从进程的非托管内存中分配的内存。
            MarshalUtility.FreeCachedHGlobal();
        }

        #endregion

        #region 公共 API [PUBLIC API]

        private static float s_GameSpeedBeforePause = 1f;

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public static void PauseGame()
        {
            if (IsGamePaused) return;

            s_GameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public static void ResumeGame()
        {
            if (!IsGamePaused) return;

            GameSpeed = s_GameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
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
        public static Coroutine StartCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return null;

            GameAppHost host = GameAppHost.Instance;
            return host != null ? host.StartCoroutine(methodName) : null;
        }

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;

            GameAppHost host = GameAppHost.Instance;
            return host != null ? host.StartCoroutine(routine) : null;
        }

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(string methodName, object value)
        {
            if (string.IsNullOrEmpty(methodName)) return null;

            GameAppHost host = GameAppHost.Instance;
            return host != null ? host.StartCoroutine(methodName, value) : null;
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return;

            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopCoroutine(methodName);
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
        public static void AddUpdateListener(Action action)
        {
            PlayerLoopDriver.AddUpdateCallback(action);
        }

        /// <summary>
        /// 添加物理帧更新事件。
        /// </summary>
        public static void AddFixedUpdateListener(Action action)
        {
            PlayerLoopDriver.AddFixedUpdateCallback(action);
        }

        /// <summary>
        /// 添加Late帧更新事件。
        /// </summary>
        public static void AddLateUpdateListener(Action action)
        {
            PlayerLoopDriver.AddLateUpdateCallback(action);
        }

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        public static void RemoveUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveUpdateCallback(action);
        }

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        public static void RemoveFixedUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveFixedUpdateCallback(action);
        }

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        public static void RemoveLateUpdateListener(Action action)
        {
            PlayerLoopDriver.RemoveLateUpdateCallback(action);
        }

        #endregion

        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 注册Destroy事件。在 <see cref="Shutdown"/> 时广播。
        /// </summary>
        public static void AddDestroyListener(Action action)
        {
            PlayerLoopDriver.AddDestroyCallback(action);
        }

        /// <summary>
        /// 反注册Destroy事件。
        /// </summary>
        public static void RemoveDestroyListener(Action action)
        {
            PlayerLoopDriver.RemoveDestroyCallback(action);
        }

        /// <summary>
        /// 注册OnDrawGizmos事件（仅编辑器）。
        /// <para>订阅写入 <see cref="PlayerLoopDriver"/> 静态表，宿主销毁不丢失；此处只确保派发者存在。</para>
        /// </summary>
        public static void AddOnDrawGizmosListener(Action action)
        {
            PlayerLoopDriver.AddDrawGizmosCallback(action);
            GameAppHost.Bootstrap();
        }

        /// <summary>
        /// 反注册OnDrawGizmos事件。
        /// </summary>
        public static void RemoveOnDrawGizmosListener(Action action)
        {
            PlayerLoopDriver.RemoveDrawGizmosCallback(action);
        }

        /// <summary>
        /// 注册OnDrawGizmosSelected事件（仅编辑器）。
        /// </summary>
        public static void AddOnDrawGizmosSelectedListener(Action action)
        {
            PlayerLoopDriver.AddDrawGizmosSelectedCallback(action);
            GameAppHost.Bootstrap();
        }

        /// <summary>
        /// 反注册OnDrawGizmosSelected事件。
        /// </summary>
        public static void RemoveOnDrawGizmosSelectedListener(Action action)
        {
            PlayerLoopDriver.RemoveDrawGizmosSelectedCallback(action);
        }

        /// <summary>
        /// 注册OnApplicationPause事件。
        /// <para>暂停回调只能由 MonoBehaviour 消息派发，故注册时一并物化 <see cref="GameAppHost"/>。</para>
        /// </summary>
        public static void AddOnApplicationPauseListener(Action<bool> action)
        {
            PlayerLoopDriver.AddApplicationPauseCallback(action);
            GameAppHost.Bootstrap();
        }

        /// <summary>
        /// 反注册OnApplicationPause事件。
        /// </summary>
        public static void RemoveOnApplicationPauseListener(Action<bool> action)
        {
            PlayerLoopDriver.RemoveApplicationPauseCallback(action);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static void RegisterBuiltinDrivers()
        {
            PlayerLoopDriver.AddUpdateCallback(Tick);
            PlayerLoopDriver.AddFixedUpdateCallback(FixedTick);
            PlayerLoopDriver.AddLateUpdateCallback(LateTick);
            PlayerLoopDriver.AddDrawGizmosCallback(DrawGizmos);
            PlayerLoopDriver.AddApplicationFocusCallback(ApplicationFocus);
            PlayerLoopDriver.AddApplicationQuitCallback(ApplicationQuit);
        }

        private static void UnregisterBuiltinDrivers()
        {
            PlayerLoopDriver.RemoveUpdateCallback(Tick);
            PlayerLoopDriver.RemoveFixedUpdateCallback(FixedTick);
            PlayerLoopDriver.RemoveLateUpdateCallback(LateTick);
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

            GameServices.FixedTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
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
            Shutdown();
        }

        private static void DrawGizmos()
        {
            GameServices.DrawGizmos();
        }

        #endregion
    }
}
