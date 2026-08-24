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

        /// <summary>
        /// 异步关闭指定作用域。对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭。
        /// </summary>
        internal async UniTask ShutdownScopeAsync(EServiceScopeKind kind)
        {
            if (TryGetScope(kind, out var scope))
            {
                await scope.DisposeAsync();
                if (!scope.IsDisposed)
                {
                    // 迭代中延迟销毁：手动完成
                    scope.Dispose();
                }
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
        /// 异步构建指定作用域：拓扑排序（含契约查重）→ 创建实例（构造注入）→ 注册 → OnInit → OnInitAsync。
        /// <para>若同作用域已有服务，先关闭再重建。</para>
        /// <para>构建失败时整体回滚：作用域恢复到未构建状态，调用方可安全重试。</para>
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
                // 1. 拓扑排序（重复契约在实例创建前 fail-fast，从源头消除孤儿）
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
                            desc.ContractType.FullName, ex);
                        throw;
                    }

                    var contracts = desc.AllContracts;
                    buildInstances[desc.ContractType] = instance;
                    for (int i = 1; i < contracts.Length; i++)
                        buildInstances[contracts[i]] = instance;

                    scope.Register(contracts, instance);
                    GameServices.SetState(instance, EServiceState.Created);
                }

                // 3. 同步 OnInit（拓扑序 = 依赖序）
                foreach (var desc in sorted)
                {
                    if (!buildInstances.TryGetValue(desc.ContractType, out var instance)) continue;
                    try
                    {
                        instance.OnInit();
                        GameServices.SetState(instance, EServiceState.Initialized);
                        GameServices.InvokeRegistered(instance, desc.ContractType, scopeKind);
                    }
                    catch (Exception ex)
                    {
                        LogUtility.Error("Service '{0}' OnInit failed:\n{1}",
                            desc.ContractType.FullName, ex);
                        throw;
                    }
                }

                // 4. 异步 OnInitAsync（拓扑序）
                foreach (var desc in sorted)
                {
                    if (!buildInstances.TryGetValue(desc.ContractType, out var instance)) continue;
                    if (instance is IAsyncInitService asyncSvc)
                    {
                        try
                        {
                            await asyncSvc.OnInitAsync();
                        }
                        catch (Exception ex)
                        {
                            LogUtility.Error("Service '{0}' OnInitAsync failed:\n{1}",
                                desc.ContractType.FullName, ex);
                            throw;
                        }
                    }
                }
            }
            catch
            {
                // 构建失败：回滚到未构建状态——已注册服务逆拓扑序 Shutdown，
                // "已创建未注册"的孤儿实例销毁（否则影子服务 / GameObject 泄漏）
                RollbackBuild(scope, buildInstances);
                throw;
            }
        }

        /// <summary>
        /// 构建失败的回滚：销毁作用域并从世界移除，清理孤儿实例。
        /// <para>孤儿 = 创建成功但 Register 被拒（如 Mono 服务实现了轮询接口）的实例——
        /// 它们永不 OnInit，须销毁 GameObject / 释放 IDisposable，而非调用 Shutdown。</para>
        /// <para>await 挂起期间作用域可能已被外部关闭：此时全部实例均已注册并随外部关闭处理，跳过孤儿检测。</para>
        /// </summary>
        private void RollbackBuild(ServiceScope scope, Dictionary<Type, IService> buildInstances)
        {
            try
            {
                List<IService> orphans = null;
                if (buildInstances != null && buildInstances.Count > 0 && !scope.IsDisposed)
                {
                    foreach (var instance in buildInstances.Values)
                    {
                        if (scope.Contains(instance)) continue;
                        orphans ??= new List<IService>();
                        if (!orphans.Contains(instance)) orphans.Add(instance);
                    }
                }

                if (!scope.IsDisposed)
                {
                    scope.Dispose();
                    ClearScope(scope.Kind);
                }

                if (orphans != null)
                {
                    for (int i = 0; i < orphans.Count; i++)
                        DestroyOrphan(orphans[i]);
                }
            }
            catch (Exception ex)
            {
                // 回滚自身的失败不得吞掉原始构建异常
                LogUtility.Error("Rollback of scope '{0}' failed:\n{1}", scope.Kind, ex);
            }
        }

        private static void DestroyOrphan(IService instance)
        {
            GameServices.SetState(instance, EServiceState.Disposed);

            if (instance is MonoBehaviour mb)
            {
                // 孤儿 Mono 服务由容器创建、无人接管：销毁 GameObject 防止场景残留
                if (mb != null)
                {
                    if (Application.isPlaying) UnityEngine.Object.Destroy(mb.gameObject);
                    else UnityEngine.Object.DestroyImmediate(mb.gameObject);
                }
                return;
            }

            if (instance is IDisposable disposable)
                disposable.Dispose();
        }

        #endregion

        #region 实例创建 [INSTANCE CREATION]

        private IService CreateInstance(ServiceDescriptor desc, ServiceScope scope, Dictionary<Type, IService> buildInstances)
        {
            if (desc.Factory != null)
            {
                var instance = desc.Factory(this);
                // 运行时兜底校验（编译期已由 Func<IServiceProvider, TInterface> 约束，
                // 覆盖非泛型注册与委托协变漏洞）：错误工厂立即失败，而非解析时静默返回 null
                if (instance == null)
                    throw new GameException(StringUtility.Format(
                        "Factory for service '{0}' returned null.", desc.ContractType.FullName));
                if (!desc.ContractType.IsInstanceOfType(instance))
                    throw new GameException(StringUtility.Format(
                        "Factory for service '{0}' returned '{1}', which does not implement the contract.",
                        desc.ContractType.FullName, instance.GetType().FullName));
                return instance;
            }

            if (desc.ImplementationType == null)
                throw new GameException(
                    StringUtility.Format("Service '{0}' has no factory or implementation type.",
                        desc.ContractType.FullName));

            if (desc.IsMonoBehaviour)
                return CreateMonoBehaviourInstance(desc, scope);

            return CreatePocoInstance(desc, scope, buildInstances);
        }

        private IService CreatePocoInstance(ServiceDescriptor desc, ServiceScope scope, Dictionary<Type, IService> buildInstances)
        {
            var implType = desc.ImplementationType;
            // 构造函数缓存：拓扑排序阶段首次解析后复用，避免每个服务两次反射扫描
            var ctor = desc.ResolvedConstructor ??= SelectConstructor(implType);

            if (ctor == null)
                throw new GameException(
                    StringUtility.Format("Service '{0}' has no public constructor.", implType.FullName));

            // 参数缓存：与构造函数同步填充，避免每次 GetParameters() 产生新数组
            var parameters = desc.ResolvedParameters ??= ctor.GetParameters();

            // 零参数快路径：跳过参数解析数组分配与循环
            if (parameters.Length == 0)
            {
                var instance = (IService)ctor.Invoke(null);
                if (instance is ServiceBase baseSvc)
                    baseSvc.InjectInternal(this);
                return instance;
            }

            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;

                if (paramType == typeof(IServiceProvider))
                {
                    args[i] = this;
                    continue;
                }

                // IServiceResolver<T> 延迟解析注入（AOT 优选路径）：
                // 使用 MakeGenericType + Activator.CreateInstance 而非 MakeGenericMethod + Delegate.CreateDelegate。
                // IL2CPP 全量泛型共享下，引用类型的泛型类型构造由运行时缓存，无动态方法绑定风险。
                // 语义与 Func<T> 一致：延迟解析目标服务，拓扑建边保证委托调用时目标已就绪。
                if (paramType.IsGenericType &&
                    paramType.GetGenericTypeDefinition() == typeof(IServiceResolver<>))
                {
                    var targetType = paramType.GetGenericArguments()[0];
                    args[i] = Activator.CreateInstance(
                        typeof(ServiceResolver<>).MakeGenericType(targetType), this);
                    continue;
                }

                // Func<T> 延迟解析注入（向后兼容保留）：依赖方持有委托，首次调用时才向容器解析 T。
                // 委托目标服务仍是构建期初始化的 singleton（拓扑建边保证就绪时序），
                // 延迟的只是"解析"这一步——用于打破服务间的强引用启动耦合。
                // 新代码应优先使用 IServiceResolver<T> 以获得更好的 AOT 兼容性。
                if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Func<>))
                {
                    args[i] = CreateLazyResolver(paramType);
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

        private static readonly MethodInfo s_ResolveDependencyMethod =
            typeof(ServiceWorld).GetMethod(nameof(ResolveDependency),
                BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// 为 Func&lt;T&gt; 构造参数创建延迟解析委托。仅捕获 this、无闭包状态，构建期一次性分配。
        /// <para>IL2CPP 说明：T 受 class 约束（引用类型走泛型代码共享），MakeGenericMethod 在
        /// 全量泛型共享下可用；值类型参数在此前已被拒绝。</para>
        /// </summary>
        private object CreateLazyResolver(Type funcType)
        {
            var targetType = funcType.GetGenericArguments()[0];
            if (targetType.IsValueType)
                throw new GameException(StringUtility.Format(
                    "Func<{0}> constructor injection requires a reference type; value types are not supported.",
                    targetType.FullName));

            return Delegate.CreateDelegate(
                funcType, this, s_ResolveDependencyMethod.MakeGenericMethod(targetType));
        }

        /// <summary>Func&lt;T&gt; 委托的解析目标。目标未注册或已随作用域关闭时抛出异常（fail-fast）。</summary>
        private T ResolveDependency<T>() where T : class
        {
            if (TryGet<T>(out var service)) return service;
            throw new GameException(StringUtility.Format(
                "Delayed resolution of '{0}' failed: service not found in any active scope " +
                "(it may have been shut down).", typeof(T).FullName));
        }

        #region 拓扑排序 [TOPOLOGICAL SORT]

        private static readonly List<Type> s_DepBuffer = new();

        // ── 复用缓冲：主线程契约保证无并发访问；Clear() 复用字典/队列内部桶存储，
        //    消除每次构建的容器分配。result 必须每次新建——BuildAsync 在 await 挂起期间
        //    持有 sorted 迭代，共享缓冲会被并发构建损坏。
        private static readonly Dictionary<Type, ServiceDescriptor> s_ByContractBuffer = new();
        private static readonly Dictionary<Type, int> s_InDegreeBuffer = new();
        private static readonly Dictionary<Type, List<Type>> s_AdjacencyBuffer = new();
        private static readonly Queue<Type> s_TopologyQueueBuffer = new();

        private static List<ServiceDescriptor> TopologicalSort(IReadOnlyList<ServiceDescriptor> descriptors)
        {
            // 1. 契约表（含查重）：重复契约（主-主 / 主-As / As-As 重叠）在实例创建前
            //    fail-fast——旧实现静默覆盖，会导致同一描述符入结果两次、另一服务被顶替，
            //    最终以误导性的"循环依赖"或迟到的 Register 拒绝收场
            s_ByContractBuffer.Clear();
            foreach (var d in descriptors)
            {
                TryMapContract(d.ContractType, d);
                if (d.AdditionalContracts != null)
                {
                    for (int i = 0; i < d.AdditionalContracts.Length; i++)
                        TryMapContract(d.AdditionalContracts[i], d);
                }
            }

            // 2. 入度表与邻接表（仅主契约作为节点）
            s_InDegreeBuffer.Clear();
            s_AdjacencyBuffer.Clear();
            foreach (var desc in descriptors)
            {
                s_InDegreeBuffer.TryAdd(desc.ContractType, 0);
                s_AdjacencyBuffer.TryAdd(desc.ContractType, new List<Type>());
            }

            // 3. 依赖建边：依赖类型可能是额外契约（As 注册）——映射回属主的主契约建边，
            //    保证依赖额外契约的服务排在属主之后
            foreach (var desc in descriptors)
            {
                s_DepBuffer.Clear();
                CollectDependencies(desc, s_DepBuffer);
                for (int i = 0; i < s_DepBuffer.Count; i++)
                {
                    var depType = s_DepBuffer[i];
                    if (!s_ByContractBuffer.TryGetValue(depType, out var depDesc)) continue;
                    if (depDesc.ContractType == desc.ContractType) continue; // 自依赖跳过

                    s_AdjacencyBuffer[depDesc.ContractType].Add(desc.ContractType);
                    s_InDegreeBuffer[desc.ContractType] =
                        s_InDegreeBuffer.GetValueOrDefault(desc.ContractType, 0) + 1;
                }
            }

            // 4. Kahn 队列
            s_TopologyQueueBuffer.Clear();
            foreach (var kvp in s_InDegreeBuffer)
            {
                if (kvp.Value == 0) s_TopologyQueueBuffer.Enqueue(kvp.Key);
            }

            var result = new List<ServiceDescriptor>(descriptors.Count);
            while (s_TopologyQueueBuffer.Count > 0)
            {
                var type = s_TopologyQueueBuffer.Dequeue();
                result.Add(s_ByContractBuffer[type]);
                var dependents = s_AdjacencyBuffer[type];
                for (int i = 0; i < dependents.Count; i++)
                {
                    s_InDegreeBuffer[dependents[i]]--;
                    if (s_InDegreeBuffer[dependents[i]] == 0)
                        s_TopologyQueueBuffer.Enqueue(dependents[i]);
                }
            }

            if (result.Count != descriptors.Count)
            {
                var processed = new HashSet<Type>();
                for (int i = 0; i < result.Count; i++)
                    processed.Add(result[i].ContractType);

                var remaining = new List<string>();
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (!processed.Contains(descriptors[i].ContractType))
                        remaining.Add(descriptors[i].ContractType.FullName);
                }

                throw new GameException(
                    StringUtility.Format("Circular dependency detected among: {0}",
                        string.Join(", ", remaining)));
            }

            return result;
        }

        /// <summary>契约映射（带查重）：契约已被其他描述符占用时抛出 <see cref="GameException"/>。</summary>
        private static void TryMapContract(Type contract, ServiceDescriptor descriptor)
        {
            if (s_ByContractBuffer.TryAdd(contract, descriptor)) return;

            var existing = s_ByContractBuffer[contract];
            throw new GameException(StringUtility.Format(
                "Contract '{0}' is registered by both '{1}' and '{2}'.",
                contract.FullName, DescribeService(existing), DescribeService(descriptor)));
        }

        private static string DescribeService(ServiceDescriptor desc)
            => desc.ImplementationType?.FullName ?? "<factory>";

        private static void CollectDependencies(ServiceDescriptor desc, List<Type> buffer)
        {
            if (desc.ImplementationType != null && !desc.IsMonoBehaviour)
            {
                // 复用描述符上缓存的构造函数（与实例创建共享一次解析）
                var ctor = desc.ResolvedConstructor ??= SelectConstructor(desc.ImplementationType);
                if (ctor != null)
                {
                    var parameters = desc.ResolvedParameters ??= ctor.GetParameters();
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var paramType = parameters[i].ParameterType;
                        if (paramType == typeof(IServiceProvider)) continue;

                        // IServiceResolver<T> 延迟解析：T 参与拓扑建边，
                        // 保证解析器调用时目标服务已创建并初始化
                        if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(IServiceResolver<>))
                            buffer.Add(paramType.GetGenericArguments()[0]);

                        // Func<T> 延迟解析：T 同样参与拓扑建边（若已注册），
                        // 保证委托运行期首次调用时目标服务已创建并初始化
                        else if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Func<>))
                            buffer.Add(paramType.GetGenericArguments()[0]);
                        else
                            buffer.Add(paramType);
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
                while (j >= 0 && _activeScopes[j].Order > scope.Order)
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

    /// <summary>
    /// <see cref="IServiceResolver{T}"/> 的内部实现。持有 <see cref="ServiceWorld"/> 引用，
    /// 调用 <see cref="Resolve"/> 时向容器查找目标服务。
    /// <para>构建期由容器通过 <c>Activator.CreateInstance</c> 创建（引用类型泛型构造，
    /// IL2CPP 全量泛型共享安全），无 <c>MakeGenericMethod</c> + <c>Delegate.CreateDelegate</c> 路径。</para>
    /// </summary>
    internal sealed class ServiceResolver<T> : IServiceResolver<T> where T : class
    {
        private readonly ServiceWorld _world;

        public ServiceResolver(ServiceWorld world)
        {
            _world = world;
        }

        public T Resolve()
        {
            if (_world.TryGet<T>(out var service)) return service;
            throw new GameException(StringUtility.Format(
                "Delayed resolution of '{0}' failed: service not found in any active scope " +
                "(it may have been shut down).", typeof(T).FullName));
        }
    }
}
