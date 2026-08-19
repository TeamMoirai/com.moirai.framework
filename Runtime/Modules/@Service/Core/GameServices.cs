using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Assertions;

namespace Moirai.Atropos
{
    public static partial class GameServices
    {
        private const int ScopeSlotCount = 3;

        private static int s_MainThreadId;
        private static readonly ServiceScope[] s_ScopeContainers = new ServiceScope[ScopeSlotCount];
        private static readonly Dictionary<RuntimeTypeHandle, ScopeBindings> s_ServiceMaps = new Dictionary<RuntimeTypeHandle, ScopeBindings>();
        // ServiceScope calls static methods directly on GameServices

        public static event Action<IService, Type, EServiceScopeKind> ServiceRegistered;
        public static event Action<IService> ServiceUnregistered;

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureMainThreadId()
        {
            s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            // Ensure App scope always exists
            EnsureScope(EServiceScopeKind.App);
        }

        private static void EnsureMainThread()
        {
            Assert.IsTrue(
                s_MainThreadId == 0 || System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId,
                "GameServices must only be used from the main thread. " +
                "From a background thread/callback, wrap the call with MainThreadDispatcher.Dispatch/DispatchAsync.");
        }

        internal static ServiceScope EnsureScope(EServiceScopeKind kind)
        {
            int idx = (int)kind;
            if (s_ScopeContainers[idx] == null || s_ScopeContainers[idx].IsDisposed)
            {
                s_ScopeContainers[idx] = new ServiceScope(kind, kind.ToString());
            }
            return s_ScopeContainers[idx];
        }

        // --- Global map management (called by ServiceScope) ---

        internal static void AddToGlobalMap(RuntimeTypeHandle handle, IService service, EServiceScopeKind scope)
        {
            if (!s_ServiceMaps.TryGetValue(handle, out var bindings))
                bindings = default;
            switch (scope)
            {
                case EServiceScopeKind.App: bindings.App = service; break;
                case EServiceScopeKind.Scene: bindings.Scene = service; break;
                case EServiceScopeKind.Gameplay: bindings.Gameplay = service; break;
            }
            s_ServiceMaps[handle] = bindings;
        }

        internal static void RemoveFromGlobalMap(RuntimeTypeHandle handle, IService service, EServiceScopeKind scope)
        {
            if (!s_ServiceMaps.TryGetValue(handle, out var bindings)) return;
            switch (scope)
            {
                case EServiceScopeKind.App: if (ReferenceEquals(bindings.App, service)) bindings.App = null; break;
                case EServiceScopeKind.Scene: if (ReferenceEquals(bindings.Scene, service)) bindings.Scene = null; break;
                case EServiceScopeKind.Gameplay: if (ReferenceEquals(bindings.Gameplay, service)) bindings.Gameplay = null; break;
            }
            if (bindings.IsEmpty) s_ServiceMaps.Remove(handle);
            else s_ServiceMaps[handle] = bindings;
        }

        internal static void SetContext(IService service, ServiceScope scope)
        {
            var ctx = new ServiceContext(s_ServiceMaps, scope);
            if (service is ServiceBase sb) sb.SetContext(ctx);
            else if (service is ServiceMonoBase mono) mono.SetContext(ctx);
        }

        internal static void RaiseServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope)
            => ServiceRegistered?.Invoke(service, interfaceType, scope);

        internal static void RaiseServiceUnregistered(IService service)
            => ServiceUnregistered?.Invoke(service);

        internal static bool IsIterating
        {
            get
            {
                for (int i = 0; i < ScopeSlotCount; i++)
                {
                    if (s_ScopeContainers[i] != null && s_ScopeContainers[i].IsIterating)
                        return true;
                }
                return false;
            }
        }

