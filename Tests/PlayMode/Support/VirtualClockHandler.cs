using System;
using Moirai.Atropos;

namespace Testing
{
    /// <summary>
    /// 时间服务用虚拟时钟（PlayMode 程序集本地副本）：以委托提供 <see cref="GameTimeHandler"/> 的双精度读数，测试自管时间推进。
    /// </summary>
    /// <remarks>
    /// 与 EditorMode 同名支撑同构——asmdef 拓扑使跨程序集共享不可行，双副本属可接受形态。
    /// 与 <see cref="GameTime"/> 的 Handler 交换注入即可让被测时间源脱离引擎帧循环（如隔开 PlayMode 测试域的每帧服务驱动），确定性推进由用例自定。
    /// <c>Handler</c> 是进程级全局旋钮——注入方必须在 <c>finally</c> 中还原原 Handler。
    /// </remarks>
    internal sealed class VirtualClockHandler : GameTimeHandler
    {
        private readonly Func<double> _scaled;
        private readonly Func<double> _unscaled;

        public VirtualClockHandler(Func<double> scaled, Func<double> unscaled)
        {
            _scaled = scaled;
            _unscaled = unscaled;
        }

        public override double ScaledNow => _scaled();

        public override double UnscaledNow => _unscaled();
    }
}
