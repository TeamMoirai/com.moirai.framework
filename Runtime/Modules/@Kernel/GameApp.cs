using Moirai.Atropos.Audio;
using Moirai.Atropos.Events;
using Moirai.Atropos.Resource;
using Moirai.Atropos.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏入口。负责生命周期驱动与服务世界轮询。
    /// <para>服务访问：业务代码一律通过各服务的静态外观（如 <see cref="AudioService"/>、<see cref="ResourceService"/>、<see cref="UIService"/>）；
    /// 动态服务查找统一走 <see cref="GameServices.GetRequiredService{T}"/> 等静态方法。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(MoiraiExecutionOrder.GAME_APP_ORDER)]
    public partial class GameApp : MonoBehaviour
    {
        #region 状态 [STATE]

        private static bool s_IsShutdown = true;

        /// <summary>
        /// 获取游戏是否已关闭。
        /// </summary>
        public static bool IsShutdown => s_IsShutdown;

        #endregion

        #region 引擎方法 [UNITY METHODS]

        private void Awake()
        {
            LogUtility.Info("GameApp Active");
            s_IsShutdown = false;

            gameObject.name = $"[{nameof(GameApp)}]";
            DontDestroyOnLoad(gameObject);

            // 注意：sceneUnloaded 在场景对象销毁之后触发（Unity 无"卸载前"全局事件），
            // 因此 Scene/Gameplay 服务的 Shutdown() 不得访问场景对象。
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            Application.lowMemory += OnLowMemory;

            GameTime.StartFrame();
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            Application.lowMemory -= OnLowMemory;
            Shutdown();
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
#endif
        }

        private void Update()
        {
            if (s_IsShutdown) return;
            GameTime.StartFrame();
            GameServices.Tick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            if (s_IsShutdown) return;
            GameTime.StartFrame();
            GameServices.FixedTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            if (s_IsShutdown) return;
            GameTime.StartFrame();
            GameServices.LateTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            GameAppMessageEvent.Trigger(
                hasFocus ? EMessageEventType.ApplicationFocus : EMessageEventType.NotApplicationFocus);
        }

        private void OnApplicationQuit()
        {
            GameAppMessageEvent.Trigger(EMessageEventType.ApplicationQuit);
            StopAllCoroutines();
        }

        private void OnDrawGizmos()
        {
            GameServices.DrawGizmos();
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            // 场景卸载时销毁 Gameplay 和 Scene 容器
            // ShutdownContainer 内部按逆拓扑序关闭服务
            GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
            GameServices.ShutdownContainer(EServiceScopeKind.Scene);
        }

        #endregion

        #region 静态方法 [STATIC METHODS]

        /// <summary>
        /// 关闭游戏框架。幂等——重复调用安全。
        /// 统一入口：编辑器退出 Play 模式和 OnDestroy 均通过此方法清理。
        /// </summary>
        public static void Shutdown()
        {
            if (s_IsShutdown) return;

            LogUtility.Info("GameApp Shutdown");
            s_IsShutdown = true;

            GameServices.Shutdown();
        }

        #endregion

        #region 低内存 [LOW MEMORY]

        /// <remarks>
        /// Application.lowMemory 由 Unity 在主线程触发（与 Application.focus/quit 一致），无需线程守卫。
        /// 两个池服务的 Handler 各自订阅 lowMemory 并自行收缩；此处仅驱动资源层卸载。
        /// </remarks>
        private void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");

            ResourceService.ForceUnloadUnusedAssets(true);
        }

        #endregion

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
    }
}
