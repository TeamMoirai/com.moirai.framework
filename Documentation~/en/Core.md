# Core Service System (@Service)

> Framework's modular base: a unified service world (`ServiceWorld`) manages construction, lifecycle, polling, and scope of all sub-services, driven by `GameApp` (MonoBehaviour).

`@Service` is the service infrastructure of the entire framework. All functional services (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `ServiceBase` that declare dependencies via the `[ServiceDependency(typeof(...))]` attribute; `GameServices.RegisterService<T>(scope, service)` is the unified registration entry that recursively pre-registers the dependency chain (zero reflection). Non-service code accesses services through each service's static facade (e.g. `AudioService.Xxx()`, `UIService.Xxx()`, `ResourceService.Xxx()`); dynamic service lookup goes through `GameApp.Services` (`IServiceProvider`). Services support three scopes: App/Scene/Gameplay. Cross-scope lookup uses a `ContractBindings` value-type struct for O(1) resolution (Gameplay > Scene > App priority). When a scene is unloaded, scene-level and gameplay-level services are automatically cleaned up.

## Core Features

- **Unified service world**: `ServiceWorld` holds a 3-slot fixed array (App/Scene/Gameplay); cross-scope lookup via `ContractBindings` value-type struct achieves O(1) resolution with no parent-chain traversal
- **Attribute-declared dependencies**: `[ServiceDependency(typeof(DepA), typeof(DepB))]` declares multiple dependencies in a single attribute; validated at compile time by `ServiceDependencyAnalyzer` (MIRAI002/MIRAI003) to ensure types implement `IService`
- **Recursive pre-registration**: `RegisterWithDependencies` recursively registers dependencies in `[ServiceDependency]` declaration order (dedup via the s_Registered bucket table + cycle detection via the s_InFlight stack fail-fast); dependees are created and initialized before dependents
- **HandlerHost static facades**: all 12 framework services follow the `[HandlerHost] XxxService : ServiceBase` static facade + serializable `XxxHandler` backend + `XxxSettings` (`[SerializeReference]` + `[ProviderDropdown]`) backend-selection pattern
- **Three-level scope** (`EServiceScopeKind.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App priority
- **Lifecycle interfaces implemented on demand**: `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable`, `IServiceGizmoDrawable`
- **`Priority`** controls polling order (higher priority polls first, shuts down later)
- **Async initialization**: services implementing `IAsyncInitService` get `OnInitAsync()` driven by the container after registration
- **Async shutdown**: services implementing `IAsyncShutdownService` are shut down asynchronously in reverse registration order by `ShutdownContainerAsync()` / `ShutdownAsync()`
- **Runtime service registration**: `GameServices.RegisterService<T>()` / `UnregisterService<T>()` dynamically add/remove individual services; the explicit-contract overload `RegisterService(scope, Type, instance)` supports interface contracts and multi-contract binding of one instance; calls during iteration default to deferring until the current cycle ends (`EDeferMode.Defer`)
- **Self-registering Mono service**: `ServiceMono<TScope>` auto-registers in Awake and auto-unregisters in OnDestroy
- **Scope order constants**: `ServiceScopeOrder` explicitly defines App/Scene/Gameplay sorting priority
- **Service events**: `onServiceRegistered`/`onServiceUnregistered` events for hot-swap notifications
- **Iteration safety**: registrations/unregistrations during polling are deferred and applied uniformly after the current cycle ends; scope disposal requested during iteration is also deferred
- **Tiered tick exception policy**: in the editor and development builds, exceptions are logged then rethrown immediately (fail-fast, surfacing defects at once); in release builds they are logged and isolated so a single faulty service does not abort other services in the same frame
- **Main thread affinity guard**: asserts calling thread in editor and development builds, zero overhead in release builds
- **Lifecycle state machine**: each service tracks `EServiceState` (Created → Initialized → ShuttingDown → Disposed) with idempotent shutdown
- **MonoBehaviour Tick constraint**: MonoBehaviour services cannot implement `IServiceTickable` etc.; use Unity's own Update/FixedUpdate/LateUpdate instead
- **Built-in service lookup**: `ServiceBase` / `ServiceMono<TScope>` provide `Require<T>()`, `TryGet<T>()`, `RequireApp<T>()`, `RequireScene<T>()`, `RequireGameplay<T>()` protected methods for runtime dependency resolution without injecting `IServiceProvider`

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `IService` | Core service contract: `Priority`, `Scope`, `OnInit()`, `Shutdown()` |
| `ServiceBase` | Abstract base class for plain C# services; dependencies declared via `[ServiceDependency]` attribute and pre-registered recursively; provides built-in `Require<T>()` / `TryGet<T>()` / `RequireApp<T>()` etc. |
| `ServiceMono<TScope>` | MonoBehaviour service base (generic scope marker); auto-registers in Awake, auto-unregisters in OnDestroy; also provides built-in lookup methods |
| `IServiceProvider` | Unified service access entry: `GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>()` / `GetRequiredServiceInScope<T>(scope)` / `TryGetServiceInScope<T>(scope)` |
| `ServiceWorld` | Unified service world: 3-slot fixed scope array + `ContractBindings` value-type struct for O(1) cross-scope lookup; implements `IServiceProvider` |
| `ServiceScope` | Per-scope registry, polling lists, and iteration safety; syncs `ServiceWorld`'s `ContractBindings` on register/unregister |
| `GameServices` | Static facade: unified registration entry `RegisterService<T>(scope, service, deferMode)` and explicit-contract overload `RegisterService(scope, Type, instance)`, unregistration, scope management (`ShutdownContainer`/`HasApp`/`HasScene`/`HasGameplay`), default-factory extension point `RegisterDefaultFactory`, polling drivers, interceptors |
| `ServiceDependencyAttribute` | Dependency declaration attribute: `[ServiceDependency(typeof(DepA), typeof(DepB))]`; declaration order is dependency registration order; compile-time validated by MIRAI002/MIRAI003 |
| `EServiceScopeKind` | Service scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `EServiceState` | Service lifecycle state: `Created`, `Initialized`, `ShuttingDown`, `Disposed` (`ServiceBase.State` property) |
| `EDeferMode` | Deferral policy for registration/unregistration during iteration: `Defer` (defer until cycle ends, default) / `Throw` (throw immediately) |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | Polling interfaces with method signatures such as `Tick(float elapseSeconds, float realElapseSeconds)` (MonoBehaviour services cannot implement these) |
| `IServiceGizmoDrawable` | Editor Gizmos drawing interface `OnDrawGizmos()` |
| `IAsyncInitService` | Async initialization interface; services implementing `OnInitAsync()` are driven by the container after registration |
| `IAsyncShutdownService` | Async shutdown interface; services implementing `OnShutdownAsync()` are shut down in reverse registration order by `ShutdownContainerAsync()` |
| `FrameworkHandler` | Handler base class (`[Serializable]`): idempotent `Internal_Init`/`Internal_Shutdown` + sync/async lifecycle callbacks; base of all XxxHandler classes |
| `ServiceScopeOrder` | Scope priority constants (App=-10000, Scene=-5000, Gameplay=0) |
| `GameApp` | MonoBehaviour entry point (`[DefaultExecutionOrder(-1000)]`); drives `GameAppSettings.Initiation` and `GameServices.Shutdown`; `GameApp.Services` is for dynamic service lookup only |
| `GameAppMessageEvent` / `EMessageEventType` | Namespace `Moirai.Atropos.Events`, framework-level pooled events (focus/unfocus/quit, SDK callbacks) |

## Quick Start

```csharp
// 1. Business code accesses framework services via static facades
TimerService.AddTimer(() => Debug.Log("1s"), 1f);
UIService.ShowUI<MainWindow>();
ResourceService.LoadAsset<Sprite>("Assets/AssetRaw/UI/icon.png");

// 2. Define a custom service — dependencies declared via the [ServiceDependency] attribute
[ServiceDependency(typeof(TimerService))]
public class MyService : ServiceBase, IServiceTickable
{
    public override int Priority => 10;              // Higher priority polls first

    public override void OnInit()
    {
        TimerService.AddTimer(() => { /* dependencies ready — use static facades directly */ }, 1f);
    }

    public override void Shutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 3. Register into a scope — the dependency chain is recursively pre-registered (order-independent)
GameServices.RegisterService(EServiceScopeKind.Gameplay, new MyService());

// 4. Shut down — services close in reverse registration order (dependents first)
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## Advanced Usage

### Lifecycle and Scope

- `GameServices.RegisterService<T>(scope, service)` is the unified registration entry: dependencies are recursively pre-registered first in `[ServiceDependency]` declaration order (dependency instances are created by the default factory table, extensible by hosts via `RegisterDefaultFactory`), then the current service is registered and its `OnInit()` driven immediately; dependees are initialized before dependents. Dependency declarations are always read from the implementation type — registering with an interface as the contract still auto-assembles the dependency chain.
- `GameServices.Shutdown()` shuts down all scopes in reverse order: Gameplay → Scene → App; `GameServices.ShutdownContainer(scope)` shuts down only the specified scope.
- `GameApp` listens to `SceneManager.sceneUnloaded` and automatically shuts down `Scene` and `Gameplay` scopes when a scene is unloaded.
- The same contract can be registered with different implementations in different scopes. `IServiceProvider` lookup order is Gameplay > Scene > App (`ContractBindings.TryGetBest()`), which can be used to temporarily replace global implementations during combat.
- Registration is idempotent: re-registering the same contract in the same scope is skipped (the existing instance is returned); circular dependencies throw `GameException` at registration time (fail-fast).

### HandlerHost Service Architecture

All 12 built-in framework services (UpdateDriver/Resource/Debugger/Audio/ObjectPool/Procedure/Localization/Scene/Timer/Save/UI/Input) follow a unified three-layer structure:

| Layer | Form | Responsibility |
|------|------|------|
| `XxxService : ServiceBase` | Static facade, marked with `[HandlerHost(typeof(XxxHandler))]` + `[ServiceDependency(...)]` | All static APIs; `OnInit` triggers handler lazy-init, `Shutdown` clears the handler |
| `XxxHandler : FrameworkHandler` | Serializable backend class | Carries the core logic; replacing the backend requires no changes to callers |
| `XxxSettings : FrameworkSettings<XxxSettings>` | ScriptableObject settings | Selects the backend implementation via `[ProviderDropdown]` + `[SerializeReference]` |

Business code always calls static facades (e.g. `AudioService.Play(...)`, `UIService.ShowUI<T>()`) without holding service instance references. Custom backends: inherit `XxxHandler`, override virtual methods → switch in the provider dropdown of `XxxSettings`.

### Lifecycle State Machine

Each service tracks its lifecycle state via `ServiceBase.State` (`EServiceState`):

| State | Description |
|-------|-------------|
| `Created` | Instance created and registered, not yet initialized |
| `Initialized` | `OnInit()` has been called; service is active |
| `ShuttingDown` | `Shutdown()` is being called |
| `Disposed` | Service has been fully shut down and removed |

Shutdown is idempotent: shutting down an already-disposed service is a no-op.

### Dependency Declaration

Service dependencies are declared via the `[ServiceDependency(typeof(...))]` attribute (multiple types in a single attribute, similar to `RequireComponent`); the registrar recursively pre-registers them — unregistered dependencies are registered first, then the current service:

```csharp
[ServiceDependency(typeof(ResourceService), typeof(TimerService))]
public sealed class UIService : ServiceBase, IServiceTickable
{
    public override void OnInit()
    {
        // By the time we get here, ResourceService/TimerService are fully initialized
        TimerService.AddTimer(() => { }, 1f);
    }

    public override void Shutdown() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}
```

- Declaration order is dependency registration order; all dependency types must implement `IService`, validated at compile time by `ServiceDependencyAnalyzer` (MIRAI002/MIRAI003)
- Dependency instances are created by the framework's factory table (zero reflection); if a custom service's dependency is not a built-in framework type, it must be explicitly registered before its dependents
- Circular dependencies throw `GameException` at registration time

For runtime lazy resolution, use the `Require<T>()` / `TryGet<T>()` methods built into `ServiceBase`:

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        if (TryGet(out DebuggerService debugger)) // optional dependency; returns false if not registered
        {
            debugger.Enable();
        }
    }
}
```

### Built-in Service Lookup Methods

`ServiceBase` and `ServiceMono<TScope>` provide the following `protected` methods for runtime dependency resolution without injecting `IServiceProvider`:

| Method | Description |
|--------|-------------|
| `Require<T>()` | Cross-scope lookup; throws `GameException` if not found (Gameplay > Scene > App priority) |
| `TryGet<T>(out T)` | Cross-scope lookup attempt; returns bool |
| `RequireApp<T>()` | Lookup in App scope only; throws `GameException` if not found |
| `RequireScene<T>()` | Lookup in Scene scope only; throws `GameException` if not found |
| `RequireGameplay<T>()` | Lookup in Gameplay scope only; throws `GameException` if not found |

### Composition Root and Built-in Service Registration

The framework's composition root is minimal: `GameAppSettings.InitializeAppServices()` (called at the `AfterAssembliesLoaded` stage) explicitly registers only the procedure chain root service:

```csharp
GameServices.RegisterService(EServiceScopeKind.App, new ProcedureService());
await ProcedureServiceSettings.StartProcedure();
```

The other 11 framework services are all pulled up automatically via the `[ServiceDependency]` dependency chain — zero reflection, order-independent, compile-time type-safe. Custom service backend implementations can be swapped in the corresponding `XxxSettings` Inspector via the provider dropdown.

### Async Initialization

```csharp
public class MyResourceService : ServiceBase, IAsyncInitService
{
    public override void OnInit() { /* Synchronous quick setup */ }

    public async UniTask OnInitAsync()
    {
        await LoadCatalogAsync(); // Driven by the container after registration
    }
}
```

Handlers (`XxxHandler : FrameworkHandler`) also support an async lifecycle: override `OnInitAsync()` / `OnShutdownAsync()`, driven explicitly by `GameAppSettings.Initiation` after synchronous initialization.

### Service Events

```csharp
GameServices.onServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

GameServices.onServiceUnregistered += (service) =>
{
    Debug.Log($"Service unregistered: {service.GetType().Name}");
};
```

### MonoBehaviour Service

Inherit `ServiceMono<TScope>` (where `TScope` is a scope marker: `AppScope` / `SceneScope` / `GameplayScope`); `Awake` auto-registers into the corresponding scope, `OnDestroy` auto-unregisters:

```csharp
public class MyMonoService : ServiceMono<AppScope>
{
    public override void OnInit() { /* Called automatically after Awake registration */ }
    public override void Shutdown() { /* Called automatically before OnDestroy unregistration */ }

    protected override void Update() { /* Driven by Unity's own lifecycle */ }
    // AppScope applies DontDestroyOnLoad automatically; SceneScope/GameplayScope are destroyed with the scene
}

// Just attach it to a scene object — no manual registration needed
```

Resolve dependencies with the built-in `Require<T>()` / `TryGet<T>()` methods; duplicate registration of the same contract automatically destroys the extra GameObject (idempotent).

> **Note**: MonoBehaviour services cannot implement `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` — they are driven by Unity's own `Update()` / `FixedUpdate()` / `LateUpdate()`.

### Service Interceptors (AOP)

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100;

    public void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds)
    {
        // Profiling before each Tick
    }

    public void OnServiceShutdown(IService service)
    {
        // Cleanup before Shutdown
    }
}

GameServices.AddInterceptor(new ProfilingInterceptor());
```

Five interception points with default empty implementations:

| Method | Timing |
|--------|--------|
| `OnServiceRegistering` | Before `OnInit()` — can throw to reject registration |
| `OnServiceRegistered` | After `OnInit()` and state transition to `Initialized` |
| `OnServiceUnregistered` | After `Shutdown()` has been called and the service removed from the registry |
| `OnServiceTick` | Before each `Tick()` call (Update path only) |
| `OnServiceShutdown` | Before `Shutdown()` call |

Multiple interceptors execute in `Priority` descending order. Interceptors are cleared on `GameServices.Shutdown()`.

### AOT-Safe Lazy Resolution

`Func<T>` injection relies on `MakeGenericMethod`, which risks trimming under IL2CPP. All framework service lookups go through the `ContractBindings` value-type table keyed by `RuntimeTypeHandle` — zero reflection, zero boxing, naturally AOT-safe:

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        // Runtime lazy resolution: direct generic method call, no MakeGenericMethod path
        var stats = Require<StatsService>();
    }
}
```

### Runtime Service Registration

Dynamically add/remove individual services (mod systems, DLC hot-loading, etc.):

```csharp
// Runtime registration — drives OnInit immediately; the dependency chain is recursively pre-registered
GameServices.RegisterService(EServiceScopeKind.Gameplay, new BuffService());

