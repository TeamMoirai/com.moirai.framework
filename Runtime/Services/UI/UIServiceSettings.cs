using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务设置：承载「启用哪几支 UI 后端」与「内置资源窗的 Resources 父目录」这两份配置。
    /// </summary>
    /// <remarks>
    /// 清单每一项是一支后端驱动者的托管引用：两支异构，元素类型因此是共同基类 <see cref="UIServiceHandler"/>。 <br />
    /// <see cref="UIService.OnInit"/> 逐支调用 <see cref="UIServiceHandler.Internal_Register"/>，实现类把自己交回本轨的认领门。 <br />
    /// 填槽、挂广播与初始化都发生在那一轨的 partial 里；清单为空就是「没有任何后端被启用」，初始化当场抬错。
    /// </remarks>
    [FrameworkSetting("[服务]UI设置", "UI窗口管理后端配置", -470)]
    public sealed class UIServiceSettings : FrameworkSettings<UIServiceSettings>
    {
        [InfoBox("启用哪几支后端就列哪几支：UIService 初始化时按这份清单逐支实例化。清单为空则初始化当场报错。", InfoMessageType.None)]
        [ProviderDropdown]
        [SerializeReference] private UIServiceHandler[] m_EnabledHandlers = new UIServiceHandler[] { new UGUIHandler() };

        [Tooltip("内置资源窗（fromResources 档）在 Resources 下的父目录：开窗传 windowId 时地址按本目录拼出。")]
        [FolderPath(ParentFolder = "Assets/Resources")]
        [SerializeField] private string m_UIFolder = "UI";

        /// <summary>启用中的后端驱动者清单（按配置填槽的唯一来路）。</summary>
        internal static UIServiceHandler[] EnabledHandlers => Instance.m_EnabledHandlers;

        /// <summary>内置资源窗在 <c>Resources</c> 下的父目录（寻址换算的目录档）。</summary>
        internal static string ResourcesFolder => Instance.m_UIFolder;

        /// <summary>
        /// 换掉启用清单：写的是配置而不是槽位，驱动者仍只由 <see cref="UIService.OnInit"/> 按这份清单造出来。
        /// </summary>
        /// <remarks>
        /// 用例用它造「只启用一支」与「一支都没启用」这两档；交回的清单不进资产（既不标脏也不回写）。
        /// </remarks>
        /// <param name="enabledHandlers">新的启用清单。</param>
        internal static void Internal_SetEnabledHandlers(UIServiceHandler[] enabledHandlers)
        {
            Instance.m_EnabledHandlers = enabledHandlers;
        }
    }
}
