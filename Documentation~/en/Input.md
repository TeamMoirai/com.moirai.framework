# Input Service

> Abstract input layer: uses a unified polling API to bridge the differences between Unity's new and old input systems and mobile UI touch input, with a built-in key prompt (Prompts) system.

The input service (`Moirai.Atropos.Input`) abstracts three input backends through `InputServiceHandler`. Business code only needs to work with `InputService`'s action-name-based API; switching backends requires no changes to the caller. The service also listens to UI modal events and application focus events, automatically blocking/restoring input, and provides cross-device key icon prompt components based on the Input System.

## Core Features

- Three configurable input backends: New Input System, Legacy Input Manager, Mobile UI touch components
- Unified action polling API: `GetButtonDown` / `GetButtonUp` / `GetButtonPressed` / `GetBool` / `GetFloat` / `GetVector2`, with action group support (`actionGroup`)
- Dedicated mouse queries: button tri-state, position, scroll wheel (scroll values normalized across new and old systems)
- Input state toggles: `Enabled` (global hard gate — action queries always return defaults), `LockPlayerController` (lock character control), `PreventInteractionUI` (lock UI interaction); residual input states are automatically reset when entering suppression. The Input System backend enforces suppression centrally by enabling/disabling whole Action Maps — consumers never need to check the flags themselves
- UI modal coordination: Listens to `UIServiceEvent`; automatically locks player control when a modal window is present
- Application focus coordination: Automatically disables input on focus lost, restores on focus gained
- Key prompt system (Prompts): Key icons automatically switch based on the current active input device, supports mixed text and sprite rendering

## Core Types

| Class/Interface | Description |
|---------|------|
| `Moirai.Atropos.Input.InputService` | Input service static facade (`[HandlerHost]`); all polling APIs are static methods forwarding through the `Handler` property. Degradation contract: when not registered/initialized, APIs return safe defaults (false/0/zero) instead of throwing |
| `Moirai.Atropos.Input.InputServiceHandler` | Input handler abstract base class (`[Serializable]`), defines all input query methods. Configured via `[SerializeReference]` in Input Settings |
| `Moirai.Atropos.Input.UnityInputSystemHandler` | Handler based on Unity Input System (macro `ENABLE_INPUT_SYSTEM`) |
| `Moirai.Atropos.Input.UnityInputManagerHandler` | Handler based on legacy Input Manager (macro `ENABLE_LEGACY_INPUT_MANAGER`) |
| `Moirai.Atropos.Input.UIMobileInputHandler` | Mobile handler, reads state from `InputButton` / `InputAxes` components in the scene |
| `Moirai.Atropos.Input.InputServiceSettings` | Framework settings ("Input Settings"), configures input handler via `[SerializeReference]` with lazy initialization |
| `Moirai.Atropos.Input.InputActionsConfiguration` | Input action configuration asset, registers bool/float/Vector2 action names by group, used for code generation |
| `Moirai.Atropos.Input.EMouseButton` | Mouse button enum: `Left = 0`, `Right = 1`, `Middle = 2` |
| `Moirai.Atropos.Input.BoolAction` / `FloatAction` / `Vector2Action` | Serializable action value structs, with pressed/released state and direction detection |
| `Moirai.Atropos.Input.InputButton` | Mobile UI button (UGUI events implement `IUIBoolAction`), menu `Tools/Input/UI/Input Button` |
| `Moirai.Atropos.Input.InputAxes` | Mobile virtual joystick (`IUIVector2Action`), supports dead zone / inversion / spring-back, menu `Tools/Input/UI/Input Axes` |
| `Moirai.Atropos.Input.PreventInputOnEnable` | Helper component: locks input on enable, restores on disable |
| `Moirai.Atropos.Input.Prompts.InputDevicePromptSystem` | Prompt system core (static class), maintains action binding to device glyph mappings |
| `Moirai.Atropos.Input.Prompts.PromptActionIcon` / `PromptActionText` / `PromptDeviceIcon` | Key icon / mixed text+sprite / device icon display components |
| `Moirai.Atropos.Input.Prompts.GlyphMap` / `GlyphCollection` | Device glyph mapping asset / glyph collection asset |

## Quick Start

After selecting an input processor in the framework settings (Project Settings -> "Input Settings"), you can directly poll actions:

```csharp
// Input System backend: groupName/actionName corresponds to Action Map/Action
if (InputService.GetButtonDown("Jump", "Player"))
{
    // Jump key pressed this frame
}

float moveX = InputService.GetFloat("Move", "Player");
Vector2 move = InputService.GetVector2("Move", "Player");

// When actionGroup is empty, actionName is treated as a full path ("Player/Jump")
bool submit = InputService.GetButtonPressed("UI/Submit");

// Mouse
if (InputService.GetMouseButtonDown(EMouseButton.Right)) { }
Vector2 pos = InputService.GetMousePosition();
Vector2 scroll = InputService.GetScrollDelta();
```

Locking/restoring input:

```csharp
InputService.LockPlayerController = true;    // Lock character movement during modal popups
InputService.PreventInteractionUI = true;    // Disable UI interaction during cutscenes
InputService.Enabled = false;                // Global disable (resets all input states)
```

