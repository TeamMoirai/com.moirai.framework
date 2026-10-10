namespace Moirai.GameMain
{
    /// <summary>
    /// 流程 => 闪屏。
    /// </summary>
    // ReSharper disable once ClassNeverInstantiated.Global
    public class ProcedureSplash : ProcedurePremainBase
    {
        public override bool UseNativeDialog => true;

        protected override void OnEnter()
        {
            base.OnEnter();

            if (SplashManager.IsValid && SplashManager.Instance.ShowSplashScreen)
            {
                // 播放 Splash 动画
                SplashManager.Instance.SplashStart();
            }
        }
        
        protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(elapseSeconds, realElapseSeconds);

            // 运行一帧后初始化资源包，意在将闪屏过程遮盖掉初始化过程
            ChangeState<ProcedureInitPackage>();
        }
    }
}