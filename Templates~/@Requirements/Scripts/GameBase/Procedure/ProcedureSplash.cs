using Moirai.Atropos.Events;
using Moirai.Atropos.Fsm;
using Moirai.Atropos.Procedure;

namespace Moirai.Main
{
    /// <summary>
    /// 流程 => 闪屏
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureSplash : ProcedurePremainBase
    {
        private IFsm<IProcedureModule> _procedureOwner;
        
        public override bool UseNativeDialog => true;

        protected override void OnEnter(IFsm<IProcedureModule> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            if (SplashScreenManager.HasInstance && SplashScreenManager.Instance.ShowSplashScreen)
            {
                _procedureOwner = procedureOwner;
                // 播放 Splash 动画
                SplashScreenEvent.SplashStart();
                EventManager.RegisterCallback<SplashScreenEvent>(OnSplashScreenEvent);
            }
            else
            {
                ChangeState<ProcedureInitPackage>(procedureOwner);
            }
        }
        
        /// <summary>
        /// 处理闪屏结束事件
        /// </summary>
        /// <param name="evt"></param>
        private void OnSplashScreenEvent(SplashScreenEvent evt)
        {
            if (evt.Stage == SplashScreenEvent.SplashStage.End)
            {
                // 闪屏结束切换至初始化资源包
                ChangeState<ProcedureInitPackage>(_procedureOwner);
                EventManager.UnregisterCallback<SplashScreenEvent>(OnSplashScreenEvent);
            }
        }
    }

    /// <summary>
    /// 闪屏事件
    /// </summary>
    public class SplashScreenEvent : EventBase<SplashScreenEvent>, IProcedureEvent
    {
        public enum SplashStage
        {
            /// <summary>
            /// 闪屏开始
            /// </summary>
            Start,

            /// <summary>
            /// 闪屏结束
            /// </summary>
            End,
        }

        /// <summary>闪屏阶段</summary>
        public SplashStage Stage { get; private set; }

        private static SplashScreenEvent GetPooled(SplashStage stage)
        {
            var evt = GetPooled();
            evt.Stage = stage;
            return evt;
        }

        public static void SplashStart()
        {
            using var evt = GetPooled(SplashStage.Start);
            EventManager.SendEvent(evt);
        }

        public static void SplashEnd()
        {
            using var evt = GetPooled(SplashStage.End);
            EventManager.SendEvent(evt);
        }
    }
}