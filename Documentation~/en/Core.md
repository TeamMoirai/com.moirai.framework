# Core Service System (@Service)

> Framework's modular base: manage construction, lifecycle, polling, and scope of all sub-services with dependency injection containers, driven by `GameApp` (MonoBehaviour).

`@Service` is the service infrastructure of the entire framework. All functional services (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `ServiceBase` that declare dependencies via constructor parameters. The `ServiceContainer` topologically sorts, constructs, and injects them in dependency order at build time; non-service code accesses services via `GameApp` cached properties (e.g. `GameApp.Audio`, `GameApp.Resource`, `GameApp.UI`) or `GameApp.Services` (`IServiceProvider`) for non-standard lookups. Services support three scopes: App/Scene/Gameplay. Cross-scope lookup follows the Gameplay → Scene → App provider chain. When a scene is unloaded, scene-level and gameplay-level services are automatically cleaned up.

## Core Features

- Constructor injection: plain C# services declare dependencies via constructor parameters (compile-time verifiable); the container resolves them automatically at build time
- Scoped containers: `ServiceContainer` handles construction, injection, initialization, and reverse-order disposal; the App ← Scene ← Gameplay parent chain enables cross-scope shadowed lookup
- Three-level scope (`EServiceScopeKind.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App shadowing order
- Topological sorting: dependencies are inferred from constructor parameters; dependees are created and initialized before dependents; circular dependencies throw at build time
- Lifecycle interfaces implemented on demand: `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable`, `IServiceGizmoDrawable`
- `Priority` controls polling order (higher priority polls first, shuts down later)
- Async initialization: services implementing `IAsyncInitService` are initialized asynchronously in topological order by `ServiceContainer.BuildAsync()`
- Service events: `ServiceRegistered`/`ServiceUnregistered` events for hot-swap notifications
- Iteration safety: registrations/unregistrations during polling are deferred and applied uniformly after the current cycle ends
- Main thread affinity guard: asserts calling thread in editor and development builds, zero overhead in release builds
- Lifecycle state machine: each service tracks `EServiceState` (Created → Initialized → ShuttingDown → Disposed) with idempotent shutdown
- Per-service tick exception isolation: a single service throwing in `Tick` does not abort other services in the same frame

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `IService` | Core service contract: `Priority`, `Scope`, `State`, `OnInit()`, `Shutdown()` |
| `ServiceBase` | Abstract base class for plain C# services; dependencies are declared via constructor parameters and injected by the container |
| `IServiceProvider` | Unified service access entry: `GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>()` |
| `ServiceCollection` | Service registration collection (created in the composition root); fluent registration via `Register<TInterface, TImpl>(scope)` |
| `ServiceContainer` | Scoped container: `BuildAsync()` performs topological sort → constructor injection → OnInit → OnInitAsync |
| `GameServices` | Static facade: container management (`BuildContainer`/`ShutdownContainer`), polling drivers, interceptors |
| `EServiceScopeKind` | Service scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `EServiceState` | Service lifecycle state: `Created`, `Initialized`, `ShuttingDown`, `Disposed` |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | Polling interfaces with method signatures such as `Tick(float elapseSeconds, float realElapseSeconds)` |
| `IServiceGizmoDrawable` | Editor Gizmos drawing interface `OnDrawGizmos()` |
| `IAsyncInitService` | Async initialization interface; services implementing `OnInitAsync()` are driven by `BuildAsync()` |
| `ServiceMono<TScope>` | MonoBehaviour service base; instances are created by the container via `AddComponent` and receive dependencies via `Inject(IServiceProvider)` |
| `GameApp` | MonoBehaviour entry point (`[DefaultExecutionOrder(-1000)]`); drives lifecycle and polling only; services are accessed via `GameApp` cached properties (e.g. `GameApp.Audio`, `GameApp.UI`) |
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

// 3. Register in the composition root (AppSettings.BuildServiceCollection, or scene/gameplay init code)
var collection = new ServiceCollection();
collection.Register<IMyService, MyService>(EServiceScopeKind.Gameplay);
GameServices.BuildContainer(EServiceScopeKind.Gameplay, collection, parent: GameServices.SceneContainer);

// 4. Build the container (create instances → constructor injection → OnInit → OnInitAsync)
await GameServices.GameplayContainer.BuildAsync();

// 5. Shut down the container — services close in reverse topological order (dependents first)
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## Advanced Usage

### Lifecycle and Scope

- `ServiceContainer.BuildAsync()` executes in topological order: create instances → register into scope → `OnInit()` → `OnInitAsync()`; dependees are initialized before dependents.
- `GameServices.Shutdown()` shuts down all containers in reverse order: Gameplay → Scene → App; `GameServices.ShutdownContainer(scope)` shuts down only the specified scope.
- `GameApp` listens to `SceneManager.sceneUnloaded` and automatically shuts down `Scene` and `Gameplay` containers when a scene is unloaded.
- The same interface can be registered with different implementations in different scopes. `IServiceProvider` lookup order is Gameplay > Scene > App (parent-chain shadowing), which can be used to temporarily replace global implementations during combat.

### Lifecycle State Machine

Each service tracks its lifecycle state via `ServiceBase.State` (`EServiceState`):

| State | Description |
|-------|-------------|
| `Created` | Instance created and registered, not yet initialized |
| `Initialized` | `OnInit()` has been called; service is active |
| `ShuttingDown` | `Shutdown()` is being called (or about to be) |
| `Disposed` | Service has been fully shut down and removed |

Shutdown is idempotent: shutting down an already-disposed service is a no-op.

### Dependency Injection

Plain C# services declare dependencies via constructor parameters; the container resolves and injects them at build time. If a dependency is not registered, the build fails with an informative `GameException`:

```csharp
public class AudioService : ServiceBase, IAudioService
{
    private readonly IResourceService _resource;

    // IResourceService must be registered (in this container or a parent) and created before this service
    public AudioService(IResourceService resource) => _resource = resource;

    public override void OnInit() { /* dependency is ready — use _resource directly */ }
}
```

To resolve lazily at runtime (e.g., optional dependencies), inject `IServiceProvider` itself:

```csharp
public class BattleService : ServiceBase
{
    private readonly IServiceProvider _provider;

    public BattleService(IServiceProvider provider) => _provider = provider;

    public override void OnInit()
    {
        // Optional dependency — returns null when not registered, no exception
        var debugger = _provider.GetService<IDebuggerService>();
    }
}
```

### Multi-Contract Registration

A single instance can be registered under multiple interfaces via the Fluent API `.As<TExtraContract>()`. All contracts share the same instance, and topological sorting recognizes dependencies through extra contract types:

```csharp
// AudioService implements both IAudioService and IAudioLoader
collection.Register<IAudioService, AudioService>(EServiceScopeKind.App)
    .As<IAudioLoader>(); // Same instance resolvable via both interfaces

// Any service depending on IAudioLoader is also correctly topologically sorted
public class AssetLoader : ServiceBase
{
    public AssetLoader(IAudioLoader audioLoader) { ... } // Resolves to the same AudioService instance
}
```

### Composition Root and Built-in Service Registration

Built-in services are declared in `AppSettings` (the composition root) via a `ServiceCollection`; implementation types can be replaced in the Inspector (e.g., replacing the implementation class of `ITimerService`). `GameApp.Awake` calls `AppContainer.BuildAsync()` to perform the actual build — dependency order is guaranteed by topological sorting, independent of registration order.

### Framework Events (GameAppMessageEvent)

`GameApp` triggers framework events in engine callbacks (namespace `Moirai.Atropos.Events`):

```csharp
// Automatically triggered by GameApp on focus/unfocus/quit:
// EMessageEventType.ApplicationFocus / NotApplicationFocus / ApplicationQuit
GameAppMessageEvent.Trigger(EMessageEventType.ApplicationQuit);

// Subscribe via EventManager (pooled events, zero GC dispatch)
EventManager.RegisterCallback<GameAppMessageEvent>(OnMessageEvent);
```

### Editor Tools

Menu `Window/Moirai/Service System` opens the service system window, displaying registered services' interfaces, implementations, scopes, priorities, and lifecycle interface implementations (data from `GameServices.GetDiagnosticInfo()`), plus the active status of each scope container.

### Async Initialization

Services implementing `IAsyncInitService` perform async initialization in topological order after all synchronous initialization completes:

```csharp
public class MyResourceService : ServiceBase, IMyResourceService, IAsyncInitService
{
    public override void OnInit()
    {
        // Synchronous quick setup (called during BuildAsync)
    }

    public async UniTask OnInitAsync()
    {
        // Async loading (called in topological order after all OnInit complete)
        await LoadCatalogAsync();
    }
}

// GameApp.Awake automatically calls:
// await GameServices.AppContainer.BuildAsync();
// await ProcedureSettings.StartProcedure();
```

### Service Events

```csharp
GameServices.ServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

GameServices.ServiceUnregistered += (service) =>
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
        // MonoBehaviours cannot use constructors — obtain dependencies via Inject
        _resource = provider.GetRequiredService<IResourceService>();
    }

    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope containers apply DontDestroyOnLoad automatically; SceneScope/GameplayScope are destroyed with the scene
}

