using System;
using System.Collections;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 协程控制：协程宿主为 GameAppHost（MonoBehaviour 派发），本文件仅静态门面转发。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="GameApp"/>——公共 API 零变化（验收：单一职责 < 400 行）。</remarks>
    public partial class GameApp
    {

        /// <summary>
        /// 启动全局协程。
        /// </summary>
        /// <param name="routine">协程枚举器；<c>null</c> 时静默返回 <c>null</c>。</param>
        /// <returns>协程句柄；宿主不可用（框架已关闭，或处于退出窗口）时为 <c>null</c> 并告警。</returns>
        public static Coroutine StartCoroutine(IEnumerator routine)
        {
            if (routine == null) return null;

            GameAppHost host = GameAppHost.Instance;
            if (host == null)
            {
                // 与「参数为空」区分开：宿主取不到意味着协程根本没跑，而调用方拿到的只是无声的 null
                LogUtility.Warning(
                    "GameApp.StartCoroutine: {0} 未启动——GameAppHost 不可用（框架已 Shutdown 或处于退出窗口）。",
                    routine.GetType().FullName);
                return null;
            }

            return host.StartCoroutine(routine);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(IEnumerator routine)
        {
            if (routine == null) return;

            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopCoroutine(routine);
        }

        /// <summary>
        /// 停止全局协程。
        /// </summary>
        public static void StopCoroutine(Coroutine routine)
        {
            if (routine == null) return;

            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopCoroutine(routine);
        }

        /// <summary>
        /// 停止所有全局协程。
        /// </summary>
        public static void StopAllCoroutines()
        {
            GameAppHost host = GameAppHost.TryGetInstance();
            host?.StopAllCoroutines();
        }
    }
}
