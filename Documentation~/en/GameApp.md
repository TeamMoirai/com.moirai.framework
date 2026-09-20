# GameApp Frame Driver and Host

> Unity lifecycle proxy for non-MonoBehaviour code: coroutine hosting, frame update injection, and Unity event injection.

## Architecture Change (Important)

Frame subscriptions moved from a MonoBehaviour host to **`PlayerLoopDriver`** (`Runtime/Core/GameApp/PlayerLoop`, namespace `Moirai.Atropos`, type is `internal` — game code reaches it through the `GameApp` facade):

- Subscriptions live in a **static registry**, not on any GameObject
- Scene loads / unexpected host destruction **do not lose** `Update`/`FixedUpdate`/`LateUpdate`/`Destroy`/`Gizmos`/`Pause` listeners
- Prior bug: listeners lived on a hidden Mono host instance events, which could be destroyed before the initial scene load, dropping every subscription

`GameApp` itself now holds **no MonoBehaviour at all** (no nested host, no GameObject field). The Unity messages that only dispatch on a MonoBehaviour — coroutines, `OnDrawGizmos(Selected)`, `OnApplicationPause` — are collected in a single `SingletonMono_Persistent` host, `GameAppHost` (`Runtime/Core/GameApp/GameAppHost.cs`). It only **forwards**; the subscriptions stay in the Driver's static tables, so destroying or rebuilding the host loses nothing.

See [PlayerLoopDriver](PlayerLoopDriver.md) for details.

## Core Features

- Coroutine hosting: `GameApp.StartCoroutine` / `StopCoroutine` / `StopAllCoroutines`
- Frame updates: `GameApp.AddUpdateListener` (Action) plus `GameApp.AddUpdateHandler` / `AddFrameHandler` (interface handlers, `IPlayerLoopPriority` aware) all write **synchronously** into the driver's registries (no `UniTask.Yield` deferral)
- Unity events: `AddDestroyListener` (broadcast on Shutdown), `AddOnApplicationPauseListener`, Gizmos APIs
- Clean shutdown: `GameApp.Shutdown` clears the Driver registry, removes this framework's PlayerLoop systems (UniTask and other third-party injections stay) and releases the host

## Core Types

| Class/Interface | Description |
|---------|------|
| `GameApp` | Framework entry static facade: lifecycle, coroutines, subscription APIs (no Mono members) |
| `PlayerLoopDriver` | Mono-free logic driver (static) |
| `IUpdateHandler` etc. | Interface handlers — preferred for new code |
| `GameAppHost` | Coroutine / Gizmos / Pause host (`SingletonMono_Persistent`) |

## Quick Start

```csharp
// Callback style (compatible API)
GameApp.AddUpdateListener(OnUpdate);
GameApp.AddFixedUpdateListener(OnFixedUpdate);
GameApp.AddLateUpdateListener(OnLateUpdate);
GameApp.RemoveUpdateListener(OnUpdate);

// Interface style (recommended): carries state, DI-injected, orderable via IPlayerLoopPriority
GameApp.AddUpdateHandler(myUpdateHandler);
GameApp.RemoveUpdateHandler(myUpdateHandler);

// A class implementing several stage interfaces, registered in one call
GameApp.AddFrameHandler(myMultiStageSystem);
GameApp.RemoveFrameHandler(myMultiStageSystem);

// Coroutines
Coroutine co = GameApp.StartCoroutine(SomeRoutine());
GameApp.StopCoroutine(co);

// Destroy (fired on GameApp.Shutdown)
GameApp.AddDestroyListener(OnShutdown);
```

## Registration Timing

`Add*Listener` / `Add*Handler` / `AddFrameHandler` write **synchronously** into static tables and are safe at any init stage (including `SubsystemRegistration`). Driving starts after PlayerLoop injection.

## Notes

- Listeners hold strong references — always pair Add/Remove; Shutdown clears the Driver.
- Gizmos APIs only have a dispatcher in the editor; subscriptions still go to the Driver's static table, so registering in a build is harmless.
- Do not manually destroy `[GameAppHost]`; it is lazily rebuilt through `Instance`, which restores dispatch only — subscriptions were never lost.
- Calling `GameApp.StartCoroutine` from a background thread throws via `SingletonMono<T>.Instance`: the host must be materialized on the main thread first (`GameApp.Initialize` guarantees this).
- Exiting Play removes only the three PlayerLoop systems this framework injected; UniTask and other third-party injections are left intact. Re-entering Play re-injects via `GameApp.Initialize`.

---
[« Documentation Index](Index.md) · [PlayerLoopDriver](PlayerLoopDriver.md) · [Core](Core.md)
