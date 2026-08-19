using System;
using System.Collections.Generic;

namespace Moirai.Atropos
{
    /// <summary>
    /// 服务生命周期作用域种类。
    /// </summary>
    public enum EServiceScopeKind : byte
    {
        /// <summary>应用级，生命周期最长，适合资源、音频、UI、计时器等全局服务。</summary>
        App = 0,
        /// <summary>场景级，主场景切换时会重置，适合当前场景状态。</summary>
        Scene = 1,
        /// <summary>玩法级，适合一局战斗或一个玩法实例的服务。</summary>
        Gameplay = 2,
    }

    /// <summary>
    /// 服务作用域容器。每个作用域独立持有自己的服务字典、tick 列表和迭代安全机制。
    /// <para>Dispose 时逆序关闭全部服务并清理——O(1) 对外、不影响其他作用域。</para>
    /// </summary>
    public sealed class ServiceScope : IDisposable
    {
        private const int MissingIndex = -1;

        private readonly Dictionary<RuntimeTypeHandle, IService> _servicesByContract = new Dictionary<RuntimeTypeHandle, IService>();
        private readonly Dictionary<IService, ServiceEntry> _entriesByService = new Dictionary<IService, ServiceEntry>(ReferenceComparer<IService>.Instance);
        private readonly List<IService> _registrationOrder = new List<IService>();

        private readonly List<IServiceTickable> _tickables = new List<IServiceTickable>();
        private readonly List<IServiceFixedTickable> _fixedTickables = new List<IServiceFixedTickable>();
        private readonly List<IServiceLateTickable> _lateTickables = new List<IServiceLateTickable>();
        private readonly List<IServiceGizmoDrawable> _gizmoDrawables = new List<IServiceGizmoDrawable>();

        private readonly List<PendingChange> _pendingChanges = new List<PendingChange>();

        private bool _tickablesDirty;
        private bool _lateTickablesDirty;
        private bool _fixedTickablesDirty;
        private bool _gizmoDrawablesDirty;
        private bool _isIterating;

