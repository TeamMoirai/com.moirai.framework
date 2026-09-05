using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos.Pool
{
#pragma warning disable IDE1006
    /// <summary>
    /// 内部简单对象池
    /// </summary>
    /// <typeparam name="T"></typeparam>
    // ReSharper disable once InconsistentNaming
    internal class _ObjectPool<T> where T : new()
#pragma warning restore IDE1006 
    {
        private readonly Stack<T> _stack = new Stack<T>();
        
        private int _maxSize;
        
        internal Func<T> CreateFunc;

        public int MaxSize
        {
            get => _maxSize;
            set
            {
                _maxSize = Math.Max(0, value);
                while (Size() > _maxSize)
                {
                    Get();
                }
            }
        }

        public _ObjectPool(Func<T> createFunc, int maxSize = 5000)
        {
            MaxSize = maxSize;

            if (createFunc == null)
            {
                CreateFunc = () => new T();
            }
            else
            {
                CreateFunc = createFunc;
            }
        }

        public int Size()
        {
            return _stack.Count;
        }

        public void Clear()
        {
            _stack.Clear();
        }

        public T Get()
        {
            T evt = _stack.Count == 0 ? CreateFunc() : _stack.Pop();
            return evt;
        }

        public void Release(T element)
        {
            // 双重释放检测：开发期 O(n) 全查 + fail-fast 抛出；发布期仅查栈顶并拒绝压栈。
            // 重复对象入池会导致后续两次 Get 返回同一引用（池污染），必须阻断。
#if UNITY_DEBUG
            if (_stack.Contains(element)) // 这是O(n)复杂度，当池子规模很大时会成为问题。
#else
            if (_stack.Count > 0 && ReferenceEquals(_stack.Peek(), element))
#endif
            {
#if UNITY_DEBUG || UNITY_EDITOR || DEVELOPMENT_BUILD
                throw new InvalidOperationException(
                    $"Internal error. Trying to release object of type '{typeof(T).Name}' that is already in the pool.");
#else
                LogUtility.Error("Internal error. Trying to destroy object that is already released to pool.");
                return; // 发布期拒绝压栈，阻断池污染
#endif
            }

            if (_stack.Count < MaxSize)
            {
                _stack.Push(element);
            }
#if UNITY_EDITOR
            else
                LogUtility.Warning("Internal error. Pool is already full, try to increase max size.");
#endif
        }
    }
}