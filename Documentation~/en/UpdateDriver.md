# UpdateDriver Service

> Provides Unity lifecycle proxy for non-MonoBehaviour code: coroutine hosting, frame update injection, and Unity event injection.

`UpdateDriver` solves the problem of plain C# classes not being able to access Unity engine callbacks. All services in the framework are plain C# classes (see [Core Service System](Core.md)). When business classes need coroutines, `Update` polling, or engine callbacks such as `OnApplicationPause`, this service allows registering callbacks onto a persistent hidden host `MainBehaviour`. The service implementation class `UpdateDriverService` lazily creates a `DontDestroyOnLoad` game object named `[UpdateDriver]` on first use, and all engine callbacks are forwarded as aggregated events.

## Core Features

- Coroutine hosting: start/stop coroutines without placing a MonoBehaviour in the scene
- Frame update injection: registration and removal of `Update` / `FixedUpdate` / `LateUpdate` frame callbacks
- Unity event injection: `OnDestroy`, `OnDrawGizmos`, `OnDrawGizmosSelected`, `OnApplicationPause`
- Lazy host creation: the `[UpdateDriver]` persistent object is created only on the first API call, zero upfront cost
- Clean shutdown: clears all events and destroys the host object when the service shuts down

## Core Types

Namespace: `Moirai.Atropos.UpdateDriver`

| Class/Interface | Description |
|---------|------|
| `UpdateDriverService` | Static facade (`[HandlerHost]`): coroutine control, frame update listening, Unity event listening registration and removal; all static APIs forward through the `Handler` property (fail-fast: lazily initialized when not ready, throws if the default factory is missing, never silently degrades) |
| `UpdateDriverServiceHandler` | Handler abstract base class defining the backend contract; the default implementation `UnityUpdateDriverHandler` manages the `[UpdateDriver]` host |

The host `MainBehaviour` (a private nested class of the handler) is the actual MonoBehaviour attached to the host, aggregating Unity callbacks via C# events; Gizmo-related callbacks are decorated with `[Conditional("UNITY_EDITOR")]` and only compile in the editor.

## Quick Start

```csharp
// Call the static facade directly

// Coroutine: driven by the framework host, no own MonoBehaviour needed
Coroutine co = UpdateDriverService.StartCoroutine(SomeRoutine());
UpdateDriverService.StopCoroutine(co);
UpdateDriverService.StopAllCoroutines();

// Frame update injection: plain classes gain Update polling
UpdateDriverService.AddUpdateListener(OnUpdate);
UpdateDriverService.AddFixedUpdateListener(OnFixedUpdate);
UpdateDriverService.AddLateUpdateListener(OnLateUpdate);

void OnUpdate() { /* Called every frame */ }
void OnFixedUpdate() { /* Called on physics frames */ }
void OnLateUpdate() { /* Called on late frames */ }

// Remove listeners (pair with Add to prevent leaks)
UpdateDriverService.RemoveUpdateListener(OnUpdate);
```

## Advanced Usage

### Unity Event Injection

```csharp
// Application pause/resume (parameter indicates pause status)
UpdateDriverService.AddOnApplicationPauseListener(OnApplicationPause);
void OnApplicationPause(bool pauseStatus) { }

// Editor Gizmos drawing
UpdateDriverService.AddOnDrawGizmosListener(DrawGizmos);
UpdateDriverService.AddOnDrawGizmosSelectedListener(DrawSelectedGizmos);

// Host destruction callback (not triggered when service Shutdown destroys the host; mainly for scenarios where the host is destroyed externally)
UpdateDriverService.AddDestroyListener(OnHostDestroy);
```

### Internal Framework Usage

`UpdateDriver` is a low-level dependency for several framework infrastructures: coroutine utilities in `UnityUtility` execute through it. The service implementation type is registered during the `AppSettings.Initiation()` stage and supports implementation replacement in the Inspector.

### Registration Timing Notes

`AddUpdateListener` / `AddFixedUpdateListener` / `AddLateUpdateListener` internally use UniTask to defer actual mounting by one frame (FixedUpdate listener registration occurs at `PlayerLoopTiming.LastEarlyUpdate`), ensuring the host completes its current frame initialization before receiving callbacks; `Remove*` methods and Unity event listener add/remove operations take effect synchronously.

## Notes

- Listeners hold strong references; always pair `Add`/`Remove` calls, otherwise the target object cannot be garbage collected. The service `Shutdown` clears all listeners uniformly.
- Gizmos and GizmosSelected APIs only work in the editor; the calls are compiled out in release builds.
- `StartCoroutine` returns `null` when passed an empty method name or empty iterator, without throwing an exception.
- The host object is `DontDestroyOnLoad` and survives across scenes; do not manually destroy the `[UpdateDriver]` object externally, otherwise all coroutines and listeners will become invalid.

---
[« Back to Main README](../../README_EN.md) · [Core](Core.md) · [Timer](Timer.md)