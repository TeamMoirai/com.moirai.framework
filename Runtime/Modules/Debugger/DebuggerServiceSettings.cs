using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试设置（<see cref="FrameworkSetting"/> 项）。
    /// <para>激活策略直接存于设置资产（独立于处理器配置）——初始化期早于服务注册的调用方（如 UI 错误日志开关解析）也可直接读取。</para>
    /// </summary>
    [FrameworkSetting("[服务]调试设置", "调试器窗口后端配置与激活策略", -380)]
    public sealed class DebuggerServiceSettings : FrameworkSettings<DebuggerServiceSettings>
    {
        #region 字段 [FIELDS]

        [InfoBox("激活策略决定调试器悬浮入口的可见性；命令行 -showdebugger 参数可强制开启。", InfoMessageType.None)]
        [SerializeField] private DebuggerActiveWindowType m_ActiveWindowType = DebuggerActiveWindowType.OnlyOpenWhenDevelopment;

        [InfoBox("默认使用内置 UI Toolkit 调试器。可替换为自定义调试器后端。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private DebuggerServiceHandlerConfig m_HandlerConfig = new DefaultDebuggerHandlerConfig();

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取调试器激活策略。
        /// </summary>
        public static DebuggerActiveWindowType ActiveWindowType => Instance.m_ActiveWindowType;

        /// <summary>
        /// 获取调试器处理器配置。
        /// </summary>
        public static DebuggerServiceHandlerConfig DebuggerServiceHandlerConfig => Instance.m_HandlerConfig;

        #endregion
    }
}
