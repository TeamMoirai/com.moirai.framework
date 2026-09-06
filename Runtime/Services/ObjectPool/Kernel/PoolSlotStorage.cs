using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Moirai.Atropos.ObjectPool
{
    /// <summary>
    /// 分页槽位存储：128 槽/页 + 页级自由栈——索引稳定、扩容免整块拷贝。
    /// <para>struct 语义——必须存储于可变字段后调用（方法直接改写字段状态）。</para>
    /// <para>槽位内容由调用方在 <see cref="AllocSlot"/> 返回后全量初始化（含链表指针复位）。</para>
    /// </summary>
    /// <typeparam name="TSlot">槽位结构类型（字段由调用方定义）。</typeparam>
    internal struct PoolSlotStorage<TSlot> where TSlot : struct
    {
        #region 常量 [CONSTANTS]

        /// <summary>
        /// 页内偏移位数（128 槽/页）。
        /// </summary>
        internal const int PAGE_BITS = 7;

        /// <summary>
        /// 每页槽位数量。
        /// </summary>
        internal const int PAGE_SIZE = 1 << PAGE_BITS;

        /// <summary>
        /// 页内偏移掩码。
        /// </summary>
        internal const int PAGE_MASK = PAGE_SIZE - 1;

        private const int INITIAL_PAGE_ARRAY_CAPACITY = 4;

        #endregion

        #region 字段 [FIELDS]

        private TSlot[][] _pages;
        private int[][] _pageFreeStacks;
        private int[] _pageFreeTops;
        private int[] _freePageStack;
        private int _freePageTop;
        private int _pageCount;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 获取当前槽位总容量（已分配页数 × 页大小）。
        /// </summary>
        public int SlotCount => _pageCount << PAGE_BITS;

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 初始化页元数据数组（槽位页按需分配）。
        /// </summary>
        public void Initialize()
        {
            _pages = SlotArrayPool<TSlot[]>.Rent(INITIAL_PAGE_ARRAY_CAPACITY);
            _pageFreeStacks = SlotArrayPool<int[]>.Rent(INITIAL_PAGE_ARRAY_CAPACITY);
            _pageFreeTops = SlotArrayPool<int>.Rent(INITIAL_PAGE_ARRAY_CAPACITY);
            _freePageStack = SlotArrayPool<int>.Rent(INITIAL_PAGE_ARRAY_CAPACITY);
            Array.Clear(_pages, 0, INITIAL_PAGE_ARRAY_CAPACITY);
            Array.Clear(_pageFreeStacks, 0, INITIAL_PAGE_ARRAY_CAPACITY);
            Array.Clear(_pageFreeTops, 0, INITIAL_PAGE_ARRAY_CAPACITY);
            Array.Clear(_freePageStack, 0, INITIAL_PAGE_ARRAY_CAPACITY);
            _freePageTop = 0;
            _pageCount = 0;
        }

        /// <summary>
        /// 获取指定扁平索引槽位的引用（索引必须有效）。
        /// </summary>
        /// <param name="index">扁平索引。</param>
        /// <returns>槽位引用。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref TSlot GetSlotRef(int index)
        {
            return ref _pages[index >> PAGE_BITS][index & PAGE_MASK];
        }

        /// <summary>
        /// 判断扁平索引是否指向已分配页内的槽位。
        /// </summary>
        /// <param name="index">扁平索引。</param>
        /// <returns>是否有效。</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsValidIndex(int index)
        {
            return index >= 0 && (index >> PAGE_BITS) < _pageCount;
        }

        /// <summary>
        /// 分配一个空闲槽位（无可复用页时自动开新页）。
        /// </summary>
        /// <returns>扁平索引；调用方须随后全量初始化槽位内容。</returns>
        public int AllocSlot()
        {
            if (_freePageTop <= 0)
            {
                AllocatePage();
            }

            int page = _freePageStack[_freePageTop - 1];
            int offset = _pageFreeStacks[page][--_pageFreeTops[page]];
            if (_pageFreeTops[page] <= 0)
            {
                _freePageTop--;
            }

            return (page << PAGE_BITS) | offset;
        }

        /// <summary>
        /// 归还槽位（调用方须已完成槽位级清理）。
        /// </summary>
        /// <param name="index">扁平索引。</param>
        public void FreeSlot(int index)
        {
            int page = index >> PAGE_BITS;
            int offset = index & PAGE_MASK;
            if (_pageFreeTops[page] == 0)
            {
                _freePageStack[_freePageTop++] = page;
            }

            _pageFreeStacks[page][_pageFreeTops[page]++] = offset;
        }

        /// <summary>
        /// 归还全部存储到共享数组池（池彻底关闭时使用）。
        /// </summary>
        public void ReturnStorage()
        {
            for (int page = 0; page < _pageCount; page++)
            {
                if (_pages[page] != null)
                {
                    SlotArrayPool<TSlot>.Return(_pages[page], true);
                    SlotArrayPool<int>.Return(_pageFreeStacks[page], true);
                }
            }

            SlotArrayPool<TSlot[]>.Return(_pages, true);
            SlotArrayPool<int[]>.Return(_pageFreeStacks, true);
            SlotArrayPool<int>.Return(_pageFreeTops, true);
            SlotArrayPool<int>.Return(_freePageStack, true);
            _pages = null;
            _pageFreeStacks = null;
            _pageFreeTops = null;
            _freePageStack = null;
            _freePageTop = 0;
            _pageCount = 0;
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private void AllocatePage()
        {
            EnsurePageArrayCapacity(_pageCount + 1);
            int page = _pageCount++;
            _pages[page] = SlotArrayPool<TSlot>.Rent(PAGE_SIZE);
            _pageFreeStacks[page] = SlotArrayPool<int>.Rent(PAGE_SIZE);
            Array.Clear(_pages[page], 0, PAGE_SIZE);
            for (int i = 0; i < PAGE_SIZE; i++)
            {
                _pageFreeStacks[page][i] = PAGE_SIZE - 1 - i;
            }

            _pageFreeTops[page] = PAGE_SIZE;
            _freePageStack[_freePageTop++] = page;
        }

        private void EnsurePageArrayCapacity(int required)
        {
            if (_pages.Length >= required)
            {
                return;
            }

            int newCapacity = Mathf.Max(required, _pages.Length << 1);
            GrowArray(ref _pages, newCapacity);
            GrowArray(ref _pageFreeStacks, newCapacity);
            GrowArray(ref _pageFreeTops, newCapacity);
            GrowArray(ref _freePageStack, newCapacity);
        }

        private static void GrowArray<T>(ref T[] array, int newCapacity)
        {
            T[] grown = SlotArrayPool<T>.Rent(newCapacity);
            Array.Clear(grown, 0, newCapacity);
            if (array != null)
            {
                Array.Copy(array, 0, grown, 0, array.Length);
                SlotArrayPool<T>.Return(array, true);
            }

            array = grown;
        }

        #endregion
    }
}
