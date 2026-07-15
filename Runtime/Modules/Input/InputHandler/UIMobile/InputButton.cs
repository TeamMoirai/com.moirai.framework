using UnityEngine;
using UnityEngine.EventSystems;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 会读取 2D 用户界面（UI）按钮的操作，然后将状态标志发送给移动端输入组件。
    /// </summary>
    [AddComponentMenu("Tools/Input/UI/Input Button")]
    public class InputButton : MonoBehaviour, IPointerUpHandler, IPointerDownHandler, IUIBoolAction
    {
        [SerializeField] private string m_ActionName = "";

        private bool _boolValue;

        #region IBoolAction

        public string ActionName => m_ActionName;

        public bool BoolValue
        {
            get => _boolValue;
            set => _boolValue = value;
        }

        #endregion

        public void OnPointerDown(PointerEventData eventData) => _boolValue = true;
        public void OnPointerUp(PointerEventData eventData) => _boolValue = false;
    }
}