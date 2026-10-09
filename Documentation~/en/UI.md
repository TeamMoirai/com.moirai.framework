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
- Stack-based window management: Insert sorting by `EUILayer` level, with auto-incrementing depth for windows on the same layer (`LAYER_DEEP = 2000`, `WINDOW_DEEP = 100`)
- Five-tier layers: `Bottom` / `UI` / `Popup` / `Tips` / `System`, where `UI`, `Popup`, and `System` are modal layers
- Three-state modal flag: `[Window(modal:)]` takes `EUIModal` — `Inherit` (default, resolved from the layer), `Modal` (forces modal on a non-modal layer), `NonModal` (forces non-modal on a modal layer); suppressing the layer below, holding the global suppression flag during a transition, and the `CurrentModal` query all read the single bit resolved at window init
- Full lifecycle: `OnCreate` -> `OnRefresh` -> `OnUpdate` -> `OnClose` -> `OnDestroy`; open/close transitions go through `IUITransition` (override `UIWindow.Transition` to return an implementation; absent means instant — open by default has no delay or input lock, closing parks immediately)
- Window registration: every window class must carry `[Window]`; the `UIWindowCodegen` source generator resolves it at compile time into `UIWindowRegistry` (descriptor + compile-time factory) — a class without the attribute cannot open
- Modal blocking: When a modal window is pushed onto the stack, interaction with underlying windows is automatically disabled (`Interactable`); `IsBlockedByModal` can be used to query blocking status
- Open-result contract: `ShowUIAwaitResult<T>` / `GetUIAwaitResult<T>` return a `UIOpenResult` whose `EUIOpenStatus` distinguishes five outcomes (`Opened` / `Failed` / `Missing` / `Timeout` / `Cancelled`) — a window whose load failed is rolled off the stack on the spot, so waiting no longer conflates null with timeout
- Two payload channels: the per-track generic bases `UGUIWindow<TArg>` / `UITKWindow<TArg>` each carry one strongly typed `Payload` slot — a **static leg** whose window class is known at compile time pushes it through the generic channel as `in TArg` (no boxing for structs, never touching `UIPayload`), while the **dynamic leg** that only has a runtime `Type` erases it into `UIPayload` (reference types store the reference itself; value types box once)
- In-flight merge, last-wins: reopening a window whose load is still in flight does not restart the load and does not push a second instance; the payload is overwritten by the last one and `OnRefresh` only ever sees the final one
- Every leg takes a `CancellationToken`: `default` costs nothing; cancellation only matters while the load is in flight — a void leg rolls back silently, an await leg rethrows `OperationCanceledException`, a result leg lands the `Cancelled` tier, distinct from `Timeout`
- Minimal navigation: `NavigationDepth` and `TryCloseTopWindow()` answer "which window opened most recently" by **open order** (the layer-sorted stack cannot answer that), and closing the top goes through the existing `CanClose` policy
- Lifecycle hook isolation: a throwing hook is quarantined and the flow continues, all by hand-written try/catch with no lambda wrappers (zero allocation on the per-frame path); see the "Lifecycle Hook Isolation" table
- Full-screen window optimization: Windows beneath a full-screen window are automatically hidden, reducing rendering and update overhead
- Window caching: `cacheTimeToDestroy` carries all three shapes on its own — `0` means no caching (destroy on close, the default), a positive value parks the instance and destroys it that many seconds later, a negative value parks it forever; a parked instance is reused directly on the next open, and re-taking it cancels the timer
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
| `Moirai.Atropos.UI.UIWindow` | Backend-agnostic window object model (inherits `UIBase`): the three panel intents — visibility / depth / interactability — plus lifecycle and open/close transitions (`Transition`); panel loading is implemented by track base classes, and a window directly inheriting it cannot open a panel |
| `Moirai.Atropos.UI.UGUIWindow` | uGUI-track window base class (`Handler/UGUI/`): applies the three intents to a GameObject / Canvas / GraphicRaycaster panel — **uGUI business windows always inherit this class** (the payload-carrying ones inherit `UGUIWindow<TArg>`) |
| `Moirai.Atropos.UI.UITKWindow` | UI Toolkit track window base class (`Handler/UITK/`): `UIDocument` shell and content-root assembly, window-level `PanelSettings` override (the extra parameter the open-window family has over the uGUI legs); UI Toolkit business windows inherit this class (the payload-carrying ones inherit `UITKWindow<TArg>`) |
| `Moirai.Atropos.UI.UGUIWindow<TArg>` / `UITKWindow<TArg>` | The two tracks' payload-carrying bases: at most one strongly typed DTO per open, read through `Payload`; static legs push it through `IUIPayloadSlot<TArg>` directly, dynamic legs erase into `UIPayload` and read the same slot back |
| `Moirai.Atropos.UI.UIPayload` | The dynamic legs' only erasure carrier (`readonly struct`): `Empty` and a `null` reference are the same case; `From` / `To<T>` / `TryGet<T>` — every failure surface is a `GameException` naming the expected type |
| `Moirai.Atropos.UI.IUIPayloadSlot<TArg>` | Internal generic bridge of the payload slot (`internal`): the ledger's generic channel lands the payload by `TArg` through it, never passing through `UIPayload`; implemented by both tracks' generic bases |
| `UIService.onWindowShown` / `onWindowClosed` | Window open/close broadcasts (`public static event Action<UIWindow>`): one per push and per pop, parked and destroyed windows both fire; subscribers own their pairing, and the facade's shutdown and reset gates detach every handler |
| `Moirai.Atropos.UI.UIWidget` | Window embedded control base class, inherits `UIBase` |
| `Moirai.Atropos.UI.WindowAttribute` | Window attribute (required), declares layer, resource address, full-screen, caching, and other configuration; resolved at compile time by the `UIWindowCodegen` source generator into `UIWindowRegistry` |
| `Moirai.Atropos.UI.EUILayer` | UI layer enum: `Bottom=0`, `UI=1`, `Popup=2`, `Tips=3`, `System=4` |
| `Moirai.Atropos.UI.EUIModal` | Modal-state enum: `Inherit=0` (resolved from the layer), `Modal=1`, `NonModal=2`; the `[Window(modal:)]` parameter type and the storage shape of `WindowAttribute.Modal` |
| `Moirai.Atropos.UI.UIWindowRegistry` | Window registry (`public static`): type handle → descriptor + compile-time factory, filled by each assembly's generated module initializer and read-only afterwards; `TryGet` is `internal` |
| `Moirai.Atropos.UI.UIWindowDescriptor` | Window metadata descriptor (`readonly struct`): the full `[Window]` argument set resolved once at registration, read directly at open time with zero reflection |
| `Moirai.Atropos.UI.IUITransition` | Open/close transition contract: `Play(open, ct)` plays and waits, `Snap(open)` drives the panel to its end state on the spot; returned from an overridden `UIWindow.Transition`, absent means instant |
| `Moirai.Atropos.UI.UIOpenResult` / `EUIOpenStatus` | Open/fetch outcome (`readonly struct` + five-tier `byte` enum: `Opened` / `Failed` / `Missing` / `Timeout` / `Cancelled`): `Window` and `Status` come back paired, `Success` and the implicit bool answer only the "ready" tier |
| `Moirai.Atropos.UI.UIServiceHelper` | Interaction helper: `IsInteractionBlockedByModal`, `IsUIObjectInteractable` |
| `Moirai.Atropos.UI.UIBindComponent` | Window/Widget component binding MonoBehaviour base class |
| `Moirai.Atropos.UI.ErrorLogger` | Runtime exception handler, displays a `LogUI` window on exception |
| `Moirai.Atropos.UI.Adapter.AdapterBase` | Layout adapter abstract base class (`Moirai.Atropos.UI.Adapter` namespace) |

