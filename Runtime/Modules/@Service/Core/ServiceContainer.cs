using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务容器。负责服务的构造注入、生命周期管理和作用域 Provider 生成。
    /// <para>每个作用域（App/Scene/Gameplay）持有独立实例，通过 parent 链实现跨作用域查找。</para>
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

        /// <summary>
        /// 此作用域的服务提供者。
        /// </summary>
        public IServiceProvider ServiceProvider => _serviceProvider;

        /// <summary>
        /// 容器所属作用域。
        /// </summary>
        public EServiceScopeKind ScopeKind => _scopeKind;

        /// <summary>
        /// 父级容器（App ← Scene ← Gameplay）。
        /// </summary>
        public ServiceContainer Parent => _parent;

        internal ServiceScope Scope => _scope;

        #endregion

        #region 构造 [CONSTRUCTION]

        /// <summary>
        /// 创建容器实例。仅存储描述符，不创建服务实例——调用 <see cref="BuildAsync"/> 完成构建。
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
        /// 异步构建：拓扑排序 → 创建实例（构造注入）→ 注册 → OnInit → OnInitAsync。
        /// <para>按拓扑序执行：被依赖服务先于依赖方创建和初始化。重复构建抛出 <see cref="GameException"/>。</para>
        /// </summary>
        public async UniTask BuildAsync()
        {
            GameServices.EnsureMainThread();
            if (_scope.IsDisposed || _scope.ServiceCount > 0)
                throw new GameException(
                    StringUtility.Format("Container '{0}' has already been built.", _scopeKind));

            // 1. 拓扑排序（从构造函数参数推断依赖 + 显式声明的依赖）
            var sorted = TopologicalSort(_descriptors);

            // 2. 按拓扑序创建实例并注册（不调用 OnInit——所有实例就位后再统一初始化）
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

                // 多契约注册：主契约 + 额外契约共享同一实例
                var contracts = desc.AllContracts;
                _instances[desc.InterfaceType] = instance;
                for (int i = 1; i < contracts.Length; i++)
                    _instances[contracts[i]] = instance;

                _scope.Register(contracts, instance);
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
            if (desc.Factory != null)
                return desc.Factory(_serviceProvider);

            if (desc.ImplementationType == null)
                throw new GameException(
                    StringUtility.Format("Service '{0}' has no factory or implementation type.",
                        desc.InterfaceType.FullName));

            if (desc.IsMonoBehaviour)
                return CreateMonoBehaviourInstance(desc);

            return CreatePocoInstance(desc);
        }

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

        private IService CreateMonoBehaviourInstance(ServiceDescriptor desc)
        {
            var implType = desc.ImplementationType;
            var go = new GameObject(implType.Name);

            // App 作用域的 MonoBehaviour 需要跨场景存活
            if (_scopeKind == EServiceScopeKind.App)
                UnityEngine.Object.DontDestroyOnLoad(go);

            var component = (IService)go.AddComponent(implType);

            if (component is ServiceMonoBase mono)
                mono.Inject(_serviceProvider);

            return component;
        }

        /// <summary>
        /// 优先选择标记了 <see cref="ServiceConstructorAttribute"/> 的构造函数；
        /// 无标记则回退到参数最多的公共构造函数。
        /// </summary>
        private static ConstructorInfo SelectConstructor(Type type)
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < ctors.Length; i++)
            {
                if (ctors[i].IsDefined(typeof(ServiceConstructorAttribute), inherit: true))
                    return ctors[i];
            }

            int maxParams = -1;
            ConstructorInfo best = null;
            for (int i = 0; i < ctors.Length; i++)
            {
                int paramCount = ctors[i].GetParameters().Length;
                if (paramCount > maxParams)
                {
                    maxParams = paramCount;
                    best = ctors[i];
                }
            }
            return best;
        }

        /// <summary>
        /// 从容器链解析依赖。
        /// </summary>
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
        /// Kahn 算法拓扑排序。依赖来源：纯 C# 服务从构造函数参数推断，MonoBehaviour 服务从 ExplicitDependencies 读取。
        /// 循环依赖抛出 <see cref="GameException"/>。
        /// </summary>
        private static readonly List<Type> s_DepBuffer = new();

        private static List<ServiceDescriptor> TopologicalSort(List<ServiceDescriptor> descriptors)
        {
            var byInterface = new Dictionary<Type, ServiceDescriptor>();
            foreach (var d in descriptors)
            {
                byInterface[d.InterfaceType] = d;
                // 额外契约也映射到同一描述符，使依赖方可通过额外契约类型被拓扑排序识别
                if (d.AdditionalContracts != null)
                {
                    for (int i = 0; i < d.AdditionalContracts.Length; i++)
                        byInterface[d.AdditionalContracts[i]] = d;
                }
            }

            var inDegree = new Dictionary<Type, int>();
            var adjacency = new Dictionary<Type, List<Type>>();

            foreach (var desc in descriptors)
            {
                inDegree.TryAdd(desc.InterfaceType, 0);
                adjacency.TryAdd(desc.InterfaceType, new List<Type>());
            }

            foreach (var desc in descriptors)
            {
                s_DepBuffer.Clear();
                CollectDependencies(desc, s_DepBuffer);
                for (int i = 0; i < s_DepBuffer.Count; i++)
                {
                    var depType = s_DepBuffer[i];
                    // 仅关注本容器内注册的依赖（跨容器依赖由 parent 链在创建期解析）
                    if (!byInterface.ContainsKey(depType)) continue;

                    adjacency[depType].Add(desc.InterfaceType);
                    inDegree[desc.InterfaceType] =
                        inDegree.GetValueOrDefault(desc.InterfaceType, 0) + 1;
                }
            }

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
                var dependents = adjacency[type];
                for (int i = 0; i < dependents.Count; i++)
                {
                    inDegree[dependents[i]]--;
                    if (inDegree[dependents[i]] == 0) queue.Enqueue(dependents[i]);
                }
            }

            if (result.Count != descriptors.Count)
            {
                var processed = new HashSet<Type>();
                for (int i = 0; i < result.Count; i++)
                    processed.Add(result[i].InterfaceType);

                var remaining = new List<string>();
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (!processed.Contains(descriptors[i].InterfaceType))
                        remaining.Add(descriptors[i].InterfaceType.FullName);
                }

                throw new GameException(
                    StringUtility.Format("Circular dependency detected among: {0}",
                        string.Join(", ", remaining)));
            }

            return result;
        }

        /// <summary>
        /// 收集描述符的所有依赖类型，填充到 <paramref name="buffer"/>。
        /// </summary>
        private static void CollectDependencies(ServiceDescriptor desc, List<Type> buffer)
        {
            if (desc.ImplementationType != null && !desc.IsMonoBehaviour)
            {
                var ctor = SelectConstructor(desc.ImplementationType);
                if (ctor != null)
                {
                    var parameters = ctor.GetParameters();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (parameters[i].ParameterType != typeof(IServiceProvider))
                            buffer.Add(parameters[i].ParameterType);
                    }
                }
            }

            if (desc.ExplicitDependencies != null)
            {
                for (int i = 0; i < desc.ExplicitDependencies.Length; i++)
                    buffer.Add(desc.ExplicitDependencies[i]);
            }
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        /// <summary>
        /// 收集此容器（含父链）内已注册服务的诊断信息。
        /// </summary>
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
