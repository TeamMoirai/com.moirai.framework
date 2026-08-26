using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 旧式计时器回调（带 object[] 参数，避免闭包）。
    /// </summary>
    public delegate void TimerCallback(object[] args);

    /// <summary>
    /// 计时器调试信息。
    /// </summary>
    public struct TimerDebugInfo
    {
        public ulong timerHandle;
        public float leftTime;
        public float duration;
        public float age;
        public byte flags;
    }

    /// <summary>
    /// 计时器调试标志位。
    /// </summary>
    public static class TimerDebugFlags
    {
        public const byte RUNNING = 1 << 0;
        public const byte LOOP = 1 << 1;
        public const byte UNSCALED = 1 << 2;
    }

    internal delegate void TimerGenericInvoker(object handler, object arg);

    internal static class TimerGenericInvokerCache<T> where T : class
    {
        public static readonly TimerGenericInvoker s_Invoke = InvokeGeneric;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InvokeGeneric(object handler, object arg)
        {
            ((Action<T>)handler).Invoke((T)arg);
        }
    }
}
