namespace Moirai.Atropos.Audio
{
    /// <summary>
    /// Clip 缓存的挂起加载等待者。由条目以侵入式双向链表持有，取消即原地摘除。
    /// </summary>
    /// <remarks>持有 <see cref="Agent"/> 与 <see cref="Generation"/> 而非闭包委托：停播/复用时可单个注销，迟到回调因世代不符落空。</remarks>
    internal sealed class AudioLoadRequest : MemoryObject
    {
        public AudioClipCacheEntry Entry;
        public AudioLoadRequest Prev;
        public AudioLoadRequest Next;
        public AudioAgent Agent;
        public int Generation;
        public System.Action<bool> Completed;

        public override void Clear()
        {
            Entry = null;
            Prev = null;
            Next = null;
            Agent = null;
            Generation = 0;
            Completed = null;
        }
    }
}
