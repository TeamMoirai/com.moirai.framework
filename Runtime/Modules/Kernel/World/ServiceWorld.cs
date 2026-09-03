using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 统一服务世界（可实例化容器）。管理 App/Scene/Gameplay 三个固定作用域的完整生命周期：
    /// 注册（两阶段：Register 仅入图）→ 初始化（<see cref="Initialize"/> 拓扑排序统一驱动 OnInit）→
    /// 查找（跨作用域 3 槽内联，Gameplay &gt; Scene &gt; App）→ 轮询（固定序扁平直驱）→ 销毁（严格逆拓扑）。
    /// <para><b>可实例化</b>：<c>new ServiceWorld()</c> 构造隔离世界（测试并行/沙盒场景）；
    /// 进程默认世界经 <see cref="GameServices"/> 静态投影访问。</para>
    /// <para><b>线程契约</b>：所有方法仅限 Unity 主线程调用。</para>
    /// </summary>
    public sealed class ServiceWorld : IDisposable
    {
        #region 常量 [CONSTANTS]

        private static readonly int s_ScopeCount = Enum.GetNames(typeof(EServiceScopeKind)).Length;

        #endregion

        #region 字段 [FIELDS]

        // 3-slot 固定数组（索引 = (int)EServiceScopeKind；固定序 App→Scene→Gameplay，零排序）
        private readonly ServiceScope[] _scopes = new ServiceScope[s_ScopeCount];

        // 跨作用域统一契约解析表：RuntimeTypeHandle → CrossScopeBindings（值类型 3 槽内联）
        // 与 ServiceScope._servicesByContract 的关系：本表是跨作用域视图，作用域表是同作用域 O(1) 视图——
        // 由容器统一维护（AddBinding/RemoveBinding），调用方不可见第二写入点。
        private readonly Dictionary<RuntimeTypeHandle, CrossScopeBindings> _bindingsByContract = new();

        // 待初始化服务（两阶段：Register 入图后挂起，Initialize 拓扑排序统一驱动）
        private readonly List<IService> _pendingInit = new List<IService>();

        // 待初始化契约 → 服务（含多契约绑定；Initialize 时据此构建拓扑索引）
        private readonly Dictionary<Type, IService> _pendingContracts = new Dictionary<Type, IService>();

        // 拦截器（实例级——隔离世界互不干扰）
        private readonly List<IServiceInterceptor> _interceptors = new List<IServiceInterceptor>();

        // 依赖元数据缓存（特性仅读取一次；主线程专用）
        private readonly Dictionary<Type, Type[]> _dependencyCache = new Dictionary<Type, Type[]>();

        private bool _initialized;
        private bool _initializing;
        private bool _disposed;

        #endregion

        #region 属性 [PROPERTIES]

        /// <summary>
        /// 世界是否已完成初始化（两阶段的第二阶段已提交）。
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// 世界是否正在初始化中（重入守卫——初始化循环内触发的新注册由外层循环接管）。
        /// </summary>
        internal bool IsInitializing => _initializing;

        /// <summary>
        /// 当前已注册的拦截器（只读视图）。
        /// </summary>
        public IReadOnlyList<IServiceInterceptor> Interceptors => _interceptors;

        /// <summary>
        /// 是否存在已注册的拦截器。轮询帧边界据此跳过通知。
        /// </summary>
        internal bool HasInterceptors => _interceptors.Count > 0;

        // 重复契约处置策略（默认与旧版一致：开发期 Warn，发布期 Skip）
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private EDuplicateContractPolicy _duplicateContractPolicy = EDuplicateContractPolicy.Warn;
#else
        private EDuplicateContractPolicy _duplicateContractPolicy = EDuplicateContractPolicy.Skip;
#endif

        /// <summary>
        /// 重复契约注册处置策略。仅作用于"同作用域内已占用契约再次显式注册不同实例"的场景。
        /// </summary>
        public EDuplicateContractPolicy DuplicateContractPolicy
        {
            get => _duplicateContractPolicy;
            set => _duplicateContractPolicy = value;
        }

        #endregion

        #region 作用域访问 [SCOPE ACCESS]

        /// <summary>
        /// App 作用域是否活跃。
        /// </summary>
        public bool HasApp => HasScope(EServiceScopeKind.App);

        /// <summary>
        /// Scene 作用域是否活跃。
        /// </summary>
        public bool HasScene => HasScope(EServiceScopeKind.Scene);

        /// <summary>
        /// Gameplay 作用域是否活跃。
        /// </summary>
        public bool HasGameplay => HasScope(EServiceScopeKind.Gameplay);

        internal bool HasScope(EServiceScopeKind kind)
            => _scopes[(int)kind] != null && !_scopes[(int)kind].IsDisposed;

        internal ServiceScope EnsureScope(EServiceScopeKind kind)
        {
            int index = (int)kind;
            if (_scopes[index] == null || _scopes[index].IsDisposed)
                _scopes[index] = new ServiceScope(kind, kind.ToString(), this);

            return _scopes[index];
        }

        internal bool TryGetScope(EServiceScopeKind kind, out ServiceScope scope)
        {
            scope = _scopes[(int)kind];
            return scope != null && !scope.IsDisposed;
        }

        #endregion

        #region 拦截器 [INTERCEPTORS]

        /// <summary>
        /// 添加服务拦截器。按 <see cref="IServiceInterceptor.Priority"/> 降序插入。
        /// </summary>
        public void AddInterceptor(IServiceInterceptor interceptor)
        {
            if (interceptor == null) return;

            int priority = interceptor.Priority;
            int insertAt = _interceptors.Count;
            for (int i = 0; i < _interceptors.Count; i++)
            {
                if (priority > _interceptors[i].Priority) { insertAt = i; break; }
            }
            _interceptors.Insert(insertAt, interceptor);
        }

        /// <summary>
        /// 移除服务拦截器。
        /// </summary>
        public void RemoveInterceptor(IServiceInterceptor interceptor)
        {
            _interceptors.Remove(interceptor);
        }

        internal void InvokeRegistering(IService service, Type contractType, EServiceScopeKind scope)
        {
            if (_interceptors.Count == 0) return;
            for (int i = 0; i < _interceptors.Count; i++)
                _interceptors[i].OnServiceRegistering(service, contractType, scope);
        }

        internal void InvokeRegistered(IService service, Type contractType, EServiceScopeKind scope)
        {
            for (int i = 0; i < _interceptors.Count; i++)
                _interceptors[i].OnServiceRegistered(service, contractType, scope);
        }

        internal void InvokeUnregistered(IService service)
        {
            for (int i = 0; i < _interceptors.Count; i++)
                _interceptors[i].OnServiceUnregistered(service);
        }

        internal void InvokeShutdown(IService service)
        {
            if (_interceptors.Count == 0) return;
            for (int i = 0; i < _interceptors.Count; i++)
                _interceptors[i].OnServiceShutdown(service);
        }

        #endregion

        #region 注册 [REGISTRATION]

        /// <summary>
        /// 注册服务到指定作用域（两阶段第一阶段：仅入图，不初始化）。
        /// <para>世界未初始化时：服务挂入待初始化图，由 <see cref="Initialize"/> 按依赖拓扑统一驱动 OnInit。</para>
        /// <para>世界已初始化时：依赖必须已就绪（缺失即抛 <see cref="GameException"/>），服务立即 OnInit——
        /// 实现 <see cref="IServiceInitializableAsync"/> 的服务禁止运行时注册（无法等待，fail-fast）。</para>
        /// <para>迭代中（Tick）调用时默认延迟到本轮迭代结束后执行（<see cref="EDeferMode.Defer"/>）。</para>
        /// </summary>
        /// <typeparam name="T">服务具体类型（契约即类型本身）。</typeparam>
        /// <param name="scope">目标作用域。</param>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例（同实例重复注册幂等返回既有实例）。</returns>
        public T Register<T>(
            EServiceScopeKind scope,
            T service,
            EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            return (T)Register(scope, typeof(T), service, deferMode);
        }

        /// <summary>
        /// 以显式契约类型注册服务实例（运行时 Type 版本）。
        /// <para>同一实例可依次以多个契约注册（多契约绑定）——首个调用创建条目，后续调用仅附加契约句柄。</para>
        /// <para>依赖声明始终从 <c>service.GetType()</c> 实现类型读取。</para>
        /// </summary>
        /// <param name="scope">目标作用域。</param>
        /// <param name="contractType">契约类型（注册键与解析键）。</param>
        /// <param name="service">要注册的服务实例。</param>
        /// <param name="deferMode">迭代中调用的延迟策略。</param>
        /// <returns>注册的服务实例。</returns>
        public IService Register(
            EServiceScopeKind scope,
            Type contractType,
            IService service,
            EDeferMode deferMode = EDeferMode.Defer)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (_disposed)
                throw new GameException("ServiceWorld has been disposed; registration is rejected.");

            ServiceScope targetScope = EnsureScope(scope);

            // 去重：契约已注册时按实例比对处置——不同实例抢占按策略，同实例静默幂等
            if (targetScope.TryGet(contractType, out IService existing))
            {
                if (!ReferenceEquals(existing, service))
                    ApplyDuplicateContractPolicy(scope, contractType, existing);
                return existing;
            }

            // 同实例已在本作用域以其他契约注册——仅附加新契约绑定
            if (targetScope.Contains(service))
            {
                targetScope.BindAdditionalContractRuntime(contractType, service, deferMode);
                return service;
            }

            if (_initialized)
            {
                // 运行时注册：依赖必须已就绪（拓扑位置即末尾追加），立即初始化
                ValidateDependenciesReady(contractType, service.GetType());

                if (service is IServiceInitializableAsync)
                {
                    throw new GameException(StringUtility.Format(
                        "Service '{0}' implements IServiceInitializableAsync and cannot be registered after world initialization (async init cannot be awaited). Register it before Initialize.",
                        service.GetType().FullName));
                }

                targetScope.RegisterRuntime(contractType, service, deferMode);
                return service;
            }

            // 两阶段第一阶段：仅入图——依赖校验推迟到 Initialize（拓扑排序时统一 fail-fast）
            // 先入作用域注册表（可能抛），成功后再挂起跟踪——避免半注册状态
            targetScope.RegisterDeferred(contractType, service, deferMode);
            TrackPending(contractType, service);
            return service;
        }

        /// <summary>
        /// 运行时注销并关闭指定作用域中的单个服务。
        /// <para>触发 <c>OnShutdown</c> 并从注册表移除；注销后可重新以同契约注册全新实例。</para>
        /// </summary>
        public bool Unregister(
            EServiceScopeKind scope,
            Type contractType,
            EDeferMode deferMode = EDeferMode.Defer)
        {
            if (contractType == null) throw new ArgumentNullException(nameof(contractType));
            if (!TryGetScope(scope, out var targetScope)) return false;

            // 待初始化服务被注销：从挂起图移除（不驱动任何生命周期）
            if (!_initialized && UntrackPending(contractType))
                return targetScope.UnregisterDeferred(contractType, deferMode);

            return targetScope.UnregisterRuntime(contractType, deferMode);
        }

        /// <summary>
        /// 运行时注销并关闭指定作用域中的单个服务（泛型版本）。
        /// </summary>
        public bool Unregister<T>(
            EServiceScopeKind scope,
            EDeferMode deferMode = EDeferMode.Defer) where T : class, IService
        {
            return Unregister(scope, typeof(T), deferMode);
        }

        private void ApplyDuplicateContractPolicy(
            EServiceScopeKind scope,
            Type contractType,
            IService existing)
        {
            switch (_duplicateContractPolicy)
            {
                case EDuplicateContractPolicy.Throw:
                    throw new GameException(StringUtility.Format(
                        "Duplicate contract registration rejected: contract '{0}' is already bound to '{1}' in {2} scope.",
                        contractType.FullName, existing.GetType().FullName, scope));

                case EDuplicateContractPolicy.Warn:
                    LogUtility.Warning(
                        "Duplicate contract registration discarded: contract '{0}' is already bound to '{1}' in {2} scope; the new instance will be ignored.",
                        contractType.FullName, existing.GetType().FullName, scope);
                    break;

                default:
                    break;
            }
        }

        /// <summary>
        /// 运行时注册路径的依赖校验：全部声明依赖必须已初始化就绪（fail-fast）。
        /// </summary>
        private void ValidateDependenciesReady(Type contractType, Type implType)
        {
            Type[] dependencies = GetDeclaredDependencies(implType);
            for (int i = 0; i < dependencies.Length; i++)
            {
                if (!IsServiceReady(dependencies[i]))
                {
                    throw new GameException(StringUtility.Format(
                        "Dependency '{0}' required by '{1}' is not initialized. Runtime registration requires all dependencies ready.",
                        dependencies[i].FullName, implType.FullName));
                }
            }
        }

        /// <summary>
        /// 契约是否已初始化就绪（跨作用域解析）。
        /// </summary>
        internal bool IsServiceReady(Type contractType)
        {
            if (!_bindingsByContract.TryGetValue(contractType.TypeHandle, out var bindings))
                return false;
            return bindings.TryGetBest(out IService service) && GameServices.GetState(service) >= EServiceState.Initialized;
        }

        /// <summary>
        /// 读取类型的 <see cref="ServiceDependencyAttribute"/> 声明（带实例级缓存）。
        /// </summary>
        internal Type[] GetDeclaredDependencies(Type serviceType)
        {
            if (_dependencyCache.TryGetValue(serviceType, out Type[] cached))
                return cached;

            object[] attrs = serviceType.GetCustomAttributes(typeof(ServiceDependencyAttribute), false);
            if (attrs.Length == 0)
            {
                _dependencyCache[serviceType] = Array.Empty<Type>();
                return Array.Empty<Type>();
            }

            int total = 0;
            for (int i = 0; i < attrs.Length; i++)
                total += ((ServiceDependencyAttribute)attrs[i]).DependencyTypes.Length;

            var deps = new Type[total];
            int offset = 0;
            for (int i = 0; i < attrs.Length; i++)
            {
                Type[] types = ((ServiceDependencyAttribute)attrs[i]).DependencyTypes;
                Array.Copy(types, 0, deps, offset, types.Length);
                offset += types.Length;
            }

            _dependencyCache[serviceType] = deps;
            return deps;
        }

        #endregion

        #region 待初始化跟踪 [PENDING INIT TRACKING]

        // _pendingInit：注册顺序的唯一服务列表（Kahn 稳定序的依据）
        // _pendingContracts：全部待初始化契约 → 服务（含多契约绑定；Initialize 时构建拓扑索引）

        private void TrackPending(Type contractType, IService service)
        {
            _pendingContracts[contractType] = service;
            for (int i = 0; i < _pendingInit.Count; i++)
            {
                if (ReferenceEquals(_pendingInit[i], service)) return;
            }
            _pendingInit.Add(service);
        }

        /// <summary>
        /// 附加契约挂到已挂起服务（多契约绑定路径）。
        /// </summary>
        internal void TrackPendingContract(Type contractType, IService service)
        {
            _pendingContracts[contractType] = service;
        }

        /// <summary>
        /// 从挂起图移除指定契约对应的服务。返回该服务是否处于待初始化状态。
        /// </summary>
        private bool UntrackPending(Type contractType)
        {
            if (!_pendingContracts.Remove(contractType, out IService service)) return false;

            // 该服务还有其他挂起契约时保留在 _pendingInit（以剩余契约为拓扑节点）
            foreach (var pair in _pendingContracts)
            {
                if (ReferenceEquals(pair.Value, service)) return true;
            }

            for (int i = 0; i < _pendingInit.Count; i++)
            {
                if (ReferenceEquals(_pendingInit[i], service))
                {
                    _pendingInit.RemoveAt(i);
                    break;
                }
            }
            return true;
        }

        #endregion

        #region 初始化 [INITIALIZATION]

        /// <summary>
        /// 初始化世界（两阶段第二阶段）：按依赖图拓扑排序统一驱动全部挂起服务的 <c>OnInit</c>。
        /// <para>顺序契约：作用域固定 App → Scene → Gameplay 逐段处理；段内按 <c>[ServiceDependency]</c> 拓扑序——
        /// 初始化顺序完全由声明决定，与注册顺序无关。缺失依赖与循环依赖在此 fail-fast。</para>
        /// <para>挂起服务中含 <see cref="IServiceInitializableAsync"/> 实现时抛 <see cref="GameException"/>——
        /// 改用 <see cref="InitializeAsync"/>。</para>
        /// </summary>
        public void Initialize()
        {
            if (_initialized || _disposed || _initializing) return;

            SortPending();

            for (int i = 0; i < _pendingInit.Count; i++)
            {
                var service = _pendingInit[i];
                if (service is IServiceInitializableAsync)
                {
                    throw new GameException(StringUtility.Format(
                        "Service '{0}' implements IServiceInitializableAsync. Use InitializeAsync instead of Initialize.",
                        service.GetType().FullName));
                }
            }

            // 重入守卫：OnInit 内的新注册追加到 _pendingInit 尾部，由本循环按索引持续推进接管
            _initializing = true;
            try
            {
                for (int i = 0; i < _pendingInit.Count; i++)
                    InitializeService(_pendingInit[i]);
            }
            finally
            {
                _initializing = false;
            }

            CompleteInitialization();
        }

        /// <summary>
        /// 异步初始化世界：拓扑序位置处逐个等待 <see cref="IServiceInitializableAsync.OnInitAsync"/>。
        /// </summary>
        public async UniTask InitializeAsync()
        {
            if (_initialized || _disposed || _initializing) return;

            SortPending();

            _initializing = true;
            try
            {
                for (int i = 0; i < _pendingInit.Count; i++)
                {
                    var service = _pendingInit[i];
                    if (service is IServiceInitializableAsync asyncInit)
                        await asyncInit.OnInitAsync();
                    InitializeService(service);
                }
            }
            finally
            {
                _initializing = false;
            }

            CompleteInitialization();
        }

        /// <summary>
        /// 拓扑排序挂起图（作用域分段 App→Scene→Gameplay，段内 Kahn）。
        /// </summary>
        private void SortPending()
        {
            if (_pendingInit.Count == 0) return;

            // 契约 → _pendingInit 索引（拓扑边解析）
            var pendingIndexByContract = new Dictionary<Type, int>(_pendingContracts.Count);
            for (int i = 0; i < _pendingInit.Count; i++)
            {
                var svc = _pendingInit[i];
                foreach (var pair in _pendingContracts)
                {
                    if (ReferenceEquals(pair.Value, svc) && !pendingIndexByContract.ContainsKey(pair.Key))
                        pendingIndexByContract[pair.Key] = i;
                }
            }

            TopologySorter.Sort(_pendingInit, pendingIndexByContract, GetDeclaredDependencies, IsServiceReady);
        }

        /// <summary>
        /// 初始化单个挂起服务：委托其作用域补齐轮询列表并驱动 OnInit（生命周期唯一路径在 scope）。
        /// </summary>
        private void InitializeService(IService service)
        {
            if (TryGetScope(service.Scope, out var scope))
                scope.ActivateService(service);
        }

        private void CompleteInitialization()
        {
            _pendingInit.Clear();
            _pendingContracts.Clear();
            _initialized = true;
        }

        #endregion

        #region 查找 [LOOKUP]

        /// <summary>
        /// 获取服务（未找到抛 <see cref="GameException"/>）。按 Gameplay &gt; Scene &gt; App 优先级返回最优服务。
        /// </summary>
        public T GetRequiredService<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            throw new GameException(StringUtility.Format(
                "Service '{0}' was not found in any active scope.", typeof(T).FullName));
        }

        /// <summary>
        /// 获取服务（未找到返回 null）。按 Gameplay &gt; Scene &gt; App 优先级返回最优服务。
        /// </summary>
        public T GetService<T>() where T : class
        {
            return TryGet(out T service) ? service : null;
        }

        /// <summary>
        /// 尝试获取服务。按 Gameplay &gt; Scene &gt; App 优先级返回最优服务。
        /// </summary>
        public bool TryGetService<T>(out T service) where T : class
        {
            return TryGet(out service);
        }

        internal bool TryGet<T>(out T service) where T : class
        {
            if (_bindingsByContract.TryGetValue(typeof(T).TypeHandle, out var bindings) &&
                bindings.TryGetBest(out var raw))
            {
                service = raw as T;
                return service != null;
            }

            service = null;
            return false;
        }

        #endregion

        #region 契约绑定管理 [BINDING MANAGEMENT]

        // 由 ServiceScope 在注册/附加契约/注销时调用——跨作用域视图的唯一写入点

        internal void AddBinding(ServiceScope scope, RuntimeTypeHandle handle, IService service)
        {
            _bindingsByContract.TryGetValue(handle, out var bindings);
            bindings.Set(scope.Kind, service);
            _bindingsByContract[handle] = bindings;
        }

        internal void RemoveBinding(ServiceScope scope, RuntimeTypeHandle handle, IService service)
        {
            if (!_bindingsByContract.TryGetValue(handle, out var bindings)) return;

            bindings.Clear(scope.Kind, service);
            if (bindings.IsEmpty)
                _bindingsByContract.Remove(handle);
            else
                _bindingsByContract[handle] = bindings;
        }

        #endregion

        #region 轮询驱动 [TICK DRIVERS]

        // 固定作用域序 App→Scene→Gameplay（3 槽恒定，零排序）；拦截器在作用域帧边界通知一次。

        /// <summary>
        /// 每帧 Update 轮询驱动。
        /// </summary>
        public void Tick(float elapseSeconds, float realElapseSeconds)
        {
            bool notify = HasInterceptors;
            for (int i = 0; i < s_ScopeCount; i++)
            {
                var scope = _scopes[i];
                if (scope == null || scope.IsDisposed) continue;

                if (notify)
                {
                    for (int k = 0; k < _interceptors.Count; k++)
                        _interceptors[k].OnBeforeScopeTick(scope.Kind, elapseSeconds, realElapseSeconds);
                }

                scope.Tick(elapseSeconds, realElapseSeconds);

                if (notify)
                {
                    for (int k = 0; k < _interceptors.Count; k++)
                        _interceptors[k].OnAfterScopeTick(scope.Kind, elapseSeconds, realElapseSeconds);
                }
            }
        }

        /// <summary>
        /// 每帧 FixedUpdate 轮询驱动。
        /// </summary>
        public void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < s_ScopeCount; i++)
            {
                var scope = _scopes[i];
                if (scope == null || scope.IsDisposed) continue;
                scope.FixedTick(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 每帧 LateUpdate 轮询驱动。
        /// </summary>
        public void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < s_ScopeCount; i++)
            {
                var scope = _scopes[i];
                if (scope == null || scope.IsDisposed) continue;
                scope.LateTick(elapseSeconds, realElapseSeconds);
            }
        }

        /// <summary>
        /// 编辑器 Gizmos 绘制驱动。
        /// </summary>
        public void DrawGizmos()
        {
            for (int i = 0; i < s_ScopeCount; i++)
            {
                var scope = _scopes[i];
                if (scope == null || scope.IsDisposed) continue;
                scope.DrawGizmos();
            }
        }

        #endregion

        #region 关闭 [SHUTDOWN]

        /// <summary>
        /// 关闭指定作用域。服务按逆初始化序（依赖方先）关闭。
        /// </summary>
        public void ShutdownScope(EServiceScopeKind kind)
        {
            if (TryGetScope(kind, out var scope))
            {
                scope.Dispose();
                _scopes[(int)kind] = null;
            }
        }

        /// <summary>
        /// 异步关闭指定作用域。对实现 <see cref="IAsyncShutdownService"/> 的服务先异步关闭。
        /// </summary>
        public async UniTask ShutdownScopeAsync(EServiceScopeKind kind)
        {
            if (TryGetScope(kind, out var scope))
            {
                await scope.DisposeAsync();
                if (!scope.IsDisposed)
                {
                    // 迭代中延迟销毁：手动完成
                    scope.Dispose();
                }
                _scopes[(int)kind] = null;
            }
        }

        /// <summary>
        /// 关闭全部作用域并销毁世界。逆序：Gameplay → Scene → App（依赖方先于被依赖方释放）。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            for (int i = s_ScopeCount - 1; i >= 0; i--)
            {
                _scopes[i]?.Dispose();
                _scopes[i] = null;
            }

            _bindingsByContract.Clear();
            _pendingInit.Clear();
            _pendingContracts.Clear();
            _interceptors.Clear();
            _dependencyCache.Clear();
            _initialized = false;
            _disposed = true;
        }

        /// <summary>
        /// 异步关闭全部作用域并销毁世界。逆序：Gameplay → Scene → App。
        /// </summary>
        public async UniTask DisposeAsync()
        {
            if (_disposed) return;

            for (int i = s_ScopeCount - 1; i >= 0; i--)
            {
                if (TryGetScope((EServiceScopeKind)i, out var scope))
                {
                    await scope.DisposeAsync();
                    if (!scope.IsDisposed) scope.Dispose();
                    _scopes[i] = null;
                }
            }

            _bindingsByContract.Clear();
            _pendingInit.Clear();
            _pendingContracts.Clear();
            _interceptors.Clear();
            _dependencyCache.Clear();
            _initialized = false;
            _disposed = true;
        }

        #endregion

        #region 诊断 [DIAGNOSTICS]

        internal void CollectDiagnosticInfo(List<GameServices.DiagnosticInfo> buffer)
        {
            for (int i = 0; i < s_ScopeCount; i++)
                _scopes[i]?.CollectDiagnosticInfo(buffer);
        }

        /// <summary>
        /// 清零全部作用域的轮询耗时统计。
        /// </summary>
        public void ResetPollStatistics()
        {
            for (int i = 0; i < s_ScopeCount; i++)
                _scopes[i]?.ResetPollStatistics();
        }

        #endregion

        #region CrossScopeBindings 值类型 [CROSS-SCOPE BINDINGS STRUCT]

        /// <summary>
        /// 跨作用域契约绑定值类型。内联 App/Scene/Gameplay 三个引用槽（null 即空槽），
        /// <see cref="TryGetBest"/> 按 Gameplay &gt; Scene &gt; App 优先级返回最优服务。
        /// </summary>
        private struct CrossScopeBindings
        {
            private IService _app;
            private IService _scene;
            private IService _gameplay;

            public bool IsEmpty => _app == null && _scene == null && _gameplay == null;

            public void Set(EServiceScopeKind kind, IService service)
            {
                switch (kind)
                {
                    case EServiceScopeKind.App: _app = service; break;
                    case EServiceScopeKind.Scene: _scene = service; break;
                    case EServiceScopeKind.Gameplay: _gameplay = service; break;
                }
            }

            public void Clear(EServiceScopeKind kind, IService service)
            {
                switch (kind)
                {
                    case EServiceScopeKind.App:
                        if (ReferenceEquals(_app, service)) _app = null;
                        break;
                    case EServiceScopeKind.Scene:
                        if (ReferenceEquals(_scene, service)) _scene = null;
                        break;
                    case EServiceScopeKind.Gameplay:
                        if (ReferenceEquals(_gameplay, service)) _gameplay = null;
                        break;
                }
            }

            public bool TryGetBest(out IService service)
            {
                if (_gameplay != null) { service = _gameplay; return true; }
                if (_scene != null) { service = _scene; return true; }
                if (_app != null) { service = _app; return true; }
                service = null;
                return false;
            }
        }

        #endregion
    }
}
