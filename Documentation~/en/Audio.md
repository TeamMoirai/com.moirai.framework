# Audio Module

> Audio system based on AudioMixer track grouping and audio agent pool, supporting handle control, fade in/out, solo, and event-driven playback.

The `Audio` module divides audio into multiple tracks (`EAudioTrack`) by usage. Each track corresponds to an `AudioCategory`, which internally maintains a set of `AudioAgent` objects (wrapping `AudioSource`) responsible for actual playback. The module is accessed via `GameModule.Audio` (`IAudioModule`), returning a `ulong` handle after playback for subsequent control such as pause, resume, and stop. It also supports indirect driving through events like `AudioPlayEvent` to avoid null references when the module is not initialized. Track and master volume settings are persisted through `SettingUtility` and automatically loaded after module initialization.

## Core Features

- Five built-in tracks `EAudioTrack`: `Sfx` (sound effects), `UI`, `Music`, `Voice`, `Ambience`, each with independent volume/mute/pause, names must match AudioMixer groups
- Agent pool playback: Each track pre-builds `AudioAgent` instances based on `MaxChannel`. When the limit is exceeded, it can either expand via `CanExpand` configuration or fade out and reuse the agent that has been playing the longest
- Handle + User ID dual management: `Play` returns a module-maintained handle; specify a user ID via `AudioPlayOptions.ID` during playback for batch control by ID
- Full transition capabilities: Single audio fade in/out (`FadeAudio`), track transition (`FadeTrack`), master track transition (`FadeMasterTrack`), zero GC for manual transitions
- Solo: `SoloSingleTrack` / `SoloAllTracks` mutes the same track or all audio during playback, `AutoUnSoloOnEnd` supports auto-removal when playback ends
- 3D spatial audio: Position, follow Transform, Doppler, attenuation curve, and other `AudioSource` parameters can all be configured in `AudioPlayOptions`
- Persistent audio: The `Persistent` option allows audio to continue playing after scene switching; other audio automatically fades out and stops when loading a new scene
- Event-driven: `AudioPlayEvent`, `AudioControlEvent`, `AudioTrackControlEvent`, `AudioTrackFadeEvent`, `AudioFadeEvent`, `AudioModuleEvent`, `AllAudiosControlEvent`

## Core Types

Namespace: `Moirai.Atropos.Audio`

| Class/Interface | Description |
|----------------|-------------|
| `IAudioModule` | Module public interface, return type of `GameModule.Audio` |
| `AudioModule` | `sealed` implementation class, inherits `Module` and implements `IUpdateModule`, registers and responds to all audio events |
| `EAudioTrack` | Track enum: `Sfx`, `UI`, `Music`, `Voice`, `Ambience` |
| `AudioCategory` | Track category, holds a list of `AudioAgent`, provides `GetAvailableAgent`, `PauseAll`, `StopAll`, etc. |
| `AudioAgent` | Audio agent, wraps `AudioSource`, responsible for loading, playback, fade in/out and state machine (`EAudioAgentRuntimeState`) |
| `EAudioAgentRuntimeState` | Agent runtime state: `None`, `Loading`, `FadingIn`, `Playing`, `FadingOut`, `End`, `Pausing` |
| `AudioGroupConfig` | Track group configuration: `AudioTrack`, `AudioMixerGroup`, default volume, `MaxChannel`, `CanExpand` and settings read/write |
| `AudioPlayOptions` | Playback option struct, provides `Default`, `Create`, `CreateLooping`, `CreateWithFade` factories |
| `AudioPlayOptionsSO` | Playback option asset (ScriptableObject), supports random/sequential clip selection, random volume/pitch, concurrency limit |
| `AudioSettings` | Framework settings (`FrameworkSetting`): configures `AudioMixer` and `AudioGroupConfig[]` |
| `AudioAssetData` | Audio asset handle wrapper (`MemoryObject`), releases `AssetHandle` on demand during recycling |
| `AudioPlayEvent` | Play event: `Trigger(AudioClip, AudioPlayOptions)` or `Trigger(path, options, bAsync, bInPool)` returns handle |
| `AudioControlEvent` | Control by ID: `Pause` / `Unpause` / `Stop(int soundID)` |
| `AudioTrackControlEvent` | Track control: `MuteTrack`, `PauseTrack`, `SetTrackVolume`, `MuteMaster`, etc. |
| `AudioFadeEvent` | Transition by ID: `PlayFade(soundID, duration, finalVolume, ease)`, `StopFade(soundID)` |
| `AudioTrackFadeEvent` | Track transition: `PlayFade(track, ...)`, `PlayMasterFade(duration, finalVolume, ease)` |
| `AudioModuleEvent` | Settings event: `SetSettings` / `LoadSettings` / `ResetSettings` |
| `AllAudiosControlEvent` | Global control: `Pause`, `Play`, `Stop`, `AllButPersistent`, `StopAllLooping` |
| `BackgroundMusic` | Component: automatically plays background music when the object is instantiated (old BGM with the same ID is automatically switched) |
| `AudioSettingsWidget` | Component: binds Slider/Toggle to master volume and individual track settings |

## Quick Start