// Explicit-contract registration — an interface as the contract key; dependencies are still read from the implementation type
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), new BuffService());

// Multi-contract binding — register one instance under multiple contracts; it initializes/shuts down only once
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(IBuffService), buff);
GameServices.RegisterService(EServiceScopeKind.Gameplay, typeof(BuffService), buff);

// Runtime unregistration — drives Shutdown immediately; the same contract can then be re-registered with a fresh instance
GameServices.UnregisterService<BuffService>(EServiceScopeKind.Gameplay);
```

> Calls during iteration (Tick) default to deferring until the current cycle ends (`EDeferMode.Defer`); pass `EDeferMode.Throw` to throw immediately (fail-fast). The contract type of RegisterService = the concrete `typeof(T)`; resolution must use the same type via `GetRequiredService<T>()`.

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

> Re-registering the same instance is always an idempotent skip returning the existing instance; dependency-chain auto pre-registration dedup is always silent — neither is affected by this policy.

### Default Factory Extension

The framework's built-in service factory table lives in `GameServices.Factories.cs`. Host projects can contribute factories
for their own services so that `[ServiceDependency]` chains auto-assemble across assemblies:

```csharp
// Contribute a default factory (registering a duplicate type fails fast with GameException)
GameServices.RegisterDefaultFactory(typeof(QuestService), static () => new QuestService());

