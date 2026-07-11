using System;
using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认字符串构建器工具实现。<br/>
    /// 优先使用 StringBuilderCache（ThreadStatic 单槽缓存，零分配），
    /// 回退到 StringBuilderPool（多槽池，减少分配）。
    /// </summary>
    /// <remarks>
    /// 架构设计（0GC）：<br/>
    /// - StringBuilderCache: ThreadStatic 单槽缓存，单线程场景下零分配<br/>
    /// - StringBuilderPool: Stack-based 多槽池，多线程或高频场景下减少分配<br/>
    /// - AdapterPool: 池化 StringBuilderAdapter 实例，避免堆分配<br/>
    /// - 优先级: Cache > Pool > new StringBuilder
    /// </remarks>
    [Serializable]
    public sealed class DefaultStringHandler : StringHandler
    {
        // StringBuilderCache: ThreadStatic 单槽缓存（优先）
        [ThreadStatic] // 每个静态类型字段对于每一个线程都是唯一的
        private static StringBuilder s_CacheStringBuilder;
        private const int MAX_CACHE_SIZE = 512;

        // StringBuilderPool: Stack-based 多槽池（回退）
        private static readonly Stack<StringBuilder> s_Pool = new Stack<StringBuilder>();
        private const int MAX_POOL_SIZE = 32;
        private const int MAX_POOL_CAPACITY = 1024;

        // AdapterPool: 池化适配器实例（0GC 关键）
        private static readonly Stack<DefaultStringBuilder> s_AdapterPool = new Stack<DefaultStringBuilder>();
        private const int MAX_ADAPTER_POOL_SIZE = 16;

        #region 实现方法 [IMPLEMENTATION METHODS]

        /// <summary>
        /// 获取一个字符串构建器适配器（0GC）。
        /// 优先从池中获取，回退到创建新实例。
        /// </summary>
        /// <param name="capacity">初始容量</param>
        /// <returns>可复用的字符串构建器适配器</returns>
        public override IStringBuilder CreateStringBuilder(int capacity = 256)
        {
            // 优先: 从适配器池获取（0GC）
            if (s_AdapterPool.Count > 0)
            {
                var adapter = s_AdapterPool.Pop();
                adapter.builder = AcquireBuilder(capacity);
                return adapter;
            }

            // 回退: 创建新适配器（仅在池空时分配）
            return new DefaultStringBuilder(AcquireBuilder(capacity));
        }

        /// <summary>
        /// 使用适配器构建字符串（简化模式）
        /// </summary>
        /// <param name="action">构建字符串的操作</param>
        /// <returns>构建的字符串</returns>
        public override string GetString(Action<IStringBuilder> action)
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
        /// 清空所有缓存和池
        /// </summary>
        public override void Clear()
        {
            s_CacheStringBuilder = null;
            s_Pool.Clear();
            s_AdapterPool.Clear();
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 释放适配器到池中（0GC）
        /// </summary>
        /// <param name="adapter">要释放的适配器</param>
        private void Release(IStringBuilder adapter)
        {
            if (adapter == null) return;

            // 释放内部 StringBuilder
            if (adapter is DefaultStringBuilder sbAdapter)
            {
                ReleaseBuilder(sbAdapter.builder);
                sbAdapter.builder = null;

                // 将适配器存入池中（0GC）
                if (s_AdapterPool.Count < MAX_ADAPTER_POOL_SIZE)
                {
                    s_AdapterPool.Push(sbAdapter);
                }
            }
        }

        /// <summary>
        /// 获取一个 StringBuilder（内部方法）
        /// </summary>
        private StringBuilder AcquireBuilder(int capacity = 256)
        {
            // 优先: ThreadStatic 缓存（零分配，单线程最快）
            StringBuilder cached = s_CacheStringBuilder;
            if (cached != null && cached.Capacity >= capacity)
            {
                s_CacheStringBuilder = null;
                cached.Clear();
                return cached;
            }

            // 回退: 多槽池
            if (s_Pool.Count > 0)
            {
                var sb = s_Pool.Pop();
                sb.Clear();
                if (sb.Capacity < capacity)
                    sb.Capacity = capacity;
                return sb;
            }

            // 最后: 创建新实例
            return new StringBuilder(capacity);
        }

        /// <summary>
        /// 释放 StringBuilder（内部方法）
        /// </summary>
        private void ReleaseBuilder(StringBuilder stringBuilder)
        {
            if (stringBuilder == null) return;

            // 优先: 存入 ThreadStatic 缓存（容量合理时）
            if (stringBuilder.Capacity <= MAX_CACHE_SIZE && s_CacheStringBuilder == null)
            {
                s_CacheStringBuilder = stringBuilder;
                return;
            }

            // 回退: 存入多槽池（容量合理且池未满时）
            if (stringBuilder.Capacity <= MAX_POOL_CAPACITY && s_Pool.Count < MAX_POOL_SIZE)
            {
                stringBuilder.Clear();
                s_Pool.Push(stringBuilder);
            }
        }

        #endregion
    }
}