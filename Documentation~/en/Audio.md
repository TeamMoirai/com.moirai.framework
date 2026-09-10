# Audio Service

> Audio system based on track grouping and an agent pool: swappable backends (Unity / FMOD / Wwise), handle lifecycle, fades, mix snapshots, and spatial occlusion.

The `Audio` service divides audio into tracks (`EAudioTrack`). The Unity backend maps each track to an `AudioCategory` with `AudioAgent` instances (wrapping `AudioSource`); middleware backends share `MiddlewareAudioHandler`. Access via the `AudioService.Xxx()` static facade. Playback returns a `ulong` handle; batch control by user ID is available (`StopByID`, `PlayFade`, etc.). Volumes persist through `SettingUtility`.

## Directory Layout

```text
Runtime/Services/Audio/
├── AudioService.cs / AudioServiceHandler.cs   # Facade + backend contract
├── AudioAgent.cs / AudioCategory.cs           # Unity agents & tracks
├── AudioAgentHostPool.cs                      # Host GameObject pool
├── AudioServiceSettings.cs
├── Handler/
│   ├── UnityAudioHandler.cs                   # Default Unity backend
│   ├── Middleware/
│   │   ├── IAudioMiddlewareBridge.cs          # Shared FMOD/Wwise bridge
│   │   └── MiddlewareAudioHandler.cs          # Shared middleware base
│   ├── Fmod/  FmodBridgeStub|Native
│   ├── FmodAudioHandler.cs
│   ├── Wwise/ WwiseBridgeStub|Native
│   └── WwiseAudioHandler.cs
├── Mix/ AudioMixStateMachine.cs               # Mix snapshot state machine
├── Spatial/ AudioOcclusionHrtf.cs             # Occlusion + HRTF
├── Models/  AudioPlayRequest / ColdParams / Options / AssetData / GroupConfig
└── Support/ BackgroundMusic / SettingsWidget
```

## Architecture (HandlerHost + Strategy)

- **`AudioService`**: Static facade; depends on `DebuggerService`, `ResourceService`
- **`AudioServiceHandler`**: Backend contract (`StopByID`, 16-byte `Play`, virtual `OnAgentPlaybackEnded`)
- **`UnityAudioHandler`**: Default Unity `AudioSource`/`AudioMixer` backend
- **`MiddlewareAudioHandler`**: Shared FMOD/Wwise base (handles, fades, buses, layering)
- **`FmodAudioHandler` / `WwiseAudioHandler`**: Thin wrappers; only implement `CreateDefaultBridge()`
- **`AudioServiceSettings`**: Backend selection, mixer/track config, optional host prefab path

### Swappable backends & scripting defines

| Define | Backend | Without define |
|--------|---------|----------------|
| (default) | `UnityAudioHandler` | — |
| `FMOD_INSTALLED` | `FmodBridgeNative` → `FmodAudioHandler` | `FmodBridgeStub` (contract/stress-test ready) |
| `WWISE_INSTALLED` | `WwiseBridgeNative` → `WwiseAudioHandler` | `WwiseBridgeStub` |

Select `FmodAudioHandler` / `WwiseAudioHandler` in `AudioServiceSettings`. Bridge contract: `IAudioMiddlewareBridge`.

## Core Features

- Five tracks: Sfx / UI / Music / Voice / Ambience
- Agent pool + priority voice stealing + `HARD_CHANNEL_CAP` (32)
- Auto handle release on stop/end via `OnAgentPlaybackEnded`
- Layered BGM: different IDs coexist; `StopByID(id)` replaces only that layer
- 16-byte hot request `AudioPlayRequest` + pooled cold params `AudioPlayColdParams`
- Mix snapshot state machine with priority + crossfade
- `AudioOcclusionHrtf`: ray occlusion → lowpass; optional HRTF spatial blend
- Host pool: Internal stack pool reuses `AudioSource` hosts

## Core Types

Namespace: `Moirai.Atropos.Audio` (middleware under `.Fmod` / `.Wwise` / `.Middleware`)

| Type | Description |
|------|-------------|
| `AudioService` | Static facade; includes `RequestMixSnapshot`, `StopByID`, 16B `Play` |
| `AudioServiceHandler` | Backend contract |
| `UnityAudioHandler` | Default Unity backend |
| `MiddlewareAudioHandler` | Shared middleware base |
| `FmodAudioHandler` / `WwiseAudioHandler` | FMOD / Wwise thin wrappers |
| `AudioPlayRequest` | 16-byte hot request |
| `AudioPlayColdParams` | Cold params (location, curves, bypass); pooled |
| `AudioPlayOptions` | Compatibility facade; `ToRequest()` / `FromOptions()` |
| `AudioMixStateMachine` / `EMixSnapshot` | Mix snapshot state machine |
| `AudioOcclusionHrtf` | Occlusion + HRTF component |
| `AudioAgentHostPool` | Internal host stack pool (warmed up from settings in OnInit) |
| `BackgroundMusic` | Layered BGM (same ID replaces, other IDs persist) |

## Quick Start

```csharp
// Option factory (compatible)
var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
ulong h = AudioService.Play(clip, options);

// 16-byte hot request (preferred)
var req = new AudioPlayRequest(id: 1, volume: 1f, pitch: 1f, EAudioTrack.Sfx, 128,
    AudioPlayFlags.DoNotAutoRecycle);
ulong h2 = AudioService.Play(clip, req, cold: null);

// Layered BGM: same ID replaces, different IDs coexist
AudioService.StopByID(10001, 0.5f);
AudioService.Play(bgm, musicOptions); // ID = 10001

// Mix snapshots
AudioService.RequestMixSnapshot(EMixSnapshot.Dialogue, 0.3f);
AudioService.ResetMixSnapshot(0.5f);
```

### Middleware backends

Add `FMOD_INSTALLED` or `WWISE_INSTALLED` in Scripting Define Symbols, import the plugin, and switch the Handler in `AudioServiceSettings`. Event paths: FMOD `event:/Name`; Wwise event name; buses `bus:/Music`, etc.

### Mix snapshots

Priorities: Default 0; Muffled/LowHealth 2; Paused/Dialogue 3; Cinematic 4. Lower cannot interrupt higher unless `force: true`. Unity uses `AudioMixerSnapshot.TransitionTo`; middleware uses `SetMiddlewareTransitionHandler`.

### Occlusion / HRTF

Add `AudioOcclusionHrtf` next to the listener: raycasts active sources, drives `AudioLowPassFilter`, optionally pushes `spatialBlend` for HRTF.

### Host stack warmup

Configure `WarmupAudioHostPool` and `AudioHostWarmupCount` in `AudioServiceSettings`; `AudioService.OnInit` warms the pool under `InstanceRoot` after the handler is ready. Without warmup, hosts are created on demand and reused from the stack.

## Notes

- `Play` returns `0UL` on failure  
- Middleware backends return null for `GetAgentByHandle` / `ForEachAgentByID` — use handle APIs  
- Manual fades and snapshot transitions advance via service `Tick`  
- Scene load auto `StopAllButPersistent`; set `Persistent = true` for cross-scene audio  
- Handles are auto-released; do not rely on long-lived manual `ReleaseHandle`  
- Cold APIs (`PlayFade` / `StopByID`) may allocate lambdas; hot path uses 16B `AudioPlayRequest`  

---
[« Back to main README](../../README.md)
