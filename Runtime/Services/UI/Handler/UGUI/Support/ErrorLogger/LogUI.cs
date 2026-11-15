using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UI;

namespace Moirai.Atropos.UI
{
    [Window(EUILayer.System, fromResources:true)]
    class LogUI : UGUIWindow<string>
    {
        private Stack<string> _errorTextString = new Stack<string>();
        
        #region 脚本工具生成的代码
        private Text _textError;
        private Button _btnClose;
        protected override void ScriptGenerator()
        {
            var binder = gameObject.GetComponent<LogUIBinder>();
            if (binder == null)
            {
                LogUtility.Error($"根物体: {gameObject.name} 缺少组件 LogUIBinder, 请检查！！！");
                return;
            }

            _textError = binder.TextLogError;
            _btnClose = binder.BtnLogClose;
            _btnClose.onClick.AddListener(OnClickCloseBtn);
        }
        #endregion

        #region 事件 [EVENTS]
        private void OnClickCloseBtn()
        {
            PopErrorLog().Forget();
        }
        #endregion
        
         protected override void OnRefresh()
         {
             _errorTextString.Push(Payload);
             _textError.text = Payload;
         }

         private async UniTaskVoid PopErrorLog()
         {
             if (_errorTextString.Count <= 0)
             {
                 await UniTask.Yield();
                 Close();
                 return;
             }

             string error = _errorTextString.Pop();
             _textError.text = error;
         }
    }
}
