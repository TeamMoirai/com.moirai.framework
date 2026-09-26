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
│      AudioVoiceDucking.cs                    # Voice 驱动的自动 Ducking
├── Spatial/ AudioOcclusionHrtf.cs             # 遮挡 + HRTF
├── Models/  AudioPlayRequest / ColdParams / Options / AssetData / GroupConfig
│          EAudioCachePolicy / AudioClipCacheEntry / AudioLoadRequest
└── Support/ BackgroundMusic / AudioSettingsWidget / BgmPlaylist / AudioEmitter
           AudioMainThread / AudioFault / AudioWarnOnce      # 主线程断言、退避式异常上报、按 key 去重告警
```

## 架构（HandlerHost + 策略）

- **`AudioService`**：静态外观，依赖 `DebuggerService`、`ResourceService`
- **`AudioServiceHandler`**：后端抽象契约（含 `StopByID`、16B 热请求 `Play`、`OnAgentPlaybackEnded` 虚回调）。Master/音轨总线过渡由本契约**直接实现**（持 `AudioFadeScheduler`，落到 `IAudioFadeTarget.ApplyFade`），后端只需实现"音量写到哪儿"
- **`AudioHandleRegistry<TVoice>` / `AudioFadeScheduler`**：Unity 与中间件后端共享的句柄注册与过渡调度（总线 Fade 用高位段伪句柄）
- **`UnityAudioHandler`**：默认 Unity `AudioSource`/`AudioMixer` 后端
- **`MiddlewareAudioHandler`**：FMOD / Wwise 共用基类（句柄、声部 Fade、总线、分层）
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

#### 事件映射表

中间件后端默认按 `clip.name` 推导事件路径（FMOD 为 `event:/<name>`）。事件由音效师命名、clip 只是占位引用时，这条隐性约定会静默推导错路径，因此在 Handler 上配一张显式映射表：

- 在 Inspector 里给 `FmodAudioHandler` / `WwiseAudioHandler` 的「事件映射表」加条目：`Clip` + `EventPath`。
- 命中映射直接用；未命中才回落到按名推导，并就该 clip **提示一次** Warning。
- 计数与播放同一条解析路径：`CurrentlyPlayingCount(clip)` 也先查映射表，否则命中映射的 clip 会恒查到 0。
- 运行期改过配置后调 `InvalidateEventMap()` 重建缓存（代码里配表时也用它）。
- `Play(string eventPath, …)` 一律按事件路径直发，不经映射表。

#### 声音库与实时参数

`AudioService.LoadBank(path)` / `UnloadBank(path)` / `SetRtpc(name, value[, handle])` 已进后端契约：

- Unity 后端无概念，`LoadBank` 返回 `false`、`SetRtpc` 空操作。
- 中间件后端按**能力接口**探测（`IAudioMiddlewareBankControl` / `IAudioMiddlewareRtpcControl`），桥接没实现该能力时提示一次并安全降级——刻意不做进 `IAudioMiddlewareBridge` 主接口，否则未实现它的真 SDK 桥在定义 `FMOD_INSTALLED` 时会直接编译不过。
- `SetRtpc` 的 `handle` 传 0 表示工程/全局参数，传播放句柄则作用于该实例。
- `FmodBridgeNative` / `WwiseBridgeNative` 已按这两个接口写好实现，但**在无 SDK 的机器上无法编译核对**：装完插件必须先过编译（G0），再按真 SDK 契约复测（G1）。
- `LoadBank` 在桥侧是**三态**（`Loaded` / `AlreadyLoaded` / `Failed`）：幂等命中与「插件启动时自行加载的 master/Init 库」都属正常，只有 `Failed` 会让外观层就该库名提示一次；外观 `AudioService.LoadBank` 仍是 `bool`，只在真的完成加载时返回 `true`。

## 核心特性

- 五轨内置 `EAudioTrack`：Sfx / UI / Music / Voice / Ambience
- 代理池 + Priority Voice Stealing + 按音轨可配的扩展硬上限（`AudioGroupConfig.MaxChannelCeiling`，缺省 32）
- 句柄自动释放：`Stop`/结束时由 `OnAgentPlaybackEnded` 清映射，避免旧句柄别名
- 分层 BGM：不同 ID 可共播；`StopByID(id)` 只替换本层
- 16 字节热请求 `AudioPlayRequest` + 池化冷参 `AudioPlayColdParams`
- 混音快照状态机：`EMixSnapshot` 优先级切换 + 交叉淡变
- 空间化：`AudioOcclusionHrtf` 射线遮挡 → 低通；可选 HRTF `spatialBlend`
- 自动 Ducking：Voice 有音在播时切 `Dialogue` 快照，播完自动归还借走的那一层
- 宿主池：`AudioAgentHostPool` 内部栈池复用 AudioSource 宿主；闲置宿主挂在 `[Warmup]` 下
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
| `AudioPlayOptions` | 完整兼容门面；`ToRequest()` / `FromOptions()` 拆分；空间整形经 `Spatial` 字段整体携带；`CachePolicy` 决定 clip 留池策略 |
| `AudioSpatialOptions` | AudioSource 空间整形（2D 声像 / 3D 衰减、多普勒、混响与自定义曲线），经 `AudioPlayOptions.Spatial` / `AudioPlayColdParams.Spatial` 进冷路径；`Default` 对齐 Unity 声学缺省，各播放工厂方法以此为起点 |
| `EAudioCachePolicy` | Clip 缓存策略：`Default`（取设置）/ `None`（用完即弃）/ `Ttl`（留池到期驱逐）/ `Pin`（常驻） |
| `AudioClipCache` | Unity 后端 Clip 租约缓存（内部）；`AssetHandlePool` 是其只读视图 |
| `AudioMixStateMachine` / `EMixSnapshot` | 混音快照状态机 |
| `AudioOcclusionHrtf` | 遮挡 + HRTF 组件 |
| `AudioAgentHostPool` | 宿主内部栈池（OnInit 按配置预热；闲置宿主在 `[Warmup]` 节点下） |
| `BackgroundMusic` | 分层 BGM 组件（同 ID 替换，异 ID 共存） |

## 快速上手

```csharp
// 选项工厂（兼容）
var options = AudioPlayOptions.Create(EAudioTrack.Sfx);
ulong h = AudioService.Play(clip, options);

