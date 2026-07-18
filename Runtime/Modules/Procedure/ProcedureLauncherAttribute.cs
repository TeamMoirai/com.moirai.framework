using System;

namespace Moirai.Atropos.Procedure
{
    /// <summary>
    /// 标记可被流程系统使用的流程类。
    /// 只有标记了此属性的 ProcedureBase 子类才会出现在 ProcedureSettings 的可用流程列表中。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class ProcedureLauncherAttribute : Attribute
    {
    }
}
