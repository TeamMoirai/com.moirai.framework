using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos.Timer
{
    /// <summary>
    /// 计时器引擎泳道（lane）标识。每个 <see cref="ITimerEngine"/> 占据一条独立泳道，
    /// 泳道号内嵌于不透明句柄，供复合外观 <see cref="DefaultTimerHandler"/> 按位路由，
    /// 从而让两套引擎各自持有独立的槽位池与句柄命名空间、互不糅合。
    /// </summary>
    internal static class TimerLaneKinds
    {
        public const byte Wheel = 0;
        public const byte Frame = 1;
        public const byte Count = 2;
    }

    /// <summary>
    /// 计时器句柄位布局：<c>[ 版本(32b) | 泳道(3b) | 槽位+1(21b) ]</c>。
    /// <para>低 32 位容纳泳道与槽位（槽位上限 2^21-2，远大于页式池最大 4096×256=1,048,576）；
    /// 高 32 位为版本号，槽位复用即自增，旧句柄因版本/泳道/槽位三重不符而自动失效（防 ABA）。</para>
    /// </summary>
    internal static class TimerHandleLayout
    {
        public const int SLOT_BITS = 21;
        public const int LANE_BITS = 3;
        public const int LANE_SHIFT = SLOT_BITS;
        public const ulong SLOT_MASK = (1UL << SLOT_BITS) - 1UL;
        public const byte LANE_MASK = (1 << LANE_BITS) - 1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong Compose(uint version, byte lane, int slotIndex)
        {
            return ((ulong)version << 32) | ((ulong)lane << LANE_SHIFT) | (ulong)((uint)(slotIndex + 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SlotOf(ulong handle)
        {
            return (int)(handle & SLOT_MASK) - 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte LaneOf(ulong handle)
        {
            return (byte)((handle >> LANE_SHIFT) & LANE_MASK);
        }
    }

    /// <summary>
    /// 完成回调类型（正交于 PROGRESS 状态位）。引擎内以 <c>using static</c> 直引 <c>HANDLER_*</c>。
    /// </summary>
    internal static class TimerHandlerTypes
    {
        public const byte HANDLER_NONE = 0;
        public const byte HANDLER_NO_ARGS = 1;
        public const byte HANDLER_GENERIC = 2;
        public const byte HANDLER_UNSAFE_STATIC = 3;
        public const byte HANDLER_UNSAFE_OBJECT = 4;
    }

    /// <summary>
    /// 槽位状态位（引擎内以 <c>using static</c> 直引 <c>STATE_*</c>）。
    /// 帧/时间的归属由引擎（泳道）本身决定，不再有 STATE_FRAME 判别位。
    /// </summary>
    internal static class TimerStates
    {
        public const byte STATE_ACTIVE = 1 << 0;
        public const byte STATE_RUNNING = 1 << 1;
        public const byte STATE_LOOP = 1 << 2;
        public const byte STATE_UNSCALED = 1 << 3;
        public const byte STATE_RELEASE_PENDING = 1 << 4;
        public const byte STATE_PROGRESS = 1 << 5;
    }

    internal delegate void TimerGenericInvoker(object handler, object arg);

    /// <summary>
    /// 泛型回调的类型缓存：按 <typeparamref name="T"/> 生成一次静态委托，避免注册期装箱/闭包分配。
    /// </summary>
    internal static class TimerGenericInvokerCache<T> where T : class
    {
        public static readonly TimerGenericInvoker Invoke = InvokeGeneric;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InvokeGeneric(object handler, object arg)
        {
            ((Action<T>)handler).Invoke((T)arg);
        }
    }

    /// <summary>
    /// 页式槽位存储的机械常量与整型分页工具（两引擎通用；具体的槽位列由各引擎自持）。
    /// <para>引擎以 <c>using static TimerPool;</c> 直引 <c>PAGE_*</c> / <c>INVALID_INDEX</c> 与分页读写静态方法。</para>
    /// </summary>
    internal static class TimerPool
    {
        public const int PAGE_SHIFT = 8;
        public const int PAGE_SIZE = 1 << PAGE_SHIFT;
        public const int PAGE_MASK = PAGE_SIZE - 1;
        public const int MAX_PAGE_COUNT = 4096;
        public const int INVALID_INDEX = -1;

        /// <summary>
        /// 整型分页（自由槽位栈 / 活跃槽位列表）：按页分配定长数组，扩容不重排既有页。
        /// </summary>
        internal sealed class IndexPage
        {
            public readonly int[] Values = new int[PAGE_SIZE];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetPagedInt(IndexPage[] pages, int index)
        {
            return pages[index >> PAGE_SHIFT].Values[index & PAGE_MASK];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetPagedInt(IndexPage[] pages, int index, int value)
        {
            pages[index >> PAGE_SHIFT].Values[index & PAGE_MASK] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnsureIndexPage(IndexPage[] pages, int pageIndex)
        {
            if (pages[pageIndex] == null)
            {
                pages[pageIndex] = new IndexPage();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int NormalizeCapacity(int capacity)
        {
            int normalizedCapacity = capacity > PAGE_SIZE ? capacity : PAGE_SIZE;
            int remainder = normalizedCapacity & PAGE_MASK;
            if (remainder != 0)
            {
                normalizedCapacity += PAGE_SIZE - remainder;
            }

            return normalizedCapacity;
        }
    }

    /// <summary>
    /// 计时器引擎契约：一条泳道（时间轮 / 帧计时）对复合外观暴露的统一操作面。
    /// <para>创建类操作不在接口上（时间/帧参数语义不同），由外观对具体引擎直接调用；
    /// 句柄路由 / 阶段推进 / 统计调试经此接口统一处理。引擎内部只认自己的句柄，
    /// 对外来泳道句柄一律解析失败并安全降级。</para>
    /// </summary>
    internal interface ITimerEngine
    {
        void Init(int capacity);

        void Shutdown();

        void Tick();

        void FixedTick();

        void LateTick();

        void Pause(ulong handle);

        void Resume(ulong handle);

        void Restart(ulong handle);

        void Cancel(ulong handle);

        void PauseAll();

        void ResumeAll();

        void CancelAll();

        bool IsRunning(ulong handle);

        bool IsDone(ulong handle);

        float GetLeftTime(ulong handle);

        float GetElapsed(ulong handle);

        float GetDuration(ulong handle);

        void GetStatistics(out int activeCount, out int poolCapacity, out int peakActiveCount, out int freeCount);

        /// <summary>
        /// 把本引擎活跃计时器填入 <paramref name="results"/> 的 <paramref name="offset"/> 起，至多 <paramref name="limit"/> 条，返回实际填充数。
        /// </summary>
        int GetAllTimers(TimerDebugInfo[] results, int offset, int limit);

#if UNITY_EDITOR
        int GetStaleOneShotTimers(TimerDebugInfo[] results, int offset, int limit);
#endif
    }
}
