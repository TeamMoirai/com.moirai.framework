using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Events;
using Moirai.Atropos.Resource;
using Moirai.Atropos.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏入口。负责生命周期驱动与服务世界轮询。
    /// <para>服务访问：业务代码一律通过各服务的静态外观（如 <see cref="AudioService"/>、<see cref="ResourceService"/>、<see cref="UIService"/>）；
    /// 动态服务查找统一走 <see cref="GameServices.GetRequiredService{T}"/> 等静态方法。</para>
    /// </summary>
    public partial class GameApp
    {
        #region 状态 [STATE]

        private static GameObject s_Entity;
        private static MainBehaviour s_Behaviour;

        private static bool s_IsShutdown = true;

        /// <summary>
        /// 获取游戏是否已关闭。
        /// </summary>
        public static bool IsShutdown => s_IsShutdown;

        #endregion

        #region 生命周期 [LIFECYCLE]

        internal static void Initialize()
        {
            if (!s_IsShutdown) return;

            LogUtility.Info("GameApp Active");
            s_IsShutdown = false;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif

            // 注意：sceneUnloaded 在场景对象销毁之后触发（Unity 无"卸载前"全局事件），
            // 因此 Scene/Gameplay 服务的 Shutdown() 不得访问场景对象。
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            MakeEntity();
            GameTime.StartFrame();
        }

        /// <summary>
        /// 关闭游戏框架。幂等——重复调用安全。
        /// 统一入口：编辑器退出 Play 模式和 OnDestroy 均通过此方法清理。
        /// </summary>
        internal static void Shutdown()
        {
            if (s_IsShutdown) return;

            LogUtility.Info("GameApp Shutdown");
            s_IsShutdown = true;

#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif

            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            GameServices.Shutdown();
            if (s_Entity != null) Object.Destroy(s_Entity);
        }

        #endregion

        #region 控制协程 [COROUTINE CONTROL]

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            return s_Behaviour.StartCoroutine(methodName);
        }

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null)
            {
                return null;
            }

            return s_Behaviour.StartCoroutine(routine);
        }

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(string methodName, object value)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            return s_Behaviour.StartCoroutine(methodName, value);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return;
            }

            if (s_Entity != null)
            {
                s_Behaviour.StopCoroutine(methodName);
            }
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(IEnumerator routine)
        {
            if (routine == null) return;

            if (s_Entity != null)
            {
                s_Behaviour.StopCoroutine(routine);
            }
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(Coroutine routine)
        {
            if (routine == null) return;

            if (s_Entity != null)
            {
                s_Behaviour.StopCoroutine(routine);
                routine = null;
            }
        }

        /// <summary>
        /// 停止所有全局协程。
        /// </summary>
        public static void StopAllCoroutines()
        {
            if (s_Entity != null)
            {
                s_Behaviour.StopAllCoroutines();
            }
        }

        #endregion

        #region 注入 Unity Update [INJECT UNITY UPDATE]

        /// <summary>
        /// 添加帧更新事件。
        /// </summary>
        public static void AddUpdateListener(Action action)
        {
            AddUpdateListenerImp(action).Forget();
        }

        private static async UniTaskVoid AddUpdateListenerImp(Action action)
        {
            await UniTask.Yield();
            s_Behaviour.AddUpdateEvent(action);
        }

        /// <summary>
        /// 添加物理帧更新事件。
        /// </summary>
        public static void AddFixedUpdateListener(Action action)
        {
            AddFixedUpdateListenerImp(action).Forget();
        }

        private static async UniTaskVoid AddFixedUpdateListenerImp(Action action)
        {
            await UniTask.Yield(PlayerLoopTiming.LastEarlyUpdate);
            s_Behaviour.AddFixedUpdateEvent(action);
        }

        /// <summary>
        /// 添加Late帧更新事件。
        /// </summary>
        public static void AddLateUpdateListener(Action action)
        {
            AddLateUpdateListenerImp(action).Forget();
        }

        private static async UniTaskVoid AddLateUpdateListenerImp(Action action)
        {
            await UniTask.Yield();
            s_Behaviour.AddLateUpdateEvent(action);
        }

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        public static void RemoveUpdateListener(Action action)
        {
            s_Behaviour.RemoveUpdateEvent(action);
        }

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        public static void RemoveFixedUpdateListener(Action action)
        {
            s_Behaviour.RemoveFixedUpdateEvent(action);
        }

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        public static void RemoveLateUpdateListener(Action action)
        {
            s_Behaviour.RemoveLateUpdateEvent(action);
        }

        #endregion

        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 注册Destroy事件。
        /// </summary>
        public static void AddDestroyListener(Action action)
        {
            s_Behaviour.AddDestroyEvent(action);
        }

        /// <summary>
        /// 反注册Destroy事件。
        /// </summary>
        public static void RemoveDestroyListener(Action action)
        {
            s_Behaviour.RemoveDestroyEvent(action);
        }

        /// <summary>
        /// 注册OnDrawGizmos事件。
        /// </summary>
        public static void AddOnDrawGizmosListener(Action action)
        {
            s_Behaviour.AddDrawGizmosEvent(action);
        }

        /// <summary>
        /// 反注册OnDrawGizmos事件。
        /// </summary>
        public static void RemoveOnDrawGizmosListener(Action action)
        {
            s_Behaviour.RemoveDrawGizmosEvent(action);
        }

        /// <summary>
        /// 注册OnDrawGizmosSelected事件。
        /// </summary>
        public static void AddOnDrawGizmosSelectedListener(Action action)
        {
            s_Behaviour.AddDrawGizmosSelectedEvent(action);
        }

        /// <summary>
        /// 反注册OnDrawGizmosSelected事件。
        /// </summary>
        public static void RemoveOnDrawGizmosSelectedListener(Action action)
        {
            s_Behaviour.RemoveDrawGizmosSelectedEvent(action);
        }

        /// <summary>
        /// 注册OnApplicationPause事件。
        /// </summary>
        public static void AddOnApplicationPauseListener(Action<bool> action)
        {
            s_Behaviour.AddApplicationPauseEvent(action);
        }

        /// <summary>
        /// 反注册OnApplicationPause事件。
        /// </summary>
        public static void RemoveOnApplicationPauseListener(Action<bool> action)
        {
            s_Behaviour.RemoveApplicationPauseEvent(action);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static void MakeEntity()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif

            if (s_Entity != null) return;

            s_Entity = new GameObject("[UpdateDriver]");
            s_Entity.SetActive(true);
            Object.DontDestroyOnLoad(s_Entity);
            s_Behaviour = s_Entity.AddComponent<MainBehaviour>();

            // 驱动内置服务
            s_Behaviour.AddUpdateEvent(Tick);
            s_Behaviour.AddFixedUpdateEvent(FixedTick);
            s_Behaviour.AddLateUpdateEvent(LateTick);
            s_Behaviour.AddApplicationFocusEvent(ApplicationFocus);
            s_Behaviour.AddApplicationQuitEvent(ApplicationQuit);
            s_Behaviour.AddDrawGizmosEvent(DrawGizmos);
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
            if (s_IsShutdown) return;
            GameTime.StartFrame();
            GameServices.Tick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private static void FixedTick()
        {
            if (s_IsShutdown) return;
            GameTime.StartFrame();
            GameServices.FixedTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private static void LateTick()
        {
            if (s_IsShutdown) return;
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
