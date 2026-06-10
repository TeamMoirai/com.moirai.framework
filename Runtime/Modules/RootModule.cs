using System;
using Moirai.Atropos.Events;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Resource;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基础模块。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-999)]
    public sealed class RootModule : MonoBehaviour
    {
        private const int DEFAULT_DPI = 96;  // default windows dpi

        private float _gameSpeedBeforePause = 1f;

        [SerializeField] private string m_EditorLanguage = "Unspecified";

        [SerializeField] private string m_VersionHelperTypeName = typeof(DefaultVersionHelper).FullName;

        [SerializeField] private string m_FormatHelperTypeName = typeof(DefaultFormatHelper).FullName;
        
        [SerializeField] private string m_LogHelperTypeName = typeof(DefaultLogHelper).FullName;
        
        [SerializeField] private string m_ObjectHelperTypeName = typeof(UnityObjectHelper).FullName;
     
        [SerializeField] private string m_JsonHelperTypeName = typeof(UnityJsonHelper).FullName;

        [SerializeField] private int m_FrameRate = 120;

        [SerializeField] private float m_GameSpeed = 1f;

        [SerializeField] private bool m_RunInBackground = true;

        [SerializeField] private bool m_NeverSleep = true;

        /// <summary>
        /// 获取或设置编辑器语言（仅编辑器内有效）。
        /// </summary>
        public string EditorLanguage
        {
            get => m_EditorLanguage;
            set
            {
                if (m_EditorLanguage == value) return;

                m_EditorLanguage = value;
                GameModule.Localization?.ChangeLanguage(value);
            }
        }
        
        /// <summary>
        /// 获取或设置游戏帧率。
        /// </summary>
        public int FrameRate
        {
            get => m_FrameRate;
            set => Application.targetFrameRate = m_FrameRate = value;
        }

        /// <summary>
        /// 获取或设置游戏速度。
        /// </summary>
        public float GameSpeed
        {
            get => m_GameSpeed;
            set => UnityEngine.Time.timeScale = m_GameSpeed = value >= 0f ? value : 0f;
        }

        /// <summary>
        /// 获取游戏是否暂停。
        /// </summary>
        public bool IsGamePaused => m_GameSpeed <= 0f;

        /// <summary>
        /// 获取是否正常游戏速度。
        /// </summary>
        public bool IsNormalGameSpeed => Math.Abs(m_GameSpeed - 1f) < 0.01f;

        /// <summary>
        /// 获取或设置是否允许后台运行。
        /// </summary>
        public bool RunInBackground
        {
            get => m_RunInBackground;
            set => Application.runInBackground = m_RunInBackground = value;
        }

        /// <summary>
        /// 获取或设置是否禁止休眠。
        /// </summary>
        public bool NeverSleep
        {
            get => m_NeverSleep;
            set
            {
                m_NeverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }

        /// <summary>
        /// 游戏框架模块初始化。
        /// </summary>
        private void Awake()
        {
            InitFormatHelper();
            InitVersionHelper();
            InitLogHelper();
    
            Log.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            Log.Info("Unity Version: {0}", Application.unityVersion);
            
            InitObjectHelper();
            InitJsonHelper();

            ConverterUtility.ScreenDpi = Screen.dpi;
            if (ConverterUtility.ScreenDpi <= 0)
            {
                ConverterUtility.ScreenDpi = DEFAULT_DPI;
            }
            
            Application.targetFrameRate = m_FrameRate;
            Time.timeScale = m_GameSpeed;
            Application.runInBackground = m_RunInBackground;
            Screen.sleepTimeout = m_NeverSleep ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;

            Application.lowMemory += OnLowMemory;
            GameTime.StartFrame();
        }

        private void Update()
        {
            GameTime.StartFrame();
            ModuleSystem.Update(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void FixedUpdate()
        {
            GameTime.StartFrame();
            ModuleSystem.FixedUpdate(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void LateUpdate()
        {
            GameTime.StartFrame();
            ModuleSystem.LateUpdate(GameTime.deltaTime, GameTime.unscaledDeltaTime);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            MessageEvent.Trigger(hasFocus ? MessageEventType.ApplicationFocus : MessageEventType.NotApplicationFocus);
        }

        private void OnApplicationQuit()
        {
            MessageEvent.Trigger(MessageEventType.ApplicationQuit);
            Application.lowMemory -= OnLowMemory;
            StopAllCoroutines();
        }

        private void OnDestroy()
        {
#if !UNITY_EDITOR
            ModuleSystem.Shutdown();
#endif
        }

        /// <summary>
        /// 暂停游戏。
        /// </summary>
        public void PauseGame()
        {
            if (IsGamePaused)
            {
                return;
            }

            _gameSpeedBeforePause = GameSpeed;
            GameSpeed = 0f;
        }

        /// <summary>
        /// 恢复游戏。
        /// </summary>
        public void ResumeGame()
        {
            if (!IsGamePaused)
            {
                return;
            }

            GameSpeed = _gameSpeedBeforePause;
        }

        /// <summary>
        /// 重置为正常游戏速度。
        /// </summary>
        public void ResetNormalGameSpeed()
        {
            if (IsNormalGameSpeed)
            {
                return;
            }

            GameSpeed = 1f;
        }

        internal void Shutdown()
        {
            Destroy(gameObject);
        }

        private void InitFormatHelper()
        {
            if (string.IsNullOrEmpty(m_FormatHelperTypeName))
            {
                return;
            }

            Type formatHelperType = AssemblyUtility.GetType(m_FormatHelperTypeName);
            if (formatHelperType == null)
            {
                throw new GameException(TextUtility.Format("Can not find format helper type '{0}'.", m_FormatHelperTypeName));
            }

            TextUtility.IFormatHelper formatHelper = (TextUtility.IFormatHelper)Activator.CreateInstance(formatHelperType);
            if (formatHelper == null)
            {
                throw new GameException(TextUtility.Format("Can not find format helper instance '{0}'.", m_FormatHelperTypeName));
            }

            TextUtility.SetFormatHelper(formatHelper);
        }

        private void InitVersionHelper()
        {
            if (string.IsNullOrEmpty(m_VersionHelperTypeName))
            {
                return;
            }

            Type versionHelperType = AssemblyUtility.GetType(m_VersionHelperTypeName);
            if (versionHelperType == null)
            {
                throw new GameException(TextUtility.Format("Can not find version helper type '{0}'.", m_VersionHelperTypeName));
            }

            VersionUtility.IVersionHelper versionHelper = (VersionUtility.IVersionHelper)Activator.CreateInstance(versionHelperType);
            if (versionHelper == null)
            {
                throw new GameException(TextUtility.Format("Can not create version helper instance '{0}'.", m_VersionHelperTypeName));
            }

            VersionUtility.SetVersionHelper(versionHelper);
        }

        private void InitLogHelper()
        {
            if (string.IsNullOrEmpty(m_LogHelperTypeName))
            {
                return;
            }

            Type logHelperType = AssemblyUtility.GetType(m_LogHelperTypeName);
            if (logHelperType == null)
            {
                throw new GameException(TextUtility.Format("Can not find log helper type '{0}'.", m_LogHelperTypeName));
            }

            LogUtility.ILogHelper logHelper = (LogUtility.ILogHelper)Activator.CreateInstance(logHelperType);
            if (logHelper == null)
            {
                throw new GameException(TextUtility.Format("Can not create log helper instance '{0}'.", m_LogHelperTypeName));
            }

            LogUtility.SetLogHelper(logHelper);
        }
        
        private void InitObjectHelper()
        {
            if (string.IsNullOrEmpty(m_ObjectHelperTypeName))
            {
                return;
            }

            Type objectHelperType = AssemblyUtility.GetType(m_ObjectHelperTypeName);
            if (objectHelperType == null)
            {
                throw new GameException(TextUtility.Format("Can not find object helper type '{0}'.", m_ObjectHelperTypeName));
            }

            ObjectUtility.IObjectHelper objectHelper = (ObjectUtility.IObjectHelper)Activator.CreateInstance(objectHelperType);
            if (objectHelper == null)
            {
                throw new GameException(TextUtility.Format("Can not create object helper instance '{0}'.", m_ObjectHelperTypeName));
            }

            ObjectUtility.SetObjectHelper(objectHelper);
        }
        
        private void InitJsonHelper()
        {
            if (string.IsNullOrEmpty(m_JsonHelperTypeName))
            {
                return;
            }

            Type jsonHelperType = AssemblyUtility.GetType(m_JsonHelperTypeName);
            if (jsonHelperType == null)
            {
                throw new GameException(TextUtility.Format("Can not find JSON helper type '{0}'.", m_JsonHelperTypeName));
            }

            JSONUtility.IJsonHelper jsonHelper = (JSONUtility.IJsonHelper)Activator.CreateInstance(jsonHelperType);
            if (jsonHelper == null)
            {
                throw new GameException(TextUtility.Format("Can not find JSON helper instance '{0}'.", m_JsonHelperTypeName));
            }

            JSONUtility.SetJsonHelper(jsonHelper);
        }
        
        private void OnLowMemory()
        {
            Log.Warning("Low memory reported...");
            
            IObjectPoolModule objectPoolModule = ModuleSystem.GetModule<IObjectPoolModule>();
            if (objectPoolModule != null)
            {
                objectPoolModule.ReleaseAllUnused();
            }

            IResourceModule resourceModule = ModuleSystem.GetModule<IResourceModule>();
            if (resourceModule != null)
            {
                resourceModule.ForceUnloadUnusedAssets(true);
            }
        }
    }
}
