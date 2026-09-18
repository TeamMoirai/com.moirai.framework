# UpdateDriver / GameApp Frame Driver

> Unity lifecycle proxy for non-MonoBehaviour code: coroutine hosting, frame update injection, and Unity event injection.

## Architecture Change (Important)

Frame subscriptions moved from a MonoBehaviour host to **`PlayerLoopDriver`** (`Runtime/Core/PlayerLoop`, namespace `Moirai.Atropos.FrameLoop`):

- Subscriptions live in a **static registry**, not on any GameObject
- Scene loads / unexpected host destruction **do not lose** `Update`/`FixedUpdate`/`LateUpdate`/`Destroy` listeners
- Prior bug: `[UpdateDriver]` host could be destroyed before the initial scene load, dropping all `MainBehaviour` events

Coroutines / Editor Gizmos / `OnApplicationPause` still use the lightweight `CoroutineHost` (`[CoroutineHost]`). Host destruction only affects those three; frame subscriptions stay intact.

See [PlayerLoopDriver](PlayerLoopDriver.md) for details.

## Core Features

- Coroutine hosting: `GameApp.StartCoroutine` / `StopCoroutine` / `StopAllCoroutines`
- Frame updates: `GameApp.AddUpdateListener` APIs write **synchronously** into `PlayerLoopDriver` (no `UniTask.Yield` deferral)
- Unity events: `AddDestroyListener` (broadcast on Shutdown), `AddOnApplicationPauseListener`, Gizmos APIs
- Clean shutdown: `GameApp.Shutdown` clears the Driver registry and restores the default PlayerLoop

## Core Types

| Class/Interface | Description |
|---------|------|
| `GameApp` | Framework entry static facade: lifecycle, coroutines, subscription APIs |
| `PlayerLoopDriver` | Mono-free logic driver (static) |
| `IUpdateHandler` etc. | Interface handlers — preferred for new code |
| `GameApp.CoroutineHost` | Coroutine / Gizmos / Pause host |

## Quick Start

```csharp
// Callback style (compatible API)
GameApp.AddUpdateListener(OnUpdate);
GameApp.AddFixedUpdateListener(OnFixedUpdate);
GameApp.AddLateUpdateListener(OnLateUpdate);
GameApp.RemoveUpdateListener(OnUpdate);

// Interface style (recommended)
using Moirai.Atropos.FrameLoop;
PlayerLoopDriver.Register(myUpdateHandler);

// Coroutines
Coroutine co = GameApp.StartCoroutine(SomeRoutine());
GameApp.StopCoroutine(co);

// Destroy (fired on GameApp.Shutdown)
GameApp.AddDestroyListener(OnShutdown);
```

## Registration Timing

`Add*Listener` / `PlayerLoopDriver.Register` write **synchronously** into static tables and are safe at any init stage (including `SubsystemRegistration`). Driving starts after PlayerLoop injection.

## Notes

- Listeners hold strong references — always pair Add/Remove; Shutdown clears the Driver.
- Gizmos APIs are editor-only and require `CoroutineHost`.
- Do not manually destroy `[CoroutineHost]`; it is lazily recreated. Frame subscriptions are unaffected.
- Exiting Play restores the default PlayerLoop (removes UniTask injection too); each library re-inits on the next Play.

---
[« Documentation Index](Index.md) · [PlayerLoopDriver](PlayerLoopDriver.md) · [Core](Core.md)
