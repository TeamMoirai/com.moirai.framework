using UnityEngine;

namespace Moirai.Atropos.Debugger
{
    /// <summary>
    /// 轮询刷新的调试器窗口基类：按固定间隔重建内容，仅窗口可见期间驱动。
    /// </summary>
    /// <remarks>
    /// 用于运行时状态信息窗口（Screen / Scene / Time / Profiler 等），免除逐行 Getter 闭包与每帧分配。 <br />
    /// 进程级常量的静态信息窗口（如 Path）传 0 禁用轮询，进入窗口时构建一次；含运行期可变字段的窗口请保持轮询。
    /// </remarks>
    public abstract class PollingDebuggerWindowBase : ScrollableDebuggerWindowBase
    {
        #region 字段 [FIELDS]

        private readonly bool _pollEnabled;
        private readonly float _refreshInterval;
        private float _countdown;

        #endregion

        #region 构造 [CONSTRUCTOR]

        /// <summary>
        /// 初始化轮询窗口基类的新实例。
        /// </summary>
        /// <param name="refreshInterval">刷新间隔（秒，最小 0.05；传 0 或负值禁用轮询——适合内容为构建期常量的静态信息窗口）。</param>
        protected PollingDebuggerWindowBase(float refreshInterval = 0.25f)
        {
            _pollEnabled = refreshInterval > 0f;
            _refreshInterval = Mathf.Max(0.05f, refreshInterval);
        }

        #endregion

        #region 生命周期 [LIFECYCLE]

        /// <inheritdoc />
        public override void OnEnter()
        {
            _countdown = 0f;
            Rebuild();
        }

        /// <inheritdoc />
        public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            if (!_pollEnabled)
            {
                return;
            }

            _countdown -= realElapseSeconds;
            if (_countdown > 0f)
            {
                return;
            }

            _countdown = _refreshInterval;
            Rebuild();
        }

        #endregion
    }
}
