using System;
using Moirai.Atropos;

namespace Testing
{
    /// <summary>
    /// 时间服务用虚拟时钟：以委托提供 <see cref="GameTimeHandler"/> 的双精度读数，测试自管时间推进（帧号游标 + Advance）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="GameTime"/> 的 Handler 交换注入即可让被测时间源脱离引擎帧循环；<c>Handler</c> 是进程级全局旋钮， <br />
    /// 用例 <c>TearDown</c> 必须在 finally 中还原原 Handler。
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
