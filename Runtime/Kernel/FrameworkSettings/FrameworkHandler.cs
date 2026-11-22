using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架处理器基类：策略模式处理器（<see cref="LogHandler"/>、JsonHandler 等）的公共父类。
    /// </summary>
    /// <remarks>
    /// 状态、就绪判据与钩子来自 <see cref="HandlerBase"/>，本类只留直驱入口，全程同步。 <br />
    /// <c>OnInit</c> 内可解析对其他 Handler 的依赖，顺序由 <see cref="GameAppSettings.Initiation"/> 的赋值序保证。 <br />
    /// 需要异步初始化的对象走服务的 <c>IService.OnInitAsync</c>，处理器挂异步钩子不会被调用。
    /// </remarks>
    [Serializable]
    public abstract class FrameworkHandler : HandlerBase
    {
        /// <summary>处理器是否已就绪。</summary>
        public virtual bool IsInitialized => IsReady;

        #region 同步生命周期 [SYNC LIFECYCLE]

        /// <summary>
        /// 初始化处理器（幂等）。生成的 <c>Handler</c> 属性 setter 在共享冷路径锁内调用，新实例发布成功后才关旧。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Internal_Init() => DriveInitialize();

        /// <summary>
        /// 关闭处理器（幂等）。
        /// </summary>
        /// <remarks>
        /// 停在 <see cref="EServiceState.Created"/>（从未装配成功）的处理器没有可释放的后端，直接跳过。 <br />
        /// 这是刻意口径，不是遗漏：容器侧 <see cref="ServiceBase"/> 不经本入口，其关停前置保持原样。
        /// </remarks>
        internal void Internal_Shutdown()
        {
            if (State == EServiceState.Created) return;

            DriveShutdown();
        }

        #endregion
    }
}
