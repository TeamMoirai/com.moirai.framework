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

        /// <summary>存储键的列表。</summary>
        public List<TKey> Keys => _keyList;

        /// <summary>存储字典实例。</summary>
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

        /// <summary>字典索引器。</summary>
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

        /// <summary>
        /// 从 <see cref="T:Moirai.Atropos.GameFrameworkDictionary`2" /> 移除所有键与值。
        /// </summary>
        public void Clear()
        {
            _keyList.Clear();
            _dictionary.Clear();
        }

        /// <summary>
        /// 向字典添加指定键与值。
        /// </summary>
        /// <param name="key">要添加元素的键。</param>
        /// <param name="item">要添加元素的值；引用类型可为 <see langword="null" />。</param>
        public virtual void Add(TKey key, TValue item)
        {
            _keyList.Add(key);
            _dictionary.Add(key, item);
        }

        /// <summary>
        /// 获取与指定键关联的值。
        /// </summary>
        /// <param name="key">要获取值的键。</param>
        /// <param name="value">当此方法返回时，若找到键，则包含与指定键关联的值； <br />
        /// 否则包含 <paramref name="value" /> 参数类型的默认值。 <br />
        /// 该参数以未初始化状态传入。</param>
        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        /// <summary>
        /// 确定 <see cref="T:System.Collections.Generic.Dictionary`2" /> 是否包含指定键。
        /// </summary>
        /// <param name="key">要在其中查找的键。</param>
        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }

        public TKey GetKey(int index)
        {
            return _keyList[index];
        }

        /// <summary>
        /// 移除指定键与关联值。
        /// </summary>
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
    /// 游戏框架顺序字典类（按键有序遍历）。
    /// </summary>
    /// <remarks>
    /// <c>Add</c> 为 O(n log n)（每次插入后全表排序），只适合小规模有序遍历；
    /// 大规模或高频插入请改用二分定位插入或 BCL <c>SortedDictionary</c>。
    /// </remarks>
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