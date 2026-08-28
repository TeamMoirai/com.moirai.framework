using UnityEngine;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器服务调试组件。
    /// <para>按需挂载到场景中的任意对象，即可在 Inspector 中查看计时器运行时统计、活跃计时器采样与“僵尸”一次性计时器（随用随加，运行时零逻辑开销）。</para>
    /// </summary>
    [AddComponentMenu("Moirai/Timer Service Debugger")]
    public sealed class TimerServiceDebugger : MonoBehaviour
    {
    }
}
