# Core Service System (@Service)

> Framework's modular base: manage the lifecycle, polling, and scope of all sub-services with plain C# classes, driven by `GameApp` (MonoBehaviour).

`@Service` is the service infrastructure of the entire framework. All functional services (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `Service`, uniformly registered, looked up, and destroyed by the static class `ServiceSystem`. `GameApp` serves as the engine entry point, driving service polling in `Update`/`FixedUpdate`/`LateUpdate`, and provides static accessors such as `GameApp.Timer`. Services support three scopes: App/Scene/Gameplay. When a scene is unloaded, scene-level and gameplay-level services can be automatically cleaned up.

## Core Features

- Plain C# services: not MonoBehaviour, no scene dependency, lifecycle precisely controlled by the framework
- Three-level scope (`ServiceScope.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App shadowing order
- Lifecycle interfaces implemented on demand: `IServiceTickable`, `IServiceFixedTickable`, `IServiceLateTickable`, `IServiceGizmoDrawable`
- `Priority` controls polling order (higher priority polls first, shuts down later)
- Async initialization: services implementing `IAsyncInitService` are initialized asynchronously via `ServiceSystem.InitializeAsync()`
- Service events: `ServiceRegistered`/`ServiceUnregistered` events for hot-swap notifications
- Iteration safety: registrations/unregistrations during polling are deferred and applied uniformly after the current cycle ends
- Main thread affinity guard: asserts calling thread in editor and development builds, zero overhead in release builds
- Lazy-loaded static accessors: `GameApp.Resource`, `GameApp.Timer`, etc. are created and cached on first access

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `Service` | Abstract base class for services, defines `OnInit()` / `Shutdown()` / `Priority` / `Scope`, and provides `Require<T>()` / `TryGet<T>(out T)` for cross-service dependency resolution |
| `ServiceSystem` | Static service management center: registration, retrieval, unregistration, polling driver, and scope shutdown |
| `ServiceScope` | Service scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | Polling interfaces with method signatures such as `Tick(float elapseSeconds, float realElapseSeconds)` |
| `IServiceGizmoDrawable` | Editor Gizmos drawing interface `OnDrawGizmos()` |
| `IAsyncInitService` | Async initialization interface; services implementing `OnInitAsync()` are driven by `ServiceSystem.InitializeAsync()` |
| `MonoServiceBehaviour<TScope>` | MonoBehaviour service base; auto-registers on Awake, App scope auto DontDestroyOnLoad |
| `GameApp` | MonoBehaviour entry point (`[DefaultExecutionOrder(-1000)]`), holds all built-in service static accessors and drives `ServiceSystem` |
| `MessageEvent` / `EMessageEventType` | Namespace `Moirai.Atropos.Events`, framework-level pooled events (focus/unfocus/quit, SDK callbacks) |

## Quick Start

```csharp
// 1. Access built-in services via GameApp static accessors (lazy loading)
ITimerService timer = GameApp.Timer;
IResourceService resource = GameApp.Resource;

// 2. Get service by interface via ServiceSystem (falls back to reflection IXxxService -> XxxService if not registered)
var service = ServiceSystem.GetService<ITimerService>();

// 3. Define a custom service
public interface IMyService { void DoSomething(); }

public class MyService : Service, IMyService, IServiceTickable
{
    public override int Priority => 10;              // Higher priority polls first
    public override ServiceScope Scope => ServiceScope.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 4. Explicit registration (required when not following the IXxxService -> XxxService naming convention)
IMyService my = ServiceSystem.RegisterService<IMyService>(new MyService());

// 5. Unregister (unregister by interface for the current highest-priority scope binding, or by instance)
ServiceSystem.UnregisterService<IMyService>();
```

## Advanced Usage

### Lifecycle and Scope

- `Service.OnInit()` is called immediately after registration completes (including interface binding and priority sorting); `Shutdown()` is called on unregistration or scope shutdown.
- `ServiceSystem.Shutdown()` shuts down all services in reverse order: Gameplay -> Scene -> App; `ServiceSystem.ShutdownScope(ServiceScope scope)` shuts down only the specified scope.
- `GameApp` listens to `SceneManager.sceneUnloaded` and automatically shuts down services in `Scene` and `Gameplay` scopes when a scene is unloaded.
- The same interface can be registered with different implementations in different scopes. `GetService<T>()` lookup order is Gameplay > Scene > App (cross-scope shadowing), which can be used to temporarily replace global implementations during combat.

### Cross-Service Dependencies

```csharp
public class BattleService : Service
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

Built-in service implementation types are registered into `ServiceSystem` via configuration in `AppSettings.Initiation()` (at `RuntimeInitializeLoadType.AfterAssembliesLoaded` stage). They can be replaced with custom implementations in the Inspector (e.g., replacing the implementation class of `ITimerService`). Configuration registration occurs before any game code, so it takes precedence over reflection fallback.

### Framework Events (MessageEvent)

`GameApp` triggers framework events in engine callbacks (namespace `Moirai.Atropos.Events`):

```csharp
// Automatically triggered by GameApp on focus/unfocus/quit:
// EMessageEventType.ApplicationFocus / NotApplicationFocus / ApplicationQuit
MessageEvent.Trigger(EMessageEventType.ApplicationQuit);

// Subscribe via EventManager (pooled events, zero GC dispatch)
EventManager.RegisterCallback<MessageEvent>(OnMessageEvent);
```

### Editor Tools

Menu `Tools/Moirai/Service System` opens the service system window, displaying registered services' interfaces, implementations, scopes, priorities, and lifecycle interface implementations (data from `ServiceSystem.GetDiagnosticInfo()`).

### Async Initialization

Services implementing `IAsyncInitService` can perform async initialization after all services are registered:

```csharp
public class MyResourceService : Service, IMyResourceService, IAsyncInitService
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
// await ServiceSystem.InitializeAsync();
// ProcedureSettings.StartProcedure().Forget();
```

### Service Events

```csharp
ServiceSystem.ServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

ServiceSystem.ServiceUnregistered += (service) =>
{
    Debug.Log($"Service unregistered: {service.GetType().Name}");
};
```

### MonoBehaviour Service

Inherit `MonoServiceBehaviour<TScope>` to register a MonoBehaviour as a service:

```csharp
public class MyMonoService : MonoServiceBehaviour<AppScope>, IMyService
{
    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope auto DontDestroyOnLoad; SceneScope/GameplayScope destroyed with scene
}
```

## Notes

- `ServiceSystem` only allows calls from the main thread; for background threads or async callbacks, use `MainThreadDispatcher`'s `Dispatch`/`DispatchAsync` to switch back to the main thread.
- `GetService<T>()` and `UnregisterService<T>()` must use interface types; passing a concrete class will throw `GameException`.
- `RegisterService<T>` fails fast: the service must implement the registered interface; re-registering the same interface in the same scope only warns and returns the existing instance.
- When exiting Play Mode in the editor, `GameApp` automatically calls `ServiceSystem.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Back to Main README](../../README_EN.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)