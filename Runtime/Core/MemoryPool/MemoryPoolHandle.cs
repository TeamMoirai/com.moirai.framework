using System;
using System.Runtime.CompilerServices;

namespace Moirai.Atropos
{
    /// <summary>
    /// 内存池缓存句柄，用于避免重复的 Type 查找。
    /// </summary>
    public readonly struct MemoryPoolHandle
    {
        private readonly MemoryPoolRegistry.MemoryPoolHandle _handle;

        /// <summary>
        /// 初始化内存池句柄的新实例。
        /// </summary>
        internal MemoryPoolHandle(MemoryPoolRegistry.MemoryPoolHandle handle)
        {
            _handle = handle;
        }

        /// <summary>
        /// 获取句柄是否有效。
        /// </summary>
        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _handle != null;
        }

        internal MemoryPoolRegistry.MemoryPoolHandle Inner
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _handle;
        }

        internal int PoolId
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _handle != null ? _handle.PoolId : 0;
        }

        /// <summary>
        /// 从内存池获取内存对象。
        /// </summary>
        /// <returns>内存对象。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MemoryObject Acquire()
        {
            ThrowIfInvalid();
            return _handle.Acquire();
        }

        /// <summary>
        /// 将内存对象归还内存池。
        /// </summary>
        /// <param name="memory">内存对象。</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Release(MemoryObject memory)
        {
            ThrowIfInvalid();
            _handle.Release(memory);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfInvalid()
        {
            if (_handle == null)
            {
                throw new InvalidOperationException("MemoryPoolHandle is invalid.");
            }
        }
    }
}
