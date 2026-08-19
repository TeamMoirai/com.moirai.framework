using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos.Collections
{
    /// <summary>
    /// 二叉堆优先队列。基于数组实现，Enqueue/Dequeue 均为 O(log n)。
    /// </summary>
    /// <typeparam name="T">必须实现 <see cref="IComparable{T}"/> 的元素类型。</typeparam>
    // From https://visualstudiomagazine.com/articles/2012/11/01/priority-queues-with-c.aspx
    public class PriorityQueue<T> : IEnumerable<T> where T : IComparable<T>
    {
        private readonly List<T> _data = new List<T>();

        /// <summary>
        /// 获取队列中的元素数量。
        /// </summary>
        public int Count => _data.Count;

        /// <summary>
        /// 入队，将元素插入优先队列并执行上浮调整。O(log n)。
        /// </summary>
        /// <param name="item">要入队的元素。</param>
        public void Enqueue(T item)
        {
            _data.Add(item);
            int ci = _data.Count - 1; // child index; start at end
            while (ci > 0)
            {
                int pi = (ci - 1) / 2; // parent index
                if (_data[ci].CompareTo(_data[pi]) >= 0) break; // child item is larger than (or equal) parent so we're done
                (_data[pi], _data[ci]) = (_data[ci], _data[pi]);
                ci = pi;
            }
        }

        /// <summary>
        /// 出队，移除并返回优先级最高的元素。O(log n)。
        /// </summary>
        /// <returns>优先级最高的元素。</returns>
        /// <remarks>调用前需确保队列非空。</remarks>
        public T Dequeue()
        {
            // assumes pq is not empty; up to calling code
            int li = _data.Count - 1; // last index (before removal)
            T frontItem = _data[0];   // fetch the front
            _data[0] = _data[li];
            _data.RemoveAt(li);

            --li; // last index (after removal)
            int pi = 0; // parent index. start at front of pq
            while (true)
            {
                int ci = pi * 2 + 1; // left child index of parent
                if (ci > li) break;  // no children so done
                int rc = ci + 1;     // right child
                if (rc <= li && _data[rc].CompareTo(_data[ci]) < 0) // if there is a rc (ci + 1), and it is smaller than left child, use the rc instead
                    ci = rc;
                if (_data[pi].CompareTo(_data[ci]) <= 0) break; // parent is smaller than (or equal to) smallest child so done
                (_data[ci], _data[pi]) = (_data[pi], _data[ci]);
                pi = ci;
            }
            return frontItem;
        }

        /// <summary>
        /// 查看队首元素但不移除。
        /// </summary>
        /// <returns>优先级最高的元素。</returns>
        public T Peek()
        {
            T frontItem = _data[0];
            return frontItem;
        }

        /// <summary>
        /// 返回迭代器，按内部数组顺序遍历（非优先级顺序）。
        /// </summary>
        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < _data.Count; ++i)
                yield return _data[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        /// 清空队列。
        /// </summary>
        public void Clear()
        {
            _data.Clear();
        }

        /// <summary>
        /// 返回队列内容的字符串表示。
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder(_data.Count * 8);
            for (int i = 0; i < _data.Count; ++i)
            {
                sb.Append(_data[i]);
                sb.Append(' ');
            }
            sb.Append("count = ").Append(_data.Count);
            return sb.ToString();
        }

        /// <summary>
        /// 验证堆属性是否满足（调试用）。
        /// </summary>
        /// <returns>如果所有父子节点都满足堆属性则返回 true。</returns>
        public bool IsConsistent()
        {
            // is the heap property true for all data?
            if (_data.Count == 0) return true;
            int li = _data.Count - 1; // last index
            for (int pi = 0; pi < _data.Count; ++pi) // each parent index
            {
                int lci = 2 * pi + 1; // left child index
                int rci = 2 * pi + 2; // right child index

                if (lci <= li && _data[pi].CompareTo(_data[lci]) > 0) return false; // if lc exists and it's greater than parent then bad.
                if (rci <= li && _data[pi].CompareTo(_data[rci]) > 0) return false; // check the right child too.
            }
            return true; // passed all checks
        }
    }
}