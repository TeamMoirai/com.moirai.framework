using UnityEngine;

namespace Moirai.Atropos.Resource
{
    /// <summary>
    /// 资源绑定扩展方法，提供声明式资源绑定 API。
    /// </summary>
    public static partial class ResourceBindingExtension
    {
        #region 字段 [FIELDS]

        private static IResourceService s_ResourceService;
        private static IResourceBindingService s_BindingService;

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static bool TryGetResourceService(out IResourceService resourceService,
            out IResourceBindingService bindingService)
        {
            resourceService = s_ResourceService;
            bindingService = s_BindingService;
            if (resourceService != null && bindingService != null &&
                ReferenceEquals(resourceService.BindingService, bindingService))
            {
                return true;
            }

            resourceService = GameApp.Resource;
            if (resourceService == null)
            {
                s_ResourceService = null;
                s_BindingService = null;
                bindingService = null;
                return false;
            }

            bindingService = resourceService.BindingService;
            if (bindingService == null)
            {
                s_ResourceService = null;
                s_BindingService = null;
                return false;
            }

            s_ResourceService = resourceService;
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
