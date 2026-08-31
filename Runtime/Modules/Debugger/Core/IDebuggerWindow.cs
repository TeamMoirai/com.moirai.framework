using UnityEngine.UIElements;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 调试器窗口接口。
    /// <para>生命周期契约：注册时 <see cref="Initialize"/>（经 <c>params</c> 参数注入依赖）→ 选中时 <see cref="OnEnter"/> 且宿主挂载 <see cref="CreateView"/> 返回的视图 → 可见期间逐帧 <see cref="OnUpdate"/> → 离开时 <see cref="OnLeave"/> → 注销或服务关闭时 <see cref="Shutdown"/>。</para>
    /// <para>视图为 UI Toolkit <see cref="VisualElement"/> 树；窗口持有自身视图状态（滚动位置、选中项等），宿主按需重建视图时重新调用 <see cref="CreateView"/>。</para>
    /// </summary>
    public interface IDebuggerWindow
    {
        /// <summary>
        /// 初始化调试器窗口。
        /// </summary>
        /// <param name="args">初始化调试器窗口参数。</param>
        void Initialize(params object[] args);

        /// <summary>
        /// 关闭调试器窗口（注销前释放资源、退订事件）。
        /// </summary>
        void Shutdown();

        /// <summary>
        /// 进入调试器窗口（视图即将挂载）。
        /// </summary>
        void OnEnter();

        /// <summary>
        /// 离开调试器窗口（视图已卸载或调试器窗口关闭）。
        /// </summary>
        void OnLeave();

        /// <summary>
        /// 调试器窗口轮询（仅窗口可见期间驱动）。
        /// </summary>
        /// <param name="elapseSeconds">逻辑流逝时间（以秒为单位）。</param>
        /// <param name="realElapseSeconds">真实流逝时间（以秒为单位）。</param>
        void OnUpdate(float elapseSeconds, float realElapseSeconds);

        /// <summary>
        /// 创建运行时 UI Toolkit 视图。
        /// </summary>
        /// <returns>窗口视图根元素（宿主负责挂载与卸载）。</returns>
        VisualElement CreateView();
    }
}
