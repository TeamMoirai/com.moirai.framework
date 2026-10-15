namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 依赖条目写入的结果档。
    /// </summary>
    internal enum UpmEntryOutcome : byte
    {
        Added,
        Updated,
        KeptUserEntry,
        Removed,
        NotFound
    }
}
