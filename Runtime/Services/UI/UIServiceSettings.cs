using Sirenix.OdinInspector;
using UnityEngine;

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI 服务设置：承载「启用哪几支 UI 后端」这份配置。
    /// </summary>
    /// <remarks>
    /// 清单里的每一项是一支后端驱动者的托管引用（两支异构，元素类型因此是共同基类 <see cref="UIServiceHandler"/>）， <br />
    /// <see cref="UIService.OnInit"/> 逐支调用它的 <see cref="UIServiceHandler.Internal_Register"/>，由实现类把自己交回本轨的认领门——填槽、挂广播、初始化都在那一轨的 partial 里；清单为空就是「没有任何后端被启用」，当场抬错。 <br />
    /// </remarks>
    [FrameworkSetting("[服务]UI设置", "UI窗口管理后端配置", -470)]
    public sealed class UIServiceSettings : FrameworkSettings<UIServiceSettings>
    {
        [InfoBox("启用哪几支后端就列哪几支：UIService 初始化时按这份清单逐支实例化。清单为空则初始化当场报错。", InfoMessageType.None)]
        [SerializeReference] private UIServiceHandler[] m_EnabledHandlers = new UIServiceHandler[] { new UGUIHandler() };

        /// <summary>启用中的后端驱动者清单（按配置填槽的唯一来路）。</summary>
        internal static UIServiceHandler[] EnabledHandlers => Instance.m_EnabledHandlers;

        /// <summary>
        /// 换掉启用清单：写的是配置，不是槽位——驱动者仍只由 <see cref="UIService.OnInit"/> 按这份清单造出来。
        /// <para>用例用它造「只启用一支」与「一支都没启用」这两档；交回的清单不进资产（没有标脏，也没有回写）。</para>
        /// </summary>
        /// <param name="enabledHandlers">新的启用清单。</param>
        internal static void Internal_SetEnabledHandlers(UIServiceHandler[] enabledHandlers)
        {
            Instance.m_EnabledHandlers = enabledHandlers;
        }
    }
}
