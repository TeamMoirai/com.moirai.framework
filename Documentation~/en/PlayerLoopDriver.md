# PlayerLoop Logic Driver

> Mono-free game frame logic driver: invoked directly by Unity PlayerLoop. Subscriptions survive scene changes and host destruction.

## Background

The previous `GameApp` stored `Update`/`FixedUpdate`/`LateUpdate` listeners on **instance events** of a hidden Mono host. That host could be destroyed before the initial scene load, silently dropping all subscriptions.

`Runtime/Services/Timer` (`TimerService`) is the unified timing subsystem (four-level timing wheel + frame timers), exposing `Delay` / `WaitFrame` and friends. It is advanced through `IServiceTickable` by `GameServices.Tick`, which is itself registered as an Update callback on this driver. `IUpdateHandler` targets game-side systems and DI composition roots — do not confuse the two paths.

## Architecture

| Component | Responsibility |
|------|------|
| `Moirai.Atropos.FrameLoop.PlayerLoopDriver` | Zero-alloc static registry + Drive entry points |
| `Moirai.Atropos.FrameLoop.PlayerLoopInjector` | Inject/restore Unity `PlayerLoopSystem` |
| `IUpdateHandler` / `IFixedUpdateHandler` / `ILateUpdateHandler` | Interface handlers (recommended, DI-friendly) |
| `IPlayerLoopPriority` | Optional drive order (lower runs first) |
| `GameApp` static APIs | Compatibility facade forwarding to Driver; holds no MonoBehaviour |
| `GameAppHost` | The single lightweight Mono host: coroutines / Gizmos / ApplicationPause, forwarding only |

Injection points:

- Start of `PlayerLoop.Update` → Framework Update
- Start of `PlayerLoop.FixedUpdate` → Framework FixedUpdate
- End of `PlayerLoop.PreLateUpdate` → Framework LateUpdate (after MonoBehaviour.LateUpdate)

Injection uses the **current** PlayerLoop so UniTask and other systems are preserved. Default loop is captured at `SubsystemRegistration` and restored on Shutdown.

Each Drive entry samples the frame clock with `GameTime.StartFrame()` before invoking handlers and then callbacks — **both subscription kinds read the same frame**.

## Quick Start

```csharp
using Moirai.Atropos.FrameLoop;

public sealed class MySystem : IUpdateHandler
{
    public void Update(float deltaTime, float unscaledDeltaTime)
    {
        // No heap allocations
    }
}

PlayerLoopDriver.Initialize();
PlayerLoopDriver.Register(new MySystem());

// Compatibility API
GameApp.AddUpdateListener(OnUpdate);
```

## Zero-Allocation Contract

`DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` and all handler implementations:

- `for` loops only; no LINQ / closures / string concat
- Register/unregister during drive is deferred into the pending buffer **of that stage**, committed when the stage finishes; cross-stage registrations never leak into each other
- A throwing subscriber cannot wedge the driving flag: `finally` resets it and commits the buffers
- Profiler markers: `PlayerLoopDriver.Update`, etc.

## Lifecycle

| Moment | Behavior |
|------|------|
| `SubsystemRegistration` | Capture default PlayerLoop; Driver marked Shutdown; `GameAppHost` clears its shutdown flag |
| `GameApp.Initialize` (AfterAssembliesLoaded) | `PlayerLoopDriver.Initialize()` injects + registers builtin ticks, then materializes `GameAppHost` |
| `GameApp.Shutdown` / exit Play | Broadcast Destroy → clear registry → restore default PlayerLoop → destroy host |
| After ECS resets PlayerLoop | Call `PlayerLoopInjector.Reinject()` |

## DI (VContainer etc.)

Register handler implementations as services; after composition root `InitializeAsync`:

```csharp
var system = container.Resolve<MySystem>();
PlayerLoopDriver.Register(system);
```

## Compatibility

- **UniTask**: injection bases on current loop and does not overwrite UniTask systems; restoring default on Play exit lets UniTask re-init next Play.
- **ECS/DOTS**: call `PlayerLoopInjector.Reinject()` after Entities resets the loop.
- **ApplicationPause**: Unity exposes no pure C# event — `GameAppHost.OnApplicationPause` forwards to `PlayerLoopDriver.RaiseApplicationPause`. Subscriptions live in the static table, so rebuilding the host restores dispatch.
- **Gizmos**: same pattern — `GameAppHost.OnDrawGizmos(Selected)` forwards to `PlayerLoopDriver.RaiseDrawGizmos(Selected)`. Only the editor has a dispatcher; in builds the table is never raised.
- **Coroutines**: `GameApp.StartCoroutine` goes through `GameAppHost.Instance`; destroying the host only kills running coroutines, never subscriptions.

## Editor Debugging

Menu **Window → PlayerLoop Debugger**:

- Recursive dump of the current PlayerLoop with Moirai markers `<< Moirai`
- Handler/callback counts per stage
- Buttons: Ensure Injected / Reinject / Restore Default

---
[« Documentation Index](Index.md) · [GameApp](GameApp.md)
