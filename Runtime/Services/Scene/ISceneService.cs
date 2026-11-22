using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景服务契约（策略模式契约接口）：场景后端的唯一实现契约，也是服务互调与单测的注入契约。
    /// </summary>
    /// <remarks>
    /// 支持主场景切换、附加场景加载/卸载、进度回调和挂起加载；由 <see cref="SceneServiceSettings"/> 序列化配置，可替换。 <br />
    /// 实现约束：实现类须继承 <see cref="Moirai.Atropos.ServiceBase"/>（绑定即注册进容器，生命周期由容器拓扑驱动）。 <br />
    /// 错误契约：加载失败（资源服务未就绪、后端加载错误、同场景在途/已登记、主场景并发互斥、地址为空）抛出 <see cref="GameException"/>（fail fast）；卸载属可重试的清理操作， <br />
    /// 失败以 <c>false</c> 报告并保留登记供重试，已登记 Loaded 项卸载时句柄失效按失败处理。 <br />
    /// 取消契约：场景加载一经发起不可中止，<see cref="LoadSceneAsync"/> 的 <see cref="CancellationToken"/> 仅取消等待与进度回调（放弃等待语义），登记与事件由后端在加载真正结束时收尾。
    /// </remarks>
    public interface ISceneService
    {
        /// <summary>当前主场景名称（经 <see cref="UnityEngine.SceneManagement.Scene.name"/> 归一化的场景短名，非资源地址）。</summary>
        /// <remarks>启动场景未经本服务加载时为引擎当前激活场景名。</remarks>
        string CurrentMainSceneName { get; }

        /// <summary>已完成加载的子场景资源地址快照（不含加载中的子场景——在途登记请用 <see cref="IsContainScene"/> 查询）。</summary>
        IReadOnlyCollection<string> LoadedSubSceneLocations { get; }

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 异步加载场景。加载失败抛出 <see cref="GameException"/>。
        /// </summary>
        /// <param name="progressCallBack">进度回调（成功完成时以 1.0 收尾一次；失败不伪报完成进度）。</param>
        /// <param name="cancellationToken">取消令牌——仅取消等待与进度回调，不中止底层加载；加载最终完成后仍会完成登记并触发事件。</param>
        /// <returns>加载完成的场景。</returns>
        UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            Action<float> progressCallBack = null, string packageName = "", CancellationToken cancellationToken = default);

        /// <summary>
        /// 同步发起场景加载（回调式）。
        /// </summary>
        /// <remarks>回调契约：无论成败恰好回调一次；失败时以默认场景回调，调用方须检查 <see cref="UnityEngine.SceneManagement.Scene.IsValid"/>。</remarks>
        void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null);

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景（设为当前活动场景）。<paramref name="location"/> 同时接受资源地址与场景短名。
        /// </summary>
        bool ActivateScene(string location);

        /// <summary>
        /// 取消挂起，允许场景继续加载并激活。仅接受资源地址（挂起场景尚未产生场景短名）。
        /// </summary>
        /// <remarks>本服务发起的挂起加载必须最终解除挂起——底层加载无中止能力。</remarks>
        bool UnSuspend(string location);

        /// <summary>
        /// 判断指定场景是否为当前主场景（身份判断，不含激活状态）。<paramref name="location"/> 同时接受资源地址与场景短名。
        /// </summary>
        bool IsMainScene(string location);

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。失败返回 <c>false</c> 并保留登记供重试；句柄已失效同样视为失败。
        /// </summary>
        UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null);

        /// <summary>
        /// 卸载子场景（回调式）。回调契约：卸载发起后无论成败恰好回调一次（参数为是否成功）；无效请求（地址未登记、存在在途操作）不发起亦不回调。
        /// </summary>
        void Unload(string location, Action<bool> callBack = null, Action<float> progressCallBack = null);

        #endregion

        /// <summary>
        /// 查询场景是否已登记（主场景含启动场景，子场景含加载中未完成的挂起加载）。<paramref name="location"/> 同时接受资源地址与场景短名。
        /// </summary>
        bool IsContainScene(string location);
    }
}
