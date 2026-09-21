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
│   ├── AudioHandleRegistry.cs                 # Shared handle registry (handle gen / user-ID map / list pool)
│   ├── AudioFadeScheduler.cs                  # Shared fade scheduler (voices + bus pseudo-handles)
│   ├── AudioClipCache.cs                      # Clip lease cache (LRU + TTL + Pin + lowMemory)
│   ├── UnityAudioHandler.cs                   # Default Unity backend
│   ├── Middleware/
│   │   ├── IAudioMiddlewareBridge.cs          # Shared FMOD/Wwise bridge
│   │   └── MiddlewareAudioHandler.cs          # Shared middleware base
│   ├── Fmod/  FmodBridgeStub|Native
│   ├── FmodAudioHandler.cs
│   ├── Wwise/ WwiseBridgeStub|Native
│   └── WwiseAudioHandler.cs
├── Mix/ AudioMixStateMachine.cs               # Mix snapshot state machine
│      AudioVoiceDucking.cs                    # Voice-driven auto ducking
├── Spatial/ AudioOcclusionHrtf.cs             # Occlusion + HRTF
├── Models/  AudioPlayRequest / ColdParams / Options / AssetData / GroupConfig
│          AudioCachePolicy / AudioClipCacheEntry / AudioLoadRequest
└── Support/ BackgroundMusic / SettingsWidget
```

## Architecture (HandlerHost + Strategy)

- **`AudioService`**: Static facade; depends on `DebuggerService`, `ResourceService`
- **`AudioServiceHandler`**: Backend contract (`StopByID`, 16-byte `Play`, virtual `OnAgentPlaybackEnded`)
- **`AudioHandleRegistry<TVoice>` / `AudioFadeScheduler`**: Shared handle registry and fade scheduling for both backends (bus fades use high-segment pseudo-handles)
- **`UnityAudioHandler`**: Default Unity `AudioSource`/`AudioMixer` backend
- **`MiddlewareAudioHandler`**: Shared FMOD/Wwise base (handles, fades, buses, layering)
- **`FmodAudioHandler` / `WwiseAudioHandler`**: Thin wrappers; only implement `CreateDefaultBridge()`
- **`AudioServiceSettings`**: Backend selection, mixer/track config, mix snapshot mapping, host pool warmup, clip cache limits

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
- Clip cache: one shared lease per address, refcounted with LRU/TTL/Pin eviction and `lowMemory` cleanup
- Auto ducking: while any Voice voice is active the mix requests `Dialogue`, then hands back the layer it borrowed

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
| `AudioPlayOptions` | Compatibility facade; `ToRequest()` / `FromOptions()`; `CachePolicy` decides lease retention |
| `AudioCachePolicy` | `Default` (from settings) / `None` (drop after use) / `Ttl` (keep until expiry) / `Pin` (resident) |
| `AudioClipCache` | Unity backend clip lease cache (internal); `AssetHandlePool` is its read-only view |
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

### Clip cache & preload

All `Play(path, ...)` loading goes through `AudioClipCache`: one resource lease per address service-wide, taken by reference across agents and only released or kept once the last user stops.

```csharp
// Resident preload (startup / before a cutscene): Pin skips LRU/TTL
AudioService.Preload("Audio/BGM/MainTheme");
AudioService.PreloadAsync("Audio/Voice/Intro", AudioCachePolicy.Ttl, ok => { /* ... */ });

// Drop after use: one-shot long audio
var options = AudioPlayOptions.Create(EAudioTrack.Voice);
options.CachePolicy = AudioCachePolicy.None;
AudioService.Play("Audio/Voice/OneShot", options);