## Quick Start

Define a window (window classes must have a parameterless constructor, i.e., `new()` constraint):

```csharp
using Moirai.Atropos.UI;

// Layer Popup, non-fullscreen, parks on close (negative = park forever; a positive value destroys on expiry)
[Window(EUILayer.Popup, location: "MainWindow", fullScreen: false, cacheTimeToDestroy: -1f)]
public class MainWindow : UGUIWindow
{
    protected override void ScriptGenerator() { }   // Generated binding code override

    protected override void OnCreate() { /* First creation, bind events */ }

    protected override void OnRefresh() { /* Refresh when opened or when the top window closes; payload-carrying windows read Payload here */ }

    protected override void OnUpdate() { /* Per-frame update (visible windows only) */ }

    protected override void OnClose() { /* Cleanup on close */ }

    protected override void OnDestroy() { /* Instance destruction */ }
}
```

Opening and closing windows:

```csharp
// Synchronous open (automatically falls back to async on WebGL)
UIService.ShowUI<MainWindow>();

// Asynchronous open (payload-free leg)
UIService.ShowUIAsync<MainWindow>();

// A payload-carrying open swaps to the two-type-argument family, payload first (read inside the window as Payload)
UIService.ShowUIAsync<DetailWindow, int>(1001);

// Dynamic leg, when the window class is only known at runtime: payload erased into UIPayload, addressing via the public static resolvers
UIService.ShowUIAsync(type, windowName, windowId,
    fromResources: false, payload: UIPayload.From(dto));

// Every leg takes a CancellationToken (default costs nothing); it only means something while the load is in flight
UIService.ShowUIAsync<MainWindow>(windowName: "Main", ct: cts.Token);

// Asynchronous open and await completion (60-second timeout; a caller cancellation rethrows OperationCanceledException)
UIWindow window = await UIService.ShowUIAsyncAwait<MainWindow>();

// Asynchronous open that awaits the terminal state: opened / failed / missing / timeout / cancelled are distinct
// (a failed window has already been rolled off the stack and must not be reused)
UIOpenResult result = await UIService.ShowUIAwaitResult<MainWindow>();
if (result.Status == EUIOpenStatus.Opened) { /* result.Window is usable */ }

// Close / Hide (auto-closes after HideTimeToClose seconds)
UIService.CloseUI<MainWindow>();
UIService.HideUI<MainWindow>();

// Query
bool exist = UIService.HasWindow<MainWindow>();
UIWindow top = UIService.GetTopWindow();

// Navigation: close the most recently opened window (open order, not layer order)
int depth = UIService.NavigationDepth;
bool closed = UIService.TryCloseTopWindow();
```

