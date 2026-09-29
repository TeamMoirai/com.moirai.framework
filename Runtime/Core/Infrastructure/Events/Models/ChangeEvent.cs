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

        /// <summary>更改发生之前的值。</summary>
        [JsonSerialize]
        public T PreviousValue { get; protected set; }
        
        /// <summary>新值。</summary>
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
        /// 从事件池获取以给定新旧值初始化的事件。
        /// </summary>
        /// <remarks>取到的事件必须经 <c>Dispose()</c> 归还池中，不要直接 <c>new</c>。</remarks>
        /// <param name="previousValue">变化前的值。</param>
        /// <param name="newValue">新值。</param>
        /// <returns>已初始化的事件。</returns>
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