namespace Moirai.Atropos
{
    /// <summary>
    /// 池化对象驱逐回调接口。
    /// </summary>
    public interface IPoolEvictable
    {
        /// <summary>
        /// 当对象被池驱逐（非正常归还）时调用。
        /// </summary>
        void OnEvict();
    }
}
