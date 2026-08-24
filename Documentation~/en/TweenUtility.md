# TweenUtility

> Framework unified tween animation facade, providing a pluggable Handler architecture with support for multiple tween engines (self-developed / PrimeTween / LitMotion).

`TweenUtility` is the static facade for tween animation in the framework. By default, it uses the self-developed `DefaultTweenHandler` based on Unity's driver loop, and can also be switched to third-party engines like `PrimeTweenHandler` or `LitMotionHandler`. All tween methods uniformly accept a `TweenEase` parameter, supporting implicit conversion from the `EEase` enum value or `AnimationCurve`.

## Core Features

- Pluggable Handler: `DefaultTweenHandler` (default, self-developed) / `PrimeTweenHandler` / `LitMotionHandler`
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
| `DefaultTweenHandler` | Default implementation, self-developed tween engine |
| `PrimeTweenHandler` | PrimeTween engine adapter |
| `LitMotionHandler` | LitMotion engine adapter |
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

```csharp
// Switch to PrimeTween
TweenUtility.Handler = new PrimeTweenHandler();

// Switch to LitMotion
TweenUtility.Handler = new LitMotionHandler();
```

## Notes

- Setting `Handler` to null throws `ArgumentNullException` (fail-fast)
- On domain reload (entering Play mode), the handler and frame listener are automatically reset to prevent stale references
- `TweenManager` cleans up caches every 60 seconds by default; this can be adjusted via `TweenHandler`'s `m_CheckInterval`
- `TweenEase` enum mode has zero heap allocation; `AnimationCurve` mode holds a reference to the curve
- The `Custom(object, ...)` zero-allocation overload should be used with static lambdas to avoid generic closure allocation

---
[« Back to Main README](../../README_EN.md)