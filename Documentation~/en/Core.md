# Core Service System (@Service)

> Framework's modular base: manage the lifecycle, polling, and scope of all sub-services with plain C# classes, driven by `GameApp` (MonoBehaviour).

`@Service` is the service infrastructure of the entire framework. All functional services (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `ServiceBase`, uniformly registered, looked up, and destroyed by the static class `GameServices`. `GameApp` serves as the engine entry point, driving service polling in `Update`/`FixedUpdate`/`LateUpdate`, and provides static accessors such as `GameApp.Timer`. Services support three scopes: App/Scene/Gameplay. When a scene is unloaded, scene-level and gameplay-level services can be automatically cleaned up.

## Core Features

- Plain C# services: not MonoBehaviour, no scene dependency, lifecycle precisely controlled by the framework
- Three-level scope (`EServiceScopeKind.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App shadowing order
- Lifecycle interfaces implemented on demand: `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable`, `IServiceGizmoDrawable`
- `Priority` controls polling order (higher priority polls first, shuts down later)
- Async initialization: services implementing `IAsyncInitService` are initialized asynchronously via `GameServices.InitializeAsync()`
- Service events: `ServiceRegistered`/`ServiceUnregistered` events for hot-swap notifications
- Iteration safety: registrations/unregistrations during polling are deferred and applied uniformly after the current cycle ends
- Main thread affinity guard: asserts calling thread in editor and development builds, zero overhead in release builds
- Dependency validation: services can declare dependencies via `Dependencies` property; unmet dependencies throw at registration time
- Lifecycle state machine: each service tracks `EServiceState` (Created → Initialized → ShuttingDown → Disposed) with idempotent shutdown
- Per-service tick exception isolation: a single service throwing in `Tick` does not abort other services in the same frame

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `IService` | Core service contract: `Priority`, `Scope`, `OnInit()`, `Shutdown()` |
| `ServiceBase` | Abstract base class for services; defines `OnInit()` / `Shutdown()` / `Priority` / `Scope` / `State` / `Dependencies`, and provides `Require<T>()` / `TryGet<T>(out T)` for cross-service dependency resolution |
| `GameServices` | Static service management center: registration, retrieval, unregistration, polling driver, and scope shutdown |
| `EServiceScopeKind` | Service scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `EServiceState` | Service lifecycle state: `Created`, `Initialized`, `ShuttingDown`, `Disposed` |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | Polling interfaces with method signatures such as `Tick(float elapseSeconds, float realElapseSeconds)` |
| `IServiceGizmoDrawable` | Editor Gizmos drawing interface `OnDrawGizmos()` |
| `IAsyncInitService` | Async initialization interface; services implementing `OnInitAsync()` are driven by `GameServices.InitializeAsync()` |
| `ServiceMono<TScope>` | MonoBehaviour service base; auto-registers on Awake via `RegisterAs` contract, App scope auto DontDestroyOnLoad |
| `GameApp` | MonoBehaviour entry point (`[DefaultExecutionOrder(-1000)]`), holds all built-in service static accessors and drives `GameServices` |
| `GameAppMessageEvent` / `EMessageEventType` | Namespace `Moirai.Atropos.Events`, framework-level pooled events (focus/unfocus/quit, SDK callbacks) |

## Quick Start

```csharp
// 1. Access built-in services via GameApp static accessors
ITimerService timer = GameApp.Timer;
IResourceService resource = GameApp.Resource;

// 2. Get service by interface via GameServices (throws GameException if not registered)
var service = GameServices.GetService<ITimerService>();

// 3. Define a custom service
public interface IMyService { void DoSomething(); }

public class MyService : ServiceBase, IMyService, IServiceTickable
{
    public override int Priority => 10;              // Higher priority polls first
    public override EServiceScopeKind Scope => EServiceScopeKind.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 4. Explicit registration
IMyService my = GameServices.RegisterService<IMyService>(new MyService());

// 5. Unregister (unregister by interface for the current highest-priority scope binding, or by instance)
GameServices.UnregisterService<IMyService>();
```

## Advanced Usage

### Lifecycle and Scope

- `ServiceBase.OnInit()` is called immediately after registration completes (including interface binding and priority sorting); `Shutdown()` is called on unregistration or scope shutdown.
- `GameServices.Shutdown()` shuts down all services in reverse order: Gameplay → Scene → App; `GameServices.ShutdownScope(EServiceScopeKind scope)` shuts down only the specified scope.
- `GameApp` listens to `SceneManager.sceneUnloaded` and automatically shuts down services in `Scene` and `Gameplay` scopes when a scene is unloaded.
- The same interface can be registered with different implementations in different scopes. `GetService<T>()` lookup order is Gameplay > Scene > App (cross-scope shadowing), which can be used to temporarily replace global implementations during combat.

### Lifecycle State Machine

Each service tracks its lifecycle state via `ServiceBase.State` (`EServiceState`):

| State | Description |
|-------|-------------|
| `Created` | Instance created but not yet registered |
| `Initialized` | `OnInit()` has been called; service is active |
| `ShuttingDown` | `Shutdown()` is being called (or about to be) |
| `Disposed` | Service has been fully shut down and removed |

Shutdown is idempotent: calling `ShutdownService` on an already-disposed service is a no-op.

### Dependency Declaration

Services can declare dependencies that are validated at registration time. If a dependency is not registered, a `GameException` is thrown with an informative message:

```csharp
public class AudioService : ServiceBase, IAudioService
{
    // Declares that IResourceService must be registered before this service
    protected internal override Type[] Dependencies => new[] { typeof(IResourceService) };

    public override void OnInit()
    {
        var resource = Require<IResourceService>();
        // ...
    }
}
```

### Cross-Service Dependencies

```csharp
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        // Throws GameException on failure (falls back from same scope up to App)
        var timer = Require<ITimerService>();

        // Optional dependency
        if (TryGet<IDebuggerService>(out var debugger)) { /* ... */ }
    }
}
```

### Built-in Service Registration

Built-in service implementation types are registered into `GameServices` via configuration in `AppSettings.Initiation()` (at `RuntimeInitializeLoadType.AfterAssembliesLoaded` stage). They can be replaced with custom implementations in the Inspector (e.g., replacing the implementation class of `ITimerService`). Registration order in `AppSettings.Initiation()` determines initialization order — ensure dependencies are registered before dependents.

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

Menu `Tools/Moirai/Service System` opens the service system window, displaying registered services' interfaces, implementations, scopes, priorities, and lifecycle interface implementations (data from `GameServices.GetDiagnosticInfo()`).

### Async Initialization

Services implementing `IAsyncInitService` can perform async initialization after all services are registered:

```csharp
public class MyResourceService : ServiceBase, IMyResourceService, IAsyncInitService
{
    public override void OnInit()
    {
        // Synchronous quick setup (called on registration)
    }

    public async UniTask OnInitAsync()
    {
        // Async loading (called during InitializeAsync)
        await LoadCatalogAsync();
    }
}

// GameApp.Awake automatically calls:
// await GameServices.InitializeAsync();
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

Inherit `ServiceMono<TScope>` to register a MonoBehaviour as a service. Override `RegisterAs` to specify the contract interface:

```csharp
public class MyMonoService : ServiceMono<AppScope>, IMyService
{
    protected override Type RegisterAs => typeof(IMyService);

    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope auto DontDestroyOnLoad; SceneScope/GameplayScope destroyed with scene
}
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

Six interception points with default empty implementations — implement only what you need:

| Method | Timing |
|--------|--------|
| `OnServiceRegistering` | Before `OnInit()` — can throw to reject registration |
| `OnServiceRegistered` | After `OnInit()` and state transition to `Initialized` |
| `OnServiceUnregistering` | Before `Shutdown()` |
| `OnServiceUnregistered` | After removal from registry |
| `OnServiceTick` | Before each `Tick()` call (Update path only) |
| `OnServiceShutdown` | Before `Shutdown()` call |

Multiple interceptors execute in `Priority` descending order. Interceptors are cleared on `GameServices.Shutdown()`.

## Notes

- `GameServices` only allows calls from the main thread; for background threads or async callbacks, use `MainThreadDispatcher`'s `Post`/`Send` to switch back to the main thread.
- `GetService<T>()` and `UnregisterService<T>()` must use interface types; passing a concrete class will throw `GameException`.
- `GetService<T>()` throws `GameException` if the service is not registered — there is no reflection fallback.
- `RegisterService<T>` fails fast: the service must implement the registered interface; re-registering the same interface in the same scope only warns and returns the existing instance.
- When exiting Play Mode in the editor, `GameApp` automatically calls `GameServices.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Back to Main README](../../README_EN.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
