using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 热路径标注：声明成员处于每帧或每事件高频执行区，由 HotPathAnalyzer 在编译期执行装箱与匿名函数检查。
    /// </summary>
    /// <remarks>
    /// 类型级标注覆盖该类型声明的全部方法体（构造函数与属性访问器在内），成员级标注只作用于自身方法体。 <br />
    /// 诊断编号：MIRAI600（值类型装箱进 object/接口目标）、MIRAI601（匿名函数/闭包分配）；仅编译期消费，运行时无行为足迹。 <br />
    /// 冷路径与初始化代码不应标注，避免误伤。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property)]
    public sealed class HotPathAttribute : Attribute
    {
    }
}
