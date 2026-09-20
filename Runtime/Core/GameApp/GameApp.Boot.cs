using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// <see cref="GameApp"/> 的启动控制面：自动启动开关、手动启动入口、组合根扩展点与启动失败上报。
    /// <para>与 <c>GameApp.cs</c> 同为 partial，分文件是为了让"怎么起"与"起之后做什么"互不干扰。</para>
    /// </summary>
    public partial class GameApp
    {
        #region 启动控制 [BOOT CONTROL]

        /// <summary>
        /// 是否由包内的 <c>RuntimeInitializeOnLoadMethod</c> 自动完成启动。
        /// <para>置 <c>false</c> 把启动时机交回项目：自建闪屏、启动失败兜底 UI，
        /// 或在装载完热更程序集之后再拉起服务。必须在
        /// <c>RuntimeInitializeLoadType.AfterAssembliesLoaded</c> 或更早设置——
        /// 包内的读取点 <c>GameAppSettings.Initiation</c> 相位是 <c>BeforeSceneLoad</c>，
        /// 与之同阶段则顺序不受保证。</para>
        /// </summary>
        public static bool AutoBoot { get; set; } = true;

        /// <summary>
        /// 启动框架：生命周期初始化 + 组合根。
        /// <para>幂等——已在线时返回 <c>false</c> 且不重复装配；<see cref="Shutdown"/> 之后可再次启动。</para>
        /// </summary>
        /// <returns>本次调用是否真正执行了启动。</returns>
        public static bool Boot()
        {
            if (!IsShutdown) return false;

            Initialize();
            // 组合根：App 作用域服务创建与构建（异步第二阶段，失败经 BootFailed 上报）
            GameAppSettings.InitializeAppServices().Forget();

            LogUtility.Info("Game Version: {0} ({1})", VersionUtility.GameVersion, VersionUtility.InternalGameVersion);
            LogUtility.Info("Unity Version: {0}", Application.unityVersion);
            return true;
        }

        #endregion

        #region 组合根扩展点 [COMPOSITION EXTENSION]

        /// <summary>
        /// 内置 App 服务全部注册之后、世界初始化（依赖拓扑排序 + 统一 <c>OnInit</c>）之前触发。
        /// <para>在此注册的服务与内置服务同等参与拓扑序，所以声明了
        /// <c>[ServiceDependency]</c> 的自定义服务能被正确排序。</para>
        /// <para><b>适用边界</b>：只有 AOT 侧程序集赶得上这个时机。热更域入口（如
        /// <c>HotfixEntry.Entrance</c>）跑在世界初始化之后，那里请直接调
        /// <c>GameServices.RegisterService</c>——它走"已初始化则立即 <c>OnInit</c>"的可用路径，
        /// 只是不再参与批量拓扑排序。</para>
        /// <para>逐个订阅调用：单个模块抛异常只影响它自己，不会吃掉其余模块与内置服务的装配。</para>
        /// </summary>
        public static event Action ServicesComposing;

        /// <summary>由组合根驱动 <see cref="ServicesComposing"/>，逐项隔离异常。</summary>
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

        /// <summary>
        /// 组合根失败时触发（内置服务注册、项目模块或世界初始化抛出异常）。
        /// <para>原因一定先以 Error 级带栈记录；本事件供项目挂崩溃上报与兜底 UI，
        /// 避免"启动失败但线上只看到黑屏"。此刻 PlayerLoop 已注入、帧驱动在跑，
        /// 但服务世界可能只初始化了一半——不要在这里继续推进游戏逻辑。</para>
        /// </summary>
        public static event Action<Exception> BootFailed;

        internal static void RaiseBootFailed(Exception exception)
        {
            BootFailed?.Invoke(exception);
        }

        #endregion
    }
}