AudioService.UnloadClipCache("Audio/BGM/MainTheme");   // Pin needs force: true
AudioService.ClearClipCache(force: true);
```

- Eviction requires "no references, not loading, no waiters". `force` only relaxes the Pin and waiter gates — a playing or fading-out reference is never released (the `AudioSource` would keep an unloaded clip).
- At `ClipCacheCapacity` the least-recently-used unreferenced entry goes first; if everything is pinned or in use a new address is refused (`Play` returns `0UL`) instead of growing without bound. TTL (`ClipCacheTtl`, `0` disables) is swept in the service `Tick`, and `Application.lowMemory` triggers one non-forced pass.
- `PutInAudioPool` / `RemoveClipFromPool` / `CleanAudioPool` and `AssetHandlePool` remain as compatibility entry points, mapping to `Preload(Pin)` / `Unload(force)` / `ClearClipCache(force)`; `bInPool: true` means "keep at least by TTL".

### Mix snapshots

Priorities: Default 0; Muffled/LowHealth 2; Paused/Dialogue 3; Cinematic 4. Lower cannot interrupt higher unless `force: true`. Unity uses `AudioMixerSnapshot.TransitionTo`; middleware uses `SetMiddlewareTransitionHandler`. Snapshot mapping (state → `AudioMixerSnapshot` + optional priority) is configured in `AudioServiceSettings.MixSnapshots` and auto-registered in `OnInit`; otherwise call `AudioMixService.RegisterSnapshot` manually.

### Auto ducking

With `AudioServiceSettings.AutoDuckingOnVoice` (off by default), any active voice on the Voice track requests `EMixSnapshot.Dialogue`, and the mix is handed back once the track goes quiet — no per-line bookkeeping in game code:

- Activity is computed by each backend (Unity scans the track's agents, middleware scans `Playing` voices) rather than counted on play/end: a missed decrement would duck the mix forever.
- Driven from the backend `Tick`, so loading and fading-out voices count as active; turning the setting off releases a held duck on that same frame.
- Coexists with snapshot priorities: when a higher-priority state (e.g. Cinematic) owns the mix the duck request is refused **and not recorded as taken**, so narration never steals the mix back mid-cutscene. The release only runs while the duck still owns `Dialogue`, and returns to the state captured before ducking rather than a hardcoded `Default`.
- Requires a `Dialogue` `AudioMixerSnapshot` in `MixSnapshots`; without it the switch is a no-op (single warning) — it looks inert, not broken.

### Occlusion / HRTF

Add `AudioOcclusionHrtf` next to the listener: raycasts active sources, drives `AudioLowPassFilter`, optionally pushes `spatialBlend` for HRTF.

### Host stack warmup

Configure `WarmupAudioHostPool` and `AudioHostWarmupCount` in `AudioServiceSettings`; `AudioService.OnInit` warms the pool under `InstanceRoot` after the handler is ready. Without warmup, hosts are created on demand and reused from the stack.

## Notes

- `Play` returns `0UL` on failure (no channel, unconfigured track, paused track, backend not initialized)  
- Paused tracks block new plays; `MasterVolume` getter always returns the unmuted setting value (consistent across backends)  
- Middleware backends return null for `GetAgentByHandle` / `ForEachAgentByID` — use handle APIs; InitialDelay / PlaybackDuration / Solo are unsupported  
- `DoNotAutoRecycle` defaults to true consistently across factories and overloads  
- No-channel warnings are throttled per track (3 s) as Warning  
- Manual fades and snapshot transitions advance via service `Tick`  
- Path-based `Play(path, ...)` loads asynchronously by default (`bAsync = true`); synchronous loading blocks the main thread — reserve it for startup/preload scenarios  
- Path playback always uses the clip cache (default policy `Ttl`); set `AudioPlayOptions.CachePolicy = None` for rare one-shots you want released immediately  
- `AssetHandlePool` is now a read-only view over the clip cache; never dispose the leases it exposes  
- Natural-end timing uses unscaled real time (`AudioSource` is not affected by `timeScale`): at `timeScale = 0` a non-looping voice still finishes in real time and auto-releases its handle  
- `Stop(handle, fadeout)` and `FadeAudio(handle, ...)` take over the same handle's volume exclusively (the later call cancels the former) — do not stack them  
- Scene load auto `StopAllButPersistent`; set `Persistent = true` for cross-scene audio  
- Handles are auto-released; do not rely on long-lived manual `ReleaseHandle`  
- Cold APIs (`PlayFade` / `StopByID`) may allocate lambdas; hot path uses 16B `AudioPlayRequest`

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Resource](Resource.md) · [UI](UI.md)
