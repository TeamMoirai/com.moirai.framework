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
        private static bool s_IsShutdown = true;

        #region 框架服务
        
        private static IDebuggerService s_Debugger;
        /// <summary>
        /// 获取调试服务。
        /// </summary>
        public static IDebuggerService Debugger => s_IsShutdown ? null : s_Debugger ??= Get<IDebuggerService>();

        private static IFSMService s_FSM;
        /// <summary>
        /// 获取有限状态机服务。
        /// </summary>
        public static IFSMService FSM => s_IsShutdown ? null : s_FSM ??= Get<IFSMService>();

        private static IProcedureService s_Procedure;
        /// <summary>
        /// 流程管理服务。
        /// </summary>
        public static IProcedureService Procedure => s_IsShutdown ? null : s_Procedure ??= Get<IProcedureService>();

        private static IObjectPoolService s_ObjectPool;
        /// <summary>
        /// 获取对象池服务。
        /// </summary>
        public static IObjectPoolService ObjectPool => s_IsShutdown ? null : s_ObjectPool ??= Get<IObjectPoolService>();

        private static IResourceService s_Resource;
        /// <summary>
        /// 获取资源服务。
        /// </summary>
        public static IResourceService Resource => s_IsShutdown ? null : s_Resource ??= Get<IResourceService>();

        private static IAudioService s_Audio;
        /// <summary>
        /// 获取音频服务。
        /// </summary>
        public static IAudioService Audio => s_IsShutdown ? null : s_Audio ??= Get<IAudioService>();

        private static IUIService s_UI;
        /// <summary>
        /// 获取UI服务。
        /// </summary>
        public static IUIService UI => s_IsShutdown ? null : s_UI ??= Get<IUIService>();

        private static ILocalizationService s_Localization;
        /// <summary>
        /// 获取多语言服务。
        /// </summary>
        public static ILocalizationService Localization => s_IsShutdown ? null : s_Localization ??= Get<ILocalizationService>();

        private static ISceneService s_Scene;
        /// <summary>
        /// 获取场景服务。
        /// </summary>
        public static ISceneService Scene => s_IsShutdown ? null : s_Scene ??= Get<ISceneService>();

        private static ITimerService s_Timer;
        /// <summary>
        /// 获取计时器服务。
        /// </summary>
        public static ITimerService Timer => s_IsShutdown ? null : s_Timer ??= Get<ITimerService>();

        private static IInputService s_Input;
        /// <summary>
        /// 获取输入服务。
        /// </summary>
        public static IInputService Input => s_IsShutdown ? null : s_Input ??= Get<IInputService>();

        private static ISaveService s_Save;
        /// <summary>
        /// 获取保存服务。
        /// </summary>
        public static ISaveService Save => s_IsShutdown ? null : s_Save ??= Get<ISaveService>();
        
        #endregion

        /// <summary>
        /// 获取游戏框架服务类。
        /// </summary>
        /// <typeparam name="T">游戏框架服务类。</typeparam>
        /// <returns>游戏框架服务实例。</returns>
        private static T Get<T>() where T : class
        {
            T service = ServiceSystem.GetService<T>();

            LogUtility.Assert(condition: service != null, $"{typeof(T)} is null");

            return service;
        }

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

            SceneManager.sceneUnloaded += OnSceneUnloaded;

            InitializeAsync().Forget();

            Application.lowMemory += OnLowMemory;
            GameTime.StartFrame();
        }

        private static async UniTaskVoid InitializeAsync()
        {
            await ServiceSystem.InitializeAsync();
            ProcedureSettings.StartProcedure().Forget();
        }

        private void OnDestroy()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
#if !UNITY_EDITOR
            ServiceSystem.Shutdown();
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
            ServiceSystem.Tick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            GameTime.StartFrame();
            ServiceSystem.FixedTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            GameTime.StartFrame();
            ServiceSystem.LateTick(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            MessageEvent.Trigger(hasFocus ? EMessageEventType.ApplicationFocus : EMessageEventType.NotApplicationFocus);
        }

        private void OnApplicationQuit()
        {
            MessageEvent.Trigger(EMessageEventType.ApplicationQuit);
            Application.lowMemory -= OnLowMemory;
            StopAllCoroutines();
        }

        private void OnDrawGizmos()
        {
            ServiceSystem.DrawGizmos();
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            ServiceSystem.ShutdownScope(ServiceScope.Scene);
            ServiceSystem.ShutdownScope(ServiceScope.Gameplay);
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

            IObjectPoolService objectPoolService = ServiceSystem.GetService<IObjectPoolService>();
            if (objectPoolService != null)
            {
                objectPoolService.ReleaseAllUnused();
            }

            IResourceService resourceService = ServiceSystem.GetService<IResourceService>();
            if (resourceService != null)
            {
                resourceService.ForceUnloadUnusedAssets(true);
            }
        }

#if UNITY_EDITOR
        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state ==  UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                // 编辑器退出 Play 时清理服务系统：不依赖域重载（兼容 Enter Play Mode Options 跳过域重载的场景）
                ServiceSystem.Shutdown();
                Shutdown();
            }
        }
#endif
    }
}