using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Resource
{
    [FrameworkSetting("资源设置", "资源加载、缓存与绑定后端配置", -495)]
    public sealed partial class ResourceSettings : FrameworkSettings<ResourceSettings>
    {
        [InfoBox("默认使用内置资源后端。可替换为自定义资源后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ResourceHandler m_ResourceHandler = new ResourceHandler();

        /// <summary>资源处理器（后端）。</summary>
        public static ResourceHandler ResourceHandler => Instance.m_ResourceHandler;
    }
}