// 16B 热请求（推荐）
var req = new AudioPlayRequest(id: 1, volume: 1f, pitch: 1f, EAudioTrack.Sfx, 128,
    EAudioPlayFlags.DoNotAutoRecycle);
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

#### 生产约定

发行前必须钉死的几条跨侧约定（音效工程与代码任一侧改动都要同步）：

- **事件命名**：`Play(clip, …)` 依赖映射表，映射表缺项时按 `clip.name` 推导（FMOD 前缀 `event:/`、Wwise 取裸名）。事件名与 clip 名不一致是**静默**播错/不播的头号来源，因此生产内容必须逐条登记进「事件映射表」，不依赖推导；按事件路径直发的 `Play(eventPath, …)` 不经表，调用约定由游戏侧规范约束。
- **总线**：框架按 `bus:/{EAudioTrack}` 下发线性音量，Master 走 `bus:/Master`。工程侧改名即等于该轨失控；Wwise 桥把总线映射成 RTPC（`bus:/Music` → `MusicVolume`），这批 RTPC 与 `SetRtpc` 用的参数名一起构成需要随包交付的**名单**，任一侧改名都要对表。
- **Bank 装卸顺序**：切场景固定为 `LoadBank(next)` → `StopAllButPersistent(fade)` → `UnloadBank(prev)`；`UnloadBank` 只在返回 `true` 时才算真的释放，返回 `false` 表示「没记账 / SDK 拒绝」，内存快照比对以 `true` 为准。
- **停止语义**：淡出由框架先写音量 Fade 到 0 再 `StopInstance(immediate: true)`；`immediate: false` 分支目前无生产调用方，接真 SDK 时按 G1 单独验（实现侧不得在淡出还没走完时就 release/回收发射体）。

#### 初始化失败的回退

`IAudioMiddlewareBridge.Initialize` 返回 `false` 时，后端把音频**整体禁用**并落一条 Error：桥引用被丢弃，此后 `Play` 返回 `0`、Bank/RTPC 空操作、`Tick` 直接返回，不会有任何一次调用打到未初始化的原生引擎。

