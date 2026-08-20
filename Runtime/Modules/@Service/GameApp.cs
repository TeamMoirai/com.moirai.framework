using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Audio;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Events;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Input;
using Moirai.Atropos.Localization;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
using Moirai.Atropos.Save;
using Moirai.Atropos.Scene;
using Moirai.Atropos.Timer;
using Moirai.Atropos.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏服务。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public partial class GameApp : MonoBehaviour
    {
        #region 框架服务 [FRAMEWORK SERVICES]

        // 懒加载缓存：首次访问时通过 GetService 查找一次，后续直接返回静态字段引用。
        // s_IsShutdown 为 true 时直接返回 null，避免 Shutdown 后访问抛异常——
        // 退出/重启场景中外部代码可能仍持有 GameApp.XX 引用，此时安全返回 null 比抛异常更合理。

        private static bool s_IsShutdown = true;

        private static IDebuggerService s_Debugger;
        /// <summary>获取调试服务。</summary>
        public static IDebuggerService Debugger => s_IsShutdown ? null : s_Debugger ??= GameServices.GetService<IDebuggerService>(EServiceScopeKind.App);

        private static IFSMService s_FSM;
        /// <summary>获取有限状态机服务。</summary>
        public static IFSMService FSM => s_IsShutdown ? null : s_FSM ??= GameServices.GetService<IFSMService>(EServiceScopeKind.App);

        private static IProcedureService s_Procedure;
        /// <summary>流程管理服务。</summary>
        public static IProcedureService Procedure => s_IsShutdown ? null : s_Procedure ??= GameServices.GetService<IProcedureService>(EServiceScopeKind.App);

        private static IObjectPoolService s_ObjectPool;
        /// <summary>获取对象池服务。</summary>
        public static IObjectPoolService ObjectPool => s_IsShutdown ? null : s_ObjectPool ??= GameServices.GetService<IObjectPoolService>(EServiceScopeKind.App);

        private static IResourceService s_Resource;
        /// <summary>获取资源服务。</summary>
        public static IResourceService Resource => s_IsShutdown ? null : s_Resource ??= GameServices.GetService<IResourceService>(EServiceScopeKind.App);

        private static IAudioService s_Audio;
        /// <summary>获取音频服务。</summary>
        public static IAudioService Audio => s_IsShutdown ? null : s_Audio ??= GameServices.GetService<IAudioService>(EServiceScopeKind.App);

        private static IUIService s_UI;
        /// <summary>获取UI服务。</summary>
        public static IUIService UI => s_IsShutdown ? null : s_UI ??= GameServices.GetService<IUIService>(EServiceScopeKind.App);

        private static ILocalizationService s_Localization;
        /// <summary>获取多语言服务。</summary>
        public static ILocalizationService Localization => s_IsShutdown ? null : s_Localization ??= GameServices.GetService<ILocalizationService>(EServiceScopeKind.App);

        private static ISceneService s_Scene;
        /// <summary>获取场景服务。</summary>
        public static ISceneService Scene => s_IsShutdown ? null : s_Scene ??= GameServices.GetService<ISceneService>(EServiceScopeKind.App);

        private static ITimerService s_Timer;
        /// <summary>获取计时器服务。</summary>
        public static ITimerService Timer => s_IsShutdown ? null : s_Timer ??= GameServices.GetService<ITimerService>(EServiceScopeKind.App);

        private static IInputService s_Input;
        /// <summary>获取输入服务。</summary>
        public static IInputService Input => s_IsShutdown ? null : s_Input ??= GameServices.GetService<IInputService>(EServiceScopeKind.App);

        private static ISaveService s_Save;
        /// <summary>获取保存服务。</summary>
        public static ISaveService Save => s_IsShutdown ? null : s_Save ??= GameServices.GetService<ISaveService>(EServiceScopeKind.App);

        #endregion

        #region 引擎方法 [UNITY METHODS]

        /// <summary>
        /// 游戏框架服务初始化。
        /// </summary>
        private void Awake()
        {
            LogUtility.Info("GameApp Active");
            s_IsShutdown = false;

            gameObject.name = $"[{nameof(GameApp)}]";
            DontDestroyOnLoad(gameObject);

            // 注意：sceneUnloaded 在场景对象销毁之后触发（Unity 无"卸载前"全局事件），
            // 因此 Scene/Gameplay 服务的 Shutdown() 不得访问场景对象。
            SceneManager.sceneUnloaded += OnSceneUnloaded;

            InitializeAsync().Forget();

            Application.lowMemory += OnLowMemory;
            GameTime.StartFrame();
        }

        private static async UniTaskVoid InitializeAsync()
        {
            try
            {
                await GameServices.InitializeAsync();
                await ProcedureSettings.StartProcedure();
            }
            catch (Exception ex)
            {
                LogUtility.Error("GameApp initialization failed:\n{0}", ex);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
#if !UNITY_EDITOR
            GameServices.Shutdown();
#endif
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
            GameTime.StartFrame();
            GameServices.Tick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            GameTime.StartFrame();
            GameServices.FixedTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            GameTime.StartFrame();
            GameServices.LateTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            GameAppMessageEvent.Trigger(hasFocus ? EMessageEventType.ApplicationFocus : EMessageEventType.NotApplicationFocus);
        }

        private void OnApplicationQuit()
        {
            GameAppMessageEvent.Trigger(EMessageEventType.ApplicationQuit);
            Application.lowMemory -= OnLowMemory;
            StopAllCoroutines();
        }

        private void OnDrawGizmos()
        {
            GameServices.DrawGizmos();
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            GameServices.ShutdownScope(EServiceScopeKind.Scene);
            GameServices.ShutdownScope(EServiceScopeKind.Gameplay);
        }

        #endregion

        public static void Shutdown()
        {
            LogUtility.Info("GameApp Shutdown");
            s_IsShutdown = true;

            s_Debugger = null;
            s_FSM = null;
            s_Procedure = null;
            s_ObjectPool = null;
            s_Resource = null;
            s_Audio = null;
            s_UI = null;
            s_Localization = null;
            s_Scene = null;
            s_Timer = null;
            s_Input = null;
            s_Save = null;
        }

        private void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");

            if (GameServices.TryResolve<IObjectPoolService>(EServiceScopeKind.App, out var objectPoolService))
                objectPoolService.ReleaseAllUnused();

            if (GameServices.TryResolve<IResourceService>(EServiceScopeKind.App, out var resourceService))
                resourceService.ForceUnloadUnusedAssets(true);
        }

#if UNITY_EDITOR
        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state ==  UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                // 编辑器退出 Play 时清理服务系统：不依赖域重载（兼容 Enter Play Mode Options 跳过域重载的场景）
                GameServices.Shutdown();
                Shutdown();
            }
        }
#endif
    }
}