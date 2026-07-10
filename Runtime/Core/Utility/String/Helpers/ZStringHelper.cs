#if ZSTRING_INSTALLED
using System;
using System.Collections.Generic;
using Cysharp.Text;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 ZString 的零分配字符串构建器工具实现。
    /// 使用 Cysharp.Text.ZString 提供完全零分配的字符串操作。
    /// </summary>
    /// <remarks>
    /// <para>适配器池化实现 0GC。</para>
    /// </remarks>
    public sealed class ZStringHelper : StringUtility.IStringHelper
    {
        // 适配器池（0GC 关键）
        private static readonly Stack<ZStringBuilder> s_AdapterPool = new Stack<ZStringBuilder>();
        private const int MAX_ADAPTER_POOL_SIZE = 16;

        /// <summary>
        /// 获取一个 ZString 字符串构建器适配器（0GC）
        /// </summary>
        /// <param name="capacity">初始容量</param>
        /// <returns>可复用的字符串构建器适配器</returns>
        public StringUtility.IStringBuilder CreateStringBuilder(int capacity = 256)
        {
            // 优先: 从适配器池获取（0GC）
            if (s_AdapterPool.Count > 0)
            {
                var adapter = s_AdapterPool.Pop();
                adapter.builder = ZString.CreateStringBuilder();
                adapter.disposed = false;
                return adapter;
            }

            // 回退: 创建新适配器（仅在池空时分配）
            return ZStringBuilder.Create();
        }

        /// <summary>
        /// 使用适配器构建字符串（简化模式，使用 ZString 零分配）
        /// </summary>
        /// <param name="action">构建字符串的操作</param>
        /// <returns>构建的字符串</returns>
        public string GetString(Action<StringUtility.IStringBuilder> action)
        {
            if (action == null) return string.Empty;

            var adapter = CreateStringBuilder();
            try
            {
                action.Invoke(adapter);
                return adapter.ToString();
            }
            finally
            {
                Release(adapter);
            }
        }

        /// <summary>
        /// 释放适配器到池中（0GC）
        /// </summary>
        /// <param name="adapter">要释放的适配器</param>
        private void Release(StringUtility.IStringBuilder adapter)
        {
            if (adapter == null) return;

            if (adapter is ZStringBuilder zsbAdapter)
            {
                // 释放内部 ZString builder
                if (!zsbAdapter.disposed)
                {
                    zsbAdapter.builder.Dispose();
                    zsbAdapter.disposed = true;
                }

                // 将适配器存入池中（0GC）
                if (s_AdapterPool.Count < MAX_ADAPTER_POOL_SIZE)
                {
                    s_AdapterPool.Push(zsbAdapter);
                }
            }
        }

        /// <summary>
        /// 清空缓存
        /// </summary>
        public void Clear()
        {
            s_AdapterPool.Clear();
        }
    }
}
#endif