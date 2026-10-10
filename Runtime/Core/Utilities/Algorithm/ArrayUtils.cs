using System;
using System.Collections;
using System.Collections.Generic;

namespace Moirai.Atropos.Collections
{
    // Code from UnityEditor.ArrayUtility
    // Helpers for builtin arrays ...
    // 这些是 O(n) 操作（其中使用 List<T>()） - 数组实际上是复制的 （http://msdn.microsoft.com/en-us/library/fkbw11z0.aspx）
    // 但它现在很有帮助
    public static class ArrayUtils
    {
        // 将 'item' 附加到 'array' 的末尾
        public static void Add<T>(ref T[] array, T item)
        {
            Array.Resize(ref array, array.Length + 1);
            array[^1] = item;
        }

        // 比较两个数组
        public static bool ArrayEquals<T>(T[] lhs, T[] rhs)
        {
            if (lhs == null || rhs == null)
                return lhs == rhs;

            if (lhs.Length != rhs.Length)
                return false;

            for (int i = 0; i < lhs.Length; i++)
            {
                if (!lhs[i].Equals(rhs[i]))
                    return false;
            }
            return true;
        }

        // 比较两个数组
        public static bool ArrayReferenceEquals<T>(T[] lhs, T[] rhs)
        {
            if (lhs == null || rhs == null)
                return lhs == rhs;

            if (lhs.Length != rhs.Length)
                return false;

            for (int i = 0; i < lhs.Length; i++)
            {
                if (!ReferenceEquals(lhs[i], rhs[i]))
                    return false;
            }
            return true;
        }

        // 将项目附加到数组的末尾
        public static void AddRange<T>(ref T[] array, T[] items)
        {
            int size = array.Length;
            Array.Resize(ref array, array.Length + items.Length);
            for (int i = 0; i < items.Length; i++)
                array[size + i] = items[i];
        }

        // 在位置 'index' 插入项 'item'
        public static void Insert<T>(ref T[] array, int index, T item)
        {
            ArrayList a = new ArrayList();
            a.AddRange(array);
            a.Insert(index, item);
            array = a.ToArray(typeof(T)) as T[];
        }

        public static void Reorder<T>(ref T[] array, int fromIndex, int toIndex)
        {
            ArrayList a = new ArrayList();
            a.AddRange(array);
            var item = array[fromIndex];
            a.RemoveAt(fromIndex);
            a.Insert(toIndex, item);
            array = a.ToArray(typeof(T)) as T[];
        }

        // 从 'array' 中删除 'item'
        public static void Remove<T>(ref T[] array, T item)
        {
            List<T> newList = new List<T>(array);
            newList.Remove(item);
            array = newList.ToArray();
        }

        public static List<T> FindAll<T>(T[] array, Predicate<T> match)
        {
            List<T> list = new List<T>(array);
            return list.FindAll(match);
        }

        public static T Find<T>(T[] array, Predicate<T> match)
        {
            List<T> list = new List<T>(array);
            return list.Find(match);
        }

        // 查找满足 Predicate 的第一个元素的索引
        public static int FindIndex<T>(T[] array, Predicate<T> match)
        {
            List<T> list = new List<T>(array);
            return list.FindIndex(match);
        }

        // 值为 'value' 的第一个元素的索引
        public static int IndexOf<T>(T[] array, T value)
        {
            List<T> list = new List<T>(array);
            return list.IndexOf(value);
        }

        // 值为 'value' 的最后一个元素的索引
        public static int LastIndexOf<T>(T[] array, T value)
        {
            List<T> list = new List<T>(array);
            return list.LastIndexOf(value);
        }

        // 删除位置 'index' 的元素
        public static void RemoveAt<T>(ref T[] array, int index)
        {
            List<T> list = new List<T>(array);
            list.RemoveAt(index);
            array = list.ToArray();
        }

        // 确定数组是否包含项
        public static bool Contains<T>(T[] array, T item)
        {
            List<T> list = new List<T>(array);
            return list.Contains(item);
        }

        // 清除数组
        public static void Clear<T>(ref T[] array)
        {
            Array.Clear(array, 0, array.Length);
            Array.Resize(ref array, 0);
        }
    }
}