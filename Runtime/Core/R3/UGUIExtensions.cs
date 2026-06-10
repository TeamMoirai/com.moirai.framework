#if R3_INSTALLED
using R3;
using UnityEngine.UI;

namespace Moirai.Atropos.R3
{
    public static class UGUIExtensions
    {
        /// <summary>
        /// 将 <see cref="ReactiveProperty{Single}"/> 绑定到滑块 <see cref="Slider"/>
        /// </summary>
        /// <param name="slider"></param>
        /// <param name="property"></param>
        /// <param name="unRegister"></param>
        public static void BindProperty(this Slider slider, ReactiveProperty<float> property, IDisposableUnregister unRegister)
        {
            slider.onValueChanged.AsObservable().Subscribe(e => property.Value = e).AddTo(unRegister);
            property.Subscribe(slider.SetValueWithoutNotify).AddTo(unRegister);
            slider.SetValueWithoutNotify(property.Value);
        }

        /// <summary>
        /// 将 <see cref="ReactiveProperty{Int32}"/> 绑定到滑块 <see cref="Slider"/>
        /// </summary>
        /// <param name="slider"></param>
        /// <param name="property"></param>
        /// <param name="unRegister"></param>
        public static void BindProperty(this Slider slider, ReactiveProperty<int> property, IDisposableUnregister unRegister)
        {
            slider.onValueChanged.AsObservable().Subscribe(e => property.Value = (int)e).AddTo(unRegister);
            property.Subscribe(e => slider.SetValueWithoutNotify(e)).AddTo(unRegister);
            slider.SetValueWithoutNotify(property.Value);
        }

        /// <summary>
        /// 将 <see cref="ReactiveProperty{Boolean}"/> 绑定到开关 <see cref="Toggle"/>
        /// </summary>
        /// <param name="toggle"></param>
        /// <param name="property"></param>
        /// <param name="unRegister"></param>
        public static void BindProperty(this Toggle toggle, ReactiveProperty<bool> property, IDisposableUnregister unRegister)
        {
            toggle.onValueChanged.AsObservable().Subscribe(e => property.Value = e).AddTo(unRegister);
            property.Subscribe(toggle.SetIsOnWithoutNotify).AddTo(unRegister);
            toggle.SetIsOnWithoutNotify(property.Value);
        }
    }
}
#endif