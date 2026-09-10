# Audio 服务

> 基于音轨分组与音频代理池的音频系统：可替换后端（Unity / FMOD / Wwise）、句柄生命周期、淡入淡出、混音快照与空间化遮挡。

`Audio` 服务按用途划分为多条音轨（`EAudioTrack`）。Unity 后端每轨对应一个 `AudioCategory`，内部维护 `AudioAgent`（封装 `AudioSource`）；中间件后端（FMOD / Wwise）走统一的 `MiddlewareAudioHandler`。服务通过 `AudioService.Xxx()` 静态外观访问，播放返回 `ulong` 句柄用于后续控制，也支持按用户 ID 批量操作（`StopByID` / `PlayFade` 等）。音量设置经 `SettingUtility` 持久化。

## 目录结构

```text
Runtime/Services/Audio/
├── AudioService.cs / AudioServiceHandler.cs   # Facade + 后端契约
├── AudioAgent.cs / AudioCategory.cs           # Unity 代理与音轨
├── AudioAgentHostPool.cs                      # 宿主 GameObject 池
├── AudioServiceSettings.cs
├── Handler/
│   ├── UnityAudioHandler.cs                   # 默认 Unity 后端
│   ├── Middleware/
│   │   ├── IAudioMiddlewareBridge.cs          # FMOD/Wwise 共用桥接
│   │   └── MiddlewareAudioHandler.cs          # 中间件共用基类
│   ├── Fmod/  FmodBridgeStub|Native
│   ├── FmodAudioHandler.cs
│   ├── Wwise/ WwiseBridgeStub|Native
│   └── WwiseAudioHandler.cs
├── Mix/ AudioMixStateMachine.cs               # 混音快照状态机
├── Spatial/ AudioOcclusionHrtf.cs             # 遮挡 + HRTF
├── Models/  AudioPlayRequest / ColdParams / Options / AssetData / GroupConfig
└── Support/ BackgroundMusic / SettingsWidget
```

## 架构（HandlerHost + 策略）

- **`AudioService`**：静态外观，依赖 `DebuggerService`、`ResourceService`
- **`AudioServiceHandler`**：后端抽象契约（含 `StopByID`、16B 热请求 `Play`、`OnAgentPlaybackEnded` 虚回调）
- **`UnityAudioHandler`**：默认 Unity `AudioSource`/`AudioMixer` 后端
- **`MiddlewareAudioHandler`**：FMOD / Wwise 共用基类（句柄、Fade、总线、分层）
- **`FmodAudioHandler` / `WwiseAudioHandler`**：薄封装，仅提供 `CreateDefaultBridge()`
- **`AudioServiceSettings`**：选择后端、配置 Mixer 与 `AudioGroupConfig[]`、可选宿主 Prefab 路径

### 可替换后端与预编译宏

| 宏 | 后端 | 未定义时 |
|---|---|---|
| （默认） | `UnityAudioHandler` | — |
| `FMOD_INSTALLED` | `FmodBridgeNative` → `FmodAudioHandler` | 使用 `FmodBridgeStub`（可跑通契约与压测） |
| `WWISE_INSTALLED` | `WwiseBridgeNative` → `WwiseAudioHandler` | 使用 `WwiseBridgeStub` |

在 `AudioServiceSettings` 的 Handler 下拉中选择 `FmodAudioHandler` / `WwiseAudioHandler` 即可切换；桥接契约为 `IAudioMiddlewareBridge`（Play/Stop/Pause/Bus/IsPlaying）。

## 核心特性

- 五轨内置 `EAudioTrack`：Sfx / UI / Music / Voice / Ambience
- 代理池 + Priority Voice Stealing + `HARD_CHANNEL_CAP`（32）
- 句柄自动释放：`Stop`/结束时由 `OnAgentPlaybackEnded` 清映射，避免旧句柄别名
- 分层 BGM：不同 ID 可共播；`StopByID(id)` 只替换本层
- 16 字节热请求 `AudioPlayRequest` + 池化冷参 `AudioPlayColdParams`
- 混音快照状态机：`EMixSnapshot` 优先级切换 + 交叉淡变
- 空间化：`AudioOcclusionHrtf` 射线遮挡 → 低通；可选 HRTF `spatialBlend`
- 宿主池：`AudioAgentHostPool` 内部栈池复用 AudioSource 宿主

## 核心类型

命名空间：`Moirai.Atropos.Audio`（中间件在 `.Fmod` / `.Wwise` / `.Middleware`）

