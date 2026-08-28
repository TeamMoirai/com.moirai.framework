using System;
using Cysharp.Threading.Tasks;
using Moirai.Atropos.Resource;
using UnityEngine.SceneManagement;

namespace Moirai.Atropos.Scene
{
    /// <summary>
    /// 场景服务外观（Facade）。
    /// <para>统一的静态场景访问入口，通过替换 <see cref="Handler"/> 即可在不同场景加载后端之间零成本切换。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="SceneServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(SceneServiceHandler))]
    [ServiceDependency(typeof(ResourceService))]
    public partial class SceneService : ServiceBase
    {
#region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="SceneServiceSettings"/> 创建默认场景处理器。
        /// </summary>
        /// <returns>默认场景处理器实例。</returns>
        private static SceneServiceHandler CreateDefaultHandler()
        {
            return SceneServiceSettings.SceneServiceHandler;
        }

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化场景服务。由容器在构建期调用。
        /// <para>确保 <c>SceneService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭场景服务。由容器在关闭期调用。
        /// </summary>
        public override void Shutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        #endregion

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
        public static UniTask<UnityEngine.SceneManagement.Scene> LoadSceneAsync(string location, LoadSceneMode sceneMode = LoadSceneMode.Single, bool suspendLoad = false, uint priority = 100,
            bool gcCollect = true, Action<float> progressCallBack = null) =>
            s_Handler != null
                ? s_Handler.LoadSceneAsync(location, sceneMode, suspendLoad, priority, gcCollect, progressCallBack)
                : UniTask.FromResult(default(UnityEngine.SceneManagement.Scene));

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
        public static void LoadScene(string location, string packageName = "", LoadSceneMode sceneMode = LoadSceneMode.Single,
            bool suspendLoad = false, uint priority = 100, bool gcCollect = true, Action<UnityEngine.SceneManagement.Scene> callBack = null, Action<float> progressCallBack = null) =>
            s_Handler?.LoadScene(location, packageName, sceneMode, suspendLoad, priority, gcCollect, callBack, progressCallBack);

        #endregion

        #region 场景控制 [SCENE CONTROL]

        /// <summary>
        /// 当前主场景名称。
        /// </summary>
        public static string CurrentMainSceneName => s_Handler?.CurrentMainSceneName ?? string.Empty;

        /// <summary>
        /// 激活场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否激活成功。</returns>
        public static bool ActivateScene(string location) => s_Handler?.ActivateScene(location) ?? false;

        /// <summary>
        /// 取消挂起。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否取消成功。</returns>
        public static bool UnSuspend(string location) => s_Handler?.UnSuspend(location) ?? false;

        /// <summary>
        /// 判断指定场景是否为当前主场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否为主场景。</returns>
        public static bool IsMainScene(string location) => s_Handler?.IsMainScene(location) ?? false;

        #endregion

        #region 场景卸载 [SCENE UNLOADING]

        /// <summary>
        /// 异步卸载子场景。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="progressCallBack">进度回调。</param>
        /// <returns>是否卸载成功。</returns>
        public static UniTask<bool> UnloadAsync(string location, Action<float> progressCallBack = null) =>
            s_Handler != null
                ? s_Handler.UnloadAsync(location, progressCallBack)
                : UniTask.FromResult(false);

        /// <summary>
        /// 卸载子场景（回调式）。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <param name="callBack">卸载完成回调。</param>
        /// <param name="progressCallBack">进度回调。</param>
        public static void Unload(string location, Action callBack = null, Action<float> progressCallBack = null) =>
            s_Handler?.Unload(location, callBack, progressCallBack);

        #endregion

        /// <summary>
        /// 查询场景是否已加载。
        /// </summary>
        /// <param name="location">场景资源定位地址。</param>
        /// <returns>是否已加载。</returns>
        public static bool IsContainScene(string location) => s_Handler?.IsContainScene(location) ?? false;
    }
}
