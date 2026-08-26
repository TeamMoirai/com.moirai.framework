using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    [FrameworkSetting("调试器设置", "调试器窗口后端配置", -457)]
    public sealed class DebuggerSettings : FrameworkSettings<DebuggerSettings>
    {
        [InfoBox("默认使用内置调试器窗口组。可替换为自定义调试器后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private DebuggerHandler m_DebuggerHandler = new DebuggerHandler();

        public static DebuggerHandler DebuggerHandler => Instance.m_DebuggerHandler;
    }
}
