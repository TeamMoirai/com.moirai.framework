using System;
using Cysharp.Threading.Tasks;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架处理器基类。所有策略模式处理器（LogHandler、JsonHandler等）继承此类。
    /// <para>生命周期：<see cref="Internal_Init"/> → <see cref="OnInit"/> → <see cref="Internal_InitAsync"/> → <see cref="OnInitAsync"/>
    /// → 运行期 → <see cref="Internal_ShutdownAsync"/> → <see cref="OnShutdownAsync"/> → <see cref="Internal_Shutdown"/> → <see cref="OnShutdown"/>。</para>
    /// <para>由 <c>HandlerHostGenerator</c> 生成的 <c>Handler</c> 属性 setter 自动调用 <see cref="Internal_Init"/>。
    /// 异步初始化由 <see cref="GameAppSettings.Initiation"/> 在 <see cref="Internal_Init"/> 之后显式 await。</para>
    /// </summary>
    [Serializable]
    public abstract class FrameworkHandler
    {
        // 标记 [NonSerialized] 以保证域重载后重置其值，避免序列化快照的状态污染
        [NonSerialized] private bool _initialized;

        /// <summary>
        /// 处理器是否已初始化。
        /// </summary>
        public bool IsInitialized => _initialized;

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
        /// <para>在此方法中解析对其他 Handler 的依赖（如 <c>ResourceUtility.Handler</c>）。
        /// 调用顺序由 <see cref="GameAppSettings.Initiation"/> 中的赋值顺序保证。</para>
        /// </summary>
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

        #region 异步生命周期 [ASYNC LIFECYCLE]

        /// <summary>
        /// 异步初始化处理器。由 <see cref="GameAppSettings.Initiation"/> 在 <see cref="Internal_Init"/> 之后显式 await。
        /// <para>用于资源异步加载、网络连接初始化、配置表加载等场景。</para>
        /// </summary>
        internal async UniTask Internal_InitAsync()
        {
            if (!_initialized) return;

            await OnInitAsync();
        }

        /// <summary>
        /// 异步关闭处理器。
        /// </summary>
        internal async UniTask Internal_ShutdownAsync()
        {
            if (!_initialized) return;

            await OnShutdownAsync();
        }

        /// <summary>
        /// 异步初始化回调。在同步 <see cref="OnInit"/> 之后执行。
        /// <para>默认实现立即完成。需要异步初始化的处理器覆写此方法。</para>
        /// </summary>
        protected virtual UniTask OnInitAsync()
            => UniTask.CompletedTask;

        /// <summary>
        /// 异步关闭回调。在同步 <see cref="OnShutdown"/> 之前执行。
        /// <para>默认实现立即完成。需要异步关闭的处理器覆写此方法。</para>
        /// </summary>
        protected virtual UniTask OnShutdownAsync()
            => UniTask.CompletedTask;

        #endregion
    }
}