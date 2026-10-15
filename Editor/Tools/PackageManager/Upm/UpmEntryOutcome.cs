namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 依赖条目写入的结果档。
    /// </summary>
    /// <remarks>
    /// 描述的是"盘上那一行现在怎么样"，不是"归属归谁"：归属判定另由 <c>UpmOwnershipData</c> 承担。<br />
    /// <see cref="Retained"/> 表示本次没写盘——可能是仍有其它使用者，也可能是该键本就由本工具持有且版本未变。
    /// </remarks>
    internal enum UpmEntryOutcome : byte
    {
        Added,
        Updated,
        KeptUserEntry,
        Retained,
        Removed,
        NotFound
    }
}
