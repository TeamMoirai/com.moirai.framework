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
│   ├── AudioHandleRegistry.cs                 # 共享句柄注册表（句柄生成/用户 ID 映射/列表池）
│   ├── AudioFadeScheduler.cs                  # 共享音量过渡调度器（声部 + 总线伪句柄）
│   ├── AudioClipCache.cs                      # Clip 租约缓存（LRU + TTL + Pin + lowMemory）
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
│          AudioCachePolicy / AudioClipCacheEntry / AudioLoadRequest
└── Support/ BackgroundMusic / SettingsWidget
```

## 架构（HandlerHost + 策略）

- **`AudioService`**：静态外观，依赖 `DebuggerService`、`ResourceService`
- **`AudioServiceHandler`**：后端抽象契约（含 `StopByID`、16B 热请求 `Play`、`OnAgentPlaybackEnded` 虚回调）
- **`AudioHandleRegistry<TVoice>` / `AudioFadeScheduler`**：Unity 与中间件后端共享的句柄注册与过渡调度（总线 Fade 用高位段伪句柄）
- **`UnityAudioHandler`**：默认 Unity `AudioSource`/`AudioMixer` 后端
- **`MiddlewareAudioHandler`**：FMOD / Wwise 共用基类（句柄、Fade、总线、分层）
- **`FmodAudioHandler` / `WwiseAudioHandler`**：薄封装，仅提供 `CreateDefaultBridge()`
- **`AudioServiceSettings`**：选择后端、配置 Mixer 与 `AudioGroupConfig[]`、混音快照映射、可选宿主池预热与 Clip 缓存档位
- **`AudioClipCache`**：Unity 后端路径播放的资源真相源（同地址共享一条租约，LRU/TTL/Pin 驱逐）

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
- Clip 缓存：路径播放同地址共享一条租约，引用计数 + LRU/TTL/Pin 驱逐 + `lowMemory` 自动清理

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
| `AudioPlayOptions` | 完整兼容门面；`ToRequest()` / `FromOptions()` 拆分；`CachePolicy` 决定 clip 留池策略 |
| `AudioCachePolicy` | Clip 缓存策略：`Default`（取设置）/ `None`（用完即弃）/ `Ttl`（留池到期驱逐）/ `Pin`（常驻） |
| `AudioClipCache` | Unity 后端 Clip 租约缓存（内部）；`AssetHandlePool` 是其只读视图 |
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

### Clip 缓存与预加载

`Play(path, ...)` 的加载统一经 `AudioClipCache`：同一地址全服务只持有一条资源租约，多个声部按引用取用，最后一个使用者停播后才按策略决定留池或释放。

```csharp
// 常驻预热（启动期/过场前）：Pin 条目不参与 LRU/TTL
AudioService.Preload("Audio/BGM/MainTheme");
AudioService.PreloadAsync("Audio/Voice/Intro", AudioCachePolicy.Ttl, ok => { /* ... */ });

// 用完即弃（一次性长音频）：引用归零立即释放租约
var options = AudioPlayOptions.Create(EAudioTrack.Voice);
options.CachePolicy = AudioCachePolicy.None;
AudioService.Play("Audio/Voice/OneShot", options);

