using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景服务外观（Facade）。
    /// <para>统一的静态场景访问入口，通过替换 <see cref="Handler"/> 即可在不同场景加载后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="SceneServiceSettings"/> 解析；settings 未配置则回退 <see cref="CreateDefaultHandler"/>。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// <para>错误契约：加载失败抛出 <see cref="GameException"/>；卸载失败以 <c>false</c> 返回值报告；服务未注册时查询降级返回默认值、加载静默无效（调用方须检查 <see cref="UnityEngine.SceneManagement.Scene.IsValid"/>）。</para>
    /// <para>生命周期事件（<see cref="MainSceneChanged"/> 等）在主线程同步触发，订阅者异常被隔离记录，不影响其他订阅者；服务关闭时静态事件会被清空。</para>
    /// <para>场景短名须尽量全局唯一：短名碰撞时按名查询/激活/卸载可能解析到错误对象（后注册者覆盖，详见处理器日志）。</para>
    /// </summary>
    [HandlerHost(typeof(SceneServiceHandler))]
    [ServiceDependency(typeof(ResourceService))]
    public partial class SceneService : ServiceBase
    {
        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 创建默认场景处理器（settings 未配置时的代码兜底）。
        /// </summary>
        /// <returns>默认场景处理器实例。</returns>
        internal static SceneServiceHandler CreateDefaultHandler() => new DefaultSceneHandler();

        /// <summary>
        /// 从 <see cref="SceneServiceSettings"/> 解析场景处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——懒加载主路径（settings 已配置时 <see cref="CreateDefaultHandler"/> 被短路）首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static SceneServiceHandler GetHandlerFromSettings()
        {
            GameServices.EnsureRegistered<SceneService>();
            return SceneServiceSettings.SceneServiceHandler;
        }

        /// <inheritdoc />
        public override int Priority => ServicePriorityOrder.MID_TIER;

        /// <summary>
        /// 初始化场景服务。由容器在构建期调用。
        /// <para>确保 <c>SceneService.Handler</c> 已赋值（触发 <c>Handler</c> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭场景服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            var handler = s_Handler;
            s_Handler = null;
            MainSceneChanged = null;
            SubSceneLoaded = null;
            SubSceneUnloaded = null;
            handler?.Internal_Shutdown();
        }

        #endregion

        #region 事件 [EVENTS]

        /// <summary>
        /// 主场景切换完成（Single 模式加载并激活后触发，参数为归一化场景短名）。
        /// <para>主线程同步触发；订阅者异常被隔离记录。</para>
        /// </summary>
        public static event Action<string> MainSceneChanged;

        /// <summary>
        /// 子场景加载完成（Additive 模式登记为已加载后触发，参数为归一化场景短名）。
        /// </summary>
        public static event Action<string> SubSceneLoaded;

        /// <summary>
        /// 子场景卸载完成（参数为归一化场景短名）。
        /// </summary>
        public static event Action<string> SubSceneUnloaded;

        /// <summary>
        /// 触发主场景切换事件（由处理器在加载收尾时调用）。
        /// </summary>
        internal static void InvokeMainSceneChangedEvent(string sceneName) => RaiseEvent(MainSceneChanged, sceneName);

        /// <summary>
        /// 触发子场景加载完成事件（由处理器在加载收尾时调用）。
        /// </summary>
        internal static void InvokeSubSceneLoadedEvent(string sceneName) => RaiseEvent(SubSceneLoaded, sceneName);

        /// <summary>
        /// 触发子场景卸载完成事件（由处理器在卸载收尾时调用）。
        /// </summary>
        internal static void InvokeSubSceneUnloadedEvent(string sceneName) => RaiseEvent(SubSceneUnloaded, sceneName);

        /// <summary>
        /// 逐订阅者隔离派发事件——单个订阅者异常仅记录日志，不中断其余订阅者。
        /// </summary>
        private static void RaiseEvent(Action<string> handlers, string sceneName)
        {
            if (handlers == null)
            {
                return;
            }

            var invocationList = handlers.GetInvocationList();
            for (var i = 0; i < invocationList.Length; i++)
            {
                try
                {
                    ((Action<string>)invocationList[i]).Invoke(sceneName);
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Scene event handler threw an exception. Handler: {0}, error: {1}",
                        invocationList[i].Method.DeclaringType?.Name, ex);
                }
            }
        }

        #endregion

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 异步加载场景。加载失败抛出 <see cref="GameException"/>。
        /// <para>挂起加载（<c>suspendLoad</c>）须经 <see cref="UnSuspend"/> 解除后才会完成——底层加载不可中止，
        /// <paramref name="cancellationToken"/> 仅取消等待与进度回调，登记由处理器收尾。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <param name="packageName">资源包名称（空串使用默认包）。</param>
        /// <param name="cancellationToken">取消令牌——放弃等待语义，不中止底层加载。</param>
        /// <returns>加载完成的场景。</returns>
        public static UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null, string packageName = "", CancellationToken cancellationToken = default) =>
            s_Handler?.LoadSceneAsync(location, sceneMode, suspendLoad, priority, gcCollect, progressCallBack, packageName, cancellationToken)
            ?? UniTask.FromResult(default(UnityEngine.SceneManagement.Scene));

        /// <summary>
        /// 同步发起场景加载（回调式）。
        /// <para>回调契约：无论成败恰好回调一次；失败时以默认场景回调，调用方须检查 <see cref="UnityEngine.SceneManagement.Scene.IsValid"/>。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="callBack">加载完成回调。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        public static void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null) =>
            s_Handler?.LoadScene(location, packageName, sceneMode, suspendLoad, priority, gcCollect, callBack, progressCallBack);

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景（设为当前活动场景）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否激活成功。</returns>
        public static bool ActivateScene(string location) => s_Handler?.ActivateScene(location) ?? false;

        /// <summary>
        /// 取消挂起。仅接受资源地址（挂起场景尚未产生场景短名）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public static bool UnSuspend(string location) => s_Handler?.UnSuspend(location) ?? false;

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。失败返回 <c>false</c> 并保留登记供重试；
        /// 句柄已失效（适配器发起卸载后置空等）同样视为失败，不会误报成功。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <returns>是否卸载成功。</returns>
        public static UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null) =>
            s_Handler?.UnloadAsync(location, progressCallBack) ?? UniTask.FromResult(false);

        /// <summary>
        /// 卸载子场景（回调式）。回调契约：卸载发起后无论成败恰好回调一次（参数为是否成功）；无效请求（地址未登记、存在在途操作）不发起亦不回调。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="callBack">卸载完成回调（参数为是否卸载成功）。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        public static void Unload(string location, Action<bool> callBack = null, Action<float> progressCallBack = null) =>
            s_Handler?.Unload(location, callBack, progressCallBack);

        #endregion

        #region 场景查询 [SCENE QUERY]

        /// <summary>
        /// 当前主场景名称（归一化场景短名，非资源地址；启动场景未经本服务加载时为引擎激活场景名）。
        /// </summary>
        public static string CurrentMainSceneName => s_Handler?.CurrentMainSceneName;

        /// <summary>
        /// 已完成加载的子场景资源地址快照（不含加载中的子场景——在途登记请用 <see cref="IsContainScene"/> 查询）。
        /// </summary>
        public static IReadOnlyCollection<string> LoadedSubSceneLocations => s_Handler?.LoadedSubSceneLocations ?? Array.Empty<string>();

        /// <summary>
        /// 判断指定场景是否为当前主场景（身份判断，不含激活状态——激活状态请比较
        /// <c>SceneManager.GetActiveScene().name</c> 与 <see cref="CurrentMainSceneName"/>）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否为主场景。</returns>
        public static bool IsMainScene(string location) => s_Handler?.IsMainScene(location) ?? false;

        /// <summary>
        /// 查询场景是否已登记（主场景含启动场景，子场景含加载中未完成的挂起加载）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否已登记。</returns>
        public static bool IsContainScene(string location) => s_Handler?.IsContainScene(location) ?? false;

        #endregion
    }
}