## Open-Leg Signatures

Eight legs per track = four payload-free + four payload-carrying; the two backends' same-named overloads are resolved by their window-base constraint, not by parameter count. Every UI Toolkit leg takes one extra `PanelSettings panelSettings = null` right before `ct` (the window-level panel configuration; `null` falls back to the shared one). The `Type`-form entry adds three dynamic legs, and there are two navigation members plus three fetch legs.

### Eight legs per track (uGUI signatures; UITK legs carry the extra `panelSettings`)

| Leg | Signature | Returns |
|---|---|---|
| Async, payload-free | `ShowUIAsync<T>(string windowName = null, string windowId = null, bool fromResources = false, CancellationToken ct = default)`, `T : UGUIWindow, new()` | `void` |
| Sync, payload-free | `ShowUI<T>(…same shape…), T : UGUIWindow, new()` | `void` |
| Await, payload-free | `ShowUIAsyncAwait<T>(…same shape…), T : UGUIWindow, new()` | `UniTask<UIWindow>` |
| Result, payload-free | `ShowUIAwaitResult<T>(…same shape…), T : UGUIWindow, new()` | `UniTask<UIOpenResult>` |
| Async, payload | `ShowUIAsync<TWindow, TArg>(in TArg payload, string windowName = null, string windowId = null, bool fromResources = false, CancellationToken ct = default)`, `TWindow : UGUIWindow<TArg>, new()` | `void` |
| Sync, payload | `ShowUI<TWindow, TArg>(in TArg payload, …same shape…)` | `void` |
| Await, payload | `ShowUIAsyncAwait<TWindow, TArg>(TArg payload, …same shape…)` | `UniTask<TWindow>` |
| Result, payload | `ShowUIAwaitResult<TWindow, TArg>(in TArg payload, …same shape…)` | `UniTask<UIOpenResult>` |

