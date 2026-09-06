using System.Collections;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 协程相关函数。
    /// </summary>
    public static class CoroutineUtility
    {
        /// <summary>
        /// 等待指定的帧数
        /// </summary>
        /// <param name="frameCount">帧数</param>
        /// <returns></returns>
        /// <example>yield return CoroutineUtility.WaitFor(1);</example>
        public static IEnumerator WaitForFrames(int frameCount)
        {
            while (frameCount > 0)
            {
                frameCount--;
                yield return null;
            }
        }

        /// <summary>
        /// 等待指定的秒数（使用常规时间） 
        /// </summary>
        /// <param name="seconds">秒数</param>
        /// <returns></returns>
        /// <example>yield return CoroutineUtility.WaitFor(1f);</example>
        public static IEnumerator WaitFor(float seconds)
        {
            for (float timer = 0f; timer < seconds; timer += Time.deltaTime)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 等待指定的秒数（使用未缩放的时间）
        /// </summary>
        /// <param name="seconds">秒数</param>
        /// <returns></returns>
        /// <example>yield return CoroutineUtility.WaitForUnscaled(1f);</example>
        public static IEnumerator WaitForUnscaled(float seconds)
        {
            for (float timer = 0f; timer < seconds; timer += Time.unscaledDeltaTime)
            {
                yield return null;
            }
        }
    }
}

