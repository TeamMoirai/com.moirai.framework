using System;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景处理器。支持主场景切换、附加场景加载/卸载、进度回调和挂起加载。
    /// <para>由 <see cref="SceneServiceSettings"/> 序列化配置，可替换为自定义场景加载后端。</para>
    /// </summary>
    [Serializable]
    public abstract class SceneServiceHandler : FrameworkHandler
    {
        /// <summary>
        /// 当前主场景名称。
        /// </summary>
        public abstract string CurrentMainSceneName { get; }

        #region 场景加载 [SCENE LOADING]

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="progressCallBack">进度回调。</param>
        /// <returns>加载完成的场景。</returns>
        public abstract UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null);

        /// <summary>
        /// 同步加载场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="sceneMode">场景加载模式。</param>
        /// <param name="suspendLoad">是否挂起加载。</param>
        /// <param name="priority">加载优先级。</param>
        /// <param name="gcCollect">主场景加载后是否执行 GC 回收。</param>
        /// <param name="callBack">加载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public abstract void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null);

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 激活场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否激活成功。</returns>
        public abstract bool ActivateScene(string location);

        /// <summary>
        /// 取消挂起。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public abstract bool UnSuspend(string location);

        /// <summary>
        /// 判断指定场景是否为当前主场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否为主场景。</returns>
        public abstract bool IsMainScene(string location);

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="progressCallBack">进度回调。</param>
        /// <returns>是否卸载成功。</returns>
        public abstract UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null);

        /// <summary>
        /// 卸载子场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public abstract void Unload(string location, Action callBack = null, Action<float> progressCallBack = null);

        #endregion

        /// <summary>
        /// 查询场景是否已加载。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否已加载。</returns>
        public abstract bool IsContainScene(string location);
    }
}
