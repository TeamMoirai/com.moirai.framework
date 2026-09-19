# Core Service System (@Service)

> Framework's modular base: a unified service world (`ServiceWorld`) manages construction, lifecycle, polling, and scope of all sub-services, driven by `GameApp` (a static facade holding no MonoBehaviour) through the PlayerLoop.

`@Service` is the service infrastructure of the entire framework. All functional services (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `ServiceBase` that declare dependencies via the `[ServiceDependency(typeof(...))]` attribute; the world build is two-phase — `GameServices.RegisterService<T>(scope, service)` only enqueues the service into the graph, and the composition root calls `GameServices.Default.InitializeAsync()` to drive all `OnInit` in dependency-graph topological order (missing/circular dependencies fail-fast with cycle members in the error; initialization order is independent of registration order); services registered at runtime (after the world is initialized) are initialized immediately (requires all dependencies ready). Non-service code accesses services through each service's static facade (e.g. `AudioService.Xxx()`, `UIService.Xxx()`, `ResourceService.Xxx()`); dynamic service lookup goes through `GameServices.GetRequiredService<T>()` static methods. Services support three scopes: App/Scene/Gameplay. Cross-scope lookup uses an inline 3-slot binding value-type struct for O(1) resolution (Gameplay > Scene > App priority). When a scene is unloaded, scene-level and gameplay-level services are automatically cleaned up. `ServiceWorld` is instantiable — `new ServiceWorld()` constructs an isolated world for tests and sandboxes without touching `GameServices.Default`.

## Core Features

- **Instantiable service world**: `ServiceWorld` can be `new`-constructed into an isolated world (tests/sandboxes), and the `GameServices` static facade is merely a projection of the default world `Default`; 3 fixed-order scope slots (App/Scene/Gameplay, zero sorting) + an inline 3-slot binding value-type struct for O(1) cross-scope lookup
- **Attribute-declared dependencies + topological initialization**: `[ServiceDependency(typeof(DepA), typeof(DepB))]` declares multiple dependencies in a single attribute, validated at compile time by `ServiceDependencyAnalyzer` (MIRAI201/MIRAI202) to ensure types implement `IService`; world initialization drives all `OnInit` uniformly via Kahn topological sorting over the declared graph (initialization order is independent of registration order)
- **Two-phase build**: `Register` only enqueues into the graph (no `OnInit` is driven during registration); `Initialize()`/`InitializeAsync()` commits the second phase — missing and circular dependencies fail-fast at initialization (the error message includes cycle members); runtime registration after the world is initialized requires all dependencies ready right now (fail-fast otherwise) and drives `OnInit` immediately — **no topological insertion**, a late service always lands after the existing ones (services implementing `IServiceInitializableAsync` are forbidden from runtime registration)
- **HandlerHost static facades**: all 13 framework services follow the `[HandlerHost] XxxService : ServiceBase` static facade + serializable `XxxHandler` backend + `XxxSettings` (`[SerializeReference]` + `[ProviderDropdown]`) backend-selection pattern
- **Three-level scope** (`EServiceScopeKind.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App priority
- **Lifecycle capability interfaces implemented on demand**: `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable`, `IServiceGizmoDrawable`, `IAsyncShutdownService` (all inherit `IService`)
- **`Priority`** controls polling order (higher priority polls first, shuts down later). Framework built-in services are uniformly ≤ -1000 (see `ServicePriorityOrder`); business services default to 0 and above
- **Async shutdown**: services implementing `IAsyncShutdownService` run `OnShutdownAsync()` before the synchronous `OnShutdown()`, in **reverse activation order** (= reverse initialization order, not reverse registration order)
- **Runtime service registration**: `GameServices.RegisterService<T>()` / `UnregisterService<T>()` dynamically add/remove individual services; the explicit-contract overload `RegisterService(scope, Type, instance)` supports interface contracts and multi-contract binding of one instance; calls during iteration default to deferring until the current cycle ends (`EDeferMode.Defer`)
- **Self-registering Mono service**: `ServiceMono<TScope>` auto-registers in Awake and auto-unregisters in OnDestroy
- **Scope ordering**: the container processes the fixed slot order App → Scene → Gameplay, which has nothing to do with `ServiceScopeOrder` (that type is a standalone constant table the container never consumes)
- **Interceptors**: `IServiceInterceptor` inserts cross-cutting logic at service register/unregister/shutdown and scope frame boundaries (`OnBeforeScopeTick`/`OnAfterScopeTick`); executes in `Priority` descending order. Except for `OnServiceRegistering` (the veto channel), callback exceptions are logged and isolated by the container — a broken observer cannot bring down the game loop
- **Iteration safety**: registrations/unregistrations during polling are deferred and applied uniformly after the current cycle ends; scope disposal requested during iteration is also deferred
- **Tiered tick exception policy**: in the editor and development builds, exceptions are logged then rethrown immediately (fail-fast, surfacing defects at once); in release builds they are logged and isolated so a single faulty service does not abort other services in the same frame
- **Main thread affinity guard**: the register/unregister/shutdown/lookup/poll entries of `GameServices` assert the main thread in editor and development builds; in release builds the assertion is not compiled at all (`#if UNITY_EDITOR || DEVELOPMENT_BUILD`), so the cost is genuinely zero. An isolated world (`new ServiceWorld()`) asserts nothing and may be used across threads for parallel tests
- **Lifecycle state machine**: each service tracks `EServiceState` (Created → Initialized → ShuttingDown → Disposed) with idempotent shutdown
- **MonoBehaviour polling constraint**: MonoBehaviour services cannot implement `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` / `IServiceGizmoDrawable` — Unity's own `Update`/`FixedUpdate`/`LateUpdate`/`OnDrawGizmos` drive those, and registering such a service is rejected
- **Unified lookup entry**: dynamic service lookup goes through `GameServices.GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>(out T)`, returning the best match by Gameplay > Scene > App priority

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `IService` | Core service contract: `Priority`, `Scope`, `OnInit()`, `OnShutdown()`. **Must derive from `ServiceBase` (plain C#) or `ServiceMono<TScope>` (MonoBehaviour)** — the container drives and reads state through `IServiceLifecycle`, so a bare `IService` implementation is rejected at registration |
| `ServiceBase` | Abstract base class for plain C# services; dependencies declared via the `[ServiceDependency]` attribute and topologically validated at world initialization (the lifecycle state machine is driven solely by the container via `IServiceLifecycle`; `State` is a read-only projection); built-in services use `Priority` ≤ -1000 |
| `ServiceMono<TScope>` | MonoBehaviour service base (generic scope marker); auto-registers in Awake, auto-unregisters in OnDestroy |
| `ServiceWorld` | Instantiable unified service world (`new ServiceWorld()` constructs an isolated world): 3 fixed-order scope slots + an inline binding value-type struct for O(1) cross-scope lookup; two-phase build via `Register`/`Initialize(Async)`; shutdown is strictly reverse-topological; unregistering or closing a scope while initialization is running fails fast; performs no thread assertion of its own |
| `ServiceScope` | Per-scope registry, polling lists (lazy-sort + swap-remove), iteration safety (deferred-change queue), and tick exception circuit breaker (counted per polling category, threshold owned by the world) |
| `TopologySorter` | Internal Kahn topological sorter: stable dequeue by registration order among equal in-degree nodes; missing/circular dependencies fail-fast (the error message includes cycle members) |
| `GameServices` | Static facade (a projection of the default world `Default`): unified registration entry `RegisterService<T>(scope, service, deferMode)` and explicit-contract overload `RegisterService(scope, Type, instance)`, unregistration, scope management (`ShutdownContainer`/`HasApp`/`HasScene`/`HasGameplay`), lazy facade self-registration (`EnsureRegistered`, internal), polling drivers, frame-boundary interceptors |
| `ServiceDependencyAttribute` | Dependency declaration attribute: `[ServiceDependency(typeof(DepA), typeof(DepB))]` declares multiple dependencies in a single attribute; compile-time MIRAI201/MIRAI202 validation + initialization-time topological sorting |
| `IServiceInitializableAsync` | Async initialization capability interface (`UniTask OnInitAsync()`); must be registered before `InitializeAsync` (runtime registration fails fast) |
| `IServiceInterceptor` | Interceptor interface: register/unregister/shutdown callbacks + scope frame boundaries (`OnBeforeScopeTick`/`OnAfterScopeTick`), executed in `Priority` descending order; all callbacks except `OnServiceRegistering` have their exceptions isolated by the container |
| `EServiceScopeKind` | Service scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `EServiceState` | Service lifecycle state: `Created`, `Initialized`, `ShuttingDown`, `Disposed` (`ServiceBase.State` property) |
| `EDeferMode` | Deferral policy for registration/unregistration during iteration: `Defer` (defer until cycle ends, default) / `Throw` (throw immediately) |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | Polling capability interfaces (all inherit `IService`) with method signatures such as `Tick(float elapseSeconds, float realElapseSeconds)` (MonoBehaviour services cannot implement these) |
| `IServiceGizmoDrawable` | Editor Gizmos drawing capability interface (inherits `IService`) `OnDrawGizmos()`; MonoBehaviour services cannot implement it (Unity already drives the magic method on the component — the container would draw twice) |
| `IAsyncShutdownService` | Async shutdown capability interface (inherits `IService`); services implementing `OnShutdownAsync()` are shut down in reverse activation order by `ShutdownContainerAsync()` |
| `FrameworkHandler` | Handler base class (`[Serializable]`): idempotent `Internal_Init`/`Internal_Shutdown` + sync/async lifecycle callbacks; base of all XxxHandler classes |
| `ServiceScopeOrder` | Scope constant table (App=-10000, Scene=-5000, Gameplay=0); **the container never consumes it** — scope order comes from the fixed slots, polling order from `IService.Priority` |
| `ServicePriorityOrder` | Framework built-in service polling priority constants (all ≤ -1000, banded separately from business services) |
| `GameApp` | Static facade entry point (no MonoBehaviour): initialized by `GameAppSettings.Initiation` at `AfterAssembliesLoaded`, registers its builtin Tick on `PlayerLoopDriver` to drive `GameServices.Tick` every frame, and calls `GameServices.Shutdown` on `Shutdown`; coroutines/Gizmos/Pause delegate to `GameAppHost` |
| `GameAppMessageEvent` / `EMessageEventType` | Namespace `Moirai.Atropos.Events`, framework-level pooled events (focus/unfocus/quit, SDK callbacks) |

## Quick Start

```csharp
// 1. Business code accesses framework services via static facades
TimerService.Delay(1f, () => Debug.Log("1s"));
UIService.ShowUI<MainWindow>();
ResourceService.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// 2. Define a custom service — dependencies declared via the [ServiceDependency] attribute
[ServiceDependency(typeof(TimerService))]
public class MyService : ServiceBase, IServiceTickable
{
    public override int Priority => 10;              // Higher priority polls first

    public override void OnInit()
    {
        TimerService.Delay(1f, () => { /* dependencies ready — use static facades directly */ });
    }

    public override void OnShutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 3. Two-phase build: registration order does not matter (topological sorting guarantees dependencies initialize first); Initialize drives all OnInit uniformly
GameServices.RegisterService(EServiceScopeKind.Gameplay, new TimerService());
GameServices.RegisterService(EServiceScopeKind.Gameplay, new MyService());
GameServices.Default.Initialize();

// 4. Shut down — services close in reverse initialization order (dependents first)
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## Advanced Usage

### Lifecycle and Scope

- `GameServices.RegisterService<T>(scope, service)` is the unified registration entry (first phase of the two-phase build: it only enqueues into the graph and does not drive `OnInit`). While the world is not yet initialized, dependency validation is deferred to the topological-sorting phase of `Initialize(Async)` (missing/circular dependencies fail-fast; the error message includes cycle members); runtime registration after the world is initialized requires all dependencies to be initialized and ready (fail-fast if missing), and drives `OnInit()` immediately once validation passes. Dependency declarations are always read from the implementation type — registering with an interface as the contract validates dependencies the same way.
- `GameServices.Shutdown()` shuts down all scopes in reverse order: Gameplay → Scene → App; `GameServices.ShutdownContainer(scope)` shuts down only the specified scope.
- `GameApp` listens to `SceneManager.sceneUnloaded` and automatically shuts down `Scene` and `Gameplay` scopes when a scene is unloaded.
- The same contract can be registered with different implementations in different scopes. `GameServices` lookup order is Gameplay > Scene > App (cross-scope binding value-type `TryGetBest()`), which can be used to temporarily replace global implementations during combat.
- Registration is idempotent: re-registering the same contract in the same scope is skipped (the existing instance is returned); circular dependencies throw `GameException` during the world-initialization topological sort (fail-fast; the error message includes cycle members).
- Unregistration granularity is the **service**, not the contract: when one instance is registered under several contracts, unregistering any one of them removes the whole instance from the scope (the remaining contracts go with it).
- While initialization is running (`ServiceWorld.IsInitializing`), `UnregisterService` and `ShutdownContainer` throw `GameException` — the pending graph is being consumed by an index-driven loop, so removing entries mid-flight would still `OnInit` the removed service (with no activation record, hence no `OnShutdown`) and shift later services out of turn. Do these after initialization returns.

### HandlerHost Service Architecture

All 13 built-in framework services (Resource/Debugger/Audio/ObjectPool/GameObjectPool/Procedure/Localization/Scene/Timer/Save/UI/Input/ConfigTable) follow a unified three-layer structure:

| Layer | Form | Responsibility |
|------|------|------|
| `XxxService : ServiceBase` | Static facade, marked with `[HandlerHost(typeof(XxxHandler))]` + `[ServiceDependency(...)]` | All static APIs; `OnInit` triggers handler lazy-init, `OnShutdown` clears the handler |
| `XxxHandler : FrameworkHandler` | Serializable backend class | Carries the core logic; replacing the backend requires no changes to callers |
| `XxxSettings : FrameworkSettings<XxxSettings>` | ScriptableObject settings | Selects the backend implementation via `[ProviderDropdown]` + `[SerializeReference]` |

Business code always calls static facades (e.g. `AudioService.Play(...)`, `UIService.ShowUI<T>()`) without holding service instance references. Custom backends: inherit `XxxHandler`, override virtual methods → switch in the provider dropdown of `XxxSettings`.

### Lifecycle State Machine

Each service tracks its lifecycle state via `ServiceBase.State` (`EServiceState`):

| State | Description |
|-------|-------------|
| `Created` | Instance created and registered, not yet initialized |
| `Initialized` | `OnInit()` has been called; service is active |
| `ShuttingDown` | `OnShutdown()` is being called |
| `Disposed` | Service has been fully shut down and removed |

Shutdown is idempotent: driving shutdown again for an already-closed service does not repeat `OnShutdown()`.

### Dependency Declaration

Service dependencies are declared via the `[ServiceDependency(typeof(...))]` attribute (multiple types in a single attribute, similar to `RequireComponent`). The world-initialization topological sort orders and validates them — **initialization order follows the declarations, not the registration order**:

```csharp
[ServiceDependency(typeof(ResourceService), typeof(TimerService))]
public sealed class UIService : ServiceBase, IServiceTickable
{
    public override void OnInit()
    {
        // By the time we get here, ResourceService/TimerService are fully initialized
        TimerService.Delay(1f, () => { });
    }

    public override void OnShutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}
```

- Dependencies are an unordered set (the service initializes once all of them are ready); all dependency types must implement `IService`, validated at compile time by `ServiceDependencyAnalyzer` (MIRAI201/MIRAI202)
- Service instances are created solely by manual registration (the framework never instantiates services implicitly). While the world is not yet initialized, dependency validation is deferred to the `Initialize(Async)` topological-sorting phase; runtime registration after initialization requires the dependencies to be ready right then and throws `GameException` immediately otherwise
- Both missing and circular dependencies throw `GameException` (fail-fast; circular errors list the cycle members): the former during the topological sort or at runtime-registration time, the latter only during the topological sort

For runtime lazy resolution, use the static lookup methods on `GameServices`:

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        if (GameServices.TryGetService(out DebuggerService debugger)) // optional dependency; returns false if not registered
        {
            debugger.Enable();
        }
    }
}
```

### Unified Service Lookup

The single entry for dynamic service lookup is the `GameServices` static facade — service classes and non-service code use the same methods, with no provider injection:

| Method | Description |
|--------|-------------|
| `GetRequiredService<T>()` | Cross-scope lookup; throws `GameException` if not found (Gameplay > Scene > App priority); also throws when no container is built |
| `GetService<T>()` | Cross-scope lookup; returns null if not found |
| `TryGetService<T>(out T)` | Cross-scope lookup attempt; returns bool |

### Composition Root and Built-in Service Registration

The framework's composition root: `GameAppSettings.InitializeAppServices()` (called at the `AfterAssembliesLoaded` stage) registers all chain services **in any order**, then commits the second phase once:

```csharp
GameServices.RegisterService(EServiceScopeKind.App, new DebuggerService());
GameServices.RegisterService(EServiceScopeKind.App, new ResourceService());
GameServices.RegisterService(EServiceScopeKind.App, new TimerService());
// ……remaining built-in services are registered the same way; order is irrelevant
await GameServices.Default.InitializeAsync();   // drives OnInit in [ServiceDependency] topological order
```

Service instances are created solely by manual registration; `[ServiceDependency]` declarations are ordered and validated by the world-initialization topological sort (missing dependencies fail fast, registration order is irrelevant). All 13 built-in services are registered explicitly by the composition root; the `CreateDefaultHandler` path of each facade still calls `GameServices.EnsureRegistered<T>()` as a fallback, so a service touched before the composition root has run still gets registered (a shut-down `GameApp` blocks this explicitly — see Lazy Self Registration). Custom service backend implementations can be swapped in the corresponding `XxxSettings` Inspector via the provider dropdown.

### Handler Async Lifecycle

Handlers (`XxxHandler : FrameworkHandler`) support an async lifecycle: override `OnInitAsync()` / `OnShutdownAsync()`, driven explicitly by `GameAppSettings.Initiation` after synchronous initialization.

### Service Events

Service lifecycle notifications go through `IServiceInterceptor` (the event API has been removed):

```csharp
public sealed class ServiceAuditInterceptor : IServiceInterceptor
{
    public void OnServiceRegistered(IService service, Type interfaceType, EServiceScopeKind scope) =>
        Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");

    public void OnServiceUnregistered(IService service) =>
        Debug.Log($"Service unregistered: {service.GetType().Name}");
}

// Register: GameServices.AddInterceptor(new ServiceAuditInterceptor());
```

### MonoBehaviour Service

Inherit `ServiceMono<TScope>` (where `TScope` is a scope marker: `AppScope` / `SceneScope` / `GameplayScope`); `Awake` auto-registers into the corresponding scope, `OnDestroy` auto-unregisters:

```csharp
public class MyMonoService : ServiceMono<AppScope>
{
    public override void OnInit() { /* Called automatically after Awake registration */ }
    public override void OnShutdown() { /* Called automatically before OnDestroy unregistration */ }

    protected override void Update() { /* Driven by Unity's own lifecycle */ }
    // AppScope applies DontDestroyOnLoad automatically; SceneScope/GameplayScope are destroyed with the scene
}

// Just attach it to a scene object — no manual registration needed
```

Resolve dependencies with `GameServices.GetRequiredService<T>()` / `TryGetService<T>()`; duplicate registration of the same contract automatically destroys the extra GameObject (idempotent).

> **Note**: MonoBehaviour services cannot implement `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` / `IServiceGizmoDrawable` — Unity's own `Update()` / `FixedUpdate()` / `LateUpdate()` / `OnDrawGizmos()` drive those. Attempting to register such a service throws `GameException` (Gizmos have no way out: Unity calls the magic method on the component unconditionally, so the container driving it again would draw twice).

### Service Interceptors (AOP)

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100;

    public void OnBeforeScopeTick(EServiceScopeKind scope, float elapseSeconds, float realElapseSeconds)
    {
        // Start of this scope's polling frame — sampling window opens
    }

    public void OnAfterScopeTick(EServiceScopeKind scope, float elapseSeconds, float realElapseSeconds)
    {
        // End of this scope's polling frame — sampling window closes
    }

    public void OnServiceShutdown(IService service)
    {
        // Before this service's OnShutdown()
    }
}

GameServices.AddInterceptor(new ProfilingInterceptor());
```

Six interception points, all with default empty implementations:

| Method | Timing | Exception handling |
|--------|--------|--------|
| `OnServiceRegistering` | Before the contract handle enters the registry | Propagates — the only veto channel; throwing rejects the registration and leaves no trace |
| `OnServiceRegistered` | After `OnInit()` completed and the state moved to `Initialized` | Logged and isolated by the container |
| `OnServiceShutdown` | Before that service's `OnShutdown()` (state is still `Initialized` at this point) | Logged and isolated by the container |
| `OnServiceUnregistered` | After `OnShutdown()` ran and the service left the registry | Logged and isolated by the container |
| `OnBeforeScopeTick` | Before all services of that scope tick this frame | Logged and isolated by the container |
| `OnAfterScopeTick` | After all services of that scope tick this frame | Logged and isolated by the container |

- **Granularity is the contract**: an instance registered under N contracts receives N `OnServiceRegistering`/`OnServiceRegistered` calls, and the `contractType` argument is the registration contract, not the implementation type. Contracts attached before initialization get their `OnServiceRegistered` deferred until after `OnInit()`, so "Registered means usable" holds.
- **There is no per-service tick callback**: polling notifications are per scope frame; per-service timing goes through the editor diagnostics side table (`PollAvgMs` / `PollPeakMs`, compile-time gated).
- Multiple interceptors execute in `Priority` descending order (equal priorities keep insertion order). Interceptors are cleared on `GameServices.Shutdown()`.
- **An observer must not take down the host**: apart from the veto channel, any exception thrown by an interceptor is caught and logged as an Error by the container, and the remaining interceptors and the observed services carry on.

### AOT-Safe Lazy Resolution

`Func<T>` injection relies on `MakeGenericMethod`, which risks trimming under IL2CPP. All framework service lookups go through an inline binding value-type table keyed by `RuntimeTypeHandle` — zero reflection, zero boxing, naturally AOT-safe:

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        // Runtime lazy resolution: direct generic method call, no MakeGenericMethod path
        var stats = GameServices.GetRequiredService<StatsService>();
    }
}
```

### Runtime Service Registration

Dynamically add/remove individual services (mod systems, DLC hot-loading, etc.):

```csharp
// Runtime registration — dependencies must be initialized-ready (fail-fast if missing); OnInit is driven immediately after validation
GameServices.RegisterService(EServiceScopeKind.Gameplay, new BuffService());

// Explicit-contract registration — an interface as the contract key; dependencies are still read from the implementation type
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), new BuffService());

// Multi-contract binding — register one instance under multiple contracts; it initializes/shuts down only once
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), buff);
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(BuffService), buff);

// Runtime unregistration — drives OnShutdown immediately; the same contract can then be re-registered with a fresh instance
// (multi-contract instance: unregistering any one contract removes the whole instance, the rest go with it)
GameServices.UnregisterService<BuffService>(EServiceScopeKind.Gameplay);
```

> `EDeferMode` only applies to the runtime path, i.e. **after the world is initialized**: calls during iteration (Tick) default to deferring until the current cycle ends (`EDeferMode.Defer`), while `EDeferMode.Throw` throws immediately (fail-fast). Before initialization a service merely enters the pending graph and never joins a polling list, so there is no iteration conflict and the parameter is ignored. The contract type of RegisterService = the concrete `typeof(T)`; resolution must use the same type via `GetRequiredService<T>()`. Both mutating operations (unregister, close scope) fail fast while initialization is running.

#### Duplicate Contract Policy [DUPLICATE CONTRACT POLICY]

When a different instance is explicitly registered under an already-occupied contract in the same scope, the behavior is governed by `GameServices.DuplicateContractPolicy`:

| Policy | Behavior | Default |
|--------|----------|---------|
| `EDuplicateContractPolicy.Skip` | Silently discard the new instance and return the existing one | Release builds |
| `EDuplicateContractPolicy.Warn` | Log a warning then discard the new instance — accidental contract hijacking is no longer silent | Editor / development builds |
| `EDuplicateContractPolicy.Throw` | Throw `GameException` (fail-fast) | Explicit configuration |

```csharp
// Enable strict validation while investigating issues
GameServices.DuplicateContractPolicy = EDuplicateContractPolicy.Throw;
```

> Re-registering the same instance is always an idempotent skip returning the existing instance; binding another contract of an already-registered instance goes through the multi-contract path and is never treated as a duplicate. Under `Warn`/`Skip` the **discarded instance is not shut down** — it never entered the graph, so the framework cannot take over its lifecycle.

### Lazy Self Registration

Service instances have no centralized factory table (`RegisterDefaultFactory` was removed along with the default factory table). Each HandlerHost service facade's default handler creation path
(`CreateDefaultHandler`) calls `GameServices.EnsureRegistered<T>()` first — when a service is accessed through its facade while unregistered,
an instance is automatically created and registered into the App scope (idempotent):

```csharp
// First access to any facade API — the service auto-registers if unregistered; polling maintenance takes effect immediately
ObjectPoolService.Spawn(...); // ObjectPoolService is thereby registered into the App scope
```

- This is the standard `RegisterService` path, so the dependency rules are unchanged: after the world is initialized dependencies must be ready right then (otherwise `GameException`), before initialization they are deferred to the topological-sorting phase
- A shut-down state blocks lazy revival: when `GameApp` has already shut down, `EnsureRegistered` throws `GameException`, and explicit `RegisterService` is the only way to rebuild the world
- The composition root registers every built-in service explicitly, so this path is now a fallback and the convenient entry for project-side services

### Async Shutdown

Services implementing `IAsyncShutdownService` run `OnShutdownAsync()` first in **reverse activation order** (= reverse initialization order), then the synchronous `OnShutdown()`:

```csharp
public class ResourceService : ServiceBase, IAsyncShutdownService
{
    public async UniTask OnShutdownAsync()
    {
        await UnloadAllAssetsAsync(); // Async asset unloading
    }

    public override void OnShutdown() { /* Synchronous cleanup */ }
}

// Async shutdown of a single scope
await GameServices.ShutdownContainerAsync(EServiceScopeKind.Gameplay);

// Async shutdown of all scopes
await GameServices.ShutdownAsync();
```

### Runtime Debugger

The runtime debugger (DebuggerComp) Service System window displays registered services' interfaces, implementations, scopes, priorities, and tick interface implementations (data from `GameServices.GetDiagnosticInfo()`), plus the active status of each scope (`HasApp` / `HasScene` / `HasGameplay`).

Editor and development builds also track per-service polling time — average `PollAvgMs`, peak `PollPeakMs`, and sample count `PollSamples` come back with the diagnostic info; call `GameServices.ResetPollStatistics()` to clear the statistics window. Release builds collect nothing (zero overhead).

## Notes

- `GameServices` only allows calls from the main thread (enforced by an assertion in editor and development builds; not compiled at all in release builds). For background threads or async callbacks, use `MainThreadDispatcher`'s `Post`/`Send` to switch back to the main thread. An isolated world (`new ServiceWorld()`) makes no such assertion and may be used across threads for parallel tests.
- Business code always accesses framework services via static facades (e.g. `AudioService.Play(...)`, `UIService.ShowUI<T>()`); dynamic service lookup goes through `GameServices.GetRequiredService<T>()` / `TryGetService<T>()`.
- `GameServices.GetRequiredService<T>()` throws `GameException` if not registered; `GetService<T>()` returns null; `TryGetService<T>()` returns bool.
- Re-registering the same contract in the same scope is an idempotent skip (the existing instance is returned); nested dependency chains are immune to duplicate registration. Registering a *different* instance under an occupied contract follows `DuplicateContractPolicy` (warn by default in development, silent in release, configurable to Throw) — and under `Warn`/`Skip` the discarded instance is never shut down by the framework.
- A service throwing during polling: logged then rethrown immediately in the editor and development builds (fail-fast); logged and isolated in release builds so other services in the same frame keep running. If the same service fails consecutively in the same polling category beyond a threshold (default 300), it is removed from that polling list with a single summary warning (circuit breaker); its entry is preserved and re-registration fully resets it. The threshold lives on the world (`ServiceWorld.TickFailureTripThreshold`, tunable inside the framework only), so isolated worlds each keep their own copy.
- Services must derive from `ServiceBase` or `ServiceMono<TScope>`: the container drives and reads state through `IServiceLifecycle`, so a bare `IService` implementation is rejected at registration (its state could never be read as `Initialized`, and anything declaring it as a dependency would be told the dependency is not ready).
- Circular dependencies throw `GameException` during the world-initialization topological sort (the error message includes cycle members); no cycle detection happens at registration time, since under the two-phase build registration order carries no dependency meaning.
- While initialization is running (`ServiceWorld.IsInitializing`), unregistering a service or shutting down a scope is rejected with `GameException`; perform those after `Initialize`/`InitializeAsync` returns.
- MonoBehaviour services cannot implement `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` / `IServiceGizmoDrawable` — use Unity's corresponding lifecycle instead.
- When exiting Play Mode in the editor, `GameApp` automatically calls `GameServices.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Timer](Timer.md) · [GameApp](GameApp.md) · [Singleton](Singleton.md)