## Advanced Usage

### Input Backend Differences

| Processor | Action Resolution | Notes |
|--------|-------------|------|
| `UnityInputSystemHandler` | Looks up `InputAction` in `InputSystem.actions` using `$"{actionGroup}/{actionName}"` | Requires Action Asset configuration in Project Settings -> Input System Package (custom assets are enabled by the handler automatically); scroll value divided by 120 to align with legacy system. Supports context suppression — player/UI maps are enabled/disabled as a whole with the suppression state (map names configurable on the handler, defaults `Player`/`UI`) |
| `UnityInputManagerHandler` | `actionName` is the Input Manager axis name; Vector2 combines `"{name} X"` / `"{name} Y"` axes | Uses `GetAxisRaw`, unregistered axes are probed once and warned. Supports only the `Enabled` global hard gate (no Action Map concept; context suppression is exclusive to the Input System backend) |
| `UIMobileInputHandler` | Looks up `InputButton` (bool) and `InputAxes` (Vector2) by `ActionName` in the scene; components self-register via `UIMobileInputRegistry` | Mouse-related interfaces always return default values; supports only the `Enabled` global hard gate |

### Mobile UI Input Components

`UIMobileInputHandler` depends on UI components in the scene to generate input. Their action names must match the character actions:

- `InputButton`: Implements `IPointerDownHandler` / `IPointerUpHandler`, `BoolValue == true` while held
- `InputAxes`: Virtual joystick, supports Radial and PerAxis dead zone modes, `m_BoundsRadius` joystick radius, `m_ReturnLerpSpeed` spring-back speed, horizontal/vertical inversion

### InputActionsConfiguration and Code Generation

Create a configuration asset via `Create Asset -> Moirai/Input/InputActions Config`, register action groups (`m_ActionsGroup`) and action name arrays of each type, and use the editor to generate strongly-typed access code (similar to Input System's Generate Class feature).

Serializable action value structs can be embedded directly in components for per-frame state updates:

```csharp
private BoolAction _jump = new BoolAction();

void Update()
{
    _jump.Value = InputService.GetBool("Jump", "Player");
    _jump.Update(Time.deltaTime);

    if (_jump.IsDown) { }             // Pressed this frame (equivalent to Started)
    if (_jump.IsPressed) { }          // Held continuously
    if (_jump.IsUp) { }               // Released this frame (equivalent to Canceled)
}
```

`Vector2Action` additionally provides `Detected`, `Right`, `Left`, `Up`, `Down` direction detection.

### Key Prompt System (Prompts)

Based on the Input System, key icons automatically switch according to the device that last generated input (`InputDevicePromptSystem.OnActiveDeviceChanged`):

```csharp
// Mixed text and sprites: PromptActionText (TextMeshProUGUI), tag format {action:actionPath}
// Example text: Press {action:UI/Submit}
m_TextField.text = InputDevicePromptSystem.InsertPromptSprites(m_OriginalText, isComposite: false);

// Standalone key icon: PromptActionIcon (Image), Action field filled with full path like "Player/Move"
Sprite sprite = InputDevicePromptSystem.GetActionPathBindingSprite("Player/Move", false);

// Device icon (e.g., controller type icon)
Sprite device = InputDevicePromptSystem.GetDeviceSprite(spriteName);
```

Configuration assets:

- `GlyphMap` (`Moirai/Input/Glyph Map`): Maps action binding paths to icons for a single device
- `GlyphCollection` (`Moirai/Input/Glyph Collection`): Multi-device glyph collection for the same theme, with fallback icons for unbound/invalid actions
- `InputSystemDevicePromptSettings` (Framework Settings): Registers InputActionAsset, glyph collection, default device priority, platform overrides, and rich text tags

Import the sample `Samples~/InputSystem Action Prompts` via Package Manager (includes Xelu Prompts icon set, sample scene, and font) to get started quickly.

### PreventInputOnEnable

When a GameObject with this component is enabled, it locks `LockPlayerController` / `PreventInteractionUI` according to the configured options; when disabled, it restores the original values. Suitable for nodal control in cutscenes, tutorials, etc.

## Notes

- The processor type is configured in the framework settings ("Input Settings") via `[SerializeReference]` and loaded lazily via `InputServiceSettings.InputServiceHandler`; switching processors requires a restart to take effect
- `UnityInputSystemHandler` / `UnityInputManagerHandler` are controlled by the `ENABLE_INPUT_SYSTEM` / `ENABLE_LEGACY_INPUT_MANAGER` macros respectively
- When a UI modal window is present, `LockPlayerController` is always true (driven by `UIServiceEvent`); this is expected behavior. On the Input System backend, the player map is disabled while the UI map stays active, so the modal's own hotkeys keep working
- Suppression gating only applies to action queries (buttons/axes/vectors); mouse queries are not gated. Actions not listed in the player/UI map configuration are unaffected by context suppression (only gated by `Enabled`)
- `GetButtonDown` / `GetButtonUp` in `UIMobileInputHandler` read per-frame latched edges from the components (semantics aligned with `WasPressedThisFrame`)
- Input queries should be polled every frame; the service itself does not push events

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [UI](UI.md) · [Scene](Scene.md)