- The await payload leg takes `payload` as a plain parameter rather than `in`: `async` methods forbid `in` parameters (CS1988), and the ledger's two async generic channels take plain `TArg` for the same reason; the other three legs stay `in TArg` (generic push, no boxing for structs)
- On the UITK payload legs `panelSettings` sits after `fromResources` and before `ct`, and the payload is always the first slot — a dedicated case pins that mis-ordering guard
- Every leg passes the claim gate first: a track that was not enabled (empty slot) throws on the spot instead of silently no-oping or fabricating a driver

### Three dynamic legs (`Type`-form entry, payload as `UIPayload`)

| Leg | Signature | Returns |
|---|---|---|
| Async | `ShowUIAsync(Type type, string windowName = null, string windowId = null, bool fromResources = false, UIPayload payload = default, CancellationToken ct = default)` | `void` |
| Sync | `ShowUI(Type type, …same shape…)` | `void` |
| Await | `ShowUIAsyncAwait(Type type, …same shape…)` | `UniTask<UIWindow>` |

Track ownership is decided by each track's self-described window base: an unclaimable type throws, and a claimed track whose driver is absent throws as well — the window is never pushed and left waiting on a load that will silently fail. This parameter table has no window-level `panelSettings` (that is the extra slot on the UITK generic legs), so the `Type`-form entry always passes `null` there.

### Two navigation members and three fetch legs

| Member | Signature | Semantics |
|---|---|---|
| Navigation depth | `NavigationDepth` (`int` property) | Length of the open-order history (the layer-sorted stack cannot answer "which window opened most recently") |
| Close the top | `TryCloseTopWindow()` | Closes the most recently opened window through the existing `CanClose` policy; returns false when the history is empty, the window refuses, or it is mid-transition, and the history keeps it |
| Fetch, await | `GetUIAsyncAwait<T>()` | Returns `null` when no such name is on the stack or that window is another type |
| Fetch, callback | `GetUIAsync<T>(Action<T> callback)` | Logs one warning when not found; the callback is not invoked |
| Fetch, result | `GetUIAwaitResult<T>()` | Returns `UIOpenResult`; the `Missing` tier means that window is not on the stack |

The three fetch legs ask the same shared stack and only wait out an already-open window's load, so they take no caller token (internally only the 60-second bound applies).

## Advanced Usage

### Payload Channels: Static and Dynamic Legs

Each window carries at most one strongly typed payload (a DTO), living in the `Payload` slot of the track's generic base. Writes follow **overwrite** semantics: every open overwrites, closing does not clear, and the value survives until the next open replaces it.

```csharp
public struct RenameWindowPayload
{
    public string InitialText;
    public int MaxLength;
}

[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow<RenameWindowPayload>   // carrying a payload requires UGUIWindow<TArg> / UITKWindow<TArg>
{
    protected override void OnRefresh()
    {
        _input.text = Payload.InitialText;                    // the payload of this very open
        _input.maxLength = Payload.MaxLength;
    }
}

// Static leg: window class known at compile time -> generic push (no boxing, never touches UIPayload)
UIService.ShowUIAsync<RenameWindow, RenameWindowPayload>(in dto);

// Dynamic leg: only a runtime Type -> erased into UIPayload (reference stays a reference, value types box once)
UIService.ShowUIAsync(type, windowName, windowId,
    fromResources: false, payload: UIPayload.From(dto));
```

> Toolchain note: under this project's toolchain (C# 9 / netstandard2.1, no `IsExternalInit` polyfill) a `readonly struct` with writable public fields does not compile (CS8340 at the init sites; `readonly` fields plus an object initializer is CS0191, `{ get; init; }` is CS0518) — a DTO is a plain `struct` with public fields and an object initializer.

