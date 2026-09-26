namespace Moirai.Atropos.Events
{
    /// <summary>
    /// ChangeEvent 的基本接口。
    /// </summary>
    public interface IChangeEvent
    {
    }
    
    /// <summary>
    /// 当字段中的值发生更改时发送事件。
    /// </summary>
    public class ChangeEvent<T> : EventBase<ChangeEvent<T>>, IChangeEvent
    {
        static ChangeEvent()
        {
            SetCreateFunction(() => new ChangeEvent<T>());
        }

        /// <summary>
        /// 更改发生之前的值。
        /// </summary>
        [JsonSerialize]
        public T PreviousValue { get; protected set; }
        
        /// <summary>
        /// 新值。
        /// </summary>
        [JsonSerialize]
        public T NewValue { get; protected set; }

        /// <summary>
        /// 将事件设置为其初始状态。
        /// </summary>
        protected override void Init()
        {
            base.Init();
            LocalInit();
        }

        private void LocalInit()
        {
            PreviousValue = default;
            NewValue = default;
        }

        /// <summary>
        /// 从事件池中获取事件，并使用给定的值对其进行初始化。
        /// 使用此功能，而不是创建新事件。使用此方法获取的事件需要释放回池中。可以使用 Dispose() 来释放它们。
        /// </summary>
        /// <param name="previousValue">The previous value.</param>
        /// <param name="newValue">The new value.</param>
        /// <returns>初始化的事件。</returns>
        public static ChangeEvent<T> GetPooled(T previousValue, T newValue)
        {
            ChangeEvent<T> e = GetPooled();
            e.PreviousValue = previousValue;
            e.NewValue = newValue;
            return e;
        }

        /// <summary>
        /// 构造函数。
        /// </summary>
        public ChangeEvent()
        {
            LocalInit();
        }
    }
}