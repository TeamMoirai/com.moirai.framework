# TweenUtility

> 框架的缓动动画统一外观，提供可插拔的 Handler 架构，支持多种补间引擎（自研/PrimeTween/LitMotion）。

`TweenUtility` 是框架的缓动动画静态外观。框架内置基于 Unity 驱动循环的自研 `DefaultTweenHandler`；PrimeTween 与 LitMotion 两支第三方引擎后端不在框架里，分别由 `plugin` 分支的 `primetween`、`litmotion` 插件提供，装上才会出现在 Provider 下拉里。所有缓动方法统一接收 `TweenEase` 参数，支持从 `EEase` 枚举值或 `AnimationCurve` 隐式转换。

## 核心特性

- 可插拔 Handler：框架内置 `DefaultTweenHandler`（自研）；PrimeTween / LitMotion 后端由 `primetween`、`litmotion` 插件提供
- `TweenEase` 统一缓动参数：`EEase` 枚举（31 种内置曲线）/ `AnimationCurve` 双模式，隐式转换零分配
- 全套 Transform 补间：`Position` / `LocalPosition` / `Rotation` / `LocalRotation` / `Scale`（各支持 Vector3 与单轴）
- UI 补间：`UIAnchoredPosition` / `UISizeDelta` / `UISliderValue` / `UIFillAmount` / `UINormalizedPosition` / `CanvasGroup.Alpha` / `Graphic.Color` 等
- 精灵/材质补间：`SpriteRenderer.Color` / `SpriteRenderer.Alpha` / `MaterialColor`
- 贝塞尔路径：`MoveBezierPath` 沿路径移动
- 自定义补间：`Custom<T>` 泛型回调（float/int/long/Vector3），零分配 `Custom(object, ...)` 重载
- 生命周期控制：`Stop` / `Complete` / `StopAll` / `CompleteAll` / `Delay`，返回 `long tweenId`
- 定期缓存清理：`TweenManager` 按间隔调用 `ReleaseUnusedTween`，防止底层库缓存膨胀

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `TweenUtility` | 静态外观，提供所有缓动方法、`EEase` 枚举、`ECycleMode` 枚举 |
| `TweenHandler` | 抽象基类，定义全部缓动抽象方法 + `TweenManager` 嵌套管理器类 |
| `TweenEase` | 统一缓动参数结构体，支持 `EEase` / `AnimationCurve` 隐式转换 |
| `TweenUtility.EEase` | 31 种内置缓动曲线枚举（Linear / InQuad / OutBounce / InElastic 等） |
| `TweenUtility.ECycleMode` | 循环模式枚举（Restart / Yoyo 等） |
| `DefaultTweenHandler` | 框架内置默认实现，自研缓动引擎 |
| `TweenHandler.TweenManager` | 定期调用已注册 handler 的 `ReleaseUnusedTween` 清理缓存 |

## 快速上手

```csharp
// 位置缓动（默认 Linear）
TweenUtility.Position(transform, targetPos, 0.3f);

// 指定缓动曲线
TweenUtility.Position(transform, targetPos, 0.3f, TweenUtility.EEase.OutQuad);

// 使用 AnimationCurve
TweenUtility.Position(transform, targetPos, 0.3f, myAnimationCurve);

// 指定起止值
TweenUtility.Position(transform, startPos, targetPos, 0.3f, TweenUtility.EEase.OutBounce);

// 缩放
TweenUtility.Scale(transform, 1.5f, 0.3f);

// 旋转（Quaternion 或 Vector3）
TweenUtility.Rotation(transform, Quaternion.Euler(0, 90, 0), 0.3f);

// 延迟
long tweenId = TweenUtility.Delay(1.0f, () => Debug.Log("Done"));

// 停止/完成
TweenUtility.Stop(tweenId);
TweenUtility.CompleteAll();
```

## 进阶用法

### 循环与时间缩放

```csharp
// 循环 3 次，Yoyo 模式
TweenUtility.Position(transform, targetPos, 0.3f,
    ease: TweenUtility.EEase.InOutQuad,
    cycles: 3,
    cycleMode: TweenUtility.ECycleMode.Yoyo,
    startDelay: 0.5f,
    useUnscaledTime: false,
    onComplete: () => Debug.Log("Done"));
```

### UI 补间

```csharp
// UI AnchoredPosition
TweenUtility.UIAnchoredPosition(rectTransform, new Vector2(100, 0), 0.3f);

// CanvasGroup 透明度
TweenUtility.Alpha(canvasGroup, 0f, 0.2f);

// Slider 值
TweenUtility.UISliderValue(slider, 0.5f, 0.3f);

// ScrollRect 归一化位置
TweenUtility.UINormalizedPosition(scrollRect, Vector2.one, 0.5f);

// Image.FillAmount
TweenUtility.UIFillAmount(image, 0f, 0.5f);
```

### 自定义补间

```csharp
// 泛型自定义（float 插值）
TweenUtility.Custom(transform, 0f, 100f, 2.0f, (t, v) => {
    Debug.Log($"Progress: {v}");
}, ease: TweenUtility.EEase.OutCirc);

// 零分配自定义（object 目标，static lambda 无闭包）
TweenUtility.Custom(transform, 0f, 100f, 2.0f, static (t, v) => {
    ((Transform)t).localScale = Vector3.one * v;
});
```

### 贝塞尔路径

```csharp
Vector3[] path = new Vector3[] {
    startPos, control1, control2, endPos
};
TweenUtility.MoveBezierPath(transform, path, 2.0f, TweenUtility.EEase.InOutSine);
```

### 切换 Handler

后端一律在 settings 里选，不在游戏代码里 `new`：`Tools/Framework Settings` 打开 `GameAppSettings`，把「补间后端」下拉切到目标引擎。

- 下拉里的 PrimeTween / LitMotion 两项分别来自 `plugin` 分支的 `primetween`、`litmotion` 插件（经 `Tools/Moirai/Package Manager` 安装）。没装插件时它们不存在，下拉只有框架内置的 `DefaultTweenHandler`。
- `primetween` 的前置是 `com.kyrylokuzyk.primetween`，宏 `PRIMETWEEN_INSTALLED` 由插件 asmdef 的 versionDefines 自动定义；让框架代码能直接引用 PrimeTween 类型的那份 `PrimeTween.Runtime.asmref` 随插件目录走（`primetween/PrimeTween/`），框架不再持有。`litmotion` 的前置是 `com.annulusgames.lit-motion`（该包在 UPM 注册表上没有正式版本，按官方指引以 git 渠道安装）加 `com.cysharp.unitask`，宏 `LITMOTION_INSTALLED` 同样由 versionDefines 自动定义。
- 该字段是 `[SerializeReference]`，切换会改变资产里的 `asm:` 归属，切完需保存资产。

## 注意事项

- `TweenUtility.Handler` 是 `internal` 静态属性（源生成器产出），游戏与插件侧代码都不直接赋值——换后端只走 `GameAppSettings` 的 Provider 下拉；该 setter 拒绝 null（fail-fast）
- 跨域重载（进入 Play 模式）时自动重置 handler 和帧监听，防止陈旧引用
- `TweenManager` 默认每 60 秒清理一次缓存，可通过 `TweenHandler` 的 `m_CheckInterval` 调整
- `TweenEase` 枚举模式零堆分配，`AnimationCurve` 模式持有曲线引用
- `Custom(object, ...)` 零分配重载配合 static lambda 使用，避免泛型闭包分配

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [UI](UI.md) · [GameApp](GameApp.md)
