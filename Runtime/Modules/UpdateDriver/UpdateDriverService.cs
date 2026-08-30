using System.Collections;
using UnityEngine;

namespace Moirai.Atropos.UpdateDriver
{
    /// <summary>
    /// 更新驱动服务外观（Facade）。
    /// <para>统一的静态协程与帧事件注入入口，通过替换 <see cref="Handler"/> 即可切换驱动后端。</para>
    /// <para>未显式设置处理器时，使用 <see cref="CreateDefaultHandler"/> 从 <see cref="UpdateDriverServiceSettings"/> 创建处理器实例。</para>
    /// <para>Handler 属性由 <c>HandlerHostGenerator</c> 源生成器自动生成（线程安全懒加载）。</para>
    /// </summary>
    [HandlerHost(typeof(UpdateDriverServiceHandler))]
    public partial class UpdateDriverService : ServiceBase
    {
        #region 属性 [PROPERTIES]

        /// <summary>
        /// 服务是否可用
        /// </summary>
        public static bool IsValid => s_Handler != null;

        #endregion

        #region 处理器 [HANDLER]

        /// <summary>
        /// 从 <see cref="UpdateDriverServiceSettings"/> 创建默认更新驱动处理器。
        /// <para>首行先确保服务已注册（<c>GameServices.EnsureRegistered</c>，幂等）——外观首次访问即完成世界注册。</para>
        /// </summary>
        /// <returns>默认更新驱动处理器实例。</returns>
        private static UpdateDriverServiceHandler CreateDefaultHandler()
        {
            GameServices.EnsureRegistered<UpdateDriverService>();
            return UpdateDriverServiceSettings.UpdateDriverServiceConfig.CreateHandler();
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <summary>
        /// 初始化更新驱动服务。由容器在构建期调用。
        /// <para>确保 <c>UpdateDriverService.Handler</c> 已赋值（触发 <see cref="CreateDefaultHandler"/> 懒加载）。</para>
        /// </summary>
        public override void OnInit()
        {
            _ = Handler;
        }

        /// <summary>
        /// 关闭更新驱动服务。由容器在关闭期调用。
        /// </summary>
        public override void OnShutdown()
        {
            s_Handler?.Internal_Shutdown();
            s_Handler = null;
        }

        #endregion

        #region 控制协程 [COROUTINE CONTROL]

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(string methodName) =>
            Handler.StartCoroutine(methodName);

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(IEnumerator routine) =>
            Handler.StartCoroutine(routine);

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        public static Coroutine StartCoroutine(string methodName, object value) =>
            Handler.StartCoroutine(methodName, value);

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(string methodName) =>
            Handler.StopCoroutine(methodName);

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(IEnumerator routine) =>
            Handler.StopCoroutine(routine);

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(Coroutine routine) =>
            Handler.StopCoroutine(routine);

        /// <summary>
        /// 停止所有全局协程。
        /// </summary>
        public static void StopAllCoroutines() =>
            Handler.StopAllCoroutines();

        #endregion

        #region 注入 Unity Update [INJECT UNITY UPDATE]

        /// <summary>
        /// 添加帧更新事件。
        /// </summary>
        public static void AddUpdateListener(System.Action action) =>
            Handler.AddUpdateListener(action);

        /// <summary>
        /// 添加物理帧更新事件。
        /// </summary>
        public static void AddFixedUpdateListener(System.Action action) =>
            Handler.AddFixedUpdateListener(action);

        /// <summary>
        /// 添加Late帧更新事件。
        /// </summary>
        public static void AddLateUpdateListener(System.Action action) =>
            Handler.AddLateUpdateListener(action);

        /// <summary>
        /// 移除帧更新事件。
        /// </summary>
        public static void RemoveUpdateListener(System.Action action) =>
            Handler.RemoveUpdateListener(action);

        /// <summary>
        /// 移除物理帧更新事件。
        /// </summary>
        public static void RemoveFixedUpdateListener(System.Action action) =>
            Handler.RemoveFixedUpdateListener(action);

        /// <summary>
        /// 移除Late帧更新事件。
        /// </summary>
        public static void RemoveLateUpdateListener(System.Action action) =>
            Handler.RemoveLateUpdateListener(action);

        #endregion

        #region Unity 事件注入 [UNITY EVENTS INJECT]

        /// <summary>
        /// 注册Destroy事件。
        /// </summary>
        public static void AddDestroyListener(System.Action action) =>
            Handler.AddDestroyListener(action);

        /// <summary>
        /// 反注册Destroy事件。
        /// </summary>
        public static void RemoveDestroyListener(System.Action action) =>
            Handler.RemoveDestroyListener(action);

        /// <summary>
        /// 注册OnDrawGizmos事件。
        /// </summary>
        public static void AddOnDrawGizmosListener(System.Action action) =>
            Handler.AddOnDrawGizmosListener(action);

        /// <summary>
        /// 反注册OnDrawGizmos事件。
        /// </summary>
        public static void RemoveOnDrawGizmosListener(System.Action action) =>
            Handler.RemoveOnDrawGizmosListener(action);

        /// <summary>
        /// 注册OnDrawGizmosSelected事件。
        /// </summary>
        public static void AddOnDrawGizmosSelectedListener(System.Action action) =>
            Handler.AddOnDrawGizmosSelectedListener(action);

        /// <summary>
        /// 反注册OnDrawGizmosSelected事件。
        /// </summary>
        public static void RemoveOnDrawGizmosSelectedListener(System.Action action) =>
            Handler.RemoveOnDrawGizmosSelectedListener(action);

        /// <summary>
        /// 注册OnApplicationPause事件。
        /// </summary>
        public static void AddOnApplicationPauseListener(System.Action<bool> action) =>
            Handler.AddOnApplicationPauseListener(action);

        /// <summary>
        /// 反注册OnApplicationPause事件。
        /// </summary>
        public static void RemoveOnApplicationPauseListener(System.Action<bool> action) =>
            Handler.RemoveOnApplicationPauseListener(action);

        #endregion
    }
}
