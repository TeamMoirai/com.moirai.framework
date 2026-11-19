using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 游戏事件标记：struct 事件类型显式声明（编译期校验入口——分析器校验标记类型满足 struct + IGameEvent 约束）。
    /// </summary>
    /// <remarks>
    /// 校验规则（EventBusAnalyzer）：标记了 [GameEvent] 的类型必须为 struct 且实现 <see cref="IGameEvent"/>； <br />
    /// struct 事件字段应为 public（发布点构造字面量）或 readonly——有状态行为字段（方法/事件）属契约违反。 <br />
    /// 池化 class 大事件（<see cref="MemoryPool"/>）不标本特性——class 事件走池化路径，无需编译期 struct 约束。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class GameEventAttribute : Attribute
    {
    }
}
