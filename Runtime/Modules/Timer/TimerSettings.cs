using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Timer
{
    [FrameworkSetting("计时器设置", "计时器后端配置", -458)]
    public sealed class TimerSettings : FrameworkSettings<TimerSettings>
    {
        [InfoBox("默认使用四级时间轮实现。可替换为自定义计时器后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private TimerHandler m_TimerHandler = new TimerHandler();

        public static TimerHandler TimerHandler => Instance.m_TimerHandler;
    }
}
