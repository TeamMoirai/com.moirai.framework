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
│          EAudioCachePolicy / AudioClipCacheEntry / AudioLoadRequest
└── Support/ BackgroundMusic / AudioSettingsWidget / BgmPlaylist / AudioEmitter
           AudioMainThread / AudioFault / AudioWarnOnce   # main-thread assert, backed-off fault reporting, deduped warnings
```

## Architecture (HandlerHost + Strategy)

- **`AudioService`**: Static facade; depends on `DebuggerService`, `ResourceService`
- **`AudioServiceHandler`**: Backend contract (`StopByID`, 16-byte `Play`, virtual `OnAgentPlaybackEnded`). Master/track bus fades are **implemented here** (the contract owns the `AudioFadeScheduler` and lands values through `IAudioFadeTarget.ApplyFade`); a backend only has to say where a volume goes
- **`AudioHandleRegistry<TVoice>` / `AudioFadeScheduler`**: Shared handle registry and fade scheduling for both backends (bus fades use high-segment pseudo-handles)
- **`UnityAudioHandler`**: Default Unity `AudioSource`/`AudioMixer` backend
- **`MiddlewareAudioHandler`**: Shared FMOD/Wwise base (handles, voice fades, buses, layering)
- **`FmodAudioHandler` / `WwiseAudioHandler`**: Thin wrappers; only implement `CreateDefaultBridge()`
- **`AudioServiceSettings`**: Backend selection, mixer/track config, mix snapshot mapping, host pool warmup, clip cache limits

### Swappable backends & scripting defines

| Define | Backend | Without define |
|--------|---------|----------------|
| (default) | `UnityAudioHandler` | — |
| `FMOD_INSTALLED` | `FmodBridgeNative` → `FmodAudioHandler` | `FmodBridgeStub` (contract/stress-test ready) |
| `WWISE_INSTALLED` | `WwiseBridgeNative` → `WwiseAudioHandler` | `WwiseBridgeStub` |

Select `FmodAudioHandler` / `WwiseAudioHandler` in `AudioServiceSettings`. Bridge contract: `IAudioMiddlewareBridge`.

#### Event map

Middleware backends derive the event path from `clip.name` (FMOD: `event:/<name>`). When the designer names events independently and the clip is only a placeholder, that implicit convention silently produces wrong paths — so the handler carries an explicit map:

- Add `Clip` + `EventPath` entries under “Event Map” on `FmodAudioHandler` / `WwiseAudioHandler` in the Inspector.
- A hit is used directly; a miss falls back to name derivation and emits a **one-time** warning for that clip.
- Counting resolves through the same path: `CurrentlyPlayingCount(clip)` consults the map too, otherwise mapped clips would always report 0.
- Call `InvalidateEventMap()` after changing the configuration at runtime (also the entry point for code-driven maps).
- `Play(string eventPath, …)` always sends the path as-is and bypasses the map.

#### Banks and live parameters

`AudioService.LoadBank(path)` / `UnloadBank(path)` / `SetRtpc(name, value[, handle])` are part of the backend contract:

- The Unity backend has no such concept: `LoadBank` returns `false`, `SetRtpc` is a no-op.
- Middleware backends probe **capability interfaces** (`IAudioMiddlewareBankControl` / `IAudioMiddlewareRtpcControl`); a bridge without the capability warns once and degrades safely. They are deliberately *not* members of `IAudioMiddlewareBridge` — that would make every real SDK bridge that doesn't implement them fail to compile the moment `FMOD_INSTALLED` / `WWISE_INSTALLED` is defined.
- `handle` 0 addresses a project/global parameter; a playback handle addresses that instance.
- `FmodBridgeNative` / `WwiseBridgeNative` now carry implementations against these interfaces, but they **cannot be compiled on a machine without the plugin**: define the macro, pass the compile gate, then re-run the bridge contract tests against the real SDK.
- At the bridge level `LoadBank` is **tri-state** (`Loaded` / `AlreadyLoaded` / `Failed`): an idempotent hit and a master/Init bank the plugin loaded itself are both normal, so only `Failed` produces a one-time warning per bank name. The facade `AudioService.LoadBank` stays `bool` and returns `true` only when this call actually completed the load.

## Core Features

- Five tracks: Sfx / UI / Music / Voice / Ambience
- Agent pool + priority voice stealing + per-track expansion ceiling (`AudioGroupConfig.MaxChannelCeiling`, 32 by default)
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
| `EAudioCachePolicy` | `Default` (from settings) / `None` (drop after use) / `Ttl` (keep until expiry) / `Pin` (resident) |
| `AudioClipCache` | Unity backend clip lease cache (internal); `AssetHandlePool` is its read-only view |
| `AudioMixStateMachine` / `EMixSnapshot` | Mix snapshot state machine |
| `AudioOcclusionHrtf` | Occlusion + HRTF component |
| `AudioAgentHostPool` | Internal host stack pool (warmed up from settings in OnInit; idle hosts under `[Warmup]`) |
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

#### Shipping conventions

Conventions that must be pinned down before release — a change on either side (audio project or code) has to be mirrored:

- **Event naming**: `Play(clip, …)` relies on the event map; a missing entry falls back to deriving from `clip.name`. Mismatched names are the number one source of silently wrong/absent audio, so every production clip must be registered explicitly. `Play(eventPath, …)` sends the path as-is and bypasses the map.
- **Buses**: the framework writes linear volume to `bus:/{EAudioTrack}` and `bus:/Master`. Renaming a bus on the project side silently detaches that track; the Wwise bridge maps buses to RTPCs (`bus:/Music` → `MusicVolume`), so those plus every `SetRtpc` name form a list that ships with the build.
- **Bank order**: on a scene change the fixed order is `LoadBank(next)` → `StopAllButPersistent(fade)` → `UnloadBank(prev)`. Only `UnloadBank` returning `true` means it actually released; compare memory snapshots against `true` results.
- **Stop semantics**: fades are driven by the framework writing instance volume down to 0 and then `StopInstance(immediate: true)`. The `immediate: false` branch has no production caller today — verify it separately against the real SDK, and note that an implementation must not release/reclaim the emitter while the tail is still fading.

#### When initialization fails

If `IAudioMiddlewareBridge.Initialize` returns `false`, the backend disables audio as a whole and logs one Error: the bridge reference is dropped, so from then on `Play` returns `0`, Bank/RTPC calls are no-ops, and `Tick` returns immediately — nothing ever reaches a native engine that did not come up.

- No fallback to the Stub: it is not in the build at all when the real SDK was configured.
- No fallback to the Unity backend: it needs clip assets and Mixer groups that a middleware project does not carry, so the result would be half-audible rather than silent.
- No retry: `Restart()` does not reopen the native engine (that would double-`Init` a healthy backend); recovery means restarting the process.
- There is no separate "is audio alive" query: check whether `Play` returned `0`, which is constant in the disabled state and never spams the log.

### Clip cache & preload

All `Play(path, ...)` loading goes through `AudioClipCache`: one resource lease per address service-wide, taken by reference across agents and only released or kept once the last user stops.

```csharp
// Resident preload (startup / before a cutscene): Pin skips LRU/TTL
AudioService.Preload("Audio/BGM/MainTheme");
AudioService.PreloadAsync("Audio/Voice/Intro", EAudioCachePolicy.Ttl, ok => { /* ... */ });

