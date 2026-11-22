using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Asset;
using UnityEngine.SceneManagement;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景服务外观（Facade）：统一静态访问入口 + 后端实例公共缓存。
    /// </summary>
    /// <remarks>
    /// 容器是唯一事实源：settings 选出的后端 handler（<see cref="DefaultSceneHandler"/>）作为 <see cref="ISceneService"/> 契约服务注册进 <see cref="GameServices"/>， <br />
    /// 本类缓存该实例（懒链注册结果回填）并把后端基础 API 组合成常用功能；服务互调经 <c>GameServices.GetRequiredService&lt;ISceneService&gt;()</c> 解析。 <br />
    /// 未显式设置处理器时，懒加载优先经 <c>GetHandlerFromSettings</c> 从 <see cref="SceneServiceSettings"/> 解析， <br />
    /// 未配置则回退 <see cref="CreateDefaultHandler"/>；<see cref="Handler"/> 由 <c>ProviderHandlerHostGenerator</c> 源生成器自动生成（懒加载 + 绑定即注册）。 <br />
    /// 错误契约：加载失败抛出 <see cref="GameException"/>；卸载失败以 <c>false</c> 返回并保留登记；服务未注册时查询降级返回默认值、 <br />
    /// 加载静默无效（调用方须检查 <see cref="UnityEngine.SceneManagement.Scene.IsValid"/>）。 <br />
    /// 生命周期事件（<see cref="onMainSceneChanged"/> 等）在主线程同步触发，订阅者异常被隔离记录，不影响其他订阅者；服务关闭时静态事件会被清空。 <br />
    /// 场景短名须尽量全局唯一：碰撞时按名查询/激活/卸载可能解析到错误对象（后注册者覆盖，详见处理器日志）。
    /// </remarks>
    [ProviderHandlerHost(typeof(ISceneService), typeof(DefaultSceneHandler))]
    public static partial class SceneService
    {
        #region 生命周期钩子 [LIFECYCLE HOOKS]

        /// <summary>
        /// 从 <see cref="SceneServiceSettings"/> 解析场景处理器（懒链 settings 来源；注册与缓存回填由生成代码完成）。
        /// </summary>
        /// <returns>settings 中配置的处理器；未配置时返回 <c>null</c> 回退到 <see cref="CreateDefaultHandler"/>。</returns>
        private static ISceneService GetHandlerFromSettings()
        {
            return SceneServiceSettings.SceneServiceHandler;
        }

        /// <summary>
        /// 清空生命周期静态事件（后端 OnShutdown 经 Manager 钩子调用；容器路径收口）。
        /// </summary>
        internal static void Internal_ClearLifecycleEvents()
        {
            onMainSceneChanged = null;
            onSubSceneLoaded = null;
            onSubSceneUnloaded = null;
        }

        #endregion

        #region 事件 [EVENTS]

        /// <summary>主场景切换完成（Single 模式加载并激活后触发，参数为归一化场景短名）。</summary>
        /// <remarks>
        /// 主线程同步触发；订阅者异常被隔离记录。
        /// </remarks>
        public static event Action<string> onMainSceneChanged;

        /// <summary>子场景加载完成（Additive 模式登记为已加载后触发，参数为归一化场景短名）。</summary>
        public static event Action<string> onSubSceneLoaded;

        /// <summary>子场景卸载完成（参数为归一化场景短名）。</summary>
        public static event Action<string> onSubSceneUnloaded;

        /// <summary>
        /// 触发主场景切换事件（由处理器在加载收尾时调用）。
        /// </summary>
        internal static void InvokeMainSceneChangedEvent(string sceneName) => RaiseEvent(onMainSceneChanged, sceneName);

        /// <summary>
        /// 触发子场景加载完成事件（由处理器在加载收尾时调用）。
        /// </summary>
        internal static void InvokeSubSceneLoadedEvent(string sceneName) => RaiseEvent(onSubSceneLoaded, sceneName);

        /// <summary>
        /// 触发子场景卸载完成事件（由处理器在卸载收尾时调用）。
        /// </summary>
        internal static void InvokeSubSceneUnloadedEvent(string sceneName) => RaiseEvent(onSubSceneUnloaded, sceneName);

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
        /// </summary>
        /// <remarks>
        /// 挂起加载（<c>suspendLoad</c>）须经 <see cref="UnSuspend"/> 解除后才会完成——底层加载不可中止，<paramref name="cancellationToken"/> 仅取消等待与进度回调， <br />
        /// 登记由处理器收尾。
        /// </remarks>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <param name="packageName">资源包名称（空串使用默认包）。</param>
        /// <param name="cancellationToken">取消令牌——放弃等待语义，不中止底层加载。</param>
        /// <returns>加载完成的场景。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            Action<float> progressCallBack = null, string packageName = "", CancellationToken cancellationToken = default) =>
            Internal_PeekHandler()?.LoadSceneAsync(location, sceneMode, suspendLoad, priority, progressCallBack, packageName, cancellationToken)
            ?? UniTask.FromResult(default(UnityEngine.SceneManagement.Scene));

        /// <summary>
        /// 同步发起场景加载（回调式）。
        /// </summary>
        /// <remarks>
        /// 回调契约：无论成败恰好回调一次；失败时以默认场景回调，调用方须检查 <see cref="UnityEngine.SceneManagement.Scene.IsValid"/>。
        /// </remarks>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="callBack">加载完成回调。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null) =>
            Internal_PeekHandler()?.LoadScene(location, packageName, sceneMode, suspendLoad, priority, callBack, progressCallBack);

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景（设为当前活动场景）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否激活成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ActivateScene(string location) => Internal_PeekHandler()?.ActivateScene(location) ?? false;

        /// <summary>
        /// 取消挂起。仅接受资源地址（挂起场景尚未产生场景短名）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool UnSuspend(string location) => Internal_PeekHandler()?.UnSuspend(location) ?? false;

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。失败返回 <c>false</c> 并保留登记供重试；句柄已失效（适配器发起卸载后置空等）同样视为失败，不会误报成功。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <returns>是否卸载成功。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null) =>
            Internal_PeekHandler()?.UnloadAsync(location, progressCallBack) ?? UniTask.FromResult(false);

        /// <summary>
        /// 卸载子场景（回调式）。回调契约：卸载发起后无论成败恰好回调一次（参数为是否成功）；无效请求（地址未登记、存在在途操作）不发起亦不回调。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="callBack">卸载完成回调（参数为是否卸载成功）。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Unload(string location, Action<bool> callBack = null, Action<float> progressCallBack = null) =>
            Internal_PeekHandler()?.Unload(location, callBack, progressCallBack);

        #endregion

        #region 场景查询 [SCENE QUERY]

        /// <summary>当前主场景名称（归一化场景短名，非资源地址；启动场景未经本服务加载时为引擎激活场景名）。</summary>
        public static string CurrentMainSceneName => Internal_PeekHandler()?.CurrentMainSceneName;

        /// <summary>已完成加载的子场景资源地址快照（不含加载中的子场景——在途登记请用 <see cref="IsContainScene"/> 查询）。</summary>
        public static IReadOnlyCollection<string> LoadedSubSceneLocations => Internal_PeekHandler()?.LoadedSubSceneLocations ?? Array.Empty<string>();

        /// <summary>
        /// 判断指定场景是否为当前主场景（身份判断，不含激活状态——激活状态请比较 <c>SceneManager.GetActiveScene().name</c> 与 <see cref="CurrentMainSceneName"/>）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否为主场景。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMainScene(string location) => Internal_PeekHandler()?.IsMainScene(location) ?? false;

        /// <summary>
        /// 查询场景是否已登记（主场景含启动场景，子场景含加载中未完成的挂起加载）。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否已登记。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsContainScene(string location) => Internal_PeekHandler()?.IsContainScene(location) ?? false;

        #endregion
    }
}
