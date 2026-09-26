namespace Moirai.Atropos.Events
{
    /// <summary>
    /// 用于保存值的控件接口，并且可以在用户输入更改该值时发出通知。
    /// </summary>
    public interface INotifyValueChanged<T>
    {
        /// <summary>
        /// 控件的值。
        /// </summary>
        T Value { get; set; }
        
        /// <summary>
        /// 设置值，即使不同，也不会通知使用 <see cref="ChangeEvent{T}"/> 注册回调
        /// </summary>
        /// <param name="newValue">要设置的新值。</param>
        void SetValueWithoutNotify(T newValue);
    }
    
    /// <summary>
    /// 用于实现 <see cref="INotifyValueChanged{T}"/> 对象的扩展方法。
    /// </summary>
    public static class NotifyValueChangedExtensions
    {
        /// <summary>
        /// 注册此回调，当值更改时，会收到 <see cref="ChangeEvent{T}"/>。
        /// </summary>
        public static bool RegisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback)
        {
            if (control is CallbackEventHandler handler)
            {
                handler.RegisterCallback(callback, TrickleDown.NoTrickleDown);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 取消注册此回调，当值发生变化时，使其不再接收 <see cref="ChangeEvent{T}"/> 。
        /// </summary>
        public static bool UnregisterValueChangedCallback<T>(this INotifyValueChanged<T> control, EventCallback<ChangeEvent<T>> callback)
        {
            if (control is CallbackEventHandler handler)
            {
                handler.UnregisterCallback(callback);
                return true;
            }
            return false;
        }
    }
}