// 回收：只清不留池的过期项由服务 Tick 自动完成，以下是显式手段
AudioService.UnloadClipCache("Audio/BGM/MainTheme");        // Pin 条目需 force: true
AudioService.ClearClipCache(force: true);                   // 连 Pin 一并清
```

- 驱逐门槛是「无人引用且不在加载中且无等待者」；`force` 只放宽 Pin 与等待者两道，**在播/淡出中的引用一律拒绝释放**（否则 AudioSource 会拿到已卸载的 clip）。
- 条目数达到 `ClipCacheCapacity` 时，最久未用的无引用条目先出局；若全部为 Pin 或全部在用，新地址直接判负（`Play` 返回 `0UL`）而不是无限增长。
- `Application.lowMemory` 触发一次非强制清理（Pin 与在播不受影响）。
- `PutInAudioPool` / `RemoveClipFromPool` / `CleanAudioPool` 与 `AssetHandlePool` 保留为兼容入口，语义分别映射为 `Preload(Pin)` / `Unload(force)` / `ClearClipCache(force)`；`bInPool: true` 等价于「至少按 TTL 留池」。

### 混音快照

| 状态 | 默认优先级 |
|------|-----------|
| Default | 0 |
| Muffled / LowHealth | 2 |
| Paused / Dialogue | 3 |
| Cinematic | 4 |

低优先级不可打断高优先级（`force: true` 可破）。Unity 后端驱动 `AudioMixerSnapshot.TransitionTo`；中间件经 `SetMiddlewareTransitionHandler` 回调。快照映射（状态 → `AudioMixerSnapshot` + 可选优先级）在 `AudioServiceSettings` 的 `MixSnapshots` 中配置，`OnInit` 自动注册；未配置时需手动 `AudioMixService.RegisterSnapshot`。

### 遮挡 / HRTF

场景挂 `AudioOcclusionHrtf`（挂在 Listener 旁）：定时射线检测活跃声源，写 `AudioLowPassFilter`，可选推高 `spatialBlend`。

### 宿主栈池预热

`AudioServiceSettings` 中配置 `WarmupAudioHostPool` 与 `AudioHostWarmupCount`；`AudioService.OnInit` 在 Handler 就绪后向 `InstanceRoot` 预热。未启用时按需创建并入栈复用。

## 配置说明

- AudioMixer 分组需暴露 `{分组名}Volume` 参数；`MixerValuesMultiplier` 默认 20  
- `AudioGroupConfig.MaxChannel` / `CanExpand` 控制通道；扩展受 `HARD_CHANNEL_CAP` 限制  
- 主音量走 `AudioListener.volume`；音轨走 Mixer 参数  
- `ClipCacheCapacity`（默认 128）/ `ClipCacheTtl`（默认 30 秒，`0` 关闭按时间驱逐）/ `DefaultClipCachePolicy`（默认 `Ttl`）三项在 `AudioServiceSettings` 的「Clip 缓存」组内配置，`Initialize` 时下发给缓存  

## 注意事项

- `Play` 返回 `0UL` 表示失败（无通道、音轨未配置、音轨暂停中、后端未初始化等）  
- 暂停的音轨会拦截新播放；`MasterVolume` getter 始终返回未静音的设置值（两后端语义一致）  
- 中间件后端 `GetAgentByHandle` / `ForEachAgentByID` 返回空——无 Unity `AudioSource` Agent，请用句柄 API；不支持 InitialDelay / PlaybackDuration / Solo  
- 各工厂方法与重载的 `DoNotAutoRecycle` 默认统一为 true（不抢占未播完的通道）  
- 无可用通道的告警按轨节流（3 秒）降级为 Warning  
- 手动 `FadeAudio` / 快照过渡依赖服务 `Tick` 推进  
- 路径播放 `Play(path, ...)` 默认异步加载（`bAsync = true`）；同步加载阻塞主线程，仅限启动期/预加载显式使用  
- 路径播放一律经 Clip 缓存，默认策略 `Ttl`：一次性的冷门音效想「用完立刻卸载」请显式设 `AudioPlayOptions.CachePolicy = None`  
- `AssetHandlePool` 现在是 `AudioClipCache` 的只读视图，值由缓存持有——外部只可枚举观测，不要 Dispose 其中的租约  
- 自然结束计时按未缩放真实时间推进（`AudioSource` 不受 `timeScale` 影响）：`timeScale = 0` 时非循环音仍会真实播完并自动释放句柄  
- `Stop(handle, fadeout)` 与 `FadeAudio(handle, ...)` 互斥接管同句柄音量（后调用者取消前者），请勿混用叠加  
- 加载新场景自动 `StopAllButPersistent`；跨场景音频设 `Persistent = true`  
- 句柄由服务自动释放，无需（也不应长期）手动 `ReleaseHandle`  
- 冷路径 API（`PlayFade` / `StopByID`）允许 lambda；热路径用 16B `AudioPlayRequest`

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Resource](Resource.md) · [UI](UI.md)
