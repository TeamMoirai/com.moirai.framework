# TweenUtility

> Framework unified tween animation facade, providing a pluggable Handler architecture with support for multiple tween engines (self-developed / PrimeTween / LitMotion).

`TweenUtility` is the static facade for tween animation in the framework. The framework ships the self-developed `DefaultTweenHandler` driven by Unity's loop; the PrimeTween and LitMotion third-party engine backends do not live in the framework — they ship as the `primetween` and `litmotion` plugins on the `plugin` branch and only appear in the Provider dropdown once installed. All tween methods uniformly accept a `TweenEase` parameter, supporting implicit conversion from the `EEase` enum value or `AnimationCurve`.

## Core Features

- Pluggable Handler: framework-built-in `DefaultTweenHandler` (in-house); the PrimeTween / LitMotion backends ship as the `primetween` and `litmotion` plugins
- `TweenEase` unified easing parameter: `EEase` enum (31 built-in curves) / `AnimationCurve` dual mode, zero-allocation implicit conversion
- Full Transform tweening: `Position` / `LocalPosition` / `Rotation` / `LocalRotation` / `Scale` (each supports Vector3 and single axis)
- UI tweening: `UIAnchoredPosition` / `UISizeDelta` / `UISliderValue` / `UIFillAmount` / `UINormalizedPosition` / `CanvasGroup.Alpha` / `Graphic.Color`, etc.
- Sprite/Material tweening: `SpriteRenderer.Color` / `SpriteRenderer.Alpha` / `MaterialColor`
- Bezier path: `MoveBezierPath` to move along a path
- Custom tweening: `Custom<T>` generic callback (float/int/long/Vector3), zero-allocation `Custom(object, ...)` overload
- Lifecycle control: `Stop` / `Complete` / `StopAll` / `CompleteAll` / `Delay`, returns `long tweenId`
- Periodic cache cleanup: `TweenManager` calls `ReleaseUnusedTween` at intervals to prevent cache bloat in the underlying library

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `TweenUtility` | Static facade, providing all tween methods, `EEase` enum, `ECycleMode` enum |
| `TweenHandler` | Abstract base class, defining all abstract tween methods + nested `TweenManager` class |
| `TweenEase` | Unified easing parameter struct, supports implicit conversion from `EEase` / `AnimationCurve` |
| `TweenUtility.EEase` | 31 built-in easing curve enums (Linear / InQuad / OutBounce / InElastic, etc.) |
| `TweenUtility.ECycleMode` | Cycle mode enum (Restart / Yoyo, etc.) |
| `DefaultTweenHandler` | Framework-built-in default implementation, self-developed tween engine |
| `TweenHandler.TweenManager` | Periodically calls `ReleaseUnusedTween` on the registered handler to clean up caches |

## Quick Start

```csharp
// Position tween (default Linear)
TweenUtility.Position(transform, targetPos, 0.3f);

// Specify easing curve
TweenUtility.Position(transform, targetPos, 0.3f, TweenUtility.EEase.OutQuad);

// Use AnimationCurve
TweenUtility.Position(transform, targetPos, 0.3f, myAnimationCurve);

// Specify start and end values
TweenUtility.Position(transform, startPos, targetPos, 0.3f, TweenUtility.EEase.OutBounce);

// Scale
TweenUtility.Scale(transform, 1.5f, 0.3f);

// Rotation (Quaternion or Vector3)
TweenUtility.Rotation(transform, Quaternion.Euler(0, 90, 0), 0.3f);

// Delay
long tweenId = TweenUtility.Delay(1.0f, () => Debug.Log("Done"));

// Stop/Complete
TweenUtility.Stop(tweenId);
TweenUtility.CompleteAll();
```

## Advanced Usage

### Loop and Time Scale

```csharp
// Loop 3 times, Yoyo mode
TweenUtility.Position(transform, targetPos, 0.3f,
    ease: TweenUtility.EEase.InOutQuad,
    cycles: 3,
    cycleMode: TweenUtility.ECycleMode.Yoyo,
    startDelay: 0.5f,
    useUnscaledTime: false,
    onComplete: () => Debug.Log("Done"));
```

### UI Tweening

```csharp
// UI AnchoredPosition
TweenUtility.UIAnchoredPosition(rectTransform, new Vector2(100, 0), 0.3f);

// CanvasGroup Alpha
TweenUtility.Alpha(canvasGroup, 0f, 0.2f);

// Slider value
TweenUtility.UISliderValue(slider, 0.5f, 0.3f);

// ScrollRect normalized position
TweenUtility.UINormalizedPosition(scrollRect, Vector2.one, 0.5f);

// Image.FillAmount
TweenUtility.UIFillAmount(image, 0f, 0.5f);
```

### Custom Tweening

```csharp
// Generic custom (float interpolation)
TweenUtility.Custom(transform, 0f, 100f, 2.0f, (t, v) => {
    Debug.Log($"Progress: {v}");
}, ease: TweenUtility.EEase.OutCirc);

// Zero-allocation custom (object target, static lambda, no closure)
TweenUtility.Custom(transform, 0f, 100f, 2.0f, static (t, v) => {
    ((Transform)t).localScale = Vector3.one * v;
});
```

### Bezier Path

```csharp
Vector3[] path = new Vector3[] {
    startPos, control1, control2, endPos
};
TweenUtility.MoveBezierPath(transform, path, 2.0f, TweenUtility.EEase.InOutSine);
```

### Switching Handler

Backends are always selected in settings, never `new`-ed in game code: open `GameAppSettings` via `Tools/Framework Settings` and switch the "tween backend" dropdown to the target engine.

- The PrimeTween and LitMotion entries in that dropdown come from the `primetween` and `litmotion` plugins on the `plugin` branch (installed through `Tools/Moirai/Package Manager`). Without the plugins they do not exist and the dropdown only offers the framework-built-in `DefaultTweenHandler`.
- `primetween` requires `com.kyrylokuzyk.primetween`; the `PRIMETWEEN_INSTALLED` macro is defined automatically by that plugin asmdef's versionDefines, and the `PrimeTween.Runtime.asmref` that lets framework code reference PrimeTween types travels with the plugin directory (`primetween/PrimeTween/`) rather than staying in the framework. `litmotion` requires `com.annulusgames.lit-motion` (which has no published UPM registry version and is installed from its git URL per the vendor's own guidance) plus `com.cysharp.unitask`; `LITMOTION_INSTALLED` is likewise defined by versionDefines.
- The field is `[SerializeReference]`, so switching changes the `asm:` ownership recorded in the asset — save the asset afterwards.

## Notes

- `TweenUtility.Handler` is an `internal` static property (emitted by the source generator) — neither game nor plugin code assigns it directly; switching backends goes exclusively through the `GameAppSettings` Provider dropdown. The setter rejects null (fail-fast)
- On domain reload (entering Play mode), the handler and frame listener are automatically reset to prevent stale references
- `TweenManager` cleans up caches every 60 seconds by default; this can be adjusted via `TweenHandler`'s `m_CheckInterval`
- `TweenEase` enum mode has zero heap allocation; `AnimationCurve` mode holds a reference to the curve
- The `Custom(object, ...)` zero-allocation overload should be used with static lambdas to avoid generic closure allocation

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [UI](UI.md) · [GameApp](GameApp.md)
