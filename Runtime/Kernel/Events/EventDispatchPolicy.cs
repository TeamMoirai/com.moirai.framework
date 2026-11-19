using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 事件派发链的异常分级策略：开发期记录后上抛，发布期隔离续跑。
    /// </summary>
    /// <remarks>
    /// <c>const</c> 门控，发布构建裁掉死分支；深度计数与引用计数归还在 <c>finally</c> 中无条件执行，与本开关无关。
    /// </remarks>
    public static class EventDispatchPolicy
    {
        /// <summary>开发期（编辑器/开发构建）派发异常上抛（调试器断在抛点）；发布构建隔离续跑。</summary>
        public const bool RETHROW_DISPATCH_EXCEPTIONS =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif
    }

    /// <summary>
    /// 发布派发模式：立即（当前调用栈同步派发）或排队（主线程队列，下一次泵送派发）。
    /// </summary>
    public enum DispatchMode
    {
        /// <summary>立即派发：当前调用栈同步完成全部回调（默认）。</summary>
        Immediate = 0,
        /// <summary>排队派发：事件进入主线程队列，下一次主线程泵送时派发。</summary>
        Queued = 1,
    }
}
