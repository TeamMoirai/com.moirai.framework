using System;
using System.Collections;
using Moirai.Atropos.Events;
using Moirai.Atropos.FrameLoop;
using UnityEngine;
using UnityEngine.SceneManagement;
using UObject = UnityEngine.Object;

namespace Moirai.Atropos
{
    public partial class GameApp
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 协程 / Editor Gizmos / ApplicationPause 宿主。
        /// <para>帧逻辑驱动已迁至 <see cref="PlayerLoopDriver"/>——本宿主被销毁不再丢失逻辑订阅。</para>
        /// </summary>
        private static GameObject s_Entity;
        private static CoroutineHost s_Behaviour;

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

            // 协程宿主：仅供 Coroutine / Gizmos / ApplicationPause，不承载帧订阅
            MakeCoroutineHost();

            GameTime.StartFrame();
        }

        /// <summary>
        /// 关闭游戏框架。幂等——重复调用安全。
        /// 统一入口：编辑器退出 Play 模式和 OnDestroy 均通过此方法清理。
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
            if (s_Entity != null) UObject.Destroy(s_Entity);
            s_Entity = null;
            s_Behaviour = null;

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

            MakeCoroutineHost();
            return s_Behaviour != null ? s_Behaviour.StartCoroutine(methodName) : null;
        }

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;

            MakeCoroutineHost();
            return s_Behaviour != null ? s_Behaviour.StartCoroutine(routine) : null;
        }

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(string methodName, object value)
        {
            if (string.IsNullOrEmpty(methodName)) return null;

            MakeCoroutineHost();
            return s_Behaviour != null ? s_Behaviour.StartCoroutine(methodName, value) : null;
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return;

            s_Behaviour?.StopCoroutine(methodName);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(IEnumerator routine)
        {
            if (routine == null) return;

            s_Behaviour?.StopCoroutine(routine);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(Coroutine routine)
        {
            if (routine == null) return;

            s_Behaviour?.StopCoroutine(routine);
        }

        /// <summary>
        /// 停止所有全局协程。
        /// </summary>
        public static void StopAllCoroutines()
        {
            s_Behaviour?.StopAllCoroutines();
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
        /// 注册OnDrawGizmos事件（仅编辑器，仍需 CoroutineHost）。
        /// </summary>
        public static void AddOnDrawGizmosListener(Action action)
        {
            MakeCoroutineHost();
            s_Behaviour?.AddDrawGizmosEvent(action);
        }

        /// <summary>
        /// 反注册OnDrawGizmos事件。
        /// </summary>
        public static void RemoveOnDrawGizmosListener(Action action)
        {
            s_Behaviour?.RemoveDrawGizmosEvent(action);
        }

        /// <summary>
        /// 注册OnDrawGizmosSelected事件（仅编辑器，仍需 CoroutineHost）。
        /// </summary>
        public static void AddOnDrawGizmosSelectedListener(Action action)
        {
            MakeCoroutineHost();
            s_Behaviour?.AddDrawGizmosSelectedEvent(action);
        }

        /// <summary>
        /// 反注册OnDrawGizmosSelected事件。
        /// </summary>
        public static void RemoveOnDrawGizmosSelectedListener(Action action)
        {
            s_Behaviour?.RemoveDrawGizmosSelectedEvent(action);
        }

        /// <summary>
        /// 注册OnApplicationPause事件。
        /// </summary>
        public static void AddOnApplicationPauseListener(Action<bool> action)
        {
            PlayerLoopDriver.AddApplicationPauseCallback(action);
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
            PlayerLoopDriver.AddApplicationFocusCallback(ApplicationFocus);
            PlayerLoopDriver.AddApplicationQuitCallback(ApplicationQuit);
        }

        private static void UnregisterBuiltinDrivers()
        {
            PlayerLoopDriver.RemoveUpdateCallback(Tick);
            PlayerLoopDriver.RemoveFixedUpdateCallback(FixedTick);
            PlayerLoopDriver.RemoveLateUpdateCallback(LateTick);
            PlayerLoopDriver.RemoveApplicationFocusCallback(ApplicationFocus);
            PlayerLoopDriver.RemoveApplicationQuitCallback(ApplicationQuit);
        }

        private static void MakeCoroutineHost()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif

            // 宿主被意外销毁时惰性重建；帧订阅在 PlayerLoopDriver，不依赖本宿主
            if (s_Entity == null)
            {
                s_Entity = new GameObject("[CoroutineHost]");
                s_Entity.SetActive(true);
                UObject.DontDestroyOnLoad(s_Entity);
            }

            if (s_Behaviour == null)
            {
                s_Behaviour = s_Entity.GetComponent<CoroutineHost>();
                if (s_Behaviour == null)
                {
                    s_Behaviour = s_Entity.AddComponent<CoroutineHost>();
                }

                s_Behaviour.AddApplicationPauseEvent(PlayerLoopDriver.RaiseApplicationPause);
                s_Behaviour.AddDrawGizmosEvent(DrawGizmos);
            }
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

        private static void Tick()
        {
            if (IsShutdown) return;

            GameTime.StartFrame();
            GameServices.Tick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private static void FixedTick()
        {
            if (IsShutdown) return;

            GameTime.StartFrame();
            GameServices.FixedTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private static void LateTick()
        {
            if (IsShutdown) return;

            GameTime.StartFrame();
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
