using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源绑定扩展方法，提供声明式资源绑定 API。
    /// </summary>
    public static partial class ResourceBindingExtension
    {
        #region 字段 [FIELDS]

        private static ResourceServiceHandler s_Handler;
        private static IResourceBindingService s_BindingService;

#if UNITY_EDITOR
        /// <summary>
        /// 重置静态状态：兼容关闭 Domain Reload 的编辑器工作流（进入 Play 时清空上一会话残留）。
        /// </summary>
        /// <remarks>缓存对归零后由 <see cref="TryGetResourceService"/> 首次取用重新解析（处理器或绑定服务失效时它本就按同口径自愈）。</remarks>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Handler = null;
            s_BindingService = null;
        }
#endif

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static bool TryGetResourceService(out ResourceServiceHandler handler,
            out IResourceBindingService bindingService)
        {
            handler = s_Handler;
            bindingService = s_BindingService;
            if (handler != null && bindingService != null &&
                ReferenceEquals(handler.BindingService, bindingService))
            {
                return true;
            }

            handler = ResourceService.Handler;
            if (handler == null)
            {
                s_Handler = null;
                s_BindingService = null;
                bindingService = null;
                return false;
            }

            bindingService = handler.BindingService;
            if (bindingService == null)
            {
                s_Handler = null;
                s_BindingService = null;
                return false;
            }

            s_Handler = handler;
            s_BindingService = bindingService;
            return true;
        }


        /// <summary>
        /// 发起即忘的绑定：不等待结果，但抛出必须落日志。
        /// </summary>
        /// <remarks>裸 <c>Forget()</c> 会静默吞掉异常（含后端不支持某条异步绑定时抛出的 <c>GameException</c>），使缺陷不可观测。</remarks>
        private static void FireAndForget(UniTask<EResourceBindStatus> bind)
        {
            bind.Forget(exception => LogUtility.Error(exception));
        }

        #endregion
    }
}
