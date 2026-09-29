using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 声明内置服务的自动注册；标记的服务类由源生成器汇入程序集级注册清单，组合根调用该清单完成注册。
    /// </summary>
    /// <remarks>
    /// 本特性只决定"注册哪些服务"；初始化顺序仍由 <see cref="ServiceDependencyAttribute"/> 依赖图在世界初始化时拓扑排序决定，与注册顺序无关。
    /// 约束（编译期 MIRAI203/204/205 fail-fast）：目标必须是非抽象、非泛型、实现 <see cref="IService"/> 且具有可访问无参构造函数的类，作用域值必须合法。
    /// Scene/Gameplay 作用域的 MonoBehaviour 服务（<see cref="ServiceMono{TScope}"/>）走 Awake 自注册，不应标记本特性。
    /// 生成的清单类为所在程序集 internal——项目侧程序集标记后， <br />
    /// 须在自身程序集内（如 <c>GameApp.ServicesComposing</c> 订阅方）调用生成的 <c>BuiltinServiceRegistration.RegisterAll</c>。
    /// </remarks>
    /// <example>
    /// <code>
    /// [AutoRegisterService]
    /// [ServiceDependency(typeof(DebuggerService))]
    /// public partial class TimerService : ServiceBase, IServiceTickable { ... }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AutoRegisterServiceAttribute : Attribute
    {
        /// <summary>
        /// 目标注册作用域。
        /// </summary>
        public EServiceScopeKind Scope { get; }

        /// <param name="scope">目标注册作用域（默认 App）。</param>
        public AutoRegisterServiceAttribute(EServiceScopeKind scope = EServiceScopeKind.App)
        {
            Scope = scope;
        }
    }
}
