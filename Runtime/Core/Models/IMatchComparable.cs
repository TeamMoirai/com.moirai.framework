namespace Moirai.Atropos
{
    /// <summary>
    /// 实现与另一个类型的对象可进行比较
    /// </summary>
    public interface IMatchComparable<in T>
    {
        /// <summary>
        /// 将对象与另一个副本进行比较
        /// </summary>
        /// <param name="other">要进行比较的副本</param>
        /// <typeparam name="T"></typeparam>
        /// <returns>二者是否匹配</returns>
        /// <remarks>一般用于序列化数据的裁剪</remarks>
        bool Matches(T other);
    }
}
