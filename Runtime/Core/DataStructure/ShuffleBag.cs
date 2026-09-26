using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 用于获取不重复随机对象的类。随机地从 bag 中取出值，并且永远不会再次获取它们
    /// <para>一轮之内按权重表正好覆盖一次（同一项按其 <c>quantity</c> 出现那么多次），取空后自动重洗下一轮；
    /// 比「每次独立随机」不会漏播，也不会连播。</para>
    /// <para>重洗时只把落在袋口的那一项换到随机非袋口位置，因此换手处不会连续两次拿到同一项，
    /// 而每轮覆盖仍严格等于权重表。旧实现在这里会把刚取出的项留在可取区，n 项时换手连点概率 <c>1/(n-1)</c>。</para>
    /// </summary>
    /// <example>
    /// Usage :
    /// <code><![CDATA[
    /// 初始化：
    /// var shuffleBag = new ShuffleBag<int>(40);
    /// for (int i = 0; i < 40; i++)
    /// {
    ///     newValue = something;
    ///     shuffleBag.Add(newValue, amount);
    /// }
    /// ]]></code>
    ///
    /// 调用：
    /// <code>float something = shuffleBag.Pick();</code>
    /// </example>
    public sealed class ShuffleBag<T>
    {
        public int Capacity { get { return _contents.Capacity; } }

        /// <summary>一轮取用的总次数，即权重表长度（各项权重之和）。</summary>
        public int Size { get { return _contents.Count; } }

        /// <summary>本轮尚未取出的数量。为 0 表示刚把本轮抽空，下一手 <see cref="Pick"/> 会先重洗。</summary>
        public int Remaining { get { return _remaining; } }

        /// <summary>最近一次取出的对象；未取过或已 <see cref="Reset"/> 时为 default。</summary>
        public T CurrentItem { get { return _currentItem; } }

        /// <summary>权重表（每轮的模板）。<see cref="Add"/> 只写这里。</summary>
        private List<T> _contents;

        /// <summary>本轮工作袋，从下标 <c>_remaining - 1</c> 往 0 依次弹出，所以 0 位是本轮最后取出的那项。</summary>
        private List<T> _round;

        private T _currentItem;
        private int _remaining;

        // 值类型装箱后与 null 比较恒为 false，"是否取过"必须另设标志，不能拿 _currentItem != null 判
        private bool _hasCurrentItem;

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="initialCapacity">初始容量</param>
        public ShuffleBag(int initialCapacity)
        {
            _contents = new List<T>(initialCapacity);
            _round = new List<T>(initialCapacity);
        }

        /// <summary>
        /// 将指定数量的对象添加到袋子中
        /// <para>本轮剩余不受影响，新项自下一轮起生效。旧实现在这里会把取用游标拨回袋尾，
        /// 使本轮已经取出过的项重新可取，"一轮不重复"当场失效。</para>
        /// </summary>
        /// <param name="item">对象</param>
        /// <param name="quantity">权重</param>
        public void Add(T item, int quantity)
        {
            for (int i = 0; i < quantity; i++)
            {
                _contents.Add(item);
            }
        }

        /// <summary>丢弃本轮剩余并忘记上一手，下一手等同全新袋子重开一轮；权重表保留。</summary>
        public void Reset()
        {
            _round.Clear();
            _remaining = 0;
            _currentItem = default(T);
            _hasCurrentItem = false;
        }

        /// <summary>清空权重表，效果含 <see cref="Reset"/>。</summary>
        public void Clear()
        {
            _contents.Clear();
            Reset();
        }

        /// <summary>
        /// 从袋子中返回一个随机对象；本轮取空时自动重洗下一轮。
        /// </summary>
        /// <returns>取出的对象；从未 <see cref="Add"/> 过（权重表为空）时返回 default 而不是抛越界</returns>
        public T Pick()
        {
            if (_remaining == 0) Refill();
            if (_remaining == 0) return default(T);

            int top = --_remaining;
            _currentItem = _round[top];
            _hasCurrentItem = true;
            return _currentItem;
        }

        /// <summary>重开一轮：整表 Fisher–Yates，再把撞上上一手的项从袋口挪走。</summary>
        private void Refill()
        {
            int count = _contents.Count;
            _round.Clear();
            for (int i = 0; i < count; i++)
            {
                _round.Add(_contents[i]);
            }

            // Fisher–Yates：弹出顺序是袋口(高下标)往 0 走，洗完整表后各位置等概率
            ShuffleUtility.Shuffle(_round, count);

            // 只处理袋口那一处的冲突：挪到随机非袋口位即可消掉换手连点，
            // 且不像"本轮排除上一手"那样把项挤到固定末位、也不缩短轮长。
            // 权重 >1 时只保证不与上一手相邻重复，轮内仍可依法多次出现该项。
            if (_hasCurrentItem && count > 1 &&
                EqualityComparer<T>.Default.Equals(_round[count - 1], _currentItem))
            {
                int k = RandomUtility.NextInt(count - 1);
                T tmp = _round[count - 1];
                _round[count - 1] = _round[k];
                _round[k] = tmp;
            }

            _remaining = count;
        }
    }
}
