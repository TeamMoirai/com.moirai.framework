using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// <see cref="GameApp"/> 的启动控制面：自动启动开关、手动启动入口、组合根扩展点与启动失败上报。
    /// </summary>
    public partial class GameApp
    {
        #region 启动控制 [BOOT CONTROL]

        /// <summary>是否由包内的 <c>RuntimeInitializeOnLoadMethod</c> 自动完成启动。</summary>
        /// <remarks>
        /// 置 <c>false</c> 可自建闪屏或等热更程序集装载完再拉起服务。 <br />
        /// 必须在 <c>AfterAssembliesLoaded</c> 或更早设置；包内读取点在 <c>BeforeSceneLoad</c>，同阶段顺序不受保证。
        /// </remarks>
        public static bool AutoBoot { get; set; } = true;

        /// <summary>
        /// 启动框架：生命周期初始化 + 组合根。
        /// </summary>
        /// <remarks>幂等：已在线时返回 <c>false</c> 且不重复装配，<see cref="Shutdown"/> 后可再次启动。</remarks>
        /// <returns>本次调用是否真正执行了启动。</returns>
        public static bool Boot()
        {
            if (!IsShutdown) return false;

            Initialize();

            // 版本信息在装配启动前输出：组合根是异步的，日志时序表达的是「开始装配」而非「装配完成」
            LogUtility.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            LogUtility.Info("Unity Version: {0}", Application.unityVersion);

            // 组合根：App 作用域服务创建与构建（异步第二阶段，失败经 BootFailed 上报）
            GameAppSettings.InitializeAppServices().Forget();
            return true;
        }

        #endregion

        #region 组合根扩展点 [COMPOSITION EXTENSION]

        /// <summary>内置 App 服务全部注册之后、世界初始化（拓扑排序 + 统一 <c>OnInit</c>）之前触发。</summary>
        /// <remarks>
        /// 在此注册的服务与内置服务同等参与拓扑序，声明 <c>[ServiceDependency]</c> 可被正确排序。 <br />
        /// 只有 AOT 侧程序集赶得上此时机；热更域入口（如 <c>HotfixEntry.Entrance</c>）跑在其后，请直接调 <c>GameServices.RegisterService</c>。 <br />
        /// 逐个订阅调用，单个模块抛异常只影响它自己。
        /// </remarks>
        public static event Action ServicesComposing;

        /// <summary>
        /// 由组合根驱动 <see cref="ServicesComposing"/>，逐项隔离异常。
        /// </summary>
        internal static void InvokeServicesComposing()
        {
            Action handlers = ServicesComposing;
            if (handlers == null) return;

            // 与关闭广播同理：这里抛一次不该让后面的项目模块静默失去装配机会。
            // GetInvocationList 有分配，属一次性启动路径，可接受。
            Delegate[] invocations = handlers.GetInvocationList();
            for (int i = 0; i < invocations.Length; i++)
            {
                Action module = (Action)invocations[i];
                try
                {
                    module();
                }
                catch (Exception exception)
                {
                    LogUtility.Error("GameApp.ServicesComposing module '{0}.{1}' threw: {2}",
                        module.Method.DeclaringType, module.Method.Name, exception);
                }
            }
        }

        #endregion

        #region 启动失败上报 [BOOT FAILURE]

        /// <summary>组合根失败时触发（内置服务注册、项目模块或世界初始化抛出异常）。</summary>
        /// <remarks>
        /// 原因已先以 Error 级带栈记录；本事件供挂崩溃上报与兜底 UI。 <br />
        /// 此刻 PlayerLoop 已在跑但服务世界可能只初始化了一半，不要在此继续推进游戏逻辑。
        /// </remarks>
        public static event Action<Exception> BootFailed;

        internal static void RaiseBootFailed(Exception exception)
        {
            BootFailed?.Invoke(exception);
        }

        #endregion
    }
}
