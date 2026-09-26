using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// 声明内置服务自动注册。标记本特性的服务类由源生成器（BuiltinServiceRegistrationGenerator）汇入
    /// 程序集级生成的 <c>BuiltinServiceRegistration.RegisterAll(ServiceWorld)</c> 清单，
    /// 组合根（<c>GameAppSettings.InitializeAppServices</c>）调用该清单完成注册——替代手写的逐服务注册调用。
    /// <para>本特性只决定"注册哪些服务"；初始化顺序仍由 <see cref="ServiceDependencyAttribute"/>
    /// 依赖图在世界初始化时拓扑排序决定，与注册顺序无关。</para>
    /// <para>约束（编译期 MIRAI203/204/205 fail-fast）：目标必须是非抽象、非泛型、实现 <see cref="IService"/>
    /// 且具有可访问无参构造函数的类，作用域值必须合法。</para>
    /// <para>Scene/Gameplay 作用域的 MonoBehaviour 服务（<see cref="ServiceMono{TScope}"/>）走 Awake 自注册，
    /// 不应标记本特性。</para>
    /// <para>生成的清单类为所在程序集 internal——项目侧程序集标记后，须在自身程序集内
    /// （如 <c>GameApp.ServicesComposing</c> 订阅方）调用生成的 <c>RegisterAll</c>。</para>
    /// </summary>
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