// Register (dependencies must be declared explicitly via DependsOn to participate in topological sorting)
collection.RegisterMono<IMyService, MyMonoService>(EServiceScopeKind.App)
    .DependsOn<IResourceService>();
```

### Service Interceptors (AOP)

Implement `IServiceInterceptor` to inject cross-cutting concerns (logging, profiling, caching, etc.) at lifecycle points:

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100; // Higher = executes first

    public void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds)
    {
        // Profiling before each Tick
    }

    public void OnServiceShutdown(IService service)
    {
        // Cleanup before Shutdown
    }
}

// Register interceptor
GameServices.AddInterceptor(new ProfilingInterceptor());
```

Five interception points with default empty implementations — implement only what you need:

| Method | Timing |
|--------|--------|
| `OnServiceRegistering` | Before `OnInit()` — can throw to reject registration |
| `OnServiceRegistered` | After `OnInit()` and state transition to `Initialized` |
| `OnServiceUnregistered` | After `Shutdown()` has been called and the service removed from the registry |
| `OnServiceTick` | Before each `Tick()` call (Update path only) |
| `OnServiceShutdown` | Before `Shutdown()` call |

Multiple interceptors execute in `Priority` descending order. Interceptors are cleared on `GameServices.Shutdown()`.

## Notes

- `GameServices` and `IServiceProvider` only allow calls from the main thread; for background threads or async callbacks, use `MainThreadDispatcher`'s `Post`/`Send` to switch back to the main thread.
- Service classes should prefer constructor injection over `GameApp` cached properties — the latter is intended for non-service code (MonoBehaviours, UI scripts, etc.).
- `GetRequiredService<T>()` throws `GameException` if not registered; `GetService<T>()` returns null; `TryGetService<T>()` returns bool.
- Calling `ServiceContainer.BuildAsync()` twice throws `GameException`; re-registering the same interface in the same scope only warns and keeps the first instance.
- Circular dependencies are detected during topological sorting in `BuildAsync()` and throw an exception.
- When exiting Play Mode in the editor, `GameApp` automatically calls `GameServices.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Back to Main README](../../README_EN.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
