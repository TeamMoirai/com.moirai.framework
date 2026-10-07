using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_OBFUZ
using Obfuz;
#endif

namespace Moirai.Atropos.UI
{
    /// <summary>
    /// UI基类。
    /// </summary>
#if ENABLE_OBFUZ
    [ObfuzIgnore(ObfuzScope.TypeName, ApplyToChildTypes = true)]
#endif
    public abstract class UIBase
    {
        /// <summary>依赖注入回调：框架在 UI 初始化或创建时调用，用于注入所需的服务 / 依赖。</summary>
#pragma warning disable CS8632 // 只能在 "#nullable" 注释上下文内的代码中使用可为 null 的引用类型的注释。
        public static Action<UIBase>? Injector;
#pragma warning restore CS8632 // 只能在 "#nullable" 注释上下文内的代码中使用可为 null 的引用类型的注释。

        /// <summary>
        /// 免域重载复位：全局可变注入点跨 Play/跨测试夹具残留会互相污染（对齐 InputService 的静态位复位范式）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInjectorOnDomainReload()
        {
            Injector = null;
        }

        /// <summary>
        /// UI类型。
        /// </summary>
        public enum UIType
        {
            /// <summary>无。</summary>
            None,
            /// <summary>弹窗。</summary>
            Window,
            /// <summary>控件。</summary>
            Widget,
        }
        
        /// <summary>所属UI父节点。</summary>
        protected UIBase _parent = null;

        /// <summary>UI父节点。</summary>
        public UIBase Parent => _parent;

        /// <summary>自定义数据集。</summary>
        protected System.Object[] _params;
        
        /// <summary>自定义数据。</summary>
        public System.Object UserData
        {
            get
            {
                if (_params != null && _params.Length >= 1)
                {
                    return _params[0];
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>自定义数据集。</summary>
        public System.Object[] Params => _params;

        /// <summary>窗口的实例资源对象。</summary>
        // ReSharper disable once InconsistentNaming
        public virtual GameObject gameObject { get; protected set; }

        /// <summary>窗口位置组件。</summary>
        // ReSharper disable once InconsistentNaming
        public virtual Transform transform { get; protected set; }

        /// <summary>窗口矩阵位置组件。</summary>
        // ReSharper disable once InconsistentNaming
        public virtual RectTransform rectTransform { get; protected set; }

        /// <summary>UI类型。</summary>
        public virtual UIType Type => UIType.None;

        /// <summary>资源是否准备完毕。</summary>
        public bool IsPrepare { get; protected set; }

        /// <summary>UI子组件列表。</summary>
        public List<UIWidget> ChildList = new List<UIWidget>();

        /// <summary>存在Update更新的UI子组件列表。</summary>
        protected List<UIWidget> _updateChildList = null;

        /// <summary>是否持有Update行为。</summary>
        protected bool _updateListValid = false;

        /// <summary>是否标记脏排序。</summary>
        protected bool _isSortingOrderDirty = false;

        /// <summary>
        /// 依赖注入。
        /// </summary>
        protected void Inject()
        {
            Injector?.Invoke(this);
        }

        /// <summary>
        /// 代码自动生成绑定。
        /// </summary>
        protected virtual void ScriptGenerator() { }

        /// <summary>
        /// 绑定UI成员元素。
        /// </summary>
        protected virtual void BindMemberProperty() { }

        /// <summary>
        /// 注册事件。
        /// </summary>
        /// <remarks>常用全局事件，eg: <see cref="Moirai.Atropos.Events.EventManager"/></remarks>
        protected virtual void RegisterEvent() { }
        
        /// <summary>
        /// 取消注册事件。
        /// </summary>
        /// <remarks>常用全局事件，eg: <see cref="Moirai.Atropos.Events.EventManager"/></remarks>
        protected virtual void UnregisterEvent() { }

        /// <summary>
        /// 窗口创建。
        /// </summary>
        protected virtual void OnCreate() { }

        /// <summary>
        /// 窗口刷新。
        /// </summary>
        /// <remarks>不限于打开窗口，关闭上层窗口时也会触发刷新。</remarks>
        protected virtual void OnRefresh() { }

        /// <summary>是否需要 Update。</summary>
        protected bool _hasOverrideUpdate = true;

        /// <summary>
        /// 窗口更新。
        /// </summary>
        /// <remarks>每帧更新，相当于 Update</remarks>
        protected virtual void OnUpdate()
        {
            _hasOverrideUpdate = false;
        }
        
        /// <summary>
        /// 窗口关闭。
        /// </summary>
        protected virtual void OnClose() { }
        
        internal void CallDestroy()
        {
            OnDestroy();
        }

        /// <summary>
        /// 窗口销毁。
        /// </summary>
        protected virtual void OnDestroy() { }

        /// <summary>
        /// 当触发窗口的层级排序。
        /// </summary>
        protected void _OnSortDepth()
        {
            if (ChildList != null)
            {
                for (int i = 0; i < ChildList.Count; i++)
                {
                    ChildList[i].OnSortDepth();
                }
            }

            OnSortDepth();
        }

        /// <summary>
        /// 当触发窗口的层级排序。
        /// </summary>
        protected virtual void OnSortDepth() { }

        /// <summary>
        /// 当因为全屏遮挡触或者窗口可见性触发窗口的显隐。
        /// </summary>
        protected virtual void OnSetVisible(bool visible) { }

        internal void SetUpdateDirty()
        {
            _updateListValid = false;
            if (Parent != null)
            {
                Parent.SetUpdateDirty();
            }
        }

        #region 查找子物体组件 [FIND CHILD COMPONENT]

        public Transform FindChild(string path)
        {
            return FindChildImp(rectTransform, path);
        }

        public Transform FindChild(Transform trans, string path)
        {
            return FindChildImp(trans, path);
        }

        public T FindChildComponent<T>(string path) where T : Component
        {
            return FindChildComponentImp<T>(rectTransform, path);
        }

        public T FindChildComponent<T>(Transform trans, string path) where T : Component
        {
            return FindChildComponentImp<T>(trans, path);
        }

        /// <summary>
        /// 查找子节点。
        /// </summary>
        /// <param name="transform">位置组件。</param>
        /// <param name="path">子节点路径。</param>
        /// <returns>位置组件。</returns>
        private static Transform FindChildImp(Transform transform, string path)
        {
            var findTrans = transform.Find(path);
            return findTrans != null ? findTrans : null;
        }

        private static T FindChildComponentImp<T>(Transform transform, string path) where T : Component
        {
            var findTrans = transform.Find(path);
            if (findTrans != null)
            {
                return findTrans.gameObject.GetComponent<T>();
            }

            return null;
        }

        #endregion
    }
}