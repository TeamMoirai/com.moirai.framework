using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务容器。负责服务的构造注入、生命周期管理和作用域 Provider 生成。
    /// <para>每个作用域（App/Scene/Gameplay）持有独立的容器实例，通过 parent 链实现跨作用域查找。</para>
    /// <para><b>构建流程</b>：<see cref="BuildAsync"/> 执行 拓扑排序 → 创建实例（构造注入）→ 注册到作用域 → OnInit → OnInitAsync。</para>
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    public sealed class ServiceContainer : IDisposable
    {
        #region 字段 [FIELDS]

        private readonly EServiceScopeKind _scopeKind;
        private readonly ServiceScope _scope;
        private readonly ServiceContainer _parent;
        private readonly ScopedServiceProvider _serviceProvider;
        private readonly List<ServiceDescriptor> _descriptors;
        private readonly Dictionary<Type, IService> _instances = new();

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>此作用域的服务提供者。</summary>
        public IServiceProvider ServiceProvider => _serviceProvider;

        /// <summary>容器所属作用域。</summary>
        public EServiceScopeKind ScopeKind => _scopeKind;

        /// <summary>父级容器（App ← Scene ← Gameplay）。</summary>
        public ServiceContainer Parent => _parent;

        /// <summary>内部作用域容器（供 GameServices 调用 Tick 等）。</summary>
        internal ServiceScope Scope => _scope;

        #endregion

        #region 构造 [CONSTRUCTION]

        /// <summary>
        /// 创建容器实例。仅存储描述符，不创建服务实例——调用 <see cref="BuildAsync"/> 完成实际构建。
        /// </summary>
        public ServiceContainer(
            EServiceScopeKind scopeKind,
            IReadOnlyList<ServiceDescriptor> descriptors,
            ServiceContainer parent = null)
        {
            _scopeKind = scopeKind;
            _scope = new ServiceScope(scopeKind, scopeKind.ToString());
            _parent = parent;
            _serviceProvider = new ScopedServiceProvider(_scope, parent?._serviceProvider);
            _descriptors = descriptors != null
                ? new List<ServiceDescriptor>(descriptors)
                : new List<ServiceDescriptor>();
        }

        #endregion

        #region 构建 [BUILD]

        /// <summary>
        /// 异步构建：拓扑排序 → 构造注入创建实例 → 注册到作用域 → OnInit → OnInitAsync。
        /// <para>按拓扑序执行：被依赖服务先于依赖方创建和初始化。</para>
        /// <para>重复构建抛出 <see cref="GameException"/>。</para>
        /// </summary>
        public async UniTask BuildAsync()
        {
            GameServices.EnsureMainThread();
            if (_scope.IsDisposed || _scope.ServiceCount > 0)
                throw new GameException(
                    StringUtility.Format("Container '{0}' has already been built.", _scopeKind));

            // 1. 拓扑排序（从构造函数参数推断依赖 + 显式声明的依赖）
            var sorted = TopologicalSort(_descriptors);

            // 2. 按拓扑序创建实例并注册到作用域（不调用 OnInit——所有实例就位后再统一初始化）
            foreach (var desc in sorted)
            {
                IService instance;
                try
                {
                    instance = CreateInstance(desc);
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Failed to create service '{0}':\n{1}",
                        desc.InterfaceType.FullName, ex);
                    throw;
                }

                _instances[desc.InterfaceType] = instance;
                _scope.Register(desc.InterfaceType, instance);
                GameServices.SetState(instance, EServiceState.Created);
            }

            // 3. 同步 OnInit（拓扑序 = 依赖序，被依赖方先初始化）
            foreach (var desc in sorted)
            {
                if (!_instances.TryGetValue(desc.InterfaceType, out var instance)) continue;
                try
                {
                    instance.OnInit();
                    GameServices.SetState(instance, EServiceState.Initialized);
                    GameServices.InvokeRegistered(instance, desc.InterfaceType, _scopeKind);
                }
                catch (Exception ex)
                {
                    LogUtility.Error("Service '{0}' OnInit failed:\n{1}",
                        desc.InterfaceType.FullName, ex);
                    throw;
                }
            }

            // 4. 异步 OnInitAsync（拓扑序）
            foreach (var desc in sorted)
            {
                if (!_instances.TryGetValue(desc.InterfaceType, out var instance)) continue;
                if (instance is IAsyncInitService asyncSvc)
                {
                    try
                    {
                        await asyncSvc.OnInitAsync();
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Service '{0}' OnInitAsync failed:\n{1}",
                            desc.InterfaceType.FullName, ex);
                        throw;
                    }
                }
            }
        }

        #endregion

        #region 实例创建 [INSTANCE CREATION]

        private IService CreateInstance(ServiceDescriptor desc)
        {
            // 工厂模式优先
            if (desc.Factory != null)
                return desc.Factory(_serviceProvider);

            if (desc.ImplementationType == null)
                throw new GameException(
                    StringUtility.Format("Service '{0}' has no factory or implementation type.",
                        desc.InterfaceType.FullName));

            // MonoBehaviour 服务：通过 AddComponent 创建
            if (desc.IsMonoBehaviour)
                return CreateMonoBehaviourInstance(desc);

            // 纯 C# 服务：通过构造函数注入
            return CreatePocoInstance(desc);
        }

        /// <summary>纯 C# 服务：反射构造 + 参数解析。</summary>
        private IService CreatePocoInstance(ServiceDescriptor desc)
        {
            var implType = desc.ImplementationType;
            var ctor = SelectConstructor(implType);

            if (ctor == null)
                throw new GameException(
                    StringUtility.Format("Service '{0}' has no public constructor.", implType.FullName));

            var parameters = ctor.GetParameters();
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                // IServiceProvider 特殊处理：注入当前作用域的 Provider
                if (paramType == typeof(IServiceProvider))
                {
                    args[i] = _serviceProvider;
                    continue;
                }

                if (!TryResolve(paramType, out var arg))
                {
                    throw new GameException(
                        StringUtility.Format(
                            "Cannot resolve parameter '{0}' for service '{1}'. " +
                            "Ensure it is registered before this service.",
                            paramType.FullName, implType.FullName));
                }
                args[i] = arg;
            }

            return (IService)ctor.Invoke(args);
        }

        /// <summary>MonoBehaviour 服务：AddComponent + Inject。</summary>
        private IService CreateMonoBehaviourInstance(ServiceDescriptor desc)
        {
            var implType = desc.ImplementationType;
            var go = new GameObject(implType.Name);

            // App 作用域的 MonoBehaviour 需要跨场景存活
            if (_scopeKind == EServiceScopeKind.App)
                UnityEngine.Object.DontDestroyOnLoad(go);

            var component = (IService)go.AddComponent(implType);

            // 调用 Inject（MonoBehaviour 的依赖注入入口）
            if (component is ServiceMonoBase mono)
                mono.Inject(_serviceProvider);

            return component;
        }

        /// <summary>
        /// 选择服务构造函数。优先选择标记了 <see cref="ServiceConstructorAttribute"/> 的构造函数；
        /// 若无标记，则回退到参数最多的公共构造函数。
        /// </summary>
        private static ConstructorInfo SelectConstructor(Type type)
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            // 优先：标记了 [ServiceConstructor] 的构造函数
            for (int i = 0; i < ctors.Length; i++)
            {
                if (ctors[i].IsDefined(typeof(ServiceConstructorAttribute), inherit: true))
                    return ctors[i];
            }

            // 回退：参数最多的公共构造函数
            return ctors.OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        }

        /// <summary>从容器链解析依赖。</summary>
        private bool TryResolve(Type type, out object instance)
        {
            if (_instances.TryGetValue(type, out IService svc))
            {
                instance = svc;
                return true;
            }
            if (_parent != null && _parent.TryResolve(type, out instance)) return true;
            instance = null;
            return false;
        }

        #endregion

        #region 拓扑排序 [TOPOLOGICAL SORT]

        /// <summary>
        /// 拓扑排序。依赖来源：纯 C# 服务从构造函数参数推断，MonoBehaviour 服务从 <c>ExplicitDependencies</c> 读取。
        /// 循环依赖会抛出 <see cref="GameException"/>。
        /// </summary>
        private static List<ServiceDescriptor> TopologicalSort(List<ServiceDescriptor> descriptors)
        {
            var byInterface = new Dictionary<Type, ServiceDescriptor>();
            foreach (var d in descriptors)
                byInterface[d.InterfaceType] = d;

            // 构建邻接表
            var inDegree = new Dictionary<Type, int>();
            var adjacency = new Dictionary<Type, List<Type>>();

            foreach (var desc in descriptors)
            {
                inDegree.TryAdd(desc.InterfaceType, 0);
                adjacency.TryAdd(desc.InterfaceType, new List<Type>());
            }

            foreach (var desc in descriptors)
            {
                var deps = CollectDependencies(desc);
                foreach (var depType in deps)
                {
                    // 仅关注本容器内注册的依赖（跨容器依赖由 parent 链在创建期解析）
                    if (!byInterface.ContainsKey(depType)) continue;

                    adjacency[depType].Add(desc.InterfaceType);
                    inDegree[desc.InterfaceType] =
                        inDegree.GetValueOrDefault(desc.InterfaceType, 0) + 1;
                }
            }

            // Kahn 算法
            var queue = new Queue<Type>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0) queue.Enqueue(kvp.Key);
            }

            var result = new List<ServiceDescriptor>(descriptors.Count);
            while (queue.Count > 0)
            {
                var type = queue.Dequeue();
                result.Add(byInterface[type]);
                foreach (var dependent in adjacency[type])
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0) queue.Enqueue(dependent);
                }
            }

            if (result.Count != descriptors.Count)
            {
                var remaining = descriptors
                    .Select(d => d.InterfaceType.FullName)
                    .Except(result.Select(d => d.InterfaceType.FullName));
                throw new GameException(
                    StringUtility.Format("Circular dependency detected among: {0}",
                        string.Join(", ", remaining)));
            }

            return result;
        }

        /// <summary>收集描述符的所有依赖类型。</summary>
        private static IEnumerable<Type> CollectDependencies(ServiceDescriptor desc)
        {
            // 从构造函数参数推断
            if (desc.ImplementationType != null && !desc.IsMonoBehaviour)
            {
                var ctor = SelectConstructor(desc.ImplementationType);
                if (ctor != null)
                {
                    foreach (var param in ctor.GetParameters())
                    {
                        if (param.ParameterType != typeof(IServiceProvider))
                            yield return param.ParameterType;
                    }
                }
            }

            // 显式声明的依赖
            if (desc.ExplicitDependencies != null)
            {
                foreach (var dep in desc.ExplicitDependencies)
                    yield return dep;
            }
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <summary>收集此容器（含父链）内已注册服务的诊断信息。</summary>
        internal void CollectDiagnosticInfo(List<GameServices.DiagnosticInfo> buffer)
        {
            _scope.CollectDiagnosticInfo(buffer);
            _parent?.CollectDiagnosticInfo(buffer);
        }

        #endregion

        #region 轮询代理 [TICK PROXY]

        public void Tick(float elapseSeconds, float realElapseSeconds)
            => _scope.Tick(elapseSeconds, realElapseSeconds);

        public void FixedTick(float elapseSeconds, float realElapseSeconds)
            => _scope.FixedTick(elapseSeconds, realElapseSeconds);

        public void LateTick(float elapseSeconds, float realElapseSeconds)
            => _scope.LateTick(elapseSeconds, realElapseSeconds);

        public void DrawGizmos() => _scope.DrawGizmos();

        #endregion

        #region 销毁 [DISPOSE]

        public void Dispose()
        {
            _scope.Dispose();
            _instances.Clear();
        }

        #endregion
    }
}
