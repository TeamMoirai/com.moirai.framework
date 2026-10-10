namespace Moirai.GameMain.UI
{
    /// <summary>
    /// UI更新界面。
    /// </summary>
    public partial class LoadUpdateUI : LauncherUIBase
    {
        public override void Show(object param)
        {
            base.Show(param);
            
            if (_binder == null) return;
            
            _binder.TextUpdateDesc.text = param?.ToString();
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            RefreshProgress(0f);
        }

        internal void RefreshProgress(float progress)
        {
            if (_binder == null) return;
            
            _binder.SliderProgress.gameObject.SetActive(true);
            _binder.SliderProgress.value = progress;
        }

        internal void RefreshVersion(string version)
        {
            if (_binder == null) return;
            
            _binder.TextVersion.text = version;
        }

        internal void RefreshAppid(string appid)
        {
            if (_binder == null) return;
            
            _binder.TextAppid.text = appid;
        }

        /// <summary>
        /// 进度条拖拽不需要响应：进度值只由热更逻辑经 RefreshProgress 写入。
        /// 注册在 <c>LoadUpdateUI.g.cs</c> 的 ScriptGenerator 中，无实现的分部方法不能作为委托传入，故保留空实现。
        /// </summary>
        private partial void OnChangeSliderProgress(float value)
        {
        }
    }
}