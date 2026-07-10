using System;

namespace Moirai.Atropos
{
    public static partial class StringUtility
    {
        /// <summary>
        /// 字符串构建器工具接口。
        /// 提供统一的字符串构建功能，支持 System.Text.StringBuilder 和 ZString。
        /// </summary>
        /// <remarks>
        /// <para>通过 IStringBuilder 适配两种 StringBuilder 实现。</para>
        /// </remarks>
        public interface IStringHelper
        {
            /// <summary>
            /// 获取一个字符串构建器适配器
            /// </summary>
            /// <param name="capacity">初始容量</param>
            /// <returns>可复用的字符串构建器适配器</returns>
            IStringBuilder CreateStringBuilder(int capacity = 256);

            /// <summary>
            /// 使用适配器构建字符串（简化模式）
            /// </summary>
            /// <param name="action">构建字符串的操作</param>
            /// <returns>构建的字符串</returns>
            string GetString(Action<IStringBuilder> action);

            /// <summary>
            /// 清空缓存/池
            /// </summary>
            void Clear();
        }
    }
}