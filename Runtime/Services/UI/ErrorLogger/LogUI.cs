using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.UI;

namespace Moirai.Atropos.UI
{
    [Window(UILayer.System, fromResources:true)]
    class LogUI : UIWindow
    {
        private Stack<string> _errorTextString = new Stack<string>();
        
        #region 脚本工具生成的代码
        private Text _textError;
        private Button _btnClose;
        protected override void ScriptGenerator()
        {
            _textError = FindChildComponent<Text>("m_textError");
            _btnClose = FindChildComponent<Button>("m_btnClose");
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
             _errorTextString.Push(UserData?.ToString());
             _textError.text = UserData?.ToString();
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
