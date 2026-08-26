using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 统一服务世界。管理 App/Scene/Gameplay 三个固定作用域的完整生命周期：
    /// 构建（实例注册 + 初始化）、查找（<see cref="ContractBindings"/> O(1)）、轮询、销毁。
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
}
