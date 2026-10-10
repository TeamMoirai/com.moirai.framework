using System;

namespace Moirai.GameMain.UI
{
    /// <summary>
    /// UI更新加载提示。
    /// </summary>
    public partial class LoadTipsUI : LauncherUIBase
    {
        /// <summary>确认按钮被按下时触发。</summary>
        public Action OnConfirmClick { get; set; }

        /// <summary>更新按钮被按下时触发。</summary>
        public Action OnUpdateClick { get; set; }

        /// <summary>取消按钮被按下时触发。</summary>
        public Action OnCancelClick { get; set; }

        public override void Show(object param)
        {
            base.Show(param);
            
            if (_binder == null) return;
            
            _binder.TextDesc.text = param?.ToString();
            _binder.BtnUpdate.gameObject.SetActive(false);
            _binder.BtnCancel.gameObject.SetActive(false);
            _binder.BtnConfirm.gameObject.SetActive(false);
        }

        public void SetAllCallback(Action onConfirm, Action onUpdate, Action onCancel)
        {
            if (_binder == null) return;

            _binder.BtnUpdate.gameObject.SetActive(false);
            _binder.BtnCancel.gameObject.SetActive(false);
            _binder.BtnConfirm.gameObject.SetActive(false);
            if (onConfirm != null)
            {
                OnConfirmClick = onConfirm;
                _binder.BtnConfirm.gameObject.SetActive(true);
            }

            if (onUpdate != null)
            {
                OnUpdateClick = onUpdate;
                _binder.BtnUpdate.gameObject.SetActive(true);
            }

            if (onCancel != null)
            {
                OnCancelClick = onCancel;
                _binder.BtnCancel.gameObject.SetActive(true);
            }
        }

        #region 事件 [EVENTS]

        /// <summary>
        /// 事件注册由 <c>LoadTipsUI.g.cs</c> 的 <c>ScriptGenerator</c> 完成，本分部只实现回调语义：
        /// 先调用外部传入的回调，再关闭本窗。
        /// </summary>
        private partial void OnClickBtnConfirm()
        {
            OnConfirmClick?.Invoke();
            Close();
        }

        private partial void OnClickBtnUpdate()
        {
            OnUpdateClick?.Invoke();
            Close();
        }

        private partial void OnClickBtnCancel()
        {
            OnCancelClick?.Invoke();
            Close();
        }

        #endregion 事件 [EVENTS]
    }
}