// Any service declaring [ServiceDependency(typeof(QuestService))] now auto-pre-registers it
```

### Async Shutdown

Services implementing `IAsyncShutdownService` are first shut down asynchronously via `OnShutdownAsync()` in reverse registration order, then synchronously via `Shutdown()`:

```csharp
public class ResourceService : ServiceBase, IAsyncShutdownService
{
    public async UniTask OnShutdownAsync()
    {
        await UnloadAllAssetsAsync(); // Async asset unloading
    }

    public override void Shutdown() { /* Synchronous cleanup */ }
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

- `GameServices` and `IServiceProvider` only allow calls from the main thread; for background threads or async callbacks, use `MainThreadDispatcher`'s `Post`/`Send` to switch back to the main thread.
- Business code always accesses framework services via static facades (e.g. `AudioService.Play(...)`, `UIService.ShowUI<T>()`); `GameApp.Services` (`IServiceProvider`) is for Gameplay/Scene dynamic service lookup only.
- `GetRequiredService<T>()` throws `GameException` if not registered; `GetService<T>()` returns null; `TryGetService<T>()` returns bool.
- Re-registering the same contract in the same scope is an idempotent skip (the existing instance is returned); nested dependency chains are immune to duplicate registration. Registering a *different* instance under an occupied contract follows `DuplicateContractPolicy` (warn by default in development, silent in release, configurable to Throw).
- A service throwing during polling: logged then rethrown immediately in the editor and development builds (fail-fast); logged and isolated in release builds so other services in the same frame keep running. If the same service fails consecutively in the same polling category beyond a threshold (default 300, tunable via `ServiceScope.s_TickFailureTripThreshold`), it is removed from that polling list with a single summary warning (circuit breaker); its entry is preserved and re-registration fully resets it.
- Circular dependencies are detected at registration time in `RegisterWithDependencies` and throw an exception (fail-fast).
- MonoBehaviour services cannot implement `IServiceTickable` etc. — use Unity's own Update lifecycle.
- When exiting Play Mode in the editor, `GameApp` automatically calls `GameServices.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Back to Main README](../../README_EN.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)

---

## Handler Host (HandlerHost)

> Via `[HandlerHost]` attribute + source generator, static utility classes automatically get a thread-safe Handler property with lazy initialization.

The framework's 7 utility facades (`LogUtility`, `SettingUtility`, `VersionUtility`, `JsonUtility`, `ObjectUtility`, `StringUtility`, `TweenUtility`) all follow the unified handler host pattern:

- **`[HandlerHost(typeof(XxxHandler))]`** marks a `static partial class`; the source generator generates a `Handler` property (`volatile` + `Interlocked` thread-safe get/set)
- **`FrameworkHandler`** is the unified base class for all handler abstract base classes, providing `Internal_Init()` / `Internal_Shutdown()` idempotent lifecycle and `OnInit()` / `OnShutdown()` virtual callbacks
- Users provide a `private static XxxHandler CreateDefaultHandler()` factory method in the partial class, called automatically on first access to `Handler`
- When `CreateDefaultHandler` is missing, the compiler reports **MIRAI001** warning (IDE provides a quick fix to generate the method); `Handler.get` throws `InvalidOperationException` at runtime if accessed without explicit assignment
- Setting `Handler` to `null` throws `ArgumentNullException` (fail-fast)
- The `s_Handler` field is `private`; partial classes of the same type can access it directly

### Usage

```csharp
[HandlerHost(typeof(LogHandler))]
public static partial class LogUtility
{
    private static LogHandler CreateDefaultHandler()
    {
#if ZLOGGER_INSTALLED
        return new ZLoggerHandler();
#else
        return new DefaultLogHandler();
#endif
    }

