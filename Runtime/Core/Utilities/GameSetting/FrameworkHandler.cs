using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架处理器基类：策略模式处理器（<see cref="LogHandler"/>、JsonHandler 等）的公共父类。
    /// </summary>
    /// <remarks>
    /// 生命周期为 <see cref="Internal_Init"/> → <see cref="OnInit"/> → 运行期 → <see cref="Internal_Shutdown"/> → <br />
    /// <see cref="OnShutdown"/>，全部同步。
    /// <c>HandlerHostGenerator</c> 生成的 <c>Handler</c> 属性 setter 自动驱动：设置时初始化新实例，替换时先关闭旧实例。
    /// 需要异步初始化的对象走 Kernel 的 <c>IService.OnInitAsync</c>，处理器挂异步钩子不会被调用。
    /// </remarks>
    [Serializable]
    public abstract class FrameworkHandler
    {
        // 标记 [NonSerialized] 以保证域重载后重置其值，避免序列化快照的状态污染
        [NonSerialized] private bool _initialized;
        /// <summary>
        /// 处理器是否已初始化。
        /// </summary>
        public virtual bool IsInitialized => _initialized;

        #region 同步生命周期 [SYNC LIFECYCLE]

        /// <summary>
        /// 初始化处理器。由 <c>HandlerHostGenerator</c> 生成的 <c>Handler</c> 属性 setter 调用。
        /// </summary>
        internal void Internal_Init()
        {
            if (_initialized) return;

            OnInit();
            _initialized = true;
        }

        /// <summary>
        /// 关闭处理器。
        /// </summary>
        internal void Internal_Shutdown()
        {
            if (!_initialized) return;

            _initialized = false;
            OnShutdown();
        }

        /// <summary>
        /// 同步初始化回调，用于接管后端资源。
        /// </summary>
        /// <remarks>可在此解析对其他 Handler 的依赖（如 <c>ResourceUtility.Handler</c>）；调用顺序由 <see cref="GameAppSettings.Initiation"/> 中的赋值顺序保证。 <br />
        /// </remarks>
        protected virtual void OnInit()
        {
        }

        /// <summary>
        /// 同步关闭回调，用于释放后端资源。仅在处理器被替换或应用退出时调用。
        /// </summary>
        protected virtual void OnShutdown()
        {
        }

        #endregion
    }
}