using System;
using System.Collections.Generic;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>服务注册描述符。记录一次服务注册的全部元信息。</summary>
    public sealed class ServiceDescriptor
    {
        /// <summary>
        /// 服务契约接口类型。
        /// </summary>
        public Type InterfaceType { get; set; }

        /// <summary>
        /// 实现类型。与 <see cref="Factory"/> 二选一。
        /// </summary>
        public Type ImplementationType { get; set; }

        /// <summary>
        /// 工厂委托（优先于 <see cref="ImplementationType"/>）。
        /// </summary>
        public Func<IServiceProvider, IService> Factory { get; set; }

        /// <summary>
        /// 所属作用域。
        /// </summary>
        public EServiceScopeKind Scope { get; set; }

        /// <summary>
        /// 轮询优先级（降序）。
        /// </summary>
        public int Priority { get; set; }

        /// <summary>
        /// 是否为 MonoBehaviour 服务（容器通过 AddComponent 创建）。
        /// </summary>
        internal bool IsMonoBehaviour { get; set; }

        /// <summary>
        /// 显式声明的额外依赖类型列表。
        /// 用于 MonoBehaviour 服务（无法从构造函数推断依赖）或工厂注册的服务。
        /// 纯 C# 服务的依赖从构造函数参数自动推断，无需设置。
        /// </summary>
        internal Type[] ExplicitDependencies { get; set; }

        /// <summary>
        /// 额外契约类型列表。允许单个实例注册在多个接口下。
        /// 通过 <see cref="ServiceRegistrationBuilder.As{TExtraContract}"/> 设置。
        /// </summary>
        internal Type[] AdditionalContracts { get; set; }

        /// <summary>
        /// 全部契约类型数组（主契约 + 额外契约）。每次访问产生新数组，构建期使用。
        /// </summary>
        internal Type[] AllContracts
        {
            get
            {
                if (AdditionalContracts == null || AdditionalContracts.Length == 0)
                    return new[] { InterfaceType };

                var result = new Type[1 + AdditionalContracts.Length];
                result[0] = InterfaceType;
                Array.Copy(AdditionalContracts, 0, result, 1, AdditionalContracts.Length);
                return result;
            }
        }
    }

    /// <summary>
    /// 服务注册集合。在组合根中创建，填充后交给 <see cref="ServiceContainer"/> 构建。
    /// </summary>
    public sealed class ServiceCollection
    {
        private readonly List<ServiceDescriptor> _descriptors = new();

        internal IReadOnlyList<ServiceDescriptor> Descriptors => _descriptors;
        internal void Clear() => _descriptors.Clear();

        #region 泛型注册（编译期类型安全） [GENERIC REGISTRATION]

        /// <summary>
        /// 注册纯 C# 服务。容器通过构造函数参数自动推断依赖并注入。
        /// </summary>
        public ServiceRegistrationBuilder Register<TInterface, TImpl>(EServiceScopeKind scope)
            where TInterface : class, IService
            where TImpl : class, TInterface
        {
            var desc = new ServiceDescriptor
            {
                InterfaceType = typeof(TInterface),
                ImplementationType = typeof(TImpl),
                Scope = scope,
            };
            _descriptors.Add(desc);
            return new ServiceRegistrationBuilder(desc);
        }

        /// <summary>
        /// 通过工厂注册服务。
        /// </summary>
        public ServiceRegistrationBuilder Register<TInterface>(
            EServiceScopeKind scope,
            Func<IServiceProvider, IService> factory)
            where TInterface : class, IService
        {
            var desc = new ServiceDescriptor
            {
                InterfaceType = typeof(TInterface),
                Factory = factory ?? throw new ArgumentNullException(nameof(factory)),
                Scope = scope,
            };
            _descriptors.Add(desc);
            return new ServiceRegistrationBuilder(desc);
        }

        /// <summary>
        /// 注册 MonoBehaviour 服务。容器通过 AddComponent 创建实例，之后调用 Inject()。
        /// </summary>
        public ServiceRegistrationBuilder RegisterMono<TInterface, TImpl>(EServiceScopeKind scope)
            where TInterface : class, IService
            where TImpl : MonoBehaviour, TInterface
        {
            var desc = new ServiceDescriptor
            {
                InterfaceType = typeof(TInterface),
                ImplementationType = typeof(TImpl),
                Scope = scope,
                IsMonoBehaviour = true,
            };
            _descriptors.Add(desc);
            return new ServiceRegistrationBuilder(desc);
        }

        #endregion

        #region 运行时类型注册（Inspector 驱动） [RUNTIME TYPE REGISTRATION]

        /// <summary>
        /// 按运行时类型注册服务。用于编译期无法确定类型的场景（如从 Inspector 字符串解析）。
        /// </summary>
        public ServiceRegistrationBuilder Register(
            Type interfaceType, Type implType, EServiceScopeKind scope)
        {
            if (interfaceType == null) throw new ArgumentNullException(nameof(interfaceType));
            if (implType == null) throw new ArgumentNullException(nameof(implType));
            if (!interfaceType.IsInterface)
                throw new GameException(
                    StringUtility.Format("'{0}' is not an interface.", interfaceType.FullName));
            if (!interfaceType.IsAssignableFrom(implType))
                throw new GameException(
                    StringUtility.Format("'{0}' does not implement '{1}'.",
                        implType.FullName, interfaceType.FullName));

            var desc = new ServiceDescriptor
            {
                InterfaceType = interfaceType,
                ImplementationType = implType,
                Scope = scope,
                IsMonoBehaviour = typeof(MonoBehaviour).IsAssignableFrom(implType),
            };
            _descriptors.Add(desc);
            return new ServiceRegistrationBuilder(desc);
        }

        #endregion
    }

    /// <summary>Fluent 注册构建器。</summary>
    public class ServiceRegistrationBuilder
    {
        private readonly ServiceDescriptor _descriptor;

        internal ServiceRegistrationBuilder(ServiceDescriptor descriptor)
        {
            _descriptor = descriptor;
        }

        /// <summary>
        /// 设置轮询优先级（降序，高优先先 Tick）。
        /// </summary>
        public ServiceRegistrationBuilder WithPriority(int priority)
        {
            _descriptor.Priority = priority;
            return this;
        }

        /// <summary>
        /// 显式声明依赖（用于 MonoBehaviour 服务或工厂注册）。
        /// 纯 C# 服务的依赖从构造函数自动推断，通常无需调用。
        /// </summary>
        public ServiceRegistrationBuilder DependsOn<T>() where T : class
        {
            var existing = _descriptor.ExplicitDependencies ?? Array.Empty<Type>();
            _descriptor.ExplicitDependencies = new Type[existing.Length + 1];
            Array.Copy(existing, _descriptor.ExplicitDependencies, existing.Length);
            _descriptor.ExplicitDependencies[existing.Length] = typeof(T);
            return this;
        }

        /// <summary>
        /// 将服务额外注册到指定契约接口。单个实例可在多个接口下被解析。
        /// <para>例如 <c>collection.Register&lt;IAudioService, AudioService&gt;(scope).As&lt;IAudioLoader&gt;()</c>
        /// 允许通过 <c>IAudioService</c> 和 <c>IAudioLoader</c> 同时解析同一实例。</para>
        /// </summary>
        public ServiceRegistrationBuilder As<TExtraContract>() where TExtraContract : class
        {
            var existing = _descriptor.AdditionalContracts ?? Array.Empty<Type>();
            var arr = new Type[existing.Length + 1];
            Array.Copy(existing, arr, existing.Length);
            arr[existing.Length] = typeof(TExtraContract);
            _descriptor.AdditionalContracts = arr;
            return this;
        }
    }
}
