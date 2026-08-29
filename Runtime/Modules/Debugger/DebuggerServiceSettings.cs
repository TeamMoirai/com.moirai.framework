using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    [FrameworkSetting("[服务]调试设置", "调试器窗口后端配置", -380)]
    public sealed class DebuggerServiceSettings : FrameworkSettings<DebuggerServiceSettings>
    {
        [InfoBox("默认使用内置调试器窗口组。可替换为自定义调试器后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private DebuggerServiceHandler m_DebuggerServiceHandler = new DefaultDebuggerHandler();

        public static DebuggerServiceHandler DebuggerServiceHandler => Instance.m_DebuggerServiceHandler;
    }
}