| 类型 | 说明 |
|------|------|
| `AudioService` | 静态外观；含 `RequestMixSnapshot` / `StopByID` / 16B `Play` |
| `AudioServiceHandler` | 后端契约 |
| `UnityAudioHandler` | 默认 Unity 后端 |
| `MiddlewareAudioHandler` | 中间件共用基类 |
| `FmodAudioHandler` / `WwiseAudioHandler` | FMOD / Wwise 薄封装 |
| `AudioPlayRequest` | 16B 热路径请求（Id/Volume/Pitch/Track/Priority/Flags） |
| `AudioPlayColdParams` | 冷路径：位置、曲线、旁通、淡入、Rolloff（池化） |
| `AudioPlayOptions` | 完整兼容门面；`ToRequest()` / `FromOptions()` 拆分 |
| `AudioMixStateMachine` / `EMixSnapshot` | 混音快照状态机 |
| `AudioOcclusionHrtf` | 遮挡 + HRTF 组件 |
| `AudioAgentHostPool` | 宿主内部栈池（OnInit 按配置预热） |
| `BackgroundMusic` | 分层 BGM 组件（同 ID 替换，异 ID 共存） |

## 快速上手

```csharp
// 选项工厂（兼容）
var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
ulong h = AudioService.Play(clip, options);

// 16B 热请求（推荐）
var req = new AudioPlayRequest(id: 1, volume: 1f, pitch: 1f, EAudioTrack.Sfx, 128,
    AudioPlayFlags.DoNotAutoRecycle);
ulong h2 = AudioService.Play(clip, req, cold: null);

// 分层 BGM：同 ID 替换，异 ID 共存
AudioService.StopByID(10001, 0.5f);
AudioService.Play(bgm, musicOptions); // ID = 10001

// 混音快照
AudioService.RequestMixSnapshot(EMixSnapshot.Dialogue, 0.3f);
AudioService.ResetMixSnapshot(0.5f);

// 音轨 / 句柄控制
AudioService.SetTrackVolume(EAudioTrack.Music, 0.8f);
AudioService.Stop(h2, fadeoutDuration: 0.2f);
```

## 进阶用法

### 中间件后端

```csharp
// Player Settings → Scripting Define Symbols 添加 FMOD_INSTALLED 或 WWISE_INSTALLED
// 导入对应插件后，在 AudioServiceSettings 将 Handler 切换为 FmodAudioHandler / WwiseAudioHandler
// 事件路径约定：FMOD = event:/Name；Wwise = 事件名；总线 bus:/Music 等
```

### 混音快照

| 状态 | 默认优先级 |
|------|-----------|
| Default | 0 |
| Muffled / LowHealth | 2 |
| Paused / Dialogue | 3 |
| Cinematic | 4 |

低优先级不可打断高优先级（`force: true` 可破）。Unity 后端驱动 `AudioMixerSnapshot.TransitionTo`；中间件经 `SetMiddlewareTransitionHandler` 回调。

### 遮挡 / HRTF

场景挂 `AudioOcclusionHrtf`（挂在 Listener 旁）：定时射线检测活跃声源，写 `AudioLowPassFilter`，可选推高 `spatialBlend`。

### 宿主栈池预热

`AudioServiceSettings` 中配置 `WarmupAudioHostPool` 与 `AudioHostWarmupCount`；`AudioService.OnInit` 在 Handler 就绪后向 `InstanceRoot` 预热。未启用时按需创建并入栈复用。

## 配置说明

- AudioMixer 分组需暴露 `{分组名}Volume` 参数；`MixerValuesMultiplier` 默认 20  
- `AudioGroupConfig.MaxChannel` / `CanExpand` 控制通道；扩展受 `HARD_CHANNEL_CAP` 限制  
- 主音量走 `AudioListener.volume`；音轨走 Mixer 参数  

## 注意事项

- `Play` 返回 `0UL` 表示失败（无通道、音轨未配置、后端未初始化等）  
- 中间件后端 `GetAgentByHandle` / `ForEachAgentByID` 返回空——无 Unity `AudioSource` Agent，请用句柄 API  
- 手动 `FadeAudio` / 快照过渡依赖服务 `Tick` 推进  
- 加载新场景自动 `StopAllButPersistent`；跨场景音频设 `Persistent = true`  
- 句柄由服务自动释放，无需（也不应长期）手动 `ReleaseHandle`  
- 冷路径 API（`PlayFade` / `StopByID`）允许 lambda；热路径用 16B `AudioPlayRequest`  

---
[« 返回主 README](../../README.md)
