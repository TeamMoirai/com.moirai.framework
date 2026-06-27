using Moirai.Atropos;
using Moirai.Atropos.Events;
using UnityEngine;
using UnityEngine.Events;

namespace Moirai.Main
{
    /// <summary>
    /// 闪屏管理器
    /// </summary>
    public class SplashScreenManager : SingletonMono<SplashScreenManager>
    {
        [Tooltip("是否播放闪屏？如果有，需要在播放结束手动调用 SplashEnd()")]
        [SerializeField] private bool m_ShowSplashScreen = false;

        [Tooltip("闪屏开始时触发的事件。用来触发播放闪屏动画序列。")]
        [SerializeField] private UnityEvent m_OnSlashStart;
        
        private bool _isSplashing;

        /// <summary>
        /// 是否播放闪屏？如果有，需要在播放结束手动调用 <see cref="SplashEnd"/>
        /// </summary>
        public bool ShowSplashScreen => m_ShowSplashScreen;

        protected override void OnInit()
        {
            EventManager.RegisterCallback<SplashScreenEvent>(OnSplashScreenEvent);
        }
        
        protected override void Shutdown()
        {
            EventManager.UnregisterCallback<SplashScreenEvent>(OnSplashScreenEvent);
        }
        
        /// <summary>
        /// 处理闪屏开始事件
        /// </summary>
        /// <param name="evt"></param>
        private void OnSplashScreenEvent(SplashScreenEvent evt)
        {
            if (_isSplashing) return;
            
            if (evt.Stage == SplashScreenEvent.SplashStage.Start)
            {
                m_OnSlashStart?.Invoke();
                _isSplashing = true;
            }
        }
        
        /// <summary>
        /// 手动触发闪屏结束
        /// </summary>
        public void SplashEnd()
        {
            if (!_isSplashing) return;
            
            SplashScreenEvent.SplashEnd();
            _isSplashing = false;
        }
    }
}