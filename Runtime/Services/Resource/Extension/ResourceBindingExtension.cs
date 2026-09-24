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
        /// 发起即忘的绑定：抛出必须落日志。
        /// <para>裸 <c>Forget()</c> 把异常整个丢掉，现场就是"界面上一个字没变、日志里也一个字没有"。
        /// 后端不支持某条异步绑定时报出来的 GameException 此前正是这样消失的——那条信息是这个问题
        /// 唯一的线索，丢掉它等于把缺陷改成不可观测。</para>
        /// </summary>
        private static void FireAndForget(UniTask<EResourceBindStatus> bind)
        {
            bind.Forget(exception => LogUtility.Error(exception));
        }

        #endregion
    }
}
