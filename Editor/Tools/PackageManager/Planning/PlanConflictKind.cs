namespace Moirai.Atropos.PackageManager
{
    /// <summary>
    /// 规划阶段判定出的冲突种类。
    /// </summary>
    internal enum PlanConflictKind : byte
    {
        MissingDependency,
        Cycle,
        VersionUnsatisfied,
        UnityFloorUnmet,
        FrameworkFloorUnmet,
        UnityCeilingExceeded,
        FrameworkCeilingExceeded
    }
}