- 不做「自动回落到 Stub」：Stub 在发行构建里根本不存在（宏未定义时编进来的就是它，而真 SDK 失败时它并不在包里）。
- 不做「回落到 Unity 后端」：Unity 后端要 clip 资产与 Mixer 分组，与中间件工程共用一套事件/音量数据，回落结果必然是半响不响。
- 不做重试：`Restart()` 不重开原生引擎，避免健康后端被二次 `Init`；恢复只在重启进程时发生。
- 框架不另开「音频是否可用」的查询面：游戏侧要判，就看 `Play` 是否为 `0` 句柄（禁用态下恒为 `0`，且不会刷屏）。

### Clip 缓存与预加载

`Play(path, ...)` 的加载统一经 `AudioClipCache`：同一地址全服务只持有一条资源租约，多个声部按引用取用，最后一个使用者停播后才按策略决定留池或释放。

```csharp
// 常驻预热（启动期/过场前）：Pin 条目不参与 LRU/TTL
AudioService.Preload("Audio/BGM/MainTheme");
AudioService.PreloadAsync("Audio/Voice/Intro", EAudioCachePolicy.Ttl, ok => { /* ... */ });

// 用完即弃（一次性长音频）：引用归零立即释放租约
var options = AudioPlayOptions.Create(EAudioTrack.Voice);
options.CachePolicy = EAudioCachePolicy.None;
AudioService.Play("Audio/Voice/OneShot", options);

// 回收：只清不留池的过期项由服务 Tick 自动完成，以下是显式手段
AudioService.UnloadClipCache("Audio/BGM/MainTheme");        // Pin 条目需 force: true
AudioService.ClearClipCache(force: true);                   // 连 Pin 一并清
```

- 驱逐门槛是「无人引用且不在加载中且无等待者」；`force` 只放宽 Pin 与等待者两道，**在播/淡出中的引用一律拒绝释放**（否则 AudioSource 会拿到已卸载的 clip）。
- 条目数达到 `ClipCacheCapacity` 时，最久未用的无引用条目先出局；若全部为 Pin 或全部在用，新地址直接判负（`Play` 返回 `0UL`）而不是无限增长。
- 地址表是**定长槽数组 + 开址桶 + 侵入式索引链**（不是 `Dictionary`）：取用/归还/驱逐都走这张表，字典的按需扩容与 rehash 尖峰就会落在播放那一帧上；容量上调时现存条目按 All 链原地重落新表，不需要临时数组。地址哈希走 Ordinal djb2，不取 `string.GetHashCode`——那个按进程随机化，两次启动的桶分布都不一样，线上冲突链无从复现。
- `AssetHandlePool` / `PoolReadOnly` 是**现算投影**而不是镜像表：缓存内部不再维护第二份字典，每次取用/归还也不多写一笔状态（漏同步就是"视图里还在、缓存里已无"的残影）。代价是枚举它每次分配一份快照——这条只有调试面板在用，属观测路径。
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

### 自动 Ducking

`AudioServiceSettings.AutoDuckingOnVoice`（默认关闭）打开后，Voice 音轨只要有声部活跃就请求 `EMixSnapshot.Dialogue`，全部播完再回落，无需游戏侧手写台词起止：

- 判定按各后端实算（Unity 扫该轨 Agent 是否空闲，中间件扫句柄表里 `Playing` 的声部），不做播放/结束计数——计数漏减一次就会永久压低混音。
- 由后端 `Tick` 驱动，因此淡出中、加载中都算「在播」；开关被关掉时当帧就把挂着的 duck 落回去。
- 与快照优先级协同：已有更高优先级状态（如 Cinematic）占着混音时，duck 请求被挡下且**不记为生效**，因此不会在演出中途把混音抢回来；回落只在仍由 duck 占着 `Dialogue` 时才做，并回到 duck 之前的状态而不是硬写 `Default`。
- 前置条件：`MixSnapshots` 里注册了 `Dialogue` 的 `AudioMixerSnapshot`。缺失时切换是空操作（一次性 Warning），表现为「开了没效果」而不是报错。

