using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景处理器。支持主场景切换、附加场景加载/卸载、进度回调和挂起加载。
    /// <para>由 <see cref="SceneServiceSettings"/> 序列化配置，可替换为自定义场景加载后端。</para>
    /// <para>错误契约：加载失败（资源服务未就绪、后端加载错误、同场景在途/已登记、主场景并发互斥、地址为空）抛出 <see cref="GameException"/>，
    /// 调用方无法用失败结果继续，故 fail fast；卸载属可重试的清理操作，失败以 <c>false</c> 返回值报告并保留登记供重试，不中断调用方流程。
    /// 已登记 Loaded 项卸载时若句柄已失效（返回 null 的卸载操作），按失败处理，不得误报成功。</para>
    /// <para>取消契约：场景加载一经发起不可中止（引擎与资源后端均无中止能力），<see cref="LoadSceneAsync"/> 的
    /// <see cref="CancellationToken"/> 仅取消等待与进度回调（放弃等待语义），登记与事件由处理器在加载真正结束时收尾。</para>
    /// </summary>
    [Serializable]
    public abstract class SceneServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 当前主场景名称（经 <see cref="UnityEngine.SceneManagement.Scene.name"/> 归一化的场景短名，非资源地址）。
        /// <para>启动场景未经本服务加载时为引擎当前激活场景名。</para>
        /// </summary>
        public abstract string CurrentMainSceneName { get; }

        /// <summary>
        /// 已完成加载的子场景资源地址快照（不含加载中的子场景——在途登记请用 <see cref="SceneService.IsContainScene"/> 查询）。
        /// </summary>
        public abstract IReadOnlyCollection<string> LoadedSubSceneLocations { get; }

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 异步加载场景。加载失败抛出 <see cref="GameException"/>。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <param name="packageName">资源包名称（空串使用默认包）。</param>
        /// <param name="cancellationToken">取消令牌——仅取消等待与进度回调，不中止底层加载；加载最终完成后仍会完成登记并触发事件。</param>
        /// <returns>加载完成的场景。</returns>
        public abstract UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null, string packageName = "", CancellationToken cancellationToken = default);

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
        public abstract void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null);

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景（设为当前活动场景）。<paramref name="location"/> 同时接受资源地址与场景短名。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否激活成功。</returns>
        public abstract bool ActivateScene(string location);

        /// <summary>
        /// 取消挂起，允许场景继续加载并激活。仅接受资源地址（挂起场景尚未产生场景短名）。
        /// <para>注意：本服务发起的挂起加载必须最终解除挂起——底层加载无中止能力。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public abstract bool UnSuspend(string location);

        /// <summary>
        /// 判断指定场景是否为当前主场景（身份判断，不含激活状态——激活状态请比较
        /// <c>SceneManager.GetActiveScene().name</c> 与 <see cref="CurrentMainSceneName"/>）。
        /// <para><paramref name="location"/> 同时接受资源地址与场景短名。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否为主场景。</returns>
        public abstract bool IsMainScene(string location);

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。失败返回 <c>false</c> 并保留登记供重试；句柄已失效同样视为失败。
        /// <paramref name="location"/> 同时接受资源地址与场景短名。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <returns>是否卸载成功。</returns>
        public abstract UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null);

        /// <summary>
        /// 卸载子场景（回调式）。回调契约：卸载发起后无论成败恰好回调一次（参数为是否成功）；无效请求（地址未登记、存在在途操作）不发起亦不回调。
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <param name="callBack">卸载完成回调（参数为是否卸载成功）。</param>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        public abstract void Unload(string location, Action<bool> callBack = null, Action<float> progressCallBack = null);

        #endregion

        /// <summary>
        /// 查询场景是否已登记（主场景含启动场景，子场景含加载中未完成的挂起加载）。
        /// <para><paramref name="location"/> 同时接受资源地址与场景短名。</para>
        /// </summary>
        /// <param name="location">场景资源定位地址或场景短名。</param>
        /// <returns>是否已登记。</returns>
        public abstract bool IsContainScene(string location);
    }
}
