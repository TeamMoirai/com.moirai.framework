using Moirai.Atropos.Audio;
using Moirai.Atropos.Debugger;
using Moirai.Atropos.Fsm;
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

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏模块。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public partial class GameModule : MonoBehaviour
    {
        private static bool s_IsShutdown = true;

        #region 框架模块
        
        private static RootModule s_Base;
        /// <summary>
        /// 获取游戏基础模块。
        /// </summary>
        public static RootModule Base => s_Base;

        private static IDebuggerModule s_Debugger;
        /// <summary>
        /// 获取调试模块。
        /// </summary>
        public static IDebuggerModule Debugger => s_IsShutdown ? null : s_Debugger ??= Get<IDebuggerModule>();

        private static IFsmModule s_Fsm;
        /// <summary>
        /// 获取有限状态机模块。
        /// </summary>
        public static IFsmModule Fsm => s_IsShutdown ? null : s_Fsm ??= Get<IFsmModule>();

        private static IProcedureModule s_Procedure;
        /// <summary>
        /// 流程管理模块。
        /// </summary>
        public static IProcedureModule Procedure => s_IsShutdown ? null : s_Procedure ??= Get<IProcedureModule>();

        private static IObjectPoolModule s_ObjectPool;
        /// <summary>
        /// 获取对象池模块。
        /// </summary>
        public static IObjectPoolModule ObjectPool => s_IsShutdown ? null : s_ObjectPool ??= Get<IObjectPoolModule>();

        private static IResourceModule s_Resource;
        /// <summary>
        /// 获取资源模块。
        /// </summary>
        public static IResourceModule Resource => s_IsShutdown ? null : s_Resource ??= Get<IResourceModule>();

        private static IAudioModule s_Audio;
        /// <summary>
        /// 获取音频模块。
        /// </summary>
        public static IAudioModule Audio => s_IsShutdown ? null : s_Audio ??= Get<IAudioModule>();

        private static UIModule s_UI;
        /// <summary>
        /// 获取UI模块。
        /// </summary>
        public static UIModule UI => s_IsShutdown ? null : s_UI ??= UIModule.Instance;

        private static ILocalizationModule s_Localization;
        /// <summary>
        /// 获取多语言模块。
        /// </summary>
        public static ILocalizationModule Localization => s_IsShutdown ? null : s_Localization ??= Get<ILocalizationModule>();

        private static ISceneModule s_Scene;
        /// <summary>
        /// 获取场景模块。
        /// </summary>
        public static ISceneModule Scene => s_IsShutdown ? null : s_Scene ??= Get<ISceneModule>();

        private static ITimerModule s_Timer;
        /// <summary>
        /// 获取计时器模块。
        /// </summary>
        public static ITimerModule Timer => s_IsShutdown ? null : s_Timer ??= Get<ITimerModule>();

        private static IInputModule s_Input;
        /// <summary>
        /// 获取输入模块。
        /// </summary>
        public static IInputModule Input => s_IsShutdown ? null : s_Input ??= Get<IInputModule>();

        private static ISaveModule s_Save;
        /// <summary>
        /// 获取保存模块。
        /// </summary>
        public static ISaveModule Save => s_IsShutdown ? null : s_Save ??= Get<ISaveModule>();
        
        #endregion

        /// <summary>
        /// 获取游戏框架模块类。
        /// </summary>
        /// <typeparam name="T">游戏框架模块类。</typeparam>
        /// <returns>游戏框架模块实例。</returns>
        private static T Get<T>() where T : class
        {
            T module = ModuleSystem.GetModule<T>();

            Log.Assert(condition: module != null, $"{typeof(T)} is null");

            return module;
        }

        private void Awake()
        {
            Log.Info("GameModule Active");
            s_IsShutdown = false;
            
            gameObject.name = $"[{nameof(GameModule)}]";
            
            s_Base = GetComponent<RootModule>();
            
            ModuleSystem.GetModule<IUpdateDriver>();
            ModuleSystem.GetModule<IResourceModule>();
            ModuleSystem.GetModule<IDebuggerModule>();
            ModuleSystem.GetModule<IFsmModule>();
            ModuleSystem.GetModule<IAudioModule>();

            ProcedureSettings.StartProcedure().Forget();

            DontDestroyOnLoad(gameObject);
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

#if UNITY_EDITOR
        private static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state ==  UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                Shutdown();
            }
        }
#endif

        public static void Shutdown()
        {
            Log.Info("GameModule Shutdown");
            s_IsShutdown = true;
 
            s_Base = null;
            s_Debugger = null;
            s_Fsm = null;
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
    }
}