```csharp
// 游戏侧一般不需要手写；要显式压低混音仍可直接请求
AudioService.RequestMixSnapshot(EMixSnapshot.Dialogue, 0.25f);
AudioService.ResetMixSnapshot(0.25f);
```

### 遮挡 / HRTF

场景挂 `AudioOcclusionHrtf`（挂在 Listener 旁）：定时射线检测活跃声源，写 `AudioLowPassFilter`，可选推高 `spatialBlend`。

### 宿主栈池预热

`AudioServiceSettings` 中配置 `WarmupAudioHostPool` 与 `AudioHostWarmupCount`；`AudioService.OnInit` 在 Handler 就绪后向 `InstanceRoot` 预热。闲置宿主统一挂在 `[Warmup]` 节点下（与各 `Audio Category - *` 平级），被 `AudioAgent` 取用时再挂到对应 Category 并改名为 `SFX - 0` 这类实例名；归还时失活挂回 `[Warmup]`。未启用预热时按需创建并在首次归还时建 `[Warmup]` 入栈复用。

### 0-GC 验收（玩家构建）

编辑器 Mono 下 `GC.GetAllocatedBytesForCurrentThread()` 与 `ProfilerRecorder(GC.Alloc)` 均恒为 0，**编辑器内无法计量托管分配**：PlayMode 的 0-GC 断言按「能力探测 + `Assert.Ignore`」处理（探测不到计量能力就跳过，绝不把「测不出」当「没有」），只承担功能回归职责。播放稳态 0 分配的**权威门禁**锚在 `Tests/Player`：

1. Test Runner → PlayMode 页签 → 搜索 `AudioPerformance` / `Allocation`；
2. 点 **Run all in Player**（玩家测试只能从该入口发起——玩家不解析 `-testResults`，结果经 PlayerConnection 回传由编辑器落盘）；
3. 跑完核对 `AudioPerformanceTests` 的 GC.Alloc 断言全绿。发布前至少跑一次。

## 配置说明

- AudioMixer 分组需暴露 `{分组名}Volume` 参数；`m_MixerValuesMultiplier` 默认 20  
- `AudioGroupConfig.MaxChannel` / `CanExpand` 控制通道；扩展受同一条轨的 `MaxChannelCeiling` 限制（缺省 32、绝对上限 128，按平台预算分轨配；预置槽位不受它约束）  
- 主音量走 `AudioListener.volume`；音轨走 Mixer 参数  
- `ClipCacheCapacity`（默认 128）/ `ClipCacheTtl`（默认 30 秒，`0` 关闭按时间驱逐）/ `DefaultClipCachePolicy`（默认 `Ttl`）三项在 `AudioServiceSettings` 的「Clip 缓存」组内配置，`Initialize` 时下发给缓存  
- `AutoDuckingOnVoice`（默认关闭）在「自动 Ducking」组内；开启前先在 `MixSnapshots` 注册 `Dialogue` 快照  

## 注意事项

