# UI Service

> Multi-backend stack window management framework (uGUI / UI Toolkit / custom backends) providing window lifecycle, layer depth sorting, modal blocking, Widget sub-controls, and multi-resolution adaptation.

The UI service (`Moirai.Atropos.UI`) abstracts UI into pure C# classes: `UIWindow` is the backend-agnostic window object model (holding the three panel intents — visibility / depth / interactability — plus lifecycle), and each rendering backend (a "track") provides its own window base class — business windows on the uGUI track inherit `UGUIWindow`, on the UI Toolkit track `UITKWindow`; `UIWidget` is an embedded sub-control. Multiple backends can coexist in the same session, sharing a single window stack: stack order, layer depth, visibility, and modal blocking are track-agnostic. `UIService` serves as the static facade exposing all APIs. Window panels are loaded and instantiated via the resource service (YooAsset) or `Resources`. Window classes themselves do not attach MonoBehaviour. All operations — opening, closing, hiding, and querying — are accessible through the `UIService.Xxx()` static methods.

## Architecture (Multi-Backend Tracks)

The UI service organizes rendering backends as "tracks": each backend's three-piece set is self-contained, and the main entry knows no specific backend.

- **`UIService`**: Static facade (`[AutoRegisterService]` + `[ServiceDependency(typeof(DebuggerService), typeof(ResourceService), typeof(TimerService), typeof(InputService))]`) carrying the generic APIs — Type-form open dispatch, close/hide/query, modal interaction lease, safe-area broadcast, and lifecycle; all public members are static methods/properties
- **Backend tracks** (each = one partial + one driver + one window base):
  - `Handler/UGUI/`: `UIService.UGUI.cs` (slot, claim gate, open-window legs, and track self-registration) + `UGUIHandler` (UI root binding, camera, error logging, CanvasScaler safe-area landing) + `UGUIWindow` (uGUI panel intent application)
  - `Handler/UITK/`: `UIService.UITK.cs` + `UITKHandler` + `UITKWindow` (`UIDocument` shell + window-level `PanelSettings`); this track's document shells parent under the uGUI track's UI root
- **`UIWindowLedger`**: holder of the shared window stack, parking table, and interaction lease — windows from every track share one stack; close/hide/query are track-agnostic
- **`UITrack`**: the track's self-description (window-claim predicate, validity probe, Type-form open implementation, shutdown order). Each track self-registers into the facade catalog from its own partial's static initializer; the main file only enumerates the catalog for dispatch, `IsValid` aggregation, and ascending shutdown — **adding a track = adding one partial self-registration, zero changes to the main file**
- **`UIServiceSettings`**: Framework settings (menu "UI Settings"); the `[SerializeReference]` enablement list `EnabledHandlers` decides which tracks get enabled at init (multiple at once); an empty list throws at init — there is no "auto-create one if unconfigured" fallback
- The service carries `[AutoRegisterService]`, so the composition root registers it from the generated built-in service list (App scope; `[ServiceDependency]` drives the topological init order); you can also register manually with `GameServices.RegisterService(EServiceScopeKind.App, new UIService())`

Shutdown order is expressed by each track's self-declared tier: the track hosting a root that other tracks' panels attach to (uGUI) takes `UITrack.SHUTDOWN_ORDER_HOST` and closes last; the rest take `SHUTDOWN_ORDER_DEFAULT` and close first.

## Core Features

