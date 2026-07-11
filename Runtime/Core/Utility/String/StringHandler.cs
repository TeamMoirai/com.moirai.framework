using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 字符串处理器基类。
    /// </summary>
    /// <remarks>
    /// 提供统一的字符串构建功能。
    /// </remarks>
    [Serializable]
    public abstract partial class StringHandler
    {
        /// <summary>
        /// 获取一个字符串构建器适配器
        /// </summary>
        /// <param name="capacity">初始容量</param>
        /// <returns>可复用的字符串构建器适配器</returns>
        public abstract IStringBuilder CreateStringBuilder(int capacity = 256);

        /// <summary>
        /// 使用适配器构建字符串（简化模式）
        /// </summary>
        /// <param name="action">构建字符串的操作</param>
        /// <returns>构建的字符串</returns>
        public abstract string GetString(Action<IStringBuilder> action);

        /// <summary>
        /// 清空缓存/池
        /// </summary>
        public abstract void Clear();
    }
}