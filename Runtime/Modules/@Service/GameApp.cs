using Moirai.Atropos.Audio;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Events;
using Moirai.Atropos.Input;
using Moirai.Atropos.Localization;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Save;
using Moirai.Atropos.Scene;
using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;
using Moirai.Atropos.UpdateDriver;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏入口。负责生命周期驱动与服务缓存。
    /// <para>服务访问：服务类内部用构造注入；非服务代码优先使用缓存属性（如 <see cref="Audio"/>、<see cref="Resource"/>、<see cref="UI"/>），
    /// 首次访问时从 Provider 懒加载一次并缓存；<see cref="Services"/> 仅供非标准查找使用。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(MoiraiExecutionOrder.GAME_APP_ORDER)]
    public partial class GameApp : MonoBehaviour
    {
        #region 公共属性 [PUBLIC PROPERTIES]

        private static bool s_IsShutdown = true;

        /// <summary>
        /// 获取游戏是否已关闭。
        /// </summary>
        public static bool IsShutdown => s_IsShutdown;

        /// <summary>
        /// 最深层活跃的服务提供者（Gameplay > Scene > App）。
        /// <para>非服务代码通过此属性访问服务；服务类应使用构造注入。</para>
        /// <para>关闭后返回 null——退出/重启场景中外部代码可能仍持有引用，安全返回 null 比抛异常更合理。</para>
        /// </summary>
        public static IServiceProvider Services => s_IsShutdown ? null : GameServices.Provider;

        #endregion

        #region 框架服务 [FRAMEWORK SERVICES]

        // 懒加载缓存：首次访问通过 Provider 查找一次，后续直接返回静态字段引用。
        // s_IsShutdown 为 true 时返回 null，避免 Shutdown 后访问指向已 Dispose 的实例。

        private static IUpdateDriverService s_UpdateDriver;
        /// <summary>
        /// 获取更新驱动服务。
        /// </summary>
        public static IUpdateDriverService UpdateDriver => s_IsShutdown ? null : s_UpdateDriver ??= Services.GetService<IUpdateDriverService>();

        private static IResourceService s_Resource;
        /// <summary>
        /// 获取资源服务。
        /// </summary>
        public static IResourceService Resource => s_IsShutdown ? null : s_Resource ??= Services.GetService<IResourceService>();

        private static IDebuggerService s_Debugger;
        /// <summary>
        /// 获取调试服务。
        /// </summary>
        public static IDebuggerService Debugger => s_IsShutdown ? null : s_Debugger ??= Services.GetService<IDebuggerService>();

        private static IAudioService s_Audio;
        /// <summary>
        /// 获取音频服务。
        /// </summary>
        public static IAudioService Audio => s_IsShutdown ? null : s_Audio ??= Services.GetService<IAudioService>();

        private static IObjectPoolService s_ObjectPool;
        /// <summary>
        /// 获取对象池服务。
        /// </summary>
        public static IObjectPoolService ObjectPool => s_IsShutdown ? null : s_ObjectPool ??= Services.GetService<IObjectPoolService>();

        private static IProcedureService s_Procedure;
        /// <summary>
        /// 获取流程管理服务。
        /// </summary>
        public static IProcedureService Procedure => s_IsShutdown ? null : s_Procedure ??= Services.GetService<IProcedureService>();

        private static ILocalizationService s_Localization;
        /// <summary>
        /// 获取多语言服务。
        /// </summary>
        public static ILocalizationService Localization => s_IsShutdown ? null : s_Localization ??= Services.GetService<ILocalizationService>();

        private static ISceneService s_Scene;
        /// <summary>
        /// 获取场景服务。
        /// </summary>
        public static ISceneService Scene => s_IsShutdown ? null : s_Scene ??= Services.GetService<ISceneService>();

        private static ITimerService s_Timer;
        /// <summary>
        /// 获取计时器服务。
        /// </summary>
        public static ITimerService Timer => s_IsShutdown ? null : s_Timer ??= Services.GetService<ITimerService>();

        private static IInputService s_Input;
        /// <summary>
        /// 获取输入服务。
        /// </summary>
        public static IInputService Input => s_IsShutdown ? null : s_Input ??= Services.GetService<IInputService>();

        private static ISaveService s_Save;
        /// <summary>
        /// 获取保存服务。
        /// </summary>
        public static ISaveService Save => s_IsShutdown ? null : s_Save ??= Services.GetService<ISaveService>();

        private static IUIService s_UI;
        /// <summary>
        /// 获取UI服务。
        /// </summary>
        public static IUIService UI => s_IsShutdown ? null : s_UI ??= Services.GetService<IUIService>();

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

            // 清除缓存的服务引用，Shutdown 后属性访问安全返回 null
            s_UpdateDriver = null;
            s_Resource = null;
            s_Debugger = null;
            s_Audio = null;
            s_ObjectPool = null;
            s_Procedure = null;
            s_Localization = null;
            s_Scene = null;
            s_Timer = null;
            s_Input = null;
            s_Save = null;
            s_UI = null;

            GameServices.Shutdown();
        }

        #endregion

        #region 低内存 [LOW MEMORY]

        /// <remarks>
        /// Application.lowMemory 由 Unity 在主线程触发（与 Application.focus/quit 一致），无需线程守卫。
        /// </remarks>
        private void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");

            ObjectPool?.ReleaseAllUnused();
            Resource?.ForceUnloadUnusedAssets(true);
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