        internal static int PendingChangesCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < ScopeSlotCount; i++)
                {
                    if (s_ScopeContainers[i] != null)
                        count += s_ScopeContainers[i].PendingChangesCount;
                }
                return count;
            }
        }

        // --- Public API ---

        public static void Tick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.Tick(elapseSeconds, realElapseSeconds);
        }

        public static void FixedTick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.FixedTick(elapseSeconds, realElapseSeconds);
        }

        public static void LateTick(float elapseSeconds, float realElapseSeconds)
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.LateTick(elapseSeconds, realElapseSeconds);
        }

        public static void DrawGizmos()
        {
            for (int i = 0; i < ScopeSlotCount; i++)
                s_ScopeContainers[i]?.DrawGizmos();
        }

        public static void Shutdown()
        {
            EnsureMainThread();
            ShutdownScope(EServiceScopeKind.Gameplay);
            ShutdownScope(EServiceScopeKind.Scene);
            ShutdownScope(EServiceScopeKind.App);
            ClearAll();
        }

        public static void ShutdownScope(EServiceScopeKind scope)
        {
            EnsureMainThread();
            int idx = (int)scope;
            if (s_ScopeContainers[idx] == null) return;
            s_ScopeContainers[idx].Dispose();
            s_ScopeContainers[idx] = null;
        }

        public static T GetService<T>() where T : class
        {
            EnsureMainThread();
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
                throw new GameException(StringUtility.Format("You must get service by interface, but '{0}' is not.", interfaceType.FullName));

            if (s_ServiceMaps.TryGetValue(interfaceType.TypeHandle, out var bindings))
            {
                var best = bindings.GetBest();
                if (best != null) return best as T;
            }

            // Reflection fallback
            string serviceName = StringUtility.Format("{0}.{1}, {2}", interfaceType.Namespace, interfaceType.Name.Substring(1), interfaceType.Assembly.GetName().Name);
            Type serviceType = Type.GetType(serviceName);
            if (serviceType == null)
                throw new GameException(StringUtility.Format("Can not find Game Framework service type '{0}'.", serviceName));

            ServiceBase service = (ServiceBase)Activator.CreateInstance(serviceType);
            RegisterService<T>(service);
            return service as T;
        }

        /// <summary>
        /// 偏好 Scope 查找：先查指定 scope，未命中走 GetBest。
        /// </summary>
        public static T GetService<T>(EServiceScopeKind preferredScope) where T : class
        {
            EnsureMainThread();
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
                throw new GameException(StringUtility.Format("You must get service by interface, but '{0}' is not.", interfaceType.FullName));

            // Try preferred scope first
            int idx = (int)preferredScope;
            if (s_ScopeContainers[idx] != null && s_ScopeContainers[idx].TryGet<T>(out var preferred))
                return preferred;

            // Fallback to GetBest
            if (s_ServiceMaps.TryGetValue(interfaceType.TypeHandle, out var bindings))
            {
                var best = bindings.GetBest();
                if (best != null) return best as T;
            }

            throw new GameException(StringUtility.Format("Service {0} not found.", interfaceType.FullName));
        }

        public static T RegisterService<T>(IService service) where T : class
        {
            EnsureMainThread();
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
                throw new GameException(StringUtility.Format("You must register service by interface, but '{0}' is not.", interfaceType.FullName));

            if (!interfaceType.IsInstanceOfType(service))
                throw new GameException(StringUtility.Format("Service '{0}' does not implement interface '{1}'.", service.GetType().FullName, interfaceType.FullName));

            var scope = EnsureScope(service.Scope);
            return scope.Register<T>(service);
        }

        public static void RegisterService<TPrimary, TSecondary>(IService service)
            where TPrimary : class
            where TSecondary : class
        {
            RegisterService<TPrimary>(service);
            RegisterService<TSecondary>(service);
        }

        public static bool UnregisterService<T>() where T : class
        {
            EnsureMainThread();
            Type interfaceType = typeof(T);
            if (!interfaceType.IsInterface)
                throw new GameException(StringUtility.Format("You must unregister service by interface, but '{0}' is not.", interfaceType.FullName));

            if (!s_ServiceMaps.TryGetValue(interfaceType.TypeHandle, out var bindings)) return false;
            var service = bindings.GetBest();
            if (service == null) return false;

            return s_ScopeContainers[(int)service.Scope]?.Unregister(service) ?? false;
        }

        public static bool UnregisterService(IService service)
        {
            if (service == null) return false;
            EnsureMainThread();
            return s_ScopeContainers[(int)service.Scope]?.Unregister(service) ?? false;
        }

        public static UniTask InitializeAsync()
        {
            EnsureMainThread();

            // Fast path: if no IAsyncInitService exists, return CompletedTask (zero alloc, no async state machine)
            bool hasAsync = false;
            var visited = new HashSet<IService>(ReferenceComparer<IService>.Instance);
            foreach (var kvp in s_ServiceMaps)
            {
                var b = kvp.Value;
                if (b.App != null && visited.Add(b.App) && b.App is IAsyncInitService) { hasAsync = true; break; }
                if (b.Scene != null && visited.Add(b.Scene) && b.Scene is IAsyncInitService) { hasAsync = true; break; }
                if (b.Gameplay != null && visited.Add(b.Gameplay) && b.Gameplay is IAsyncInitService) { hasAsync = true; break; }
            }

            return hasAsync ? InitializeAsyncCore() : UniTask.CompletedTask;
        }

        private static async UniTask InitializeAsyncCore()
        {
            var visited = new HashSet<IService>(ReferenceComparer<IService>.Instance);
            foreach (var kvp in s_ServiceMaps)
            {
                var b = kvp.Value;
                if (b.App != null && visited.Add(b.App) && b.App is IAsyncInitService asyncApp)
                    await asyncApp.OnInitAsync();
                if (b.Scene != null && visited.Add(b.Scene) && b.Scene is IAsyncInitService asyncScene)
                    await asyncScene.OnInitAsync();
                if (b.Gameplay != null && visited.Add(b.Gameplay) && b.Gameplay is IAsyncInitService asyncGameplay)
                    await asyncGameplay.OnInitAsync();
            }
        }

        private static void ClearAll()
        {
            for (int i = 0; i < ScopeSlotCount; i++)
            {
                s_ScopeContainers[i]?.Dispose();
                s_ScopeContainers[i] = null;
            }
            s_ServiceMaps.Clear();
            ServiceRegistered = null;
            ServiceUnregistered = null;
            MemoryPool.ClearAll();
            MarshalUtility.FreeCachedHGlobal();
        }
    }
}
