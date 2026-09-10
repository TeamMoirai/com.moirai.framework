using System.Collections.Generic;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// <see cref="AudioPlayColdParams"/> 轻量栈池，避免每次 Play 分配冷参数实例。
    /// </summary>
    internal static class AudioPlayColdParamsPool
    {
        private static readonly Stack<AudioPlayColdParams> s_Stack = new Stack<AudioPlayColdParams>(8);

        public static AudioPlayColdParams Acquire()
        {
            if (s_Stack.Count > 0)
            {
                var p = s_Stack.Pop();
                if (p != null) return p;
            }

            return new AudioPlayColdParams();
        }

        public static void Release(AudioPlayColdParams p)
        {
            if (p == null) return;
            p.ResetToDefault();
            s_Stack.Push(p);
        }

        public static void Clear() => s_Stack.Clear();
    }
}
