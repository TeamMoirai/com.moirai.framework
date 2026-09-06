using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.ConfigTable
{
    [FrameworkSetting("[服务]配置表设置", "配置表后端选择", -510)]
    public sealed partial class ConfigTableServiceSettings : FrameworkSettings<ConfigTableServiceSettings>
    {
        [InfoBox("默认使用兜底实现（记录错误并返回空结果）。游戏侧生成代码后应替换为自定义处理器。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private ConfigTableServiceHandler m_ConfigTableServiceHandler = new DefaultConfigTableHandler();

        public static ConfigTableServiceHandler ConfigTableServiceHandler => Instance.m_ConfigTableServiceHandler;
    }
}