```csharp
// Access the module
IAudioModule audio = GameModule.Audio;

// 1. Play using AudioClip (Create factory presets common defaults)
AudioPlayOptions options = AudioPlayOptions.Create(EAudioTrack.Sfx);
ulong handle = audio.Play(clip, options);

// 2. Looping BGM: load from the resource system by path, async + cached handle
AudioPlayOptions bgmOptions = AudioPlayOptions.CreateLooping(EAudioTrack.Music);
ulong bgm = audio.Play("Assets/AssetRaw/Default/Audio/bgm_main.mp3", bgmOptions, bAsync: true, bInPool: true);

// 3. Fade-in playback
ulong fadeIn = audio.Play(clip, AudioPlayOptions.CreateWithFade(EAudioTrack.Music, 2f));

// 4. Control via handle
audio.Pause(handle);
audio.Unpause(handle);
audio.Stop(handle, fadeoutDuration: 0.5f);
bool playing = audio.IsPlaying(handle);
AudioAgent agent = audio.GetAgentByHandle(handle); // Access internal AudioSource, etc.

// 5. Track volume and mute (writes to AudioMixer exposed parameters and persists)
audio.SetTrackVolume(EAudioTrack.Music, 0.8f);
audio.SetTrackMute(EAudioTrack.Sfx, true);

// 6. Volume transition
audio.FadeAudio(bgm, 2f, 1f, 0.3f, default);           // Single audio 1 -> 0.3
audio.FadeTrack(EAudioTrack.Music, 2f, 1f, 0.5f);       // Entire track
audio.FadeMasterTrack(1.5f, 1f, 0.8f);                  // Master track
```

## Advanced Usage

### Event-Driven Playback and Control by ID

Specify a user ID via `AudioPlayOptions.ID` during playback, then use events to batch-operate all instances with the same ID without needing to save handles yourself:

```csharp
// Event-driven playback (returns ulong handle)
ulong voice = AudioPlayEvent.Trigger(clip, AudioPlayOptions.CreateLooping(EAudioTrack.Voice));

// Specify user ID using the long-parameter overload (AudioPlayOptions.ID setter is internal)
GameModule.Audio.Play(clip, EAudioTrack.Voice, Vector3.zero, loop: true, id: 33);

AudioControlEvent.Pause(33);                    // Pause all audio with ID 33
AudioControlEvent.Stop(33);                     // Stop
AudioFadeEvent.PlayFade(33, 2f, 0.3f);          // Transition to 0.3 volume over 2 seconds

// Track-level control
AudioTrackControlEvent.PauseTrack(EAudioTrack.UI);
AudioTrackControlEvent.SetTrackVolume(EAudioTrack.Music, 0.5f);
AudioTrackControlEvent.MuteMaster();

// Global control
AllAudiosControlEvent.Stop();
AllAudiosControlEvent.AllButPersistent();       // Stop all audio except Persistent

// Settings persistence (write / load / reset, call SettingUtility.Save to persist)
AudioModuleEvent.SetSettings();
AudioModuleEvent.LoadSettings();
AudioModuleEvent.ResetSettings();
```

### Long-Parameter Overload and Querying

`Play` provides long overloads with all parameters exposed (both clip and path versions), convenient for one-shot configuration of 3D audio:

```csharp
ulong h = GameModule.Audio.Play(clip, EAudioTrack.Sfx, position,
    volume: 0.9f, spatialBlend: 1f, rolloffMode: AudioRolloffMode.Linear,
    minDistance: 2f, maxDistance: 60f, attachToTransform: enemy.transform);

// Querying
IReadOnlyList<AudioAgent> agents = GameModule.Audio.FindAgentsByID(33); // Shared buffer, consume ASAP
int count = GameModule.Audio.CurrentlyPlayingCount(clip);
```

### Playback Option Asset

Create a `Moirai Framework/Audio/Play Options SO` asset to configure random audio arrays (random/sequential/non-repeating mode), random volume/pitch ranges, per-clip concurrency limits, etc. At runtime, call its `Play(Vector3 location)` method:

```csharp
[SerializeField] private AudioPlayOptionsSO shootSfx;
void OnShoot() => shootSfx.Play(muzzle.position);
```

### Audio Resource Pool

Frequently loaded clips can be preloaded into a handle pool and reused by passing `bInPool: true` during playback:

```csharp
GameModule.Audio.PutInAudioPool(new List<string> { "Assets/.../hit.mp3" });
GameModule.Audio.RemoveClipFromPool(new List<string> { "Assets/.../hit.mp3" });
GameModule.Audio.CleanAudioPool();
```

## Configuration Notes

- `AudioSettings` (menu: Audio Settings) configures `AudioMixer` and `AudioGroupConfig` for each track; if not configured, the code falls back to reading groups under `Master/` from `Resources/AudioMixer` and matches `EAudioTrack` by group name
- AudioMixer groups must expose a volume parameter named `{GroupName}Volume` (e.g., `MusicVolume`), and the module writes to this parameter using logarithmic conversion for track volume
- `AudioGroupConfig.MixerValuesMultiplier` (default 20) is the conversion coefficient from normalized volume to decibels
- It can also be passed explicitly during initialization: `audio.Initialize(instanceRoot, audioMixer, audioGroupConfigs)`

## Notes

- `Play` returns `0UL` to indicate playback failure (no available agent, track not configured, or audio disabled in the editor)
- When `DoNotAutoRecycleIfNotDonePlaying` is `false` (default for `new AudioPlayOptions`), exceeding the maximum sound count will fade out and interrupt the longest-playing audio; `Default` and `Create` factory series default to `true`
- `FindAgentsByID` / `FindAgentsByClip` returns an internal shared buffer; results must be consumed before the next call
- When loading a new scene, the module automatically calls `StopAllButPersistent`; audio that needs to persist across scenes must set `Persistent = true`
- In the editor, the module mounts `AudioDebugger` on the root node for Inspector debugging; when audio is disabled in the editor (`unityAudioDisabled`), all interfaces silently fail
- Master volume takes effect via `AudioListener.volume`, while track volume takes effect via AudioMixer parameters; the two mechanisms differ

---
[« Back to Main README](../README_EN.md)