namespace Moirai.Atropos
{
    /// <summary>
    /// 内存池对象基类。
    /// </summary>
    public abstract class MemoryObject
    {
        internal MemoryPoolHandle OwnerHandle;
        internal int PoolId;
        internal int SlotId;
        internal int PageGeneration;
        internal int SlotGeneration;
        internal byte State;

        /// <summary>
        /// 清理内存对象回收入池。
        /// </summary>
        public abstract void Clear();
    }
}
