#if ZSTRING_INSTALLED
using System;
using System.Collections.Generic;
using Cysharp.Text;

namespace Moirai.Atropos
{
    /// <summary>
    /// 基于 ZString 的零分配字符串构建器工具实现。<br/>
    /// 使用 <see cref="Cysharp.Text.ZString"/> 提供完全零分配的字符串操作。
    /// </summary>
    /// <remarks>
    /// <para>适配器池化实现 0GC。</para>
    /// </remarks>
    [Serializable]
    public sealed class ZStringHandler : StringHandler
    {
        // 适配器池（0GC 关键）
        private static readonly Stack<ZStringBuilder> s_AdapterPool = new Stack<ZStringBuilder>();
        private const int MAX_ADAPTER_POOL_SIZE = 16;

        // 池操作锁：适配器池为跨线程共享静态栈，Pop/Push/Clear 需原子化（后台线程格式化场景）
        private static readonly object s_PoolLock = new object();

        #region 实现方法 [IMPLEMENTATION METHODS]

        /// <summary>
        /// 获取一个 ZString 字符串构建器适配器（0GC）
        /// </summary>
        /// <param name="capacity">初始容量</param>
        /// <returns>可复用的字符串构建器适配器</returns>
        public override IStringBuilder CreateStringBuilder(int capacity = 256)
        {
            // 优先: 从适配器池获取（0GC）
            lock (s_PoolLock)
            {
                if (s_AdapterPool.Count > 0)
                {
                    var adapter = s_AdapterPool.Pop();
                    adapter.inPool = false;
                    adapter.builder = ZString.CreateStringBuilder();
                    adapter.disposed = false;
                    return adapter;
                }
            }

            // 回退: 创建新适配器（仅在池空时分配）
            return ZStringBuilder.Create();
        }

        /// <summary>
        /// 使用适配器构建字符串（简化模式，使用 ZString 零分配）
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
        /// 清空缓存
        /// </summary>
        public override void Clear()
        {
            lock (s_PoolLock)
            {
                s_AdapterPool.Clear();
            }
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        /// <summary>
        /// 释放适配器到池中（0GC）。委托给 <see cref="ZStringBuilder.Dispose"/>，
        /// 保证 GetString / Format / ToStringAndDispose 所有路径统一走池回收。
        /// </summary>
        /// <param name="adapter">要释放的适配器</param>
        private void Release(IStringBuilder adapter)
        {
            (adapter as ZStringBuilder)?.Dispose();
        }

        /// <summary>
        /// 将适配器归还池中（0GC）。
        /// 由 <see cref="ZStringBuilder.Dispose"/> 回调，
        /// 修复原先 Dispose 后适配器对象直接丢弃导致的池泄漏。
        /// </summary>
        internal static void Return(ZStringBuilder adapter)
        {
            if (adapter == null) return;

            // 锁内完成 inPool 检查与入池，防同适配器被并发归还导致双重入池
            lock (s_PoolLock)
            {
                if (adapter.inPool) return;

                // 释放内部 ZString builder
                if (!adapter.disposed)
                {
                    adapter.builder.Dispose();
                    adapter.disposed = true;
                }

                adapter.inPool = true;

                // 将适配器存入池中（0GC）
                if (s_AdapterPool.Count < MAX_ADAPTER_POOL_SIZE)
                {
                    s_AdapterPool.Push(adapter);
                }
            }
        }

        #endregion
    }
}
#endif