    // ... facade methods invoke via Handler
    public static void Info(string msg) => Handler.Log(/* ... */);
}
```

### Source Generator Output

The source generator produces `{ClassName}.g.cs` for each class marked with `[HandlerHost]`, containing:

| Member | Description |
|--------|-------------|
| `s_Handler` | `private static volatile` handler field |
| `s_DefaultFactory` | `private static Func<T>` = `CreateDefaultHandler` (generated when the method exists) |
| `Handler` | `public static` property: get lazy-inits via Interlocked; set replaces and shuts down the previous handler |
| `Handler.set` | Inits the new handler → `Interlocked.Exchange` → calls `Internal_Shutdown()` on the previous handler |

### Handler Inheritance Hierarchy

```
FrameworkHandler (abstract)
├── OnInit() / OnShutdown()  — virtual callbacks
├── Internal_Init() / Internal_Shutdown()  — idempotent lifecycle entry
├── LogHandler : FrameworkHandler
├── SettingHandler : FrameworkHandler
├── VersionHandler : FrameworkHandler
├── JsonHandler : FrameworkHandler
├── ObjectHandler : FrameworkHandler
├── StringHandler : FrameworkHandler
├── TweenHandler : FrameworkHandler  (overrides Internal_Init to register TweenManager)
└── InputServiceHandler : FrameworkHandler
```
