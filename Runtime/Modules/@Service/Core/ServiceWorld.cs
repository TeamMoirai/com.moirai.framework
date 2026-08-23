using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 统一服务世界。管理 App/Scene/Gameplay 三个固定作用域的完整生命周期：
    /// 构建（拓扑排序 + 构造注入 + 初始化）、查找（<see cref="ContractBindings"/> O(1)）、轮询、销毁。
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    internal sealed class ServiceWorld : IDisposable, IServiceProvider
    {
        #region 常量 [CONSTANTS]

        private const int SCOPE_COUNT = 3;

        #endregion

        #region 字段 [FIELDS]

        // 3-slot 固定数组（索引 = (int)EServiceScopeKind）
        private readonly ServiceScope[] _scopes = new ServiceScope[SCOPE_COUNT];

        // 统一契约表：RuntimeTypeHandle → ContractBindings（值类型，零堆分配）
        private readonly Dictionary<RuntimeTypeHandle, ContractBindings> _servicesByContract = new();

        // 活跃作用域排序（按 Kind 升序：App → Scene → Gameplay）
        private readonly ServiceScope[] _activeScopes = new ServiceScope[SCOPE_COUNT];
        private int _activeScopeCount;
        private bool _scopesDirty;

        #endregion

        #region 作用域访问 [SCOPE ACCESS]

        internal bool HasScope(EServiceScopeKind kind)
            => _scopes[(int)kind] != null && !_scopes[(int)kind].IsDisposed;

        internal ServiceScope EnsureScope(EServiceScopeKind kind)
        {
            int index = (int)kind;
            if (_scopes[index] == null || _scopes[index].IsDisposed)
            {
                _scopes[index] = new ServiceScope(kind, kind.ToString(), this);
                _activeScopes[_activeScopeCount++] = _scopes[index];
                _scopesDirty = true;
            }

            return _scopes[index];
        }

        internal bool TryGetScope(EServiceScopeKind kind, out ServiceScope scope)
        {
            scope = _scopes[(int)kind];
            return scope != null && !scope.IsDisposed;
        }

        internal void ShutdownScope(EServiceScopeKind kind)
        {
            if (TryGetScope(kind, out var scope))
            {
                scope.Dispose();
                ClearScope(kind);
            }
        }

        private void ClearScope(EServiceScopeKind kind)
        {
            int index = (int)kind;
            _scopes[index] = null;

            for (int i = 0; i < _activeScopeCount; i++)
            {
                if (_activeScopes[i] != null && _activeScopes[i].Kind == kind)
                {
                    _activeScopes[i] = _activeScopes[--_activeScopeCount];
                    _activeScopes[_activeScopeCount] = null;
                    _scopesDirty = true;
                    break;
                }
            }
        }

        #endregion

        #region IServiceProvider 实现 [SERVICE PROVIDER]

        /// <summary>
        /// 获取服务（未找到抛 <see cref="GameException"/>）。
        /// </summary>
        public T GetRequiredService<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", typeof(T).FullName));
        }

        /// <summary>
        /// 获取服务（未找到返回 null）。
        /// </summary>
        public T GetService<T>() where T : class
            => TryGet<T>(out var service) ? service : null;

        /// <summary>
        /// 尝试获取服务。
        /// </summary>
        public bool TryGetService<T>(out T service) where T : class
            => TryGet<T>(out service);

        /// <summary>
        /// 在指定作用域中获取服务（未找到抛 <see cref="GameException"/>）。
        /// </summary>
        public T GetRequiredServiceInScope<T>(EServiceScopeKind scope) where T : class
        {
            if (TryGetScope(scope, out var targetScope) && targetScope.TryGet<T>(out var svc))
                return svc;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in {1} scope.", typeof(T).FullName, scope));
        }

        /// <summary>
        /// 在指定作用域中尝试获取服务。
        /// </summary>
        public bool TryGetServiceInScope<T>(EServiceScopeKind scope, out T service) where T : class
        {
            if (TryGetScope(scope, out var targetScope) && targetScope.TryGet<T>(out service))
                return true;
            service = null;
            return false;
        }

        /// <summary>
        /// 按运行时类型获取服务（未找到抛 <see cref="GameException"/>）。用于反射场景。
        /// </summary>
        public IService GetRequiredService(Type serviceType)
        {
            if (TryGet(serviceType, null, out IService service))
                return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", serviceType.FullName));
        }

        /// <summary>
        /// 按运行时类型获取服务（未找到返回 null）。用于反射场景。
        /// </summary>
        public IService GetService(Type serviceType)
            => TryGet(serviceType, null, out IService service) ? service : null;

        #endregion

        #region 统一契约查找 [UNIFIED CONTRACT LOOKUP]

        internal bool TryGet<T>(ServiceScope preferredScope, out T service) where T : class
        {
            // 快路径：先查 preferred scope 的本地字典
            if (preferredScope != null && !preferredScope.IsDisposed && preferredScope.TryGet<T>(out service))
                return true;

            // 跨作用域：ContractBindings.TryGetBest()
            if (_servicesByContract.TryGetValue(typeof(T).TypeHandle, out var bindings) &&
                bindings.TryGetBest(out var raw))
            {
                service = raw as T;
                return service != null;
            }

            service = null;
            return false;
        }

        internal bool TryGet<T>(out T service) where T : class
            => TryGet<T>(null, out service);

        internal bool TryGet(Type serviceType, ServiceScope preferredScope, out IService service)
        {
            if (serviceType == null)
            {
                service = null;
                return false;
            }

            // 快路径：先查 preferred scope
            if (preferredScope != null && !preferredScope.IsDisposed &&
                preferredScope.TryGet(serviceType, out service))
                return true;

            // 跨作用域
            if (_servicesByContract.TryGetValue(serviceType.TypeHandle, out var bindings) &&
                bindings.TryGetBest(out var raw))
            {
                service = raw;
                return service != null;
            }

            service = null;
            return false;
        }

        internal T Require<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", typeof(T).FullName));
        }

        #endregion

        #region 契约管理 [CONTRACT MANAGEMENT]

        internal void AddContract(ServiceScope scope, RuntimeTypeHandle handle, IService service)
        {
            if (!_servicesByContract.TryGetValue(handle, out var bindings))
            {
                bindings = default;
                _servicesByContract.Add(handle, bindings);
            }

            bindings.Set(scope.Kind, service);
            _servicesByContract[handle] = bindings;
        }

        internal void RemoveContract(ServiceScope scope, RuntimeTypeHandle handle, IService service)
        {
            if (!_servicesByContract.TryGetValue(handle, out var bindings)) return;

            bindings.Clear(scope.Kind, service);
            if (bindings.IsEmpty)
                _servicesByContract.Remove(handle);
            else
                _servicesByContract[handle] = bindings;
        }

        #endregion

        #region 构建 [BUILD]

        /// <summary>
        /// 异步构建指定作用域：拓扑排序 → 创建实例（构造注入）→ 注册 → OnInit → OnInitAsync。
        /// <para>若同作用域已有服务，先关闭再重建。</para>
        /// </summary>
        internal async UniTask BuildAsync(
            EServiceScopeKind scopeKind,
            IReadOnlyList<ServiceDescriptor> descriptors)
        {
            var scope = EnsureScope(scopeKind);
            if (scope.ServiceCount > 0)
                throw new GameException(
                    StringUtility.Format("Scope '{0}' has already been built.", scopeKind));

            if (descriptors == null || descriptors.Count == 0)
                return;

            var buildInstances = new Dictionary<Type, IService>();

            try
            {
                // 1. 拓扑排序
                var sorted = TopologicalSort(descriptors);

                // 2. 按拓扑序创建实例并注册
                foreach (var desc in sorted)
                {
                    IService instance;
                    try
                    {
                        instance = CreateInstance(desc, scope, buildInstances);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Failed to create service '{0}':\n{1}",
                            desc.InterfaceType.FullName, ex);
                        throw;
                    }

                    var contracts = desc.AllContracts;
                    buildInstances[desc.InterfaceType] = instance;
                    for (int i = 1; i < contracts.Length; i++)
                        buildInstances[contracts[i]] = instance;

                    scope.Register(contracts, instance);
                    GameServices.SetState(instance, EServiceState.Created);
                }

                // 3. 同步 OnInit（拓扑序 = 依赖序）
                foreach (var desc in sorted)
                {
                    if (!buildInstances.TryGetValue(desc.InterfaceType, out var instance)) continue;
                    try
                    {
                        instance.OnInit();
                        GameServices.SetState(instance, EServiceState.Initialized);
                        GameServices.InvokeRegistered(instance, desc.InterfaceType, scopeKind);
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
                    if (!buildInstances.TryGetValue(desc.InterfaceType, out var instance)) continue;
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
            finally
            {
                buildInstances = null;
            }
        }

        #endregion

        #region 实例创建 [INSTANCE CREATION]

        private IService CreateInstance(ServiceDescriptor desc, ServiceScope scope, Dictionary<Type, IService> buildInstances)
        {
            if (desc.Factory != null)
                return desc.Factory(this);

            if (desc.ImplementationType == null)
                throw new GameException(
                    StringUtility.Format("Service '{0}' has no factory or implementation type.",
                        desc.InterfaceType.FullName));

            if (desc.IsMonoBehaviour)
                return CreateMonoBehaviourInstance(desc, scope);

            return CreatePocoInstance(desc, scope, buildInstances);
        }

        private IService CreatePocoInstance(ServiceDescriptor desc, ServiceScope scope, Dictionary<Type, IService> buildInstances)
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

                if (paramType == typeof(IServiceProvider))
                {
                    args[i] = this;
                    continue;
                }

                if (!TryResolve(paramType, scope, buildInstances, out var arg))
                {
                    throw new GameException(
                        StringUtility.Format(
                            "Cannot resolve parameter '{0}' for service '{1}'. " +
                            "Ensure it is registered before this service.",
                            paramType.FullName, implType.FullName));
                }
                args[i] = arg;
            }

            var component = (IService)ctor.Invoke(args);

            if (component is ServiceBase sb)
                sb.InjectInternal(this);

            return component;
        }

        private IService CreateMonoBehaviourInstance(ServiceDescriptor desc, ServiceScope scope)
        {
            var implType = desc.ImplementationType;
            var go = new GameObject(implType.Name);

            if (scope.Kind == EServiceScopeKind.App)
                UnityEngine.Object.DontDestroyOnLoad(go);

            var component = (IService)go.AddComponent(implType);

            if (component is ServiceMonoBase mono)
                mono.InjectInternal(this);

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
        /// 构建期依赖解析：先查构建缓存，再查统一契约表。
        /// </summary>
        private bool TryResolve(Type type, ServiceScope scope, Dictionary<Type, IService> buildInstances, out object instance)
        {
            if (buildInstances != null && buildInstances.TryGetValue(type, out IService svc))
            {
                instance = svc;
                return true;
            }

            if (TryGet(type, scope, out IService worldSvc))
            {
                instance = worldSvc;
                return true;
            }

            instance = null;
            return false;
        }

        #endregion

        #region 拓扑排序 [TOPOLOGICAL SORT]

        private static readonly List<Type> s_DepBuffer = new();

        private static List<ServiceDescriptor> TopologicalSort(IReadOnlyList<ServiceDescriptor> descriptors)
        {
            var byInterface = new Dictionary<Type, ServiceDescriptor>();
            foreach (var d in descriptors)
            {
                byInterface[d.InterfaceType] = d;
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

        #region 轮询驱动 [TICK DRIVERS]

        internal void Tick(float elapseSeconds, float realElapseSeconds)
        {
            SortScopesIfDirty();
            for (int i = 0; i < _activeScopeCount; i++)
                _activeScopes[i].Tick(elapseSeconds, realElapseSeconds);
        }

        internal void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            SortScopesIfDirty();
            for (int i = 0; i < _activeScopeCount; i++)
                _activeScopes[i].FixedTick(elapseSeconds, realElapseSeconds);
        }

        internal void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            SortScopesIfDirty();
            for (int i = 0; i < _activeScopeCount; i++)
                _activeScopes[i].LateTick(elapseSeconds, realElapseSeconds);
        }

        internal void DrawGizmos()
        {
            SortScopesIfDirty();
            for (int i = 0; i < _activeScopeCount; i++)
                _activeScopes[i].DrawGizmos();
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        internal void CollectDiagnosticInfo(List<GameServices.DiagnosticInfo> buffer)
        {
            for (int i = 0; i < SCOPE_COUNT; i++)
                _scopes[i]?.CollectDiagnosticInfo(buffer);
        }

        #endregion

        #region 销毁 [DISPOSE]

        public void Dispose()
        {
            for (int i = SCOPE_COUNT - 1; i >= 0; i--)
            {
                _scopes[i]?.Dispose();
                _scopes[i] = null;
            }

            _activeScopeCount = 0;
            _servicesByContract.Clear();
            _scopesDirty = false;
        }

        #endregion

        #region 排序 [SORTING]

        private void SortScopesIfDirty()
        {
            if (!_scopesDirty) return;

            for (int i = 1; i < _activeScopeCount; i++)
            {
                var scope = _activeScopes[i];
                int j = i - 1;
                while (j >= 0 && _activeScopes[j].Kind > scope.Kind)
                {
                    _activeScopes[j + 1] = _activeScopes[j];
                    j--;
                }

                _activeScopes[j + 1] = scope;
            }

            _scopesDirty = false;
        }

        #endregion

        #region ContractBindings 值类型 [CONTRACT BINDINGS STRUCT]

        /// <summary>
        /// 契约绑定值类型。内联 App/Scene/Gameplay 三个绑定槽，
        /// <see cref="TryGetBest"/> 按 Gameplay > Scene > App 优先级返回最优服务。
        /// </summary>
        private struct ContractBindings
        {
            private ServiceBinding _app;
            private ServiceBinding _scene;
            private ServiceBinding _gameplay;

            public bool IsEmpty => !_app.HasValue && !_scene.HasValue && !_gameplay.HasValue;

            public void Set(EServiceScopeKind kind, IService service)
            {
                switch (kind)
                {
                    case EServiceScopeKind.App:
                        _app = new ServiceBinding(service);
                        break;
                    case EServiceScopeKind.Scene:
                        _scene = new ServiceBinding(service);
                        break;
                    case EServiceScopeKind.Gameplay:
                        _gameplay = new ServiceBinding(service);
                        break;
                }
            }

            public void Clear(EServiceScopeKind kind, IService service)
            {
                switch (kind)
                {
                    case EServiceScopeKind.App:
                        if (_app.HasValue && ReferenceEquals(_app.Service, service))
                            _app = default;
                        break;
                    case EServiceScopeKind.Scene:
                        if (_scene.HasValue && ReferenceEquals(_scene.Service, service))
                            _scene = default;
                        break;
                    case EServiceScopeKind.Gameplay:
                        if (_gameplay.HasValue && ReferenceEquals(_gameplay.Service, service))
                            _gameplay = default;
                        break;
                }
            }

            public bool TryGetBest(out IService service)
            {
                if (_gameplay.HasValue)
                {
                    service = _gameplay.Service;
                    return true;
                }

                if (_scene.HasValue)
                {
                    service = _scene.Service;
                    return true;
                }

                if (_app.HasValue)
                {
                    service = _app.Service;
                    return true;
                }

                service = null;
                return false;
            }
        }

        private struct ServiceBinding
        {
            public IService Service;
            public bool HasValue;

            public ServiceBinding(IService service)
            {
                Service = service;
                HasValue = true;
            }
        }

        #endregion
    }
}
