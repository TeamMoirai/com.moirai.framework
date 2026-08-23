# Core Service System (@Service)

> Framework's modular base: a unified service world (`ServiceWorld`) manages construction, lifecycle, polling, and scope of all sub-services, driven by `GameApp` (MonoBehaviour).

`@Service` is the service infrastructure of the entire framework. All functional services (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `ServiceBase` that declare dependencies via constructor parameters. The `ServiceWorld` topologically sorts, constructs, and injects them in dependency order at build time; non-service code accesses services via `GameApp` cached properties (e.g. `GameApp.Audio`, `GameApp.Resource`, `GameApp.UI`) or `GameApp.Services` (`IServiceProvider`) for non-standard lookups. Services support three scopes: App/Scene/Gameplay. Cross-scope lookup uses a `ContractBindings` value-type struct for O(1) resolution (Gameplay > Scene > App priority). When a scene is unloaded, scene-level and gameplay-level services are automatically cleaned up.

## Core Features

- **Unified service world**: `ServiceWorld` holds a 3-slot fixed array (App/Scene/Gameplay); cross-scope lookup via `ContractBindings` value-type struct achieves O(1) resolution with no parent-chain traversal
- **Constructor injection**: plain C# services declare dependencies via constructor parameters (compile-time verifiable); the container resolves them automatically at build time
- **Topological sorting**: Kahn algorithm infers dependencies from constructor parameters; dependees are created and initialized before dependents; circular dependencies throw at build time
- **Three-level scope** (`EServiceScopeKind.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App priority
- **Lifecycle interfaces implemented on demand**: `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable`, `IServiceGizmoDrawable`
- **`Priority`** controls polling order (higher priority polls first, shuts down later)
- **Async initialization**: services implementing `IAsyncInitService` are initialized asynchronously in topological order by `BuildAsync()`
- **Service events**: `onServiceRegistered`/`onServiceUnregistered` events for hot-swap notifications
- **Iteration safety**: registrations during polling are deferred and applied uniformly after the current cycle ends; scope disposal requested during iteration is also deferred
- **Per-service tick exception isolation**: a single service throwing in `Tick` does not abort other services in the same frame
- **Main thread affinity guard**: asserts calling thread in editor and development builds, zero overhead in release builds
- **Lifecycle state machine**: each service tracks `EServiceState` (Created → Initialized → ShuttingDown → Disposed) with idempotent shutdown
- **MonoBehaviour Tick constraint**: MonoBehaviour services cannot implement `IServiceTickable` etc.; use Unity's own Update/FixedUpdate/LateUpdate instead
- **Built-in service lookup**: `ServiceBase` / `ServiceMonoBase` provide `Require<T>()`, `TryGet<T>()`, `RequireApp<T>()`, `RequireScene<T>()`, `RequireGameplay<T>()` protected methods for runtime dependency resolution without injecting `IServiceProvider`

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `IService` | Core service contract: `Priority`, `Scope`, `OnInit()`, `Shutdown()` |
| `ServiceBase` | Abstract base class for plain C# services; dependencies declared via constructor parameters and injected by the container; provides built-in `Require<T>()` / `TryGet<T>()` / `RequireApp<T>()` etc. |
| `ServiceMonoBase` | MonoBehaviour service base; instances created by the container via `AddComponent` and receive dependencies via `Inject(IServiceProvider)`; also provides built-in lookup methods |
| `IServiceProvider` | Unified service access entry: `GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>()` / `GetRequiredServiceInScope<T>(scope)` / `TryGetServiceInScope<T>(scope)` |
| `ServiceWorld` | Unified service world: `BuildAsync(scope, collection)` performs topological sort → constructor injection → OnInit → OnInitAsync; implements `IServiceProvider`; holds `ContractBindings` value-type struct for O(1) cross-scope lookup |
| `ServiceScope` | Per-scope registry, polling lists, and iteration safety; syncs `ServiceWorld`'s `ContractBindings` on register/unregister |
| `ServiceCollection` | Service registration collection (created in the composition root); fluent registration via `Register<TInterface, TImpl>(scope)` |
| `GameServices` | Static facade: scope management (`BuildAsync`/`ShutdownContainer`/`HasApp`/`HasScene`/`HasGameplay`), polling drivers, interceptors |
| `EServiceScopeKind` | Service scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `EServiceState` | Service lifecycle state: `Created`, `Initialized`, `ShuttingDown`, `Disposed` (`ServiceBase.State` / `ServiceMonoBase.State` property) |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | Polling interfaces with method signatures such as `Tick(float elapseSeconds, float realElapseSeconds)` (MonoBehaviour services cannot implement these) |
| `IServiceGizmoDrawable` | Editor Gizmos drawing interface `OnDrawGizmos()` |
| `IAsyncInitService` | Async initialization interface; services implementing `OnInitAsync()` are driven by `BuildAsync()` |
| `ServiceMono<TScope>` | Generic MonoBehaviour service base; scope determined at compile time via `TScope` |
| `GameApp` | MonoBehaviour entry point (`[DefaultExecutionOrder(-1000)]`); drives lifecycle and polling only; services accessed via `GameApp` cached properties (e.g. `GameApp.Audio`, `GameApp.UI`) |
| `GameAppMessageEvent` / `EMessageEventType` | Namespace `Moirai.Atropos.Events`, framework-level pooled events (focus/unfocus/quit, SDK callbacks) |

## Quick Start

```csharp
// 1. Non-service code accesses services via GameApp cached properties
ITimerService timer = GameApp.Timer;
IResourceService resource = GameApp.Resource; // null if not registered

// 2. Define a custom service — dependencies declared via constructor
public interface IMyService { void DoSomething(); }

public class MyService : ServiceBase, IMyService, IServiceTickable
{
    private readonly ITimerService _timer; // constructor injection, resolved by the container

    public MyService(ITimerService timer) => _timer = timer;

    public override int Priority => 10;              // Higher priority polls first
    public override EServiceScopeKind Scope => EServiceScopeKind.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 3. Register and build in the composition root
var collection = new ServiceCollection();
collection.Register<IMyService, MyService>(EServiceScopeKind.Gameplay);
await GameServices.BuildAsync(EServiceScopeKind.Gameplay, collection);

// 4. Shut down — services close in reverse topological order (dependents first)
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## Advanced Usage

### Lifecycle and Scope

- `GameServices.BuildAsync(scope, collection)` executes in topological order: create instances → register into scope → `OnInit()` → `OnInitAsync()`; dependees are initialized before dependents.
- `GameServices.Shutdown()` shuts down all scopes in reverse order: Gameplay → Scene → App; `GameServices.ShutdownContainer(scope)` shuts down only the specified scope.
- `GameApp` listens to `SceneManager.sceneUnloaded` and automatically shuts down `Scene` and `Gameplay` scopes when a scene is unloaded.
- The same interface can be registered with different implementations in different scopes. `IServiceProvider` lookup order is Gameplay > Scene > App (`ContractBindings.TryGetBest()`), which can be used to temporarily replace global implementations during combat.
- Building an already-built scope throws `GameException`; call `ShutdownContainer` first to rebuild.

### Lifecycle State Machine

Each service tracks its lifecycle state via `ServiceBase.State` (`EServiceState`):

| State | Description |
|-------|-------------|
| `Created` | Instance created and registered, not yet initialized |
| `Initialized` | `OnInit()` has been called; service is active |
| `ShuttingDown` | `Shutdown()` is being called |
| `Disposed` | Service has been fully shut down and removed |

Shutdown is idempotent: shutting down an already-disposed service is a no-op.

### Dependency Injection

Plain C# services declare dependencies via constructor parameters; the container resolves and injects them at build time. If a dependency is not registered, the build fails with an informative `GameException`:

```csharp
public class AudioService : ServiceBase, IAudioService
{
    private readonly IResourceService _resource;

    // IResourceService must be registered (in this scope or cross-scope) and created before this service
    public AudioService(IResourceService resource) => _resource = resource;

    public override void OnInit() { /* dependency is ready — use _resource directly */ }
}
```

For runtime lazy resolution (e.g., optional dependencies), there are two approaches:

```csharp
// Approach 1: Inject IServiceProvider itself (for external assemblies or non-ServiceBase classes)
public class BattleService : ServiceBase
{
    private readonly IServiceProvider _provider;

    public BattleService(IServiceProvider provider) => _provider = provider;

    public override void OnInit()
    {
        var debugger = _provider.GetService<IDebuggerService>(); // optional, returns null if not registered
    }
}

// Approach 2: Use ServiceBase built-in Require<T>() / TryGet<T>() (no IServiceProvider injection needed)
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        if (TryGet(out IDebuggerService debugger)) // optional dependency
        {
            debugger.Enable();
        }
    }
}
```

### Built-in Service Lookup Methods

`ServiceBase` and `ServiceMonoBase` provide the following `protected` methods for runtime dependency resolution without injecting `IServiceProvider`:

| Method | Description |
|--------|-------------|
| `Require<T>()` | Cross-scope lookup; throws `GameException` if not found (Gameplay > Scene > App priority) |
| `TryGet<T>(out T)` | Cross-scope lookup attempt; returns bool |
| `RequireApp<T>()` | Lookup in App scope only; throws `GameException` if not found |
| `RequireScene<T>()` | Lookup in Scene scope only; throws `GameException` if not found |
| `RequireGameplay<T>()` | Lookup in Gameplay scope only; throws `GameException` if not found |

### Multi-Contract Registration

A single instance can be registered under multiple interfaces via the Fluent API `.As<TExtraContract>()`:

```csharp
collection.Register<IAudioService, AudioService>(EServiceScopeKind.App)
    .As<IAudioLoader>(); // Same instance resolvable via both interfaces
```

### Composition Root and Built-in Service Registration

Built-in services are declared in `AppSettings` (the composition root) via a `ServiceCollection`; implementation types can be replaced in the Inspector. `GameAppSettings` calls `await GameServices.BuildAsync(EServiceScopeKind.App, collection)` to perform the actual build — dependency order is guaranteed by topological sorting, independent of registration order.

### Async Initialization

```csharp
public class MyResourceService : ServiceBase, IMyResourceService, IAsyncInitService
{
    public override void OnInit() { /* Synchronous quick setup */ }

    public async UniTask OnInitAsync()
    {
        await LoadCatalogAsync(); // Called in topological order after all OnInit complete
    }
}

// GameAppSettings automatically calls:
// await GameServices.BuildAsync(EServiceScopeKind.App, collection);
// await ProcedureSettings.StartProcedure();
```

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

Inherit `ServiceMono<TScope>` and override `Inject` to declare dependencies. The container creates the instance via `AddComponent` and calls `Inject(IServiceProvider)`:

```csharp
public class MyMonoService : ServiceMono<AppScope>, IMyService
{
    private IResourceService _resource;

    protected internal override void Inject(IServiceProvider provider)
    {
        _resource = provider.GetRequiredService<IResourceService>();
    }

    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope applies DontDestroyOnLoad automatically; SceneScope/GameplayScope are destroyed with the scene
}

// Register (dependencies must be declared explicitly via DependsOn to participate in topological sorting)
collection.RegisterMono<IMyService, MyMonoService>(EServiceScopeKind.App)
    .DependsOn<IResourceService>();
```

> **Note**: MonoBehaviour services cannot implement `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable`. If a MonoBehaviour service implements these interfaces, registration throws `GameException`. Use Unity's own `Update()` / `FixedUpdate()` / `LateUpdate()`.

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

### Runtime Debugger

The runtime debugger (DebuggerComp) Service System window displays registered services' interfaces, implementations, scopes, priorities, and tick interface implementations (data from `GameServices.GetDiagnosticInfo()`), plus the active status of each scope (`HasApp` / `HasScene` / `HasGameplay`).

## Notes

- `GameServices` and `IServiceProvider` only allow calls from the main thread; for background threads or async callbacks, use `MainThreadDispatcher`'s `Post`/`Send` to switch back to the main thread.
- Service classes should prefer constructor injection over `GameApp` cached properties — the latter is intended for non-service code (MonoBehaviours, UI scripts, etc.).
- `GetRequiredService<T>()` throws `GameException` if not registered; `GetService<T>()` returns null; `TryGetService<T>()` returns bool.
- Building an already-built scope throws `GameException`; call `ShutdownContainer` first to rebuild.
- Re-registering the same interface in the same scope only warns and keeps the first instance.
- Circular dependencies are detected during topological sorting in `BuildAsync()` and throw an exception.
- MonoBehaviour services cannot implement `IServiceTickable` etc. — use Unity's own Update lifecycle.
- When exiting Play Mode in the editor, `GameApp` automatically calls `GameServices.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Back to Main README](../../README_EN.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