- `Play` 返回 `0UL` 表示失败（无通道、音轨未配置、音轨暂停中、后端未初始化等）  
- 暂停的音轨会拦截新播放；`MasterVolume` getter 始终返回未静音的设置值（两后端语义一致）  
- 后端整体失效时音量面统一读 0、写无效：Unity 侧指编辑器菜单关掉的音频（`AudioSettings.unityAudioDisabled`，玩家构建恒为 false），中间件侧指桥接 `Initialize` 明确失败（本次运行不自愈，恢复要重启进程）。**「桥接还没建起来」不算失效**——启动中间态里 getter 必须照实报设置值，否则初始化完成前打开设置面板会把滑杆读成 0，用户一动就把 0 写回并持久化。禁用态下 `FadeMasterTrack` / `FadeTrack` 也不排程（`SoundIsFadingOut` 因此不会报着一个正在进行的、永远不会响的过渡）  
- 音轨暂停标记由契约持有（`_pausedTracks`），但**数组的建立与释放仍按后端各自的时机**：Unity 侧在后端 `Initialize` 之前调 `PauseTrack` 不会记上（也不报错），关停后标记全部作废。要在启动期就静音某条音轨，请配 `AudioServiceSettings` 而不是等 `PauseTrack`  
- 中间件后端 `GetAgentByHandle` / `ForEachAgentByID` 返回空——无 Unity `AudioSource` Agent，请用句柄 API；不支持 InitialDelay / PlaybackDuration / Solo  
- 各工厂方法与重载的 `DoNotAutoRecycle` 默认统一为 true（不抢占未播完的通道）  
- 无可用通道的告警按轨节流（3 秒）降级为 Warning  
- 手动 `FadeAudio` / 快照过渡依赖服务 `Tick` 推进  
- Master / 音轨过渡由契约基类实现（两后端同一条路）：`duration <= 0` 等价于直接赋值且不排过渡；`StopFadeMasterTrack` / `StopFadeTrack` **只撤过渡、不还原已写出去的音量**（停在哪儿就是哪儿）；对同一总线重复请求过渡是顶掉前一条而非叠加  
- 路径播放 `Play(path, ...)` 默认异步加载（`bAsync = true`）；同步加载阻塞主线程，仅限启动期/预加载显式使用  
- 路径播放一律经 Clip 缓存，默认策略 `Ttl`：一次性的冷门音效想「用完立刻卸载」请显式设 `AudioPlayOptions.CachePolicy = None`  
- 加载失败的地址进入 5 秒冷却（`Configure` 的 `failureCooldownSeconds` 可调，`0` 关闭）：否则一个写错的事件地址被高频触发时会每次都重穿资源层。`ClearClipCache(force: true)` 会连冷却一起重置  
- 播放入口只在开发构建断言主线程（句柄表与缓存的 LRU/引用计数无跨线程保护）；后台线程里请经 `MainThreadDispatcher.Post` 转投。租约来源若从非主线程回调，缓存会转投主线程处理，转投失败则就地归还租约并报错，绝不跨线程改结构  
- 服务 `Tick` 在音频内部做故障隔离：某处抛异常只会被退避上报（同位置 5 秒内不重复打印），不会把音频踢出轮询，也不会连带冻住同帧的其它服务  
- 切后台时冻结 `AudioListener.pause`（保留各 `AudioSource` 播放位置），回前台解冻；不订阅 `OnApplicationFocus`（桌面切窗不应静音）。在后台被关停也会补一次解冻，不留全局静音状态  
- `AssetHandlePool` 现在是 Clip 缓存的只读视图（契约成员类型为 `IReadOnlyDictionary`）：租约由缓存持有，外部既改不动记账也释放不了租约  
- 自然结束计时按未缩放真实时间推进（`AudioSource` 不受 `timeScale` 影响）：`timeScale = 0` 时非循环音仍会真实播完并自动释放句柄  
- `Stop(handle, fadeoutDuration)` 与 `FadeAudio(handle, ...)` 互斥接管同句柄音量（后调用者取消前者），请勿混用叠加  
- 整景切换（`Single`）自动 `StopAllButPersistent`；**Additive（叠加/流式分区）加载永不自动停音**——叠加加载没有"停掉全部非持久音"的合理用例，需要收口的游戏流程请在自己明确的切换点显式调 `StopAllButPersistent`；跨场景音频设 `Persistent = true`  
- `BgmPlaylist` 分层 ID：**正数 = 显式分层，两个实例填同一正数 ID 属配置错误，按 fail-fast 报 Error 且后者不播放**（不静默改派）；`0` = 按实例自动分配（负区间保留，永不与显式值冲突）。负数显式 ID 同样报错——它属于自动分配保留区间  
- `AudioPlayOptionsSO` 的「时间」参数（`PlaybackTime` / `PlaybackDuration`，含随机区间）随每次 `Play` 实际生效；`MaximumConcurrentInstances` / `DoNotPlayIfClipAlreadyPlaying` 作用于**本次候选 clip**（随机曲集下不是上一曲）  
- 句柄由服务自动释放，无需（也不应长期）手动 `ReleaseHandle`  
- 游戏内调试器 `Profiler/Audio` 除音量/音轨控制外，还显示 Clip 缓存条目/容量、在途、常驻、失败冷却、当前混音快照与 Ducking 占用，并提供清空缓存按钮——排查"音效没出来"先看这里  
- 冷路径 API（`PlayFade` / `StopByID`）允许 lambda；热路径用 16B `AudioPlayRequest`

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Resource](Resource.md) · [UI](UI.md)