- Multi-backend coexistence: the two built-in tracks (uGUI, UI Toolkit) can coexist in one session sharing a single window stack; adding a backend only requires its own partial file — zero changes to the main entry
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
| `Moirai.Atropos.UI.UIService` | UI service static facade carrying the generic APIs: Type-form open, close/hide/query, modal lease, safe-area broadcast; static properties `IsValid` (true when any track's driver is in place), `CurrentModal`, and the track-specific queries `UIRoot` / `UICamera` (answered by the uGUI track only; `null` when unenabled) |
| `Moirai.Atropos.UI.UIServiceHandler` | Backend driver abstract base class (inherits `FrameworkHandler`): the common contract of `UGUIHandler` / `UITKHandler`, including the per-track window predicate `IsWindowOnOwnTrack` and the self-describing registration `Internal_Register` |
| `Moirai.Atropos.UI.UGUIHandler` | uGUI track driver (`Handler/UGUI/`): UI root binding, UI camera, error logging, CanvasScaler safe-area conversion landing |
| `Moirai.Atropos.UI.UITKHandler` | UI Toolkit track driver (`Handler/UITK/`): closes its own track's windows on shutdown; the panel body lives on `UITKWindow` |
| `Moirai.Atropos.UI.UITrack` | Track self-description: window-claim predicate, validity probe, Type-form open implementation, shutdown order; each track self-registers into the facade catalog from its partial's static initializer |
| `Moirai.Atropos.UI.UIServiceSettings` | Framework settings: the `EnabledHandlers` enablement list (`[SerializeReference]`) decides which backends get initialized at init; multiple entries coexist |
| `Moirai.Atropos.UI.UIRootBinding` | UI root binding component: put it on the scene object acting as the UI root; `SingletonMono` first-wins registers it, and the uGUI track's `UGUIHandler` reads it via `TryGetInstance()` (never auto-creates; lookup by name is gone) |
| `Moirai.Atropos.UI.UIBase` | UI base class, defines lifecycle virtual methods and Widget creation API |
| `Moirai.Atropos.UI.UIWindow` | Backend-agnostic window object model (inherits `UIBase`): the three panel intents — visibility / depth / interactability — plus lifecycle and open/close animations; panel loading is implemented by track base classes, and a window directly inheriting it cannot open a panel |
| `Moirai.Atropos.UI.UGUIWindow` | uGUI-track window base class (`Handler/UGUI/`): applies the three intents to a GameObject / Canvas / GraphicRaycaster panel — **uGUI business windows always inherit this class** |
| `Moirai.Atropos.UI.UITKWindow` | UI Toolkit track window base class (`Handler/UITK/`): `UIDocument` shell and content-root assembly, window-level `PanelSettings` override (the extra parameter the open-window family has over the uGUI legs); UI Toolkit business windows inherit this class |
| `Moirai.Atropos.UI.UIWidget` | Window embedded control base class, inherits `UIBase` |
| `Moirai.Atropos.UI.WindowAttribute` | Window attribute, declares layer, resource address, full-screen, caching, and other configuration |
| `Moirai.Atropos.UI.UILayer` | UI layer enum: `Bottom=0`, `UI=1`, `Popup=2`, `Tips=3`, `System=4` |
| `Moirai.Atropos.UI.UIServiceEvent` | Window open/close events (`Shown` / `Closed`), dispatched via `EventManager` |
| `Moirai.Atropos.UI.UIServiceHelper` | Interaction helper: `IsInteractionBlockedByModal`, `IsUIObjectInteractable` |
| `Moirai.Atropos.UI.UIBindComponent` | Window/Widget component binding MonoBehaviour base class |
| `Moirai.Atropos.UI.ErrorLogger` | Runtime exception handler, displays a `LogUI` window on exception |
| `Moirai.Atropos.UI.Adapter.AdapterBase` | Layout adapter abstract base class (`Moirai.Atropos.UI.Adapter` namespace) |

## Quick Start

Define a window (window classes must have a parameterless constructor, i.e., `new()` constraint):

```csharp
using Moirai.Atropos.UI;

// Layer Popup, non-fullscreen, cache instance on close
[Window(UILayer.Popup, location: "MainWindow", fullScreen: false, cacheInstance: true)]
public class MainWindow : UGUIWindow
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
UIService.ShowUI<MainWindow>();

// Asynchronous open, with optional custom parameters (accessed via UserData / Params inside the window)
UIService.ShowUIAsync<MainWindow>(userData: new object[] { 1001 });

// Asynchronous open and await completion (60-second timeout)
UIWindow window = await UIService.ShowUIAsyncAwait<MainWindow>();

// Close / Hide (auto-closes after HideTimeToClose seconds)
UIService.CloseUI<MainWindow>();
UIService.HideUI<MainWindow>();

// Query
bool exist = UIService.HasWindow<MainWindow>();
UIWindow top = UIService.GetTopWindow();
```

## Advanced Usage

### Multi-Backend and Third-Track Extension

Each backend = one "track", made of three pieces: the `UIService.<Track>.cs` partial (slot, claim gate, open-window legs, and `UITrack` self-registration), the `<Track>Handler` (driver), and the `<Track>Window` (window base class). The main entry `UIService.cs` carries only the generic logic — Type-form claim dispatch, shared stack orchestration, `IsValid` aggregation, and ascending shutdown — and registers no specific backend.

The complete checklist for adding a new backend (e.g., FairyGUI):

1. `Handler/FairyGUI/FairyGUIWindow.cs`: the window base class, inheriting `UIWindow` and overriding the seven panel hooks (`LoadPanel` / `LoadPanelAsync` / `ApplyVisible` / `ApplyDepth` / `ApplyInteractable` / `ParkPanel` / `DestroyPanel`)
2. `Handler/FairyGUI/FairyGUIHandler.cs`: the driver, inheriting `UIServiceHandler`; self-describes its window predicate (`IsWindowOnOwnTrack`) and its registration gate (`Internal_Register` → this track partial's claim gate)
3. `Handler/FairyGUI/UIService.FairyGUI.cs`: the track partial — driver slot + claim gate (compare-exchange slot, attach the shutdown callback), three broadcast subscriptions (per-frame duty / safe area / notch simulation), the open-window legs (the generic `ShowUI<T>` family constrained on `FairyGUIWindow`), and the static `UITrack` self-registration (track name, window base class, shutdown tier)
4. Add the track's driver entry to the `UIServiceSettings` enablement list (Inspector managed-reference list)

Pick the shutdown tier by where the panels attach: panels under the track's own root take `UITrack.SHUTDOWN_ORDER_DEFAULT`; panels attached under another track's host root (e.g., UI Toolkit shells under the uGUI root) take `UITrack.SHUTDOWN_ORDER_HOST` and close last. From then on, claim dispatch, `IsValid`, and shutdown collection all cover the new track automatically by enumerating the catalog — zero changes to the main file.

### Window Layer and Depth

The window stack is sorted by insertion order at the `WindowLayer` level. `OnSortWindowDepth` sets `sortingOrder` on the Canvas starting from `layer * LAYER_DEEP`, incrementing by `WINDOW_DEEP` for each window on the same layer. When a modal layer window (`UI`/`Popup`/`System`) is pushed onto the stack, the immediately underlying window is automatically set to non-interactable:

```csharp
// Close all windows except the System layer
UIService.CloseAllWithOut(UILayer.System);

// Check if a UI object is blocked by a modal window
bool blocked = UIService.IsBlockedByModal(gameObject);
```

### Widget Sub-Controls

Widgets reuse the window's lifecycle methods and are driven by their parent window. Create them via the `UIWidget` factory methods inside a window/Widget:

```csharp
// Create on an existing node inside the window
var item = new HeroItemWidget();
item.Create(this, widgetRootGo);

// Create by instantiating from a resource location
var item2 = new HeroItemWidget();
item2.CreateByPath("HeroItem", this, parentTrans);

// Create from a prefab copy (commonly used for list items)
var item3 = new HeroItemWidget();
item3.CreateByPrefab(this, goPrefab, parentTrans);

// Destroy / hand back
item.Destroy();
```

### Open/Close Animation and Interaction Lock

Windows have a built-in default 0.5-second open / 0.25-second close wait time, which can be overridden with custom animations. During animation, the window automatically locks interaction, and modal windows also coordinate with the input service (`InputService.PreventInteractionUI`). The hand-back happens in the *current* transition: the global suppression flag is cleared only by its recorded owner (`UIInteractionLease`), and an animation continuation superseded by a reopen/destroy neither unlocks nor hides the window, so an overridden animation does not need to detect being taken over itself:

```csharp
protected override async UniTask OpenAnimation()
{
    await panel.DOFade(1f, 0.3f);  // Play custom animation
}
```

### Deferred Close (Popup Self-Close Policy)

Window self-close settles immediately by default; popup-style windows can override `DeferCloseUntilInteractable => true` to defer the close until interactable (open animation finished, modal above released), then pass the `CanClose` gate:

```csharp
[Window(UILayer.Popup)]
public class RenameWindow : UGUIWindow
{
    protected override bool DeferCloseUntilInteractable => true;   // wait until interactable

    protected override bool CanClose => _input.text.Length > 0;   // failing the gate lands in OnCloseFail

    protected override void OnCloseFail() { /* notify invalid input; the window stays on the stack */ }
}
```

- The policy lives on `UIWindow` (the backend-agnostic object model): windows on both the uGUI and UI Toolkit tracks override it the same way, with no per-track intermediate base class
- The wait passively observes: if the window is taken over by a reopen/destroy while waiting, that round terminates silently and the takeover side handles the hand-back; a destroyed window never polls in vain
- The real close after the gate passes shares the same settlement path as the immediate mode (`CloseUI` / the shared stack) — there are not two settlement systems

### Safe Area and UIAdapter

- Within a window: `SetUIFit(RectTransform, liuHaiFit, topSpacing, bottomFit, bottomSpacing)` adjusts the specified node for notch screen top/bottom padding; `SetUINotFit` excludes individual nodes.
- Global: Static method `UIService.ApplyScreenSafeRect(Rect)` directly adjusts UIRoot; `UIService.SimulateIPhoneXNotchScreen()` simulates a notched screen in the editor.
- Layout adapters (`Moirai.Atropos.UI.Adapter`): `SafeAreaAdapter` (safe area), `HorizontalAdapter` / `VerticalAdapter` (horizontal/vertical auto-arrangement, supports `Gap`), `AngleAdapter` (radial arrangement, supports `Distance`, `BiasAngle`, `Clockwise`). All are MonoBehaviour components that can recalculate each frame.

### Runtime Error Window

The service registers `ErrorLogger` (capturing `LogType.Exception` and automatically showing the built-in `LogUI` window — `[Window(UILayer.System, fromResources:true)]`, prefab at the service's `Resources/LogUI.prefab`) only when the debugger configuration (`DebuggerService.ActiveWindowType`) says error logging **is** enabled. Enablement rule: `AlwaysOpen` always; `OnlyOpenWhenDevelopment` follows development builds; `OnlyOpenInEditor` follows the editor; `AlwaysClose` and `OnlyOpenWhenDevelopment` outside a development build (i.e. the default release shape) never enable it, so exceptions pop no window.

### Editor Binding Code Generation

Select the root node of a UI prefab and use the menu:

- `GameObject/ScriptGenerator/生成绑定代码` (Generate Binding Code): Generates a `partial class XXX : UGUIWindow` window script and `XXXBinder : UIBindComponent` binding component
- `GameObject/ScriptGenerator/复制绑定属性` (Copy Binding Properties): Copies member variable code to the clipboard

## Notes

- The uGUI track's UI root is registered by the `UIRootBinding` component on a scene object (which must have a `Canvas` under it): `SingletonMono` first-wins, a later duplicate's whole GameObject is destroyed; read it via `TryGetInstance()`, which only reads back and never auto-creates. The backend picks it up on the first Update tick, logs one Error when nothing is bound and one Fatal when the bound root has no Canvas, then keeps waiting each frame (late additive scenes, runtime-instantiated roots, and a Canvas added later all bind). Once bound, the UI root is automatically set to `DontDestroyOnLoad` (play mode only). **Lookup by object name is gone** — renaming silently breaks it, and under multiple scenes or hot updates a same-named object can win. The UI Toolkit track's document shells also parent under this root and are collected before the root is destroyed on shutdown.
- `ShowUI` synchronous loading depends on the resource service's synchronous loading capability; on WebGL it automatically falls back to async; `ShowUIAsync` is recommended
- `HideUI` only takes effect when `HideTimeToClose > 0`; otherwise it is equivalent to `CloseUI`
- `GetUIAsyncAwait<T>()` / `GetUIAsync<T>` only waits for the loading of an already-open window; returns null / no callback if the window does not exist
- Window updates (`OnUpdate`) are only triggered for visible windows; full-screen windows will block the visibility of windows beneath them

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Input](Input.md) · [Scene](Scene.md) · [Audio](Audio.md)
