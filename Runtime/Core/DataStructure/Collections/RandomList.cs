using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Moirai.Atropos.Collections
{
    public class RandomList<T>: IReadOnlyList<T>
    {
        private readonly List<T> _items;
        
        private readonly List<double> _weights;
        
        private readonly System.Random _random;
        
        private T _lastSelected;
        
        public int Count => _items.Count;
        
        public T this[int index] => _items[index];
        
        public RandomList(int capacity)
        {
            _items = new List<T>(capacity);
            _weights = new List<double>(capacity);
            _random = new System.Random();
        }
        
        public RandomList()
        {
            _items = new List<T>();
            _weights = new List<double>();
            _random = new System.Random();
        }
        
        public void Add(T item, double weight = 1)
        {
            _items.Add(item);
            _weights.Add(weight);
        }

        public T GetNext(double decayFactor = 0.9)
        {
            while (true)
            {
                double totalWeight = _weights.Sum() - (_lastSelected != null ? _weights[_items.IndexOf(_lastSelected)] : 0);
                double randomNumber = _random.NextDouble() * totalWeight;
                double cumulativeWeight = 0;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Equals(_lastSelected))
                    {
                        // 跳过最后的选定项目
                        continue;
                    }

                    cumulativeWeight += _weights[i];
                    if (randomNumber < cumulativeWeight)
                    {
                        T selected = _items[i];
                        // 减少所选项目的权重以供将来选择
                        _weights[i] *= decayFactor;
                        // 更新最后选择的项目
                        return _lastSelected = selected;
                    }
                }

                // 如果所有项都是最后选定的项，则将 lastSelected 重置为默认值
                _lastSelected = default;
                // 再次执行选择
            }
        }
        
        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}