namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// 洗牌袋：一轮内不重复、耗尽后可重洗的随机下标源（BgmPlaylist Shuffle 专用）。
    /// <para>算法本体在 <see cref="ShuffleBag{T}"/>，这里只补「按下标建袋 + 曲目数变化即重建」这一段：
    /// 播放列表可在运行期被改，而 <see cref="ShuffleBag{T}"/> 自己不知道下标从几开始算新列表。</para>
    /// <para>比「每次 Random + 防连点」正确：一轮正好覆盖全部曲目一次，不会在落到末位下标时被
    /// 顺序回绕逻辑误判为列表收尾，也不会大量漏播/重播。</para>
    /// </summary>
    internal sealed class ShuffleIndexBag
    {
        private readonly ShuffleBag<int> _bag = new ShuffleBag<int>(16);
        private int _trackCount = -1;

        /// <summary>袋内剩余数量（本轮未播下标数）。</summary>
        public int Remaining => _bag.Remaining;

        /// <summary>清空并重置（PlayFromStart / 曲目数变化）。</summary>
        public void Reset()
        {
            _bag.Reset();
            _trackCount = -1;
        }

        /// <summary>
        /// 取下一个随机下标；袋空时自动重洗一轮（换手不连点，单曲时直接回它自己）。
        /// </summary>
        /// <param name="trackCount">当前曲目数量；与上次不一致时视为新列表并重洗。</param>
        public int Next(int trackCount)
        {
            if (trackCount <= 0) return -1;

            if (_trackCount != trackCount)
            {
                _bag.Clear();
                for (int i = 0; i < trackCount; i++)
                {
                    _bag.Add(i, 1);
                }

                _trackCount = trackCount;
            }

            return _bag.Pick();
        }
    }
}
