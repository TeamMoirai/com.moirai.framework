# UI Module

> UGUI-based stack window management framework providing window lifecycle, layer depth sorting, modal blocking, Widget sub-controls, and multi-resolution adaptation.

The UI module (`Moirai.Atropos.UI`) abstracts UI into pure C# `UIWindow` / `UIWidget` classes, with `UIModule` managing the window stack, layer depth, and visibility. Window panels are loaded and instantiated via the resource module (YooAsset) or `Resources`. Window classes themselves do not attach MonoBehaviour. All operations — opening, closing, hiding, and querying — are accessible through the `GameModule.UI` static accessor.

## Core Features

- Stack-based window management: Insert sorting by `UILayer` level, with auto-incrementing depth for windows on the same layer (`LAYER_DEEP = 2000`, `WINDOW_DEEP = 100`)
- Five-tier layers: `Bottom` / `UI` / `Popup` / `Tips` / `System`, where `UI`, `Popup`, and `System` are modal layers
- Full lifecycle: `OnCreate` -> `OnRefresh` -> `OnUpdate` -> `OnClose` -> `OnDestroy`, with overridable open/close animations
- Modal blocking: When a modal window is pushed onto the stack, interaction with underlying windows is automatically disabled (`Interactable`); `IsBlockedByModal` can be used to query blocking status
- Full-screen window optimization: Windows beneath a full-screen window are automatically hidden, reducing rendering and update overhead
- Window caching: When `cacheInstance` is enabled, the window instance is not destroyed on close, and subsequent opens reuse the same instance
- Widget sub-controls: Embedded controls within a window reuse the same lifecycle, supporting creation by node path, resource path, or prefab
- Multi-resolution adaptation: Safe area (notch screen) adaptation, `UIAdapter` layout adapters (horizontal / vertical / radial / safe area)
- Editor code generation: `GameObject/ScriptGenerator` menu automatically generates UI binding code

## Core Types

| Class/Interface | Description |
|---------|------|
| `Moirai.Atropos.UI.IUIModule` | UI module interface, returned by `GameModule.UI` |
| `Moirai.Atropos.UI.UIModule` | UI module implementation, manages window stack, depth sorting, and visibility control; static properties `UIRoot`, `Resource` |
| `Moirai.Atropos.UI.UIBase` | UI base class, defines lifecycle virtual methods and Widget creation API |
| `Moirai.Atropos.UI.UIWindow` | Window abstract base class, inherits `UIBase`, includes Canvas depth, visibility, interactability, and open/close animations |
| `Moirai.Atropos.UI.UIWidget` | Window embedded control base class, inherits `UIBase` |
| `Moirai.Atropos.UI.WindowAttribute` | Window attribute, declares layer, resource address, full-screen, caching, and other configuration |
| `Moirai.Atropos.UI.UILayer` | UI layer enum: `Bottom=0`, `UI=1`, `Popup=2`, `Tips=3`, `System=4` |
| `Moirai.Atropos.UI.UIModuleEvent` | Window open/close events (`Shown` / `Closed`), dispatched via `EventManager` |
| `Moirai.Atropos.UI.UIModuleHelper` | Interaction helper: `IsInteractionBlockedByModal`, `IsUIObjectInteractable` |
| `Moirai.Atropos.UI.IUIResourceLoader` | UI resource loader interface, default implementation `UIResourceLoader` uses the resource module |
| `Moirai.Atropos.UI.UIBindComponent` | Window/Widget component binding MonoBehaviour base class |
| `Moirai.Atropos.UI.ErrorLogger` | Runtime exception handler, displays a `LogUI` window on exception |
| `Moirai.Atropos.UI.Adapter.AdapterBase` | Layout adapter abstract base class (`Moirai.Atropos.UI.Adapter` namespace) |

## Quick Start

Define a window (window classes must have a parameterless constructor, i.e., `new()` constraint):

```csharp
using Moirai.Atropos.UI;

// Layer Popup, non-fullscreen, cache instance on close
[Window(UILayer.Popup, location: "MainWindow", fullScreen: false, cacheInstance: true)]
public class MainWindow : UIWindow
{
    protected override void ScriptGenerator() { }   // Generated binding code override

    protected override void OnCreate() { /* First creation, bind events */ }

    protected override void OnRefresh() { /* Refresh when opened or when the top window closes; access parameters via UserData/Params */ }

    protected override void OnUpdate() { /* Per-frame update (visible windows only) */ }

    protected override void OnClose() { /* Cleanup on close */ }

    protected override void OnDestroy() { /* Instance destruction */ }
}
```

Opening and closing windows:

