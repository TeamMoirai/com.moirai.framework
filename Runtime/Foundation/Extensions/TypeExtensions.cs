namespace System
{
    /// <summary>
    /// <see cref="Type"/> 类型扩展方法集。
    /// </summary>
    [UnityEngine.Scripting.Preserve]
    public static class TypeExtension
    {
        /// <summary>
        /// 判断当前类型是否为实现了指定接口的具体类型（非接口且非抽象类）。
        /// </summary>
        /// <param name="self">待检查的类型。</param>
        /// <param name="target">目标接口类型。</param>
        /// <returns>当前类型实现了 <paramref name="target"/> 且为具体类型时返回 <c>true</c>，否则返回 <c>false</c>。</returns>
        [UnityEngine.Scripting.Preserve]
        public static bool IsImplWithInterface(this Type self, Type target)
        {
            return self.GetInterface(target.FullName) != null && !self.IsInterface && !self.IsAbstract;
        }
    }
}