using System;
using System.Collections;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 引擎参数外观：FrameRate/GameSpeed/Pause/RunInBackground/NeverSleep（Application/Time 包装）。
    /// </summary>
    /// <remarks>partial 拆分自 <see cref="GameApp"/>——公共 API 零变化（验收：单一职责 < 400 行）。</remarks>
    public partial class GameApp
    {

        // 引擎状态的门面而非框架存活状态：由 Initialize 从配置播种，运行期写入即刻作用于引擎；
        // IsShutdown 之后仍可读写不抛，下次 Initialize 重新播种为配置基线。
        private static int s_FrameRate;
        private static float s_GameSpeed = 1f;
        private static bool s_RunInBackground;
        private static bool s_NeverSleep;
        private static int s_PauseDepth;

        /// <summary>获取游戏是否已关闭。</summary>
        public static bool IsShutdown { get; internal set; } = true;

        /// <summary>获取或设置游戏帧率。</summary>
        public static int FrameRate
        {
            get => s_FrameRate;
            set => Application.targetFrameRate = s_FrameRate = value;
        }

        /// <summary>获取或设置期望的游戏速度（映射到 <c>Time.timeScale</c>）。</summary>
        /// <remarks>
        /// 暂停期间写入只更新解除暂停后的目标值，<c>Time.timeScale</c> 保持 0（暂停优先）。 <br />
        /// 判断时间是否真正冻结请读 <c>GameSpeed &lt;= 0</c> 或 <c>Time.timeScale</c>，不要读 <see cref="IsGamePaused"/>。
        /// </remarks>
        public static float GameSpeed
        {
            get => s_GameSpeed;
            set
            {
                s_GameSpeed = value >= 0f ? value : 0f;

                // 暂停中只记下期望值，等 ResumeGame 把计数降到 0 时再生效
                if (s_PauseDepth == 0) Time.timeScale = s_GameSpeed;
            }
        }

        /// <summary>获取游戏是否被暂停，即 <see cref="PauseGame"/> 的引用计数是否非零。</summary>
        /// <remarks>与速度解耦：把速度调到 0 不算暂停。</remarks>
        public static bool IsGamePaused => s_PauseDepth > 0;

        /// <summary>获取当前的暂停请求层数（<see cref="PauseGame"/> 加一、<see cref="ResumeGame"/> 减一）。</summary>
        /// <remarks>仅供调试定位未配对 <see cref="ResumeGame"/> 的层；判暂停请读 <see cref="IsGamePaused"/>。</remarks>
        internal static int PauseDepth => s_PauseDepth;

        /// <summary>获取是否正常游戏速度（期望值约等于 1，容差 0.01）。暂停不影响本判定。</summary>
        public static bool IsNormalGameSpeed => Mathf.Abs(s_GameSpeed - 1f) < 0.01f;

        /// <summary>获取或设置是否允许后台运行。</summary>
        public static bool RunInBackground
        {
            get => s_RunInBackground;
            set => Application.runInBackground = s_RunInBackground = value;
        }

        /// <summary>获取或设置是否禁止休眠。</summary>
        public static bool NeverSleep
        {
            get => s_NeverSleep;
            set
            {
                s_NeverSleep = value;
                Screen.sleepTimeout = value ? SleepTimeout.NeverSleep : SleepTimeout.SystemSetting;
            }
        }


        /// <summary>
        /// 暂停游戏：引用计数式，各来源须各自 <see cref="ResumeGame"/> 才真正恢复。
        /// </summary>
        /// <remarks>计数 0→1 时把 <c>Time.timeScale</c> 压到 0，已在暂停中只加计数；暂停期间的 <see cref="GameSpeed"/> 写入只更新恢复目标。</remarks>
        public static void PauseGame()
        {
            if (s_PauseDepth == 0) Time.timeScale = 0f;

            s_PauseDepth++;
        }

        /// <summary>
        /// 恢复游戏，<see cref="PauseGame"/> 的逆操作；计数归零才真正回速。
        /// </summary>
        /// <remarks>计数已为 0 时是空操作，不会把 <c>Time.timeScale</c> 拉回陈旧值。</remarks>
        public static void ResumeGame()
        {
            if (s_PauseDepth == 0) return;

            if (--s_PauseDepth == 0) Time.timeScale = s_GameSpeed;
        }

        /// <summary>
        /// 重置为正常游戏速度。暂停中调用只更新恢复目标，不会顺手解除暂停。
        /// </summary>
        public static void ResetGameSpeed()
        {
            if (IsNormalGameSpeed) return;

            GameSpeed = 1f;
        }
    }
}
