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

        private static ResourceOwner EnsureOwner(IResourceBindingService bindingService, Component target)
        {
            ResourceOwner owner = target.GetComponent<ResourceOwner>();
            if (owner == null)
            {
                owner = target.gameObject.AddComponent<ResourceOwner>();
            }

            bindingService.RegisterOwner(owner);
            return owner;
        }

        #endregion
    }
}
