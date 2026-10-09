# UI Payload Migration

> Migration notes for the hard cut: the `params object[]` payload shape is retired from every public leg, replaced by a strongly typed `Payload` slot (static legs) and the `UIPayload` erasure carrier (dynamic legs); the `UIWindowEvent` and `UIServiceEvent` event classes are retired alongside it. This page is the one-page checklist for an external business project (the fifth consumer): declare a DTO and swap the base class -> two destinations for write sites -> four behavior changes.

The full contract and the signature tables live in [UI Service](UI.md). Payload-free call sites — the overwhelming majority — need no change: `ShowUI<T>` / `ShowUIAsync<T>` / `ShowUIAsyncAwait<T>` / `ShowUIAwaitResult<T>` only gained a trailing `ct`, so their existing arguments still bind.

## 1. Declare a DTO and swap the base class

For every payload-carrying window, declare one DTO (`struct` preferred — the static leg's `in TArg` pushes it generically without boxing) and swap to the slot-carrying base:

| Old | New |
|---|---|
| `class MyWindow : UGUIWindow` | `class MyWindow : UGUIWindow<MyWindowPayload>` |
| `class MyWindow : UITKWindow` | `class MyWindow : UITKWindow<MyWindowPayload>` |
| Read sites `UserData?.ToString()` / `(string)UserData` | `Payload` (already strongly typed, no cast) |
| Read sites `_params[i]` / `Params[i]` (including the `Params.Length` emptiness check) | `Payload.Field` (at most one DTO per open; fields you did not set keep the DTO's own default) |

```csharp
// Old shape (retired): positional arguments, read by index
[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow
{
    protected override void OnRefresh()
    {
        var initial = Params.Length > 0 ? (string)Params[0] : string.Empty;
        _input.text = initial;
    }
}

// New shape: one DTO, read by name
public struct RenameWindowPayload
{
    public string InitialText;
    public int MaxLength;
}

[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow<RenameWindowPayload>
{
    protected override void OnRefresh()
    {
        _input.text = Payload.InitialText;
        _input.maxLength = Payload.MaxLength;
    }
}
```

> Toolchain note: under this project's toolchain (C# 9 / netstandard2.1, no `IsExternalInit` polyfill) a `readonly struct` with writable public fields does not compile (CS8340 at the init sites; `readonly` fields plus an object initializer is CS0191, `{ get; init; }` is CS0518) — a DTO is a plain `struct` with public fields and an object initializer.

- One DTO carries every field: the old shape spread three values over three positions, the new one removes the possibility of mis-ordering entirely — which slot the payload takes and which takes `panelSettings` is fixed by the signature table
- `Payload` is overwritten per open and never cleared on close: no null check is needed before reading it (you get this open's value), and a payload-free reopen of the same window leaves the residual in place until the next overwrite
- Windows that carry no payload do not need a base swap: inheriting `UGUIWindow` / `UITKWindow` directly still opens fine — it just throws `GameException` (window class named in the message) the moment a non-empty payload is pushed into it, which points at the call site whose base was never swapped instead of swallowing it

## 2. Two destinations for write sites

After the read sites, rewrite the call sites, split by "is the window class known at compile time".

### Static leg: window class known at compile time (most write sites)

Payload first, two type arguments naming the window class and the DTO class:

```csharp
var dto = new RenameWindowPayload { InitialText = current, MaxLength = 16 };

UIService.ShowUIAsync<RenameWindow, RenameWindowPayload>(in dto);                                  // async
UIService.ShowUI<RenameWindow, RenameWindowPayload>(in dto);                                       // sync tier
RenameWindow w = await UIService.ShowUIAsyncAwait<RenameWindow, RenameWindowPayload>(dto);         // await leg (async forbids `in`)
UIOpenResult r = await UIService.ShowUIAwaitResult<RenameWindow, RenameWindowPayload>(in dto);     // result leg
```

Landed signature and position order: `(in TArg payload, string windowId = null, bool fromResources = false, CancellationToken ct = default)`; the UI Toolkit legs take one extra `PanelSettings panelSettings = null` before `ct`, and the payload is still always the first slot.

When you hand-write a `Show` helper, pass the id straight to the leg (the old shape computed the address first; the facade now resolves it per band):

```csharp
public static void ShowRenameWindow(RenameWindowPayload dto)
{
    const string WindowId = "RenameWindow";
    UIService.ShowUIAsync<RenameWindow, RenameWindowPayload>(
        in dto,
        WindowId);
}
```

- The window id is not an asset address: with `fromResources` it is joined onto the Resources parent folder from `UIServiceSettings`, otherwise the config table answers it; the conversion happens only when the ledger creates a new instance
- The fetch mode defaults to the union of the caller's `fromResources` and `[Window(fromResources:)]` (true || attribute); with the config-table service unready the lookup answers `null` and an unknown id answers an empty string — both land in the load-failure rollback rather than being papered over here

### Dynamic leg: only a runtime `Type` (type-substitution seams, registry-driven opens)

`TArg` is unknown at runtime, so the payload is erased into the single carrier `UIPayload`:

```csharp
// The retired shape queued a positional argument array in the trailing slot; the new shape carries the payload in the single UIPayload carrier
UIService.ShowUIAsync(type, windowId, false, UIPayload.From(dto), ct);
UIService.ShowUI(type, windowId, false, UIPayload.From(dto), ct);        // sync tier, same shape
UIWindow win = await UIService.ShowUIAsyncAwait(type, windowId, false, UIPayload.From(dto), ct);
```

- The dynamic family has exactly three legs (async / sync / await) and `UIPayload payload` always sits right before `ct`; there is no `Type`-form result leg
- A reference payload stores the reference: zero allocation, and the arriving `Payload` is the same reference you sent; a value type boxes once through `object` — keep hot-path primitives and structs on the static leg
- An empty payload and a `null` reference are the same case: `UIPayload.Empty`, `default(UIPayload)` and `UIPayload.From(null)` are equivalent, and that is what the payload-free legs pass
- Reading it back uses the window class's `TArg`: `To<T>()` throws `GameException` on a type mismatch or an empty payload against a value type (message names the expected type); use `TryGet<T>(out T)` when you would rather not throw
- Keep `mgr.SomeWindowType`-style branches (runtime window-class substitution) on the dynamic leg and move the direct branches to the static leg: one DTO shape serves both channels, so no separate carrier type is needed for the dynamic path

## 3. Four behavior changes

1. **`UIWindowEvent` is retired outright**: the event class and every form of `Show` / `Close` / `Hide` / `CloseAll` are deleted, so opening and closing no longer travel the "send an event, a subscriber relays it" hop — call sites use a facade leg and run immediately, and even the payload-free `Show` has to move. Old -> new: `Show<T>(id)` -> `UIService.ShowUIAsync<T>(id)` (use `ShowUI<T>` for the synchronous tier), `Show(type, id)` -> `UIService.ShowUIAsync(type, id)`, `Close<T>(id)` -> `UIService.CloseUI<T>(id)`, `Close(type, id)` -> `UIService.CloseUI(type, id)`, `Hide<T>(id)` -> `UIService.HideUI<T>(id)`, `Hide(type, id)` -> `UIService.HideUI(type, id)`, `CloseAll()` -> `UIService.CloseAll()`. Payload call sites follow section 2.
2. **Open/close receipts move to static facade events**: `UIServiceEvent` (and with it the marker interface `IUIEvent`) is deleted and no longer dispatched through `EventManager`; subscribe to `UIService.onWindowShown` / `UIService.onWindowClosed` instead (`public static event Action<UIWindow>`, the parameter being that window). Exactly one fires per push and per pop, and both parking and destruction fire it; unsubscribing stays your own pairing, while the facade's shutdown and reset gates detach the whole batch.
3. **In-flight merge, last-wins**: reopening a window whose load is still in flight — any leg, either channel — does not restart the load and does not push a second instance; the payload is overwritten by the last one and `OnRefresh` runs once when the panel is ready, seeing only the final payload. The old assumption that "two Shows refresh twice" no longer holds — if you need to change content mid-flight, wait for the result leg or close before reopening.
4. **Every leg takes a `CancellationToken`**: trailing `CancellationToken ct = default`, costing nothing when you omit it, and the token is only consumed while the load is in flight (a ready reuse and a re-park hand back synchronously without consuming `ct`; reusing a window that is still loading registers the caller token just the same, and cancelling it aborts that in-flight load — the same semantics as the in-flight merge above). Cancellation lands per leg: a void leg rolls back off the stack silently (no Error), an await leg rethrows `OperationCanceledException`, and a result leg returns `EUIOpenStatus.Cancelled` (a new tier, distinguishable from `Timeout`). A `Failed` window has been rolled back and voided; a `Cancelled` result only means this wait settled as cancelled — whether the load continues depends on the remaining waiters (it rolls back if no one is left waiting). Neither may be treated as ready.

## Closing checklist

- No `userData` / `UserData` / `Params` / `_params` / `params object[]` UI-payload shapes remain anywhere
- Every payload-carrying window base has its `<TArg>`; the same DTO shape serves both channels
- Neither `UIWindowEvent` nor `UIServiceEvent` appears anywhere any more, and addressing goes through the `ResolveWindowLocation` / `ResolveFromResources` pair
- The three failure tiers are all self-identifying: a base you forgot to swap -> the generic constraint will not bind (compile time) or a `GameException` naming the window class when a non-empty payload arrives (runtime); a slot-type mismatch -> `GameException` with both expected and actual type names; passing `in` to the await leg -> that leg already takes a plain parameter, so just follow the signature

---
[« Documentation Index](Index.md) · [UI Service](UI.md) · [Main README](../../README_EN.md)