```csharp
// Synchronous open (automatically falls back to async on WebGL)
GameModule.UI.ShowUI<MainWindow>();

// Asynchronous open, with optional custom parameters (accessed via UserData / Params inside the window)
GameModule.UI.ShowUIAsync<MainWindow>(userData: 1001);

// Asynchronous open and await completion (60-second timeout)
UIWindow window = await GameModule.UI.ShowUIAsyncAwait<MainWindow>();

// Close / Hide (auto-closes after HideTimeToClose seconds)
GameModule.UI.CloseUI<MainWindow>();
GameModule.UI.HideUI<MainWindow>();

// Query
bool exist = GameModule.UI.HasWindow<MainWindow>();
UIWindow top = GameModule.UI.GetTopWindow();
```

## Advanced Usage

### Window Layer and Depth

The window stack is sorted by insertion order at the `WindowLayer` level. `OnSortWindowDepth` sets `sortingOrder` on the Canvas starting from `layer * LAYER_DEEP`, incrementing by `WINDOW_DEEP` for each window on the same layer. When a modal layer window (`UI`/`Popup`/`System`) is pushed onto the stack, the immediately underlying window is automatically set to non-interactable:

```csharp
// Close all windows except the System layer
GameModule.UI.CloseAllWithOut(UILayer.System);

// Check if a UI object is blocked by a modal window
bool blocked = GameModule.UI.IsBlockedByModal(gameObject);
```

### Widget Sub-Controls

Widgets reuse the window's lifecycle methods and are driven by their parent window. Create them inside a window/Widget via the factory methods provided by `UIBase`:

```csharp
// Create from an existing node path within the window
HeroItemWidget item = CreateWidget<HeroItemWidget>("m_list/m_heroItem");

// Create synchronously/asynchronously by resource location path
HeroItemWidget item2 = CreateWidgetByPath<HeroItemWidget>(parentTrans, "HeroItem");
HeroItemWidget item3 = await CreateWidgetByPathAsync<HeroItemWidget>(parentTrans, "HeroItem");

// Create from a prefab copy (commonly used for list items)
HeroItemWidget item4 = CreateWidgetByPrefab<HeroItemWidget>(prefab, parentTrans);

// Batch resize list icons (includes async frame-by-frame version AsyncAdjustIconNum)
AdjustIconNum<HeroItemWidget>(_items, count, parentTrans, prefab);
```

### Open/Close Animation and Interaction Lock

Windows have a built-in default 0.5-second open / 0.25-second close wait time, which can be overridden with custom animations. During animation, the window automatically locks interaction, and modal windows also coordinate with the input module (`GameModule.Input.PreventInteractionUI`):

```csharp
protected override async UniTask OpenAnimation()
{
    await panel.DOFade(1f, 0.3f);  // Play custom animation
}
```

### Safe Area and UIAdapter

- Within a window: `SetUIFit(RectTransform, liuHaiFit, topSpacing, bottomFit, bottomSpacing)` adjusts the specified node for notch screen top/bottom padding; `SetUINotFit` excludes individual nodes.
- Global: Static method `UIModule.ApplyScreenSafeRect(Rect)` directly adjusts UIRoot; `UIModule.SimulateIPhoneXNotchScreen()` simulates a notched screen in the editor.
- Layout adapters (`Moirai.Atropos.UI.Adapter`): `SafeAreaAdapter` (safe area), `HorizontalAdapter` / `VerticalAdapter` (horizontal/vertical auto-arrangement, supports `Gap`), `AngleAdapter` (radial arrangement, supports `Distance`, `BiasAngle`, `Clockwise`). All are MonoBehaviour components that can recalculate each frame.

### Runtime Error Window

When the debugger configuration (`DebuggerComp.ActiveWindowType`) determines that error logging is not enabled, the module registers `ErrorLogger` to capture `LogType.Exception` and automatically displays the built-in `LogUI` window (`[Window(UILayer.System, fromResources:true)]`, prefab located at module `Resources/LogUI.prefab`) for viewing exception stack traces one by one.

### Editor Binding Code Generation

Select the root node of a UI prefab and use the menu:

- `GameObject/ScriptGenerator/Generate Binding Code`: Generates a `partial class XXX : UIWindow` script and `XXXBinder : UIBindComponent` binding component
- `GameObject/ScriptGenerator/Copy Binding Properties`: Copies member variable code to the clipboard

## Notes

- A GameObject named `UIRoot` with a `Canvas` child must exist in the scene, otherwise initialization will report a Fatal error; UIRoot will automatically be set to `DontDestroyOnLoad`
- `ShowUI` synchronous loading depends on the resource module's synchronous loading capability; on WebGL it automatically falls back to async; `ShowUIAsync` is recommended
- `HideUI` only takes effect when `HideTimeToClose > 0`; otherwise it is equivalent to `CloseUI`
- `GetUIAsyncAwait<T>()` / `GetUIAsync<T>` only waits for the loading of an already-open window; returns null / no callback if the window does not exist
- Window updates (`OnUpdate`) are only triggered for visible windows; full-screen windows will block the visibility of windows beneath them

---
[« Back to Main README](../README_EN.md) · [Input](Input.md) · [Scene](Scene.md)