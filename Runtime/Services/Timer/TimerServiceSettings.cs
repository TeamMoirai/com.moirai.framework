using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Timer
{
    [FrameworkSetting("[服务]计时器设置", "计时器后端配置", -430)]
    public sealed class TimerServiceSettings : FrameworkSettings<TimerServiceSettings>
    {
        [InfoBox("默认使用四级时间轮实现。可替换为自定义计时器后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private TimerServiceHandler m_TimerServiceHandler = TimerService.CreateDefaultHandler();
        /// <summary>
        /// 默认计时器处理器后端。
        /// </summary>
        public static TimerServiceHandler TimerServiceHandler => Instance.m_TimerServiceHandler;
    }
}
