namespace Moirai.Atropos.Editor
{
    /// <summary>
    /// 规划阶段判定出的冲突种类。
    /// </summary>
    internal enum PlanConflictKind : byte
    {
        MissingDependency,
        Cycle,
        VersionUnsatisfied,
        UnityFloorUnmet
    }
}
