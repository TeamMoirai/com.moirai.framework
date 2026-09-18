# PlayerLoop Logic Driver

> Mono-free game frame logic driver: invoked directly by Unity PlayerLoop. Subscriptions survive scene changes and host destruction.

## Background

The previous `GameApp` stored `Update`/`FixedUpdate`/`LateUpdate` listeners on a hidden host `MainBehaviour` (`[UpdateDriver]`). That host could be destroyed before the initial scene load, silently dropping all subscriptions.

`Runtime/Core/Schedulers` is a Timer/FrameCounter subsystem (Unreal TimerManager style). It depends on a scene GameObject and has no general `IUpdateHandler` registration surface — it **cannot** serve as the game logic driver.

## Architecture

| Component | Responsibility |
|------|------|
| `Moirai.Atropos.FrameLoop.PlayerLoopDriver` | Zero-alloc static registry + Drive entry points |
| `Moirai.Atropos.FrameLoop.PlayerLoopInjector` | Inject/restore Unity `PlayerLoopSystem` |
| `IUpdateHandler` / `IFixedUpdateHandler` / `ILateUpdateHandler` | Interface handlers (recommended, DI-friendly) |
| `IPlayerLoopPriority` | Optional drive order (lower runs first) |
| `GameApp` static APIs | Compatibility facade forwarding to Driver |
| `CoroutineHost` | Coroutines / Gizmos / ApplicationPause only |

Injection points:

- Start of `PlayerLoop.Update` → Framework Update
- Start of `PlayerLoop.FixedUpdate` → Framework FixedUpdate
- End of `PlayerLoop.PreLateUpdate` → Framework LateUpdate (after MonoBehaviour.LateUpdate)

Injection uses the **current** PlayerLoop so UniTask and other systems are preserved. Default loop is captured at `SubsystemRegistration` and restored on Shutdown.

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
- Register/unregister during drive is deferred until after iteration
- Profiler markers: `PlayerLoopDriver.Update`, etc.

## Lifecycle

| Moment | Behavior |
|------|------|
| `SubsystemRegistration` | Capture default PlayerLoop; Driver marked Shutdown |
| `GameApp.Initialize` (AfterAssembliesLoaded) | `PlayerLoopDriver.Initialize()` injects + registers builtin ticks |
| `GameApp.Shutdown` / exit Play | Broadcast Destroy → clear registry → `SchedulerRunner.Shutdown` → restore default PlayerLoop |
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
- **ApplicationPause**: no pure C# API — `CoroutineHost` forwards to `PlayerLoopDriver.RaiseApplicationPause`.
- **Coroutines**: still require `GameApp.StartCoroutine` (`[CoroutineHost]`); host destruction does not affect frame subscriptions.

## Editor Debugging

Menu **Window → PlayerLoop Debugger**:

- Recursive dump of the current PlayerLoop with Moirai markers `<< Moirai`
- Handler/callback counts per stage
- Buttons: Ensure Injected / Reinject / Restore Default

---
[« Documentation Index](Index.md) · [UpdateDriver](UpdateDriver.md)