        internal ServiceScope(EServiceScopeKind kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        internal EServiceScopeKind Kind { get; }
        public string Name { get; }
        internal bool IsDisposed { get; private set; }
        internal bool IsIterating => _isIterating;
        internal int PendingChangesCount => _pendingChanges.Count;
        internal int ServiceCount => _registrationOrder.Count;

        internal bool HasContract<T>() where T : class
            => _servicesByContract.ContainsKey(typeof(T).TypeHandle);

        internal bool TryGet<T>(out T service) where T : class
        {
            if (_servicesByContract.TryGetValue(typeof(T).TypeHandle, out var raw))
            {
                service = raw as T;
                return service != null;
            }
            service = null;
            return false;
        }

        internal T Require<T>() where T : class
        {
            if (TryGet(out T service)) return service;
            throw new GameException(StringUtility.Format("Scope {0} does not contain service {1}.", Name, typeof(T).FullName));
        }

        internal T Register<T>(IService service) where T : class
        {
            var interfaceType = typeof(T);
            var handle = interfaceType.TypeHandle;

            if (_servicesByContract.ContainsKey(handle))
            {
                var existing = _servicesByContract[handle];
                LogUtility.Warning("{0} has already been registered in {1} scope.", interfaceType.FullName, Kind);
                return existing as T;
            }

            if (_isIterating)
            {
                _pendingChanges.Add(PendingChange.Register(service, interfaceType, Kind));
                return service as T;
            }

            RegisterInternal(service, interfaceType, handle);
            return service as T;
        }

        internal void RegisterInternal(IService service, Type interfaceType, RuntimeTypeHandle handle)
        {
            _servicesByContract[handle] = service;

            GameServices.SetContext(service, this);
            GameServices.AddToGlobalMap(handle, service, Kind);

            var entry = new ServiceEntry
            {
                InterfaceHandle = handle,
                Scope = Kind,
            };

            InsertSorted(_registrationOrder, service);
            if (service is IServiceTickable tickable) { InsertSorted(_tickables, tickable); _tickablesDirty = true; }
            if (service is IServiceFixedTickable fixedTickable) { InsertSorted(_fixedTickables, fixedTickable); _fixedTickablesDirty = true; }
            if (service is IServiceLateTickable lateTickable) { InsertSorted(_lateTickables, lateTickable); _lateTickablesDirty = true; }
            if (service is IServiceGizmoDrawable gizmo) { InsertSorted(_gizmoDrawables, gizmo); _gizmoDrawablesDirty = true; }

            RebuildIndices();
            _entriesByService[service] = entry;

            service.OnInit();
            GameServices.RaiseServiceRegistered(service, interfaceType, Kind);
        }

        internal bool Unregister(IService service)
        {
            if (service == null || !_entriesByService.TryGetValue(service, out var entry)) return false;

            if (_isIterating)
            {
                if (entry.PendingRemove) return true;
                entry.PendingRemove = true;
                _entriesByService[service] = entry;
                _pendingChanges.Add(PendingChange.Unregister(service));
                return true;
            }

            ShutdownService(service);
            return true;
        }

        private void ShutdownService(IService service)
        {
            if (!_entriesByService.TryGetValue(service, out var entry)) return;

            try { service.Shutdown(); }
            catch (Exception ex) { LogUtility.Error(ex.ToString()); }

            entry.PendingRemove = false;
            RemoveServiceInternal(service, entry);
        }

        private void RemoveServiceInternal(IService service, ServiceEntry entry)
        {
            _servicesByContract.Remove(entry.InterfaceHandle);
            GameServices.RemoveFromGlobalMap(entry.InterfaceHandle, service, Kind);

            SwapRemoveFromList(_registrationOrder, service, nameof(_registrationOrder));
            if (service is IServiceTickable) SwapRemoveFromList(_tickables, (IServiceTickable)service, nameof(_tickables));
            if (service is IServiceFixedTickable) SwapRemoveFromList(_fixedTickables, (IServiceFixedTickable)service, nameof(_fixedTickables));
            if (service is IServiceLateTickable) SwapRemoveFromList(_lateTickables, (IServiceLateTickable)service, nameof(_lateTickables));
            if (service is IServiceGizmoDrawable) SwapRemoveFromList(_gizmoDrawables, (IServiceGizmoDrawable)service, nameof(_gizmoDrawables));

            _entriesByService.Remove(service);
            GameServices.RaiseServiceUnregistered(service);
        }

        internal void Tick(float elapseSeconds, float realElapseSeconds)
        {
            SortIfDirty();
            _isIterating = true;
            try
            {
                int count = _tickables.Count;
                for (int i = 0; i < count; i++)
                    _tickables[i].Tick(elapseSeconds, realElapseSeconds);
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        internal void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            SortIfDirty();
            _isIterating = true;
            try
            {
                int count = _fixedTickables.Count;
                for (int i = 0; i < count; i++)
                    _fixedTickables[i].FixedTick(elapseSeconds, realElapseSeconds);
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        internal void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            SortIfDirty();
            _isIterating = true;
            try
            {
                int count = _lateTickables.Count;
                for (int i = 0; i < count; i++)
                    _lateTickables[i].LateTick(elapseSeconds, realElapseSeconds);
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        internal void DrawGizmos()
        {
            SortIfDirty();
            _isIterating = true;
            try
            {
                int count = _gizmoDrawables.Count;
                for (int i = 0; i < count; i++)
                    _gizmoDrawables[i].OnDrawGizmos();
            }
            finally
            {
                _isIterating = false;
                FlushPendingChanges();
            }
        }

        private void FlushPendingChanges()
        {
            if (_pendingChanges.Count == 0) return;
            for (int i = 0; i < _pendingChanges.Count; i++)
            {
                var change = _pendingChanges[i];
                if (change.IsRegister)
                {
                    if (!_servicesByContract.ContainsKey(change.InterfaceType.TypeHandle))
                        RegisterInternal(change.Service, change.InterfaceType, change.InterfaceType.TypeHandle);
                }
                else
                {
                    ShutdownService(change.Service);
                }
            }
            _pendingChanges.Clear();
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            _isIterating = false;
            _pendingChanges.Clear();

            for (int i = _registrationOrder.Count - 1; i >= 0; i--)
            {
                var service = _registrationOrder[i];
                if (service == null) continue;
                if (!_entriesByService.TryGetValue(service, out var entry)) continue;
                ShutdownService(service);
            }

            _registrationOrder.Clear();
            _tickables.Clear();
            _fixedTickables.Clear();
            _lateTickables.Clear();
            _gizmoDrawables.Clear();
            _entriesByService.Clear();
            _servicesByContract.Clear();
            IsDisposed = true;
        }

        private void SortIfDirty()
        {
            if (_tickablesDirty) { _tickables.Sort(CompareByPriority); RebuildTickableIndices(); _tickablesDirty = false; }
            if (_fixedTickablesDirty) { _fixedTickables.Sort(CompareByPriority); RebuildFixedTickableIndices(); _fixedTickablesDirty = false; }
            if (_lateTickablesDirty) { _lateTickables.Sort(CompareByPriority); RebuildLateTickableIndices(); _lateTickablesDirty = false; }
            if (_gizmoDrawablesDirty) { _gizmoDrawables.Sort(CompareByPriority); RebuildGizmoIndices(); _gizmoDrawablesDirty = false; }
        }

        private void RebuildIndices()
        {
            // After InsertSorted on _registrationOrder, rebuild AllIndex in entries
            for (int i = 0; i < _registrationOrder.Count; i++)
            {
                if (_entriesByService.TryGetValue(_registrationOrder[i], out var e))
                {
                    e.AllIndex = i;
                    _entriesByService[_registrationOrder[i]] = e;
                }
            }
        }

        private void RebuildTickableIndices()
        {
            for (int i = 0; i < _tickables.Count; i++)
                if (_tickables[i] is IService s && _entriesByService.TryGetValue(s, out var e))
                { e.UpdateIndex = i; _entriesByService[s] = e; }
        }
        private void RebuildFixedTickableIndices()
        {
            for (int i = 0; i < _fixedTickables.Count; i++)
                if (_fixedTickables[i] is IService s && _entriesByService.TryGetValue(s, out var e))
                { e.FixedUpdateIndex = i; _entriesByService[s] = e; }
        }
        private void RebuildLateTickableIndices()
        {
            for (int i = 0; i < _lateTickables.Count; i++)
                if (_lateTickables[i] is IService s && _entriesByService.TryGetValue(s, out var e))
                { e.LateUpdateIndex = i; _entriesByService[s] = e; }
        }
        private void RebuildGizmoIndices()
        {
            for (int i = 0; i < _gizmoDrawables.Count; i++)
                if (_gizmoDrawables[i] is IService s && _entriesByService.TryGetValue(s, out var e))
                { e.GizmoIndex = i; _entriesByService[s] = e; }
        }

        private static int CompareByPriority<T>(T a, T b)
        {
            int left = (a is IService ia) ? ia.Priority : 0;
            int right = (b is IService ib) ? ib.Priority : 0;
            return right.CompareTo(left); // descending: high priority first
        }

        private static void InsertSorted<T>(List<T> list, T item) where T : class
        {
            int priority = (item is IService si) ? si.Priority : 0;
            int insertAt = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                int existingPriority = (list[i] is IService ei) ? ei.Priority : 0;
                if (priority > existingPriority) { insertAt = i; break; }
            }
            list.Insert(insertAt, item);
        }

        private void SwapRemoveFromList<T>(List<T> list, T item, string listName) where T : class
        {
            int index = list.IndexOf(item);
            if (index < 0) return;
            int lastIndex = list.Count - 1;
            if (index != lastIndex)
            {
                T moved = list[lastIndex];
                list[index] = moved;
                // Update moved entry's index
                if (moved is IService movedService && _entriesByService.TryGetValue(movedService, out var e))
                {
                    switch (listName)
                    {
                        case nameof(_registrationOrder): e.AllIndex = index; break;
                        case nameof(_tickables): e.UpdateIndex = index; break;
                        case nameof(_fixedTickables): e.FixedUpdateIndex = index; break;
                        case nameof(_lateTickables): e.LateUpdateIndex = index; break;
                        case nameof(_gizmoDrawables): e.GizmoIndex = index; break;
                    }
                    _entriesByService[movedService] = e;
                }
            }
            list.RemoveAt(lastIndex);
        }

        // --- 诊断 ---

        internal struct ServiceEntry
        {
            public RuntimeTypeHandle InterfaceHandle;
            public EServiceScopeKind Scope;
            public int AllIndex;
            public int UpdateIndex;
            public int FixedUpdateIndex;
            public int LateUpdateIndex;
            public int GizmoIndex;
            public bool PendingRemove;
        }

        internal struct PendingChange
        {
            public readonly bool IsRegister;
            public readonly IService Service;
            public readonly Type InterfaceType;
            public readonly EServiceScopeKind Scope;

            private PendingChange(bool isRegister, IService service, Type interfaceType, EServiceScopeKind scope)
            {
                IsRegister = isRegister;
                Service = service;
                InterfaceType = interfaceType;
                Scope = scope;
            }

            public static PendingChange Register(IService service, Type interfaceType, EServiceScopeKind scope)
                => new PendingChange(true, service, interfaceType, scope);
            public static PendingChange Unregister(IService service)
                => new PendingChange(false, service, null, default);
        }
    }
}
