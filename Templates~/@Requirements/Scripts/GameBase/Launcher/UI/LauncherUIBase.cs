using Moirai.Atropos;
using Moirai.Atropos.UI;
using UnityEngine;

namespace Moirai.GameMain.UI
{
    /// <summary>
    /// 热更窗口基类：只认面板物体与显隐，不参与框架的窗口栈。
    /// </summary>
    public class LauncherUIBase
    {
        /// <summary>面板物体；由 <see cref="LauncherMgr"/> 实例化预制体后注入，注入前为 <c>null</c>。</summary>
        public GameObject gameObject;

        /// <summary>面板变换；面板物体被销毁后为 <c>null</c>。</summary>
        public Transform transform
        {
            get
            {
                if (gameObject != null)
                {
                    return gameObject.transform;
                }

                return null;
            }
        }

        /// <summary>面板矩形变换；面板不是 RectTransform 时为 <c>null</c>。</summary>
        public RectTransform rectTransform => transform as RectTransform;

        /// <summary>
        /// 面板就绪后的一次性绑定入口。
        /// </summary>
        protected virtual void ScriptGenerator()
        {
        }

        /// <summary>
        /// 取回面板根上的绑定器，并把缺失与未接线报成一条日志。
        /// </summary>
        /// <typeparam name="T">本窗的绑定器类型。</typeparam>
        /// <returns>取到的绑定器；面板上没有或槽位没接齐时回 <c>null</c>。</returns>
        protected T BindBinder<T>() where T : UIBindComponent, ILauncherBinder
        {
            var binder = gameObject != null ? gameObject.GetComponent<T>() : null;
            if (binder == null)
            {
                LogUtility.Error("引导窗 {0} 缺少组件 {1}，面板不会绑定。", GetType().Name, typeof(T).Name);
                return null;
            }

            var unbound = binder.DescribeUnboundSlots();
            if (unbound != null)
            {
                LogUtility.Error("引导窗 {0} 的 {1} 未接线：{2}", GetType().Name, typeof(T).Name, unbound);
                return null;
            }

            return binder;
        }

        /// <summary>
        /// 面板注入后的初始化入口：走一次 <see cref="ScriptGenerator"/> 绑定。
        /// </summary>
        public void OnInit()
        {
            ScriptGenerator();
        }

        /// <summary>
        /// 显示窗口并交回载荷；初次显示走一次 <see cref="OnOpen"/>。
        /// </summary>
        /// <param name="param">窗口载荷。</param>
        public virtual void Show(object param)
        {
            if (gameObject != null && !gameObject.activeSelf)
            {
                OnOpen();
            }

            gameObject?.SetActive(true);
        }

        /// <summary>
        /// 初次打开。
        /// </summary>
        protected virtual void OnOpen()
        {
        }

        /// <summary>
        /// 隐藏面板：只关物体激活开关，不销毁面板，也不移出管理器的登记表。
        /// </summary>
        public void Hide()
        {
            gameObject?.SetActive(false);
        }

        /// <summary>
        /// 关闭本窗：把控制权交回 <see cref="LauncherMgr"/>。
        /// </summary>
        /// <remarks>
        /// 时序不变量：本方法经 <see cref="LauncherMgr.CloseUI(LauncherUIBase)"/> 移键并销毁物体，
        /// 返回后调用方持有的面板与绑定器都不可再用。这是本类唯一会把调用方送回管理器的那条边。
        /// </remarks>
        public void Close()
        {
            LauncherMgr.CloseUI(this);
        }
    }
}
