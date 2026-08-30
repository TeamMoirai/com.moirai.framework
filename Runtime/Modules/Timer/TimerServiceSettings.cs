using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Timer
{
    [FrameworkSetting("[服务]计时器设置", "计时器后端配置", -430)]
    public sealed class TimerServiceSettings : FrameworkSettings<TimerServiceSettings>
    {
        [InfoBox("默认使用四级时间轮实现。可替换为自定义计时器后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private TimerServiceHandlerConfig m_HandlerConfig = new DefaultTimerHandlerConfig();

        public static TimerServiceHandlerConfig TimerServiceHandlerConfig => Instance.m_HandlerConfig;
    }
}
