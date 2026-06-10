using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 用于获取不重复随机对象的类。随机地从 bag 中取出值，并且永远不会再次获取它们
    /// </summary>
    /// <example>
    /// Usage :
    /// <code><![CDATA[
    /// 初始化：
    /// var shuffleBag = new ShuffleBag(40);
    /// for (int i = 0; i<40; i++)
    /// {
    ///     newValue = something;
    ///     shuffleBag.Add(newValue, amount);
    /// }
    /// ]]></code>
    ///
    /// 调用：
    /// <code>float something = shuffleBag.Pick();</code>
    /// </example>
    public class ShuffleBag<T>
    {
        public virtual int Capacity { get { return _contents.Capacity; } }
        public virtual int Size { get { return _contents.Count; } }

        protected List<T> _contents;
        protected T _currentItem;
        protected int _currentIndex = -1;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="initialCapacity">初始容量</param>
        public ShuffleBag(int initialCapacity)
        {
            _contents = new List<T>(initialCapacity);
        }

        /// <summary>
        /// 将指定数量的对象添加到袋子中
        /// </summary>
        /// <param name="item">对象</param>
        /// <param name="quantity">权重</param>
        public virtual void Add(T item, int quantity)
        {
            for (int i = 0; i < quantity; i++)
            {
                _contents.Add(item);
            }
            _currentIndex = Size - 1;
        }

        /// <summary>
        /// 从袋子中返回一个随机对象
        /// </summary>
        /// <returns></returns>
        public T Pick()
        {
            if (_currentIndex < 1)
            {
                _currentIndex = Size - 1;
                _currentItem = _contents[0];
                return _currentItem;
            }

            int position = UnityEngine.Random.Range(0, _currentIndex);

            _currentItem = _contents[position];
            _contents[position] = _contents[_currentIndex];
            _contents[_currentIndex] = _currentItem;
            _currentIndex--;

            return _currentItem;
        }
    }
}
