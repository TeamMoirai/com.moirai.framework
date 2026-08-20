using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Events;
using Moirai.Atropos.ObjectPool;
using Moirai.Atropos.Procedure;
using Moirai.Atropos.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏入口。仅负责生命周期驱动——不持有服务静态属性。
    /// <para>服务访问统一通过 <see cref="Services"/>（<see cref="IServiceProvider"/>）：
    /// <c>GameApp.Services.GetRequiredService&lt;IAudioService&gt;()</c>。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public partial class GameApp : MonoBehaviour
    {
        #region 公共属性 [PUBLIC PROPERTIES]

        private static bool s_IsShutdown = true;

        /// <summary>
        /// 最深层活跃的服务提供者（Gameplay > Scene > App）。
        /// <para>非服务代码通过此属性访问服务；服务类应使用构造注入。</para>
        /// <para>关闭后返回 null——退出/重启场景中外部代码可能仍持有引用，安全返回 null 比抛异常更合理。</para>
        /// </summary>
        public static IServiceProvider Services => s_IsShutdown ? null : GameServices.Provider;

        /// <summary>获取游戏是否已关闭。</summary>
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

            // 异步构建 App 容器 + 启动流程
            InitializeAsync().Forget();

            GameTime.StartFrame();
        }

        private static async UniTaskVoid InitializeAsync()
        {
            try
            {
                // App 容器已在 AppSettings.Initiation() 中创建（仅存储描述符）
                // 此处执行实际构建：创建实例 → 注入 → OnInit → OnInitAsync
                if (GameServices.AppContainer != null)
                    await GameServices.AppContainer.BuildAsync();

                // 启动游戏流程
                await ProcedureSettings.StartProcedure();
            }
            catch (Exception ex)
            {
                LogUtility.Error("GameApp initialization failed:\n{0}", ex);
                // 可扩展：弹出错误 UI 或退出游戏
            }
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

        // Application.lowMemory 由 Unity 在主线程触发（与 Application.focus/quit 一致），无需线程守卫。
        private void OnLowMemory()
        {
            LogUtility.Warning("Low memory reported...");

            // 通过 Provider 安全访问——避免直接引用服务实例
            var provider = GameServices.Provider;
            if (provider == null) return;

            if (provider.TryGetService<IObjectPoolService>(out var pool))
                pool.ReleaseAllUnused();
            if (provider.TryGetService<IResourceService>(out var resource))
                resource.ForceUnloadUnusedAssets(true);
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
