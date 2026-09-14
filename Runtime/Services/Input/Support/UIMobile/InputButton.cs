using UnityEngine;
using UnityEngine.EventSystems;

namespace Moirai.Atropos.Input
{
    /// <summary>
    /// 会读取 2D 用户界面（UI）按钮的操作，然后将状态标志发送给移动端输入组件。
    /// <para>边沿在指针事件里按帧闩锁（与查询次数无关），保证同帧点按不丢边、
    /// 且与 Input System 的 <c>WasPressedThisFrame</c> 语义一致。</para>
    /// </summary>
    [AddComponentMenu("Tools/Input/UI/Input Button")]
    public class InputButton : MonoBehaviour, IPointerUpHandler, IPointerDownHandler, IUIBoolAction
    {
        [SerializeField] private string m_ActionName = "";

        private bool _boolValue;
        private int _pressedFrame = int.MinValue;
        private int _releasedFrame = int.MinValue;

        #region 布尔动作接口 [IBoolAction]

        public string ActionName => m_ActionName;

        public bool BoolValue
        {
            get => _boolValue;
            set => _boolValue = value;
        }

        #endregion

        /// <summary>本帧是否被按下（指针事件闩锁，同帧多次查询幂等）。</summary>
        public bool WasPressedThisFrame => WasPressedAt(Time.frameCount);

        /// <summary>本帧是否被抬起（指针事件闩锁，同帧多次查询幂等）。</summary>
        public bool WasReleasedThisFrame => WasReleasedAt(Time.frameCount);

        public void OnPointerDown(PointerEventData eventData) => Press(Time.frameCount);
        public void OnPointerUp(PointerEventData eventData) => Release(Time.frameCount);

        /// <summary>按下列锁按下边沿（测试可注入帧号）。</summary>
        internal void Press(int frame)
        {
            _boolValue = true;
            _pressedFrame = frame;
        }

        /// <summary>按下列锁抬起边沿（测试可注入帧号）。</summary>
        internal void Release(int frame)
        {
            _boolValue = false;
            _releasedFrame = frame;
        }

        /// <summary>清空按住状态与本帧边沿（输入压制/重置时调用，避免残留边沿泄漏）。</summary>
        internal void ResetState()
        {
            _boolValue = false;
            _pressedFrame = int.MinValue;
            _releasedFrame = int.MinValue;
        }

        internal bool WasPressedAt(int frame) => _pressedFrame == frame;

        internal bool WasReleasedAt(int frame) => _releasedFrame == frame;

        // 自注册到 UIMobileInputRegistry：延迟实例化（对象池/动态生成）的虚拟按键也可被查询，销毁后自动注销
        private void OnEnable() => UIMobileInputRegistry.Register(this);
        private void OnDisable() => UIMobileInputRegistry.Unregister(this);
    }
}