// Drop after use: one-shot long audio
var options = AudioPlayOptions.Create(EAudioTrack.Voice);
options.CachePolicy = EAudioCachePolicy.None;
AudioService.Play("Audio/Voice/OneShot", options);

AudioService.UnloadClipCache("Audio/BGM/MainTheme");   // Pin needs force: true
AudioService.ClearClipCache(force: true);
```

- Eviction requires "no references, not loading, no waiters". `force` only relaxes the Pin and waiter gates — a playing or fading-out reference is never released (the `AudioSource` would keep an unloaded clip).
- At `ClipCacheCapacity` the least-recently-used unreferenced entry goes first; if everything is pinned or in use a new address is refused (`Play` returns `0UL`) instead of growing without bound. TTL (`ClipCacheTtl`, `0` disables) is swept in the service `Tick`, and `Application.lowMemory` triggers one non-forced pass.
- The address table is a **fixed slot array + open addressing + intrusive index chains**, not a `Dictionary`: acquire/release/evict all walk it, so a dictionary's on-demand growth and rehash spike can't land on a playback frame. Raising the capacity re-seats live entries into the new tables along the All chain, with no temporary array. Address hashing is ordinal djb2 rather than `string.GetHashCode`, which is randomized per process — two runs of the same build get different bucket distributions and a production collision chain becomes unreproducible.
- `AssetHandlePool` / `PoolReadOnly` is a **computed projection**, not a mirrored table: the cache keeps no second dictionary and writes nothing extra on acquire/release (a missed sync is exactly the "still in the view, gone from the cache" phantom). The price is one snapshot allocation per enumeration — that path only serves the debugger panel.
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

Configure `WarmupAudioHostPool` and `AudioHostWarmupCount` in `AudioServiceSettings`; `AudioService.OnInit` warms the pool under `InstanceRoot` after the handler is ready. Idle hosts live under a `[Warmup]` node (sibling of each `Audio Category - *`); `AudioAgent` re-parents a host to its category and renames it (e.g. `SFX - 0`) on acquire, and returns it under `[Warmup]` on release. Without warmup, hosts are created on demand and the `[Warmup]` node is created on first release.

### 0-GC acceptance (player build)

Editor Mono reports `GC.GetAllocatedBytesForCurrentThread()` and `ProfilerRecorder(GC.Alloc)` as constantly 0 — **managed allocations cannot be measured inside the editor**. PlayMode 0-GC assertions therefore follow a capability probe + `Assert.Ignore` (skip when the counter is unavailable; never treat "cannot measure" as "no allocations") and only carry functional regression. The authoritative gate for a 0-allocation play steady state is anchored in `Tests/Player`:

1. Test Runner → PlayMode tab → search `AudioPerformance` / `Allocation`;
2. Click **Run all in Player** (player tests can only be launched from there — the player does not parse `-testResults`; results travel back via PlayerConnection and are written by the editor);
3. Verify the GC.Alloc assertions in `AudioPerformanceTests` are green. Run it at least once before release.

## Configuration

- Mixer groups must expose a `{group name}Volume` parameter; `m_MixerValuesMultiplier` defaults to 20
- `AudioGroupConfig.MaxChannel` / `CanExpand` drive the channels; expansion is capped by the same track's `MaxChannelCeiling` (32 by default, absolute ceiling 128 — budget it per track per platform; preset slots are not bound by it)
- Master volume goes through `AudioListener.volume`; track volume goes through Mixer parameters
- `ClipCacheCapacity` (128 by default) / `ClipCacheTtl` (30 seconds, `0` turns time-based eviction off) / `DefaultClipCachePolicy` (`Ttl`) live in the "Clip cache" group of `AudioServiceSettings` and are handed to the cache during `Initialize`
- `AutoDuckingOnVoice` (off by default) is in the "Auto Ducking" group; register a `Dialogue` snapshot in `MixSnapshots` before switching it on

## Notes

- `Play` returns `0UL` on failure (no channel, unconfigured track, paused track, backend not initialized)  
- Paused tracks block new plays; `MasterVolume` getter always returns the unmuted setting value (consistent across backends)  
- When the backend is wholly unavailable the volume surface reads 0 and ignores writes: on Unity that means audio switched off from the editor menu (`AudioSettings.unityAudioDisabled`, always false in a player build), on middleware it means the bridge's `Initialize` explicitly failed (not recovered at runtime — restarting the process is the only way back). **A bridge that hasn't been created yet is not "unavailable"** — during that startup window the getters must keep reporting the stored settings, otherwise a settings panel opened before initialization shows every slider at 0 and the first user interaction persists that 0. While unavailable, `FadeMasterTrack` / `FadeTrack` schedule nothing, so `SoundIsFadingOut` never reports a fade that will never sound  
- The per-track pause flags are owned by the contract, but **allocation and reset stay on each backend's own schedule**: on the Unity side a `PauseTrack` before the backend's `Initialize` is silently dropped, and `OnShutdown` voids every flag. To have a track muted from boot, configure `AudioServiceSettings` rather than relying on `PauseTrack`  
- Middleware backends return null for `GetAgentByHandle` / `ForEachAgentByID` — use handle APIs; InitialDelay / PlaybackDuration / Solo are unsupported  
- `DoNotAutoRecycle` defaults to true consistently across factories and overloads  
- No-channel warnings are throttled per track (3 s) as Warning  
- Manual fades and snapshot transitions advance via service `Tick`  
- Path-based `Play(path, ...)` loads asynchronously by default (`bAsync = true`); synchronous loading blocks the main thread — reserve it for startup/preload scenarios  
- Path playback always uses the clip cache (default policy `Ttl`); set `AudioPlayOptions.CachePolicy = None` for rare one-shots you want released immediately  
- Failed addresses enter a 5-second cooldown (`failureCooldownSeconds` in `Configure`, `0` disables) — otherwise a mistyped event path that is triggered often re-enters the resource layer every time. `ClearClipCache(force: true)` resets the cooldowns too  
- Play entry points assert the main thread in development builds (the handle table and the cache's LRU/refcount have no cross-thread protection); from a worker thread, hop via `MainThreadDispatcher.Post`. If a lease source ever calls back off-thread, the cache marshals the result and, if marshalling is impossible, releases the lease and reports instead of mutating structures cross-thread  
- The service `Tick` isolates faults inside Audio: a throw is reported with backoff (no repeat for 5s per site) and never removes Audio from polling nor freezes other services that frame  
- Backgrounding freezes `AudioListener.pause` (each `AudioSource` keeps its position) and foregrounding restores it; `OnApplicationFocus` is deliberately not observed (desktop alt-tab must not mute). Shutting down while backgrounded still unfreezes  
- `AssetHandlePool` is now a read-only view over the clip cache (the contract member is typed `IReadOnlyDictionary`): leases are owned by the cache, so external code can neither rewrite the ledger nor dispose a lease  
- Natural-end timing uses unscaled real time (`AudioSource` is not affected by `timeScale`): at `timeScale = 0` a non-looping voice still finishes in real time and auto-releases its handle  
- Master/track fades are implemented by the contract base class (one code path for both backends): `duration <= 0` means assign immediately and schedule nothing; `StopFadeMasterTrack` / `StopFadeTrack` **cancel the fade without restoring volume already written** — wherever it stopped is where it stays; re-requesting a fade on the same bus replaces the pending one rather than stacking it  
- `Stop(handle, fadeoutDuration)` and `FadeAudio(handle, ...)` take over the same handle's volume exclusively (the later call cancels the former) — do not stack them  
- Full scene changes (`Single`) auto `StopAllButPersistent`; **Additive (streamed section) loads do not stop audio by default** — enable `AudioServiceSettings.StopNonPersistentOnAdditiveSceneLoad` when they must (same decision in both backends); set `Persistent = true` for cross-scene audio
- `BgmPlaylist` layer IDs: **positive = explicit layer; two instances claiming the same positive ID is a configuration error that fails fast with an Error, and the second instance does not play** (no silent re-assignment); `0` = auto-assigned per instance (reserved negative range, never collides with explicit values). A negative explicit ID is rejected the same way — that range is reserved for auto assignment
- The "Time" parameters of `AudioPlayOptionsSO` (`PlaybackTime` / `PlaybackDuration`, including random ranges) take effect on every `Play`; `MaximumConcurrentInstances` / `DoNotPlayIfClipAlreadyPlaying` evaluate the **candidate clip of this play** (not the previous one under a random set)  
- Handles are auto-released; do not rely on long-lived manual `ReleaseHandle`  
- The in-game debugger's `Profiler/Audio` panel now also shows clip cache entries/capacity, in-flight loads, pinned count, failure cooldowns, the current mix snapshot and ducking ownership, plus cache-clear buttons — check it first when "a sound didn't play"  
- Cold APIs (`PlayFade` / `StopByID`) may allocate lambdas; hot path uses 16B `AudioPlayRequest`

---
[« Documentation Index](Index.md) · [Main README](../../README_EN.md) · [Resource](Resource.md) · [UI](UI.md)
