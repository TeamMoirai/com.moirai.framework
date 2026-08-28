using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UpdateDriver
{
    [FrameworkSetting("[服务]更新驱动设置", "协程与帧事件驱动后端配置", -440)]
    public sealed class UpdateDriverServiceSettings : FrameworkSettings<UpdateDriverServiceSettings>
    {
        [InfoBox("默认使用常驻 GameObject 驱动。可替换为自定义驱动后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private UpdateDriverServiceHandler m_UpdateDriverServiceHandler = new UnityUpdateDriverHandler();

        public static UpdateDriverServiceHandler UpdateDriverServiceHandler => Instance.m_UpdateDriverServiceHandler;
    }
}
