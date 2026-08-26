using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UpdateDriver
{
    [FrameworkSetting("更新驱动设置", "协程与帧事件驱动后端配置", -459)]
    public sealed class UpdateDriverSettings : FrameworkSettings<UpdateDriverSettings>
    {
        [InfoBox("默认使用常驻 GameObject 驱动。可替换为自定义驱动后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private UpdateDriverHandler m_UpdateDriverHandler = new UpdateDriverHandler();

        public static UpdateDriverHandler UpdateDriverHandler => Instance.m_UpdateDriverHandler;
    }
}