- Payload writes precede the prepare receipt and the push on the re-park and the new-open branches (both channels share the same rule), so `OnRefresh` always reads this open's payload; on the reuse branch the Pop->Push precedes the slot check (that window is already fully on the stack — a failed check still throws, but the receipt and the re-ordering have already happened)
- A slotless window (directly inheriting `UGUIWindow` / `UITKWindow`) given a non-empty payload throws on the spot — fail-fast, never silently swallowed; an empty payload on a slotless window is the legal case every payload-free leg takes
- A name hit whose slot type mismatches (`SetPayloadChecked` matching `IUIPayloadSlot<TArg>`), or a facade hit whose instance is not `TWindow`, throws `GameException` with both the expected and actual type names; "the throw precedes un-parking and pushing" describes the **re-park and new-open** branches, so it neither half-opens a window nor consumes the parked state (that instance is still retrievable from the parking table); on the reuse branch the Pop->Push precedes the slot check (see the bullet above), so the receipt and the re-ordering have already happened when it throws
- `UIPayload` failure surfaces: `To<T>` throws `GameException` on a type mismatch or an empty payload against a value type (message names the expected type); `TryGet<T>` returns false without throwing; `From(null)` reduces to `Empty`
- Allocation tiers: payload-free round-trips (the steady re-park path), static-leg struct/class payloads, and dynamic-leg class payloads all promise a zero delta; only a primitive/value-type payload on a dynamic leg may box once. The meter is `GC.GetAllocatedBytesForCurrentThread` (per-thread basis; the L3 `[Explicit]` benchmark `UIOpenAllocBenchmarkTests`, measured bytes exported to `Temp/ui-open-alloc-benchmark.txt` — under editor Mono that counter reads 0, so the real verdict comes from the player-side report)

### In-Flight Merging and Cancellation

Reopening a window whose load is still in flight — from any leg, static or dynamic channel — **merges into that in-flight load**: no second load is started, no second instance is pushed, and the payload is overwritten by the last one; `OnRefresh` runs once when the panel is ready and sees only the final payload.

- The caller token is only consumed while the load is in flight: a ready reuse and a re-park hand back synchronously without consuming `ct` (honest semantics — nothing pretends to be cancellable); reusing a window that is still loading registers the caller token just the same, and cancelling it aborts that in-flight load (the same semantics as the in-flight merge paragraph above)
- Three distinct landing points: a void leg's cancellation zeroes the waiter count, aborts the in-flight load and rolls it off the stack (a silent cancel, no Error logged); an await leg rethrows `OperationCanceledException`; a result leg returns `EUIOpenStatus.Cancelled`
- Separate from the timeout tier: the wait bound is 60 seconds, and a timeout logs one warning then still hands back that window (the result leg returns `Timeout`, the window may still be loading); a `Cancelled` result only means this wait settled as cancelled — whether the load continues depends on the remaining waiters (it rolls back if no one is left waiting) — neither tier may be treated as ready
- Waiter bookkeeping stays balanced: each waiter joins once and leaves once; a waiter settled by timeout does not decrement the count, and the load is only aborted once every registered, cancellable waiter has left

### Minimal Navigation

The window stack is insert-sorted by layer, so it cannot answer "which window opened most recently"; the ledger keeps a separate open-order history (`Push` appends, removal takes it out, and reuse or re-park moves that window to newest through Pop->Push). That history answers it.

```csharp
// Back button: close the most recently opened window through the existing CanClose policy (refusal returns false, history keeps it)
if (!UIService.TryCloseTopWindow()) UIService.CloseAll();

int depth = UIService.NavigationDepth;   // length of the open-order history
```

No routing table, no navigation events, no guard chain — `NavigationDepth` and `TryCloseTopWindow()` are the whole surface of this layer.

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
UIService.CloseAllWithOut(EUILayer.System);

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

### Open/Close Transitions and Interaction Lock

Windows have no built-in open/close animation by default — opening and closing settle instantly, with no input lock and no interaction-suppression window. Provide a transition by overriding `UIWindow.Transition` to return an `IUITransition` (`Play(open, ct)` for the animated pass, `Snap(open)` for skip paths); while a transition plays, the window locks interaction, and modal windows also coordinate with the input service (`InputService.PreventInteractionUI`). The hand-back happens in the *current* transition: the global suppression flag is cleared only by its recorded owner (`UIInteractionLease`), and a transition continuation superseded by a reopen/destroy neither unlocks nor hides the window, so a transition implementation does not need to detect being taken over itself:

```csharp
private CanvasGroup _canvasGroup;   // GetComponent once in OnCreate and keep the reference
private IUITransition _transition;

// Reuse the cached instance: the property is taken once per open and once per close, so allocating there
// puts allocations back on the open/close path
protected internal override IUITransition Transition => _transition ??= new FadeTransition(this);

private sealed class FadeTransition : IUITransition
{
    private readonly MainWindow _window;

    public FadeTransition(MainWindow window) => _window = window;

    public async UniTask Play(bool open, CancellationToken ct)
        => await _window._canvasGroup.DOFade(open ? 1f : 0f, 0.3f).WithCancellation(ct);

    // The framework calls this on skip paths (superseded, shutdown, immediate settle) to land the panel
    public void Snap(bool open) => _window._canvasGroup.alpha = open ? 1f : 0f;
}
```

### Window Self-Close (Wait and Gate)

A window closing itself (`Close()`) always waits until it is interactable — the open animation finished, or the modal above it released it — then passes the `CanClose` gate; when the gate is false the window stays on the stack and receives `OnCloseFail`:

```csharp
[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow
{
    protected override bool CanClose => _input.text.Length > 0;   // failing the gate lands in OnCloseFail

    protected override void OnCloseFail() { /* notify invalid input; the window stays on the stack */ }
}
```

- The policy lives on `UIWindow` (the backend-agnostic object model): windows on both the uGUI and UI Toolkit tracks override it the same way, with no per-track intermediate base class
- The wait passively observes: if the window is taken over by a reopen/destroy while waiting, that round terminates silently and the takeover side handles the hand-back; a destroyed window never polls in vain
- `ForceClose()` settles on the spot, skipping both the wait and the gate — the escape hatch for a window that must close immediately
- Only self-close goes through this path: closing from outside via `UIService.CloseUI` / `CloseAll` goes straight to the shared stack and is not affected by the wait or the gate

### Lifecycle Hook Isolation

The policy is "quarantine and continue": a throwing hook logs and moves on, never dragging other windows of the same batch down with it and never leaving its own flow half-finished. All of it is hand-written try/catch with no lambda wrappers — the per-frame path stays allocation-free.

| Hook slot | When it throws |
|---|---|
| Creation chain (`Inject` / `ScriptGenerator` / `BindMemberProperty` / `RegisterEvent` / `OnCreate`) | Same collection as a failed load: `RollbackFailedLoad` -> the window leaves the stack in the explicit failed state |
| `OnRefresh` | Error log, the window is still pushed onto the stack |
| Per-window `Internal_Update` inside the ledger's `Tick` | Error log, the next window continues (the loop bails out immediately if a callback rewrites the stack, so a stale index is never read) |
| Per-widget update inside `UpdateCore` | Error log, the remaining widgets of the frame still settle |
| `CanClose` | Error log, counted as a refusal (fail-closed) — `TryCloseTopWindow` and window self-close share this one gate |
| `OnClose` / `OnDestroy` / `UnregisterEvent` / the `Apply*` panel hooks | Error log, the close/destroy flow runs to completion (never half) |
| Open/close transition `Play` / `Snap` | `Play` throws -> fall back to `Snap` to land the end state; `Snap` throws too -> log it and stop, flow continues |

### Safe Area and UIAdapter

- Within a window: `SetUIFit(RectTransform, liuHaiFit, topSpacing, bottomFit, bottomSpacing)` adjusts the specified node for notch screen top/bottom padding; `SetUINotFit` excludes individual nodes.
- Global: Static method `UIService.ApplyScreenSafeRect(Rect)` directly adjusts UIRoot; `UIService.SimulateIPhoneXNotchScreen()` simulates a notched screen in the editor.
- Layout adapters (`Moirai.Atropos.UI.Adapter`): `SafeAreaAdapter` (safe area), `HorizontalAdapter` / `VerticalAdapter` (horizontal/vertical auto-arrangement, supports `Gap`), `AngleAdapter` (radial arrangement, supports `Distance`, `BiasAngle`, `Clockwise`). All are MonoBehaviour components that can recalculate each frame.

### Runtime Error Window

