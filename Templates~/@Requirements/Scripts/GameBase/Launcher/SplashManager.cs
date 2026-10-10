using Moirai.Atropos;
using UnityEngine;
using UnityEngine.Events;

namespace Moirai.GameMain
{
    /// <summary>
    /// 闪屏管理器。
    /// </summary>
    public class SplashManager : SingletonMono<SplashManager>
    {
        [Tooltip("是否播放闪屏？如果有，需要在播放结束手动调用 SplashEnd()")]
        [SerializeField] private bool m_ShowSplashScreen = false;

        [Tooltip("闪屏开始时触发的事件。用来触发播放闪屏动画序列。")]
        [SerializeField] private UnityEvent m_OnSlashStart;
        [Tooltip("闪屏结束时触发的事件。用来通知闪屏结束。")]
        [SerializeField] private UnityEvent m_OnSlashEnd;
        
        private bool _isSplashing;

        /// <summary>是否播放闪屏？如果有，需要在播放结束手动调用 <see cref="SplashEnd"/>。</summary>
        public bool ShowSplashScreen => m_ShowSplashScreen;

        /// <summary>
        /// 处理闪屏开始事件。
        /// </summary>
        internal void SplashStart()
        {
            if (_isSplashing) return;
            
            m_OnSlashStart?.Invoke();
            _isSplashing = true;
        }
        
        /// <summary>
        /// 手动触发闪屏结束。
        /// </summary>
        public void SplashEnd()
        {
            if (!_isSplashing) return;
            
            m_OnSlashEnd?.Invoke();
            _isSplashing = false;
        }
    }
}