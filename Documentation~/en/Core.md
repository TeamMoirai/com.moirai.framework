# Core Module System (@Core)

> Framework's modular base: manage the lifecycle, polling, and scope of all sub-modules with plain C# classes, driven by `GameModule` (MonoBehaviour).

`@Core` is the module infrastructure of the entire framework. All functional modules (resources, UI, audio, timers, etc.) are plain C# classes inheriting from `Module`, uniformly registered, looked up, and destroyed by the static class `ModuleSystem`. `GameModule` serves as the engine entry point, driving module polling in `Update`/`FixedUpdate`/`LateUpdate`, and provides static accessors such as `GameModule.Timer`. Modules support three scopes: App/Scene/Gameplay. When a scene is unloaded, scene-level and gameplay-level modules can be automatically cleaned up.

## Core Features

- Plain C# modules: not MonoBehaviour, no scene dependency, lifecycle precisely controlled by the framework
- Three-level scope (`ModuleScope.App` / `Scene` / `Gameplay`), cross-scope lookup follows Gameplay > Scene > App shadowing order
- Lifecycle interfaces implemented on demand: `IUpdateModule`, `IFixedUpdateModule`, `ILateUpdateModule`, `IGizmoModule`
- `Priority` controls polling order (higher priority polls first, shuts down later)
- Iteration safety: registrations/unregistrations during polling are deferred and applied uniformly after the current cycle ends
- Main thread affinity guard: asserts calling thread in editor and development builds, zero overhead in release builds
- Lazy-loaded static accessors: `GameModule.Resource`, `GameModule.Timer`, etc. are created and cached on first access

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `Module` | Abstract base class for modules, defines `OnInit()` / `Shutdown()` / `Priority` / `Scope`, and provides `Require<T>()` / `TryGet<T>(out T)` for cross-module dependency resolution |
| `ModuleSystem` | Static module management center: registration, retrieval, unregistration, polling driver, and scope shutdown |
| `ModuleScope` | Module scope enum: `App` (global), `Scene` (reset on scene unload), `Gameplay` (single session) |
| `IUpdateModule` / `IFixedUpdateModule` / `ILateUpdateModule` | Polling interfaces with method signatures such as `Update(float elapseSeconds, float realElapseSeconds)` |
| `IGizmoModule` | Editor Gizmos drawing interface `OnDrawGizmos()` |
| `GameModule` | MonoBehaviour entry point (`[DefaultExecutionOrder(-1000)]`), holds all built-in module static accessors and drives `ModuleSystem` |
| `MessageEvent` / `EMessageEventType` | Namespace `Moirai.Atropos.Events`, framework-level pooled events (focus/unfocus/quit, SDK callbacks) |

## Quick Start

```csharp
// 1. Access built-in modules via GameModule static accessors (lazy loading)
ITimerModule timer = GameModule.Timer;
IResourceModule resource = GameModule.Resource;

// 2. Get module by interface via ModuleSystem (falls back to reflection IXxxModule -> XxxModule if not registered)
var module = ModuleSystem.GetModule<ITimerModule>();

// 3. Define a custom module
public interface IMyModule { void DoSomething(); }

public class MyModule : Module, IMyModule, IUpdateModule
{
    public override int Priority => 10;              // Higher priority polls first
    public override ModuleScope Scope => ModuleScope.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Update(float elapseSeconds, float realElapseSeconds) { }
}

// 4. Explicit registration (required when not following the IXxxModule -> XxxModule naming convention)
IMyModule my = ModuleSystem.RegisterModule<IMyModule>(new MyModule());

// 5. Unregister (unregister by interface for the current highest-priority scope binding, or by instance)
ModuleSystem.UnregisterModule<IMyModule>();
```

## Advanced Usage

### Lifecycle and Scope

- `Module.OnInit()` is called immediately after registration completes (including interface binding and priority sorting); `Shutdown()` is called on unregistration or scope shutdown.
- `ModuleSystem.Shutdown()` shuts down all modules in reverse order: Gameplay -> Scene -> App; `ModuleSystem.ShutdownScope(ModuleScope scope)` shuts down only the specified scope.
- `GameModule` listens to `SceneManager.sceneUnloaded` and automatically shuts down modules in `Scene` and `Gameplay` scopes when a scene is unloaded.
- The same interface can be registered with different implementations in different scopes. `GetModule<T>()` lookup order is Gameplay > Scene > App (cross-scope shadowing), which can be used to temporarily replace global implementations during combat.

### Cross-Module Dependencies

```csharp
public class BattleModule : Module
{
    public override void OnInit()
    {
        // Throws GameException on failure (falls back from same scope up to App)
        var timer = Require<ITimerModule>();

        // Optional dependency
        if (TryGet<IDebuggerModule>(out var debugger)) { /* ... */ }
    }
}
```

### Built-in Module Registration

Built-in module implementation types are registered into `ModuleSystem` via configuration in `AppSettings.Initiation()` (at `RuntimeInitializeLoadType.AfterAssembliesLoaded` stage). They can be replaced with custom implementations in the Inspector (e.g., replacing the implementation class of `ITimerModule`). Configuration registration occurs before any game code, so it takes precedence over reflection fallback.

### Framework Events (MessageEvent)

`GameModule` triggers framework events in engine callbacks (namespace `Moirai.Atropos.Events`):

```csharp
// Automatically triggered by GameModule on focus/unfocus/quit:
// EMessageEventType.ApplicationFocus / NotApplicationFocus / ApplicationQuit
MessageEvent.Trigger(EMessageEventType.ApplicationQuit);

// Subscribe via EventManager (pooled events, zero GC dispatch)
EventManager.RegisterCallback<MessageEvent>(OnMessageEvent);
```

### Editor Tools

Menu `Tools/Moirai/Module System` opens the module system window, displaying registered modules' interfaces, implementations, scopes, priorities, and lifecycle interface implementations (data from `ModuleSystem.GetDiagnosticInfo()`).

## Notes

- `ModuleSystem` only allows calls from the main thread; for background threads or async callbacks, use `MainThreadDispatcher`'s `Dispatch`/`DispatchAsync` to switch back to the main thread.
- `GetModule<T>()` and `UnregisterModule<T>()` must use interface types; passing a concrete class will throw `GameException`.
- `RegisterModule<T>` fails fast: the module must implement the registered interface; re-registering the same interface in the same scope only warns and returns the existing instance.
- When exiting Play Mode in the editor, `GameModule` automatically calls `ModuleSystem.Shutdown()`, compatible with the Enter Play Mode Options setting that skips domain reload.

---
[« Back to Main README](../../README_EN.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)