The service registers `ErrorLogger` (capturing `LogType.Exception` and automatically showing the built-in `LogUI` window — `[Window(EUILayer.System, fromResources:true)]`, prefab at the service's `Resources/LogUI.prefab`; `LogUI : UGUIWindow<string>`, the exception text landing in its `Payload` through the payload leg) only when the debugger configuration (`DebuggerService.ActiveWindowType`) says error logging **is** enabled. Enablement rule: `AlwaysOpen` always; `OnlyOpenWhenDevelopment` follows development builds; `OnlyOpenInEditor` follows the editor; `AlwaysClose` and `OnlyOpenWhenDevelopment` outside a development build (i.e. the default release shape) never enable it, so exceptions pop no window.

### Editor Binding Code Generation

Select the root node of a UI prefab and use the menu:

- `GameObject/ScriptGenerator/生成绑定代码` (Generate Binding Code): Generates a `partial class XXX : UGUIWindow` window script and `XXXBinder : UIBindComponent` binding component
- `GameObject/ScriptGenerator/复制绑定属性` (Copy Binding Properties): Copies member variable code to the clipboard

## Notes

- The uGUI track's UI root is registered by the `UIRootBinding` component on a scene object (which must have a `Canvas` under it): `SingletonMono` first-wins, a later duplicate's whole GameObject is destroyed; read it via `TryGetInstance()`, which only reads back and never auto-creates. The backend picks it up on the first Update tick, logs one Error when nothing is bound and one Fatal when the bound root has no Canvas, then keeps waiting each frame (late additive scenes, runtime-instantiated roots, and a Canvas added later all bind). Once bound, the UI root is automatically set to `DontDestroyOnLoad` (play mode only). **Lookup is not by object name** — renames are harmless, and a same-named object cannot be picked up as the root. The UI Toolkit track's document shells also parent under this root and are collected before the root is destroyed on shutdown.
- `ShowUI` synchronous loading depends on the resource service's synchronous loading capability; on WebGL it automatically falls back to async; `ShowUIAsync` is recommended
- `HideUI` only takes effect when `HideTimeToClose > 0`; otherwise it is equivalent to `CloseUI`
- `GetUIAsyncAwait<T>()` / `GetUIAsync<T>` only waits for the loading of an already-open window; returns null / no callback if the window does not exist
- Window updates (`OnUpdate`) are only triggered for visible windows; full-screen windows will block the visibility of windows beneath them
- A payload-carrying window must inherit `UGUIWindow<TArg>` / `UITKWindow<TArg>`; a slotless base given a non-empty payload throws on the spot. Payloads are overwritten per open and never cleared on close (the value survives until the next open), so there is no read-once-and-clear semantics
- The static leg's `in TArg` is the primary path for primitives and structs (generic push, zero boxing); `UIPayload` serves only the dynamic legs, where a value type boxes once — do not route hot-path structs through the dynamic leg
- `default` costs nothing on every leg's `CancellationToken`, and the token is only consumed while the load is in flight: a ready reuse and a re-park do not consume `ct`; reusing a window that is still loading registers the caller token just the same, and cancelling it aborts that in-flight load (the same semantics as the in-flight merge)
- Window open/close receipts are a static facade broadcast: `UIService.onWindowShown += OnWindowShownEvent` / `onWindowClosed += OnWindowClosedEvent` (parameter `UIWindow`) — exactly one per push and per pop, fired for both parking and destruction. Subscribers own their own un-pairing, and the facade's shutdown and reset gates detach the whole batch
- Optional parking TTL: `[Window(cacheTimeToDestroy: …)]` — `0` means no caching (destroy on close, the default), a positive value parks the instance and destroys it that many seconds later, a negative value parks it forever; on expiry the ledger removes the parked instance from the parking table and destroys it for good, and re-taking it cancels the timer
- Addressing lives in the facade: the third slot of every open leg is the window **id** — with `fromResources` it is joined onto the Resources parent folder held by `UIServiceSettings`, otherwise it is looked up through `ConfigTableService.GetUIWindowLocation`; only an empty id falls back to `[Window(location)]` (and to the type name when that is empty too). The old `UIManager` and its two public static resolvers are retired, and the conversion happens in exactly one place: the ledger's create-new-instance branch (a stack reuse and a re-parked window never hit the table)

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [UI Migration](UIMigration.md) · [Input](Input.md) · [Scene](Scene.md) · [Audio](Audio.md)
