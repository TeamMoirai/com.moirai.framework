using System.Collections.Generic;

namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 洗牌袋：一轮内不重复、耗尽后可重洗的随机下标源（BgmPlaylist Shuffle 专用）。
    /// <para>比「每次 Random + 防连点」正确：一轮正好覆盖全部曲目一次，不会在落到末位下标时被
    /// 顺序回绕逻辑误判为列表收尾，也不会大量漏播/重播。</para>
    /// </summary>
    internal sealed class ShuffleIndexBag
    {
        private readonly List<int> _bag = new List<int>(16);
        private int _last = -1;
        private int _trackCount = -1;

        /// <summary>袋内剩余数量（未重洗的未播下标数）。</summary>
        public int Remaining => _bag.Count;

        /// <summary>清空并重置（PlayFromStart / 曲目数变化）。</summary>
        public void Reset()
        {
            _bag.Clear();
            _last = -1;
            _trackCount = -1;
        }

        /// <summary>
        /// 取下一个随机下标；袋空时自动重洗一轮（排除刚播过的 <c>_last</c>，单曲时直接回它自己）。
        /// </summary>
        /// <param name="trackCount">当前曲目数量；与上次不一致时视为新列表并重洗。</param>
        public int Next(int trackCount)
        {
            if (trackCount <= 0) return -1;
            if (trackCount == 1)
            {
                _last = 0;
                return 0;
            }

            if (_trackCount != trackCount)
            {
                Reset();
                _trackCount = trackCount;
            }

            if (_bag.Count == 0) Fill(trackCount);

            int pickIndex = _bag.Count - 1;
            int pick = _bag[pickIndex];
            _bag.RemoveAt(pickIndex);
            _last = pick;
            return pick;
        }

        private void Fill(int trackCount)
        {
            _bag.Clear();
            for (int i = 0; i < trackCount; i++)
            {
                if (i != _last) _bag.Add(i);
            }

            // 只剩上一首可选（或理论空袋）时允许重复，避免抽空
            if (_bag.Count == 0) _bag.Add(_last < 0 ? 0 : _last);

            // Fisher–Yates：从末尾弹出时各下标等概率
            for (int i = _bag.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int tmp = _bag[i];
                _bag[i] = _bag[j];
                _bag[j] = tmp;
            }
        }
    }
}
