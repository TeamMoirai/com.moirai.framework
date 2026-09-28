using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏框架字典类。
    /// </summary>
    /// <typeparam name="TKey">指定字典Key的元素类型。</typeparam>
    /// <typeparam name="TValue">指定字典Value的元素类型。</typeparam>
    public class GameDictionary<TKey, TValue>
    {
        protected readonly List<TKey> _keyList = new List<TKey>();
        protected readonly Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();

        /// <summary>
        /// 存储键的列表。
        /// </summary>
        public List<TKey> Keys => _keyList;

        /// <summary>
        /// 存储字典实例。
        /// </summary>
        public int Count => _keyList.Count;

        /// <summary>
        /// 通过KEY的数组下标获取元素。
        /// </summary>
        /// <param name="index">下标。</param>
        /// <returns>TValue。</returns>
        public TValue GetValueByIndex(int index)
        {
            return _dictionary[_keyList[index]];
        }

        /// <summary>
        /// 通过KEY的数组下标设置元素。
        /// </summary>
        /// <param name="index">下标。</param>
        /// <param name="item">TValue。</param>
        public void SetValue(int index, TValue item)
        {
            _dictionary[_keyList[index]] = item;
        }

        /// <summary>
        /// 字典索引器。
        /// </summary>
        /// <param name="key">TKey。</param>
        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                if (!ContainsKey(key))
                {
                    Add(key, value);
                }
                else
                {
                    _dictionary[key] = value;
                }
            }
        }

        /// <summary>Removes all keys and values from the <see cref="T:Moirai.Atropos.GameFrameworkDictionary`2" />.</summary>
        public void Clear()
        {
            _keyList.Clear();
            _dictionary.Clear();
        }

        /// <summary>Adds the specified key and value to the dictionary.</summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="item">The value of the element to add. The value can be <see langword="null" /> for reference types.</param>
        public virtual void Add(TKey key, TValue item)
        {
            _keyList.Add(key);
            _dictionary.Add(key, item);
        }

        /// <summary>Gets the value associated with the specified key.</summary>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value" /> parameter. This parameter is passed uninitialized.</param>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        /// <summary>Determines whether the <see cref="T:System.Collections.Generic.Dictionary`2" /> contains the specified key.</summary>
        /// <param name="key">The key to locate in the </param>
        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }

        public TKey GetKey(int index)
        {
            return _keyList[index];
        }

        /// <summary>移除指定键与关联值。</summary>
        /// <param name="key">要移除的键。</param>
        /// <returns>任一容器实际删除了条目即返回 true。</returns>
        public bool Remove(TKey key)
        {
            // 两容器分别执行、合并结果：&& 短路会在键表缺失而字典存在时静默漏删字典项，
            // 且 Count/Keys 出自 _keyList、与字典内容漂移。
            bool removedFromList = _keyList.Remove(key);
            bool removedFromDictionary = _dictionary.Remove(key);
            return removedFromList || removedFromDictionary;
        }
    }

    /// <summary>
    /// 游戏框架顺序字典类。
    /// <para><b>复杂度契约</b>：Add 为 O(n log n)（每次插入后全表排序）——面向小规模有序遍历场景；
    /// 大规模高频插入请改用有序结构（二分定位插入或 BCL SortedDictionary），本类不承诺插入性能。</para>
    /// </summary>
    /// <typeparam name="TKey">指定字典Key的元素类型。</typeparam>
    /// <typeparam name="TValue">指定字典Value的元素类型。</typeparam>
    public class GameSortedDictionary<TKey, TValue> : GameDictionary<TKey, TValue>
    {
        public override void Add(TKey key, TValue item)
        {
            base.Add(key, item);
            _keyList.Sort();
        }
    }
}