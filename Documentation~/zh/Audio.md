# Audio 服务

> 基于 AudioMixer 音轨分组与音频代理池的音频系统，支持句柄控制、淡入淡出与独奏播放。

`Audio` 服务将音频按用途划分为多条音轨（`EAudioTrack`），每条音轨对应一个 `AudioCategory`，内部维护一组 `AudioAgent`（封装 `AudioSource`）负责实际播放。服务通过 `AudioService.Xxx()` 静态外观访问（后端逻辑在抽象契约 `AudioServiceHandler` 的默认实现 `UnityAudioHandler` 中），播放后返回 `ulong` 句柄用于暂停、恢复、停止等后续控制，也支持按用户 ID（`AudioPlayOptions.ID`）通过 `ForEachHandleByID` / `PlayFade` 等批量操作，无需自行保存句柄。音轨与主音量的设置会通过 `SettingUtility` 持久化，并在服务初始化后自动加载。

## 架构（HandlerHost 模式）

音频服务采用与框架其他服务一致的 HandlerHost 零反射架构：

- **`AudioService`**：静态外观（`[HandlerHost(typeof(AudioServiceHandler))]` + `[ServiceDependency(typeof(DebuggerService), typeof(ResourceService))]`），全部公共成员为静态方法，经 `Handler` 属性转发（fail-fast：未就绪时按需初始化，工厂缺失时抛异常，不静默降级）
- **`AudioServiceHandler`**：可序列化抽象基类（继承 `FrameworkHandler`，策略模式抽象策略），定义供外观调用的后端契约
- **`UnityAudioHandler`**：`AudioServiceHandler` 的默认实现（基于 Unity `AudioSource`/`AudioMixer`，位于 `Handler/` 目录），承载代理池管理、播放状态机、淡入淡出等核心逻辑
- **`AudioServiceSettings`**：框架设置，通过 `[ProviderDropdown]` 选择音频后端实现并配置 `AudioMixer` 与 `AudioGroupConfig[]`
- 服务注册由依赖链自动拉起，也可手动 `GameServices.RegisterService(EServiceScopeKind.App, new AudioService())`

## 核心特性

- 五条内置音轨 `EAudioTrack`：`Sfx`（常规音效）、`UI`、`Music`、`Voice`、`Ambience`，每轨独立音量/静音/暂停，命名需与 AudioMixer 分组一致
- 代理池播放：每轨按 `MaxChannel` 预建 `AudioAgent`，超出上限时可按 `CanExpand` 配置扩容，或淡出复用播放时间最久的代理
- 句柄 + 用户 ID 双重管理：`Play` 返回服务自维护句柄；播放时通过 `AudioPlayOptions.ID` 指定用户 ID，可按 ID 批量控制
- 完整过渡能力：单条音频淡入/淡出（`FadeAudio`）、音轨过渡（`FadeTrack`）、主音轨过渡（`FadeMasterTrack`），手动过渡零 GC
- Solo 独奏：`SoloSingleTrack` / `SoloAllTracks` 播放时静音同轨或全部音频，`AutoUnSoloOnEnd` 支持播完自动解除
- 3D 空间音效：位置、跟随 Transform、多普勒、衰减曲线等 `AudioSource` 参数均可在 `AudioPlayOptions` 中配置
- 持久音频：`Persistent` 选项让音频在场景切换后继续播放，其余音频在加载新场景时自动淡出停止
- 按 ID 批量操作：`ForEachHandleByID(id, handler)` / `ForEachAgentByID(id, action)` 遍历指定用户 ID 的音频，`PlayFade` / `StopFade` 直接对指定 ID 做音量过渡
- 设置统一入口：`SetSettings` / `LoadSettings` / `RemoveSetting` 一次写入/加载/移除主音轨与全部音轨设置

## 核心类型

命名空间：`Moirai.Atropos.Audio`

| 类/接口 | 说明 |
|---------|------|
| `AudioService` | 静态外观（`[HandlerHost]`）：`Play` / `Pause` / `Stop` / `FadeXxx` 等全部静态 API |
| `AudioServiceHandler` | 音频后端处理器抽象基类（继承 `FrameworkHandler`），定义外观调用的完整后端契约 |
| `UnityAudioHandler` | 默认音频后端（基于 Unity `AudioSource`/`AudioMixer`）：代理池、状态机与过渡核心逻辑 |
| `EAudioTrack` | 音轨枚举：`Sfx`、`UI`、`Music`、`Voice`、`Ambience` |
| `AudioCategory` | 音轨类别，持有 `AudioAgent` 列表，提供 `GetAvailableAgent`、`PauseAll`、`StopAll` 等 |
| `AudioAgent` | 音频代理，封装 `AudioSource`，负责加载、播放、淡入淡出与状态机（`EAudioAgentRuntimeState`） |
| `EAudioAgentRuntimeState` | 代理运行时状态：`None`、`Loading`、`FadingIn`、`Playing`、`FadingOut`、`End`、`Pausing` |
| `AudioGroupConfig` | 音轨组配置：`AudioTrack`、`AudioMixerGroup`、默认音量、`MaxChannel`、`CanExpand` 及设置读写 |
| `AudioPlayOptions` | 播放选项结构体，提供 `Default`、`Create`、`CreateLooping`、`CreateWithFade` 工厂 |
| `AudioPlayOptionsSO` | 播放选项资产（ScriptableObject），支持随机/顺序选 clip、随机音量音调、并发数限制 |
| `AudioServiceSettings` | 框架设置（`FrameworkSetting`）：`[ProviderDropdown]` 选择后端，配置 `AudioMixer` 与 `AudioGroupConfig[]` |
| `AudioAssetData` | 音频资源句柄包装（`MemoryObject`），回收时按需释放 `AssetHandle` |
| `BackgroundMusic` | 组件：物体实例化时自动播放背景音乐（同 ID 旧 BGM 自动切换） |
| `AudioSettingsWidget` | 组件：将 Slider/Toggle 绑定到主音量与各音轨设置 |

## 快速上手

```csharp
// 1. 使用 AudioClip 播放（Create 工厂预设了常用默认值）
AudioPlayOptions options = AudioPlayOptions.Create(EAudioTrack.Sfx);
ulong handle = AudioService.Play(clip, options);

// 2. 循环 BGM：从资源系统按路径加载，异步 + 缓存句柄
AudioPlayOptions bgmOptions = AudioPlayOptions.CreateLooping(EAudioTrack.Music);
ulong bgm = AudioService.Play("Assets/AssetRaw/Default/Audio/bgm_main.mp3", bgmOptions, bAsync: true, bInPool: true);

// 3. 淡入播放
ulong fadeIn = AudioService.Play(clip, AudioPlayOptions.CreateWithFade(EAudioTrack.Music, 2f));

// 4. 通过句柄控制
AudioService.Pause(handle);
AudioService.Unpause(handle);
AudioService.Stop(handle, fadeoutDuration: 0.5f);
bool playing = AudioService.IsPlaying(handle);
AudioAgent agent = AudioService.GetAgentByHandle(handle); // 访问内部 AudioSource 等

// 5. 音轨音量与静音（会写入 AudioMixer 暴露参数并持久化）
AudioService.SetTrackVolume(EAudioTrack.Music, 0.8f);
AudioService.SetTrackMute(EAudioTrack.Sfx, true);

// 6. 音量过渡
AudioService.FadeAudio(bgm, 2f, 1f, 0.3f, default);           // 单条音频 1 -> 0.3
AudioService.FadeTrack(EAudioTrack.Music, 2f, 1f, 0.5f);       // 整条音轨
AudioService.FadeMasterTrack(1.5f, 1f, 0.8f);                  // 主音轨
```

## 进阶用法

### 按 ID 控制与批量操作

播放时通过长参数重载指定用户 ID（`AudioPlayOptions.ID` 的 setter 为 internal，需走长重载），之后无需保存句柄即可遍历同一 ID 的所有实例进行批量操作：

```csharp
// 播放（返回 ulong 句柄），指定用户 ID
ulong voice = AudioService.Play(clip, EAudioTrack.Voice, Vector3.zero, loop: true, id: 33);

// 遍历指定 ID 的句柄 / 代理
AudioService.ForEachHandleByID(33, handle => AudioService.Pause(handle));   // 暂停所有 ID 为 33 的音频
AudioService.ForEachHandleByID(33, handle => AudioService.Stop(handle));    // 停止
AudioService.ForEachAgentByID(33, agent => { /* 访问代理内部状态 */ });
AudioService.PlayFade(33, 2f, 0.3f);          // 2 秒内过渡到 0.3 音量
AudioService.StopFade(33);                     // 停止 ID 33 的音量过渡

// 音轨级控制
AudioService.PauseTrack(EAudioTrack.UI);
AudioService.SetTrackVolume(EAudioTrack.Music, 0.5f);
AudioService.MasterMute = true;                // 主音轨静音

// 全局控制
AudioService.StopAll();
AudioService.StopAllButPersistent();           // 停止除 Persistent 外的所有音频

// 设置持久化（写入 / 加载 / 移除，需保存时调用 SettingUtility.Save）
AudioService.SetSettings();
AudioService.LoadSettings();
AudioService.RemoveSetting();
```

### 长参数重载与查找

`Play` 提供展开全部参数的长重载（clip 与 path 两个版本），便于一次性配置 3D 音频：

```csharp
ulong h = AudioService.Play(clip, EAudioTrack.Sfx, position,
    volume: 0.9f, spatialBlend: 1f, rolloffMode: AudioRolloffMode.Linear,
    minDistance: 2f, maxDistance: 60f, attachToTransform: enemy.transform);

// 查询
AudioService.ForEachAgentByID(33, agent => { /* 遍历播放过 ID 33 的代理 */ });
AudioService.ForEachHandleByID(33, handle => { /* 遍历 ID 33 的句柄 */ });
int count = AudioService.CurrentlyPlayingCount(clip);
```

### 播放选项资产

创建 `Moirai Framework/Audio/Play Options SO` 资产，可配置随机音频数组（随机/顺序/不重复模式）、随机音量音调区间、同 clip 并发上限等，运行时调用其 `Play(Vector3 location)` 即可：

```csharp
[SerializeField] private AudioPlayOptionsSO shootSfx;
void OnShoot() => shootSfx.Play(muzzle.position);
```

### 音频资源池

频繁加载的 clip 可预载到句柄池，播放时配合 `bInPool: true` 复用：

```csharp
AudioService.PutInAudioPool(new List<string> { "Assets/.../hit.mp3" });
AudioService.RemoveClipFromPool(new List<string> { "Assets/.../hit.mp3" });
AudioService.CleanAudioPool();
```

## 配置说明

- `AudioServiceSettings`（菜单中的「音频设置」）配置 `AudioMixer` 与各音轨的 `AudioGroupConfig`；未配置时代码会从 `Resources/AudioMixer` 兜底读取 `Master/` 下分组并按分组名匹配 `EAudioTrack`
- AudioMixer 分组需暴露名为 `{分组名}Volume` 的音量参数（如 `MusicVolume`），服务以对数换写该参数实现音轨音量
- `AudioGroupConfig.MixerValuesMultiplier`（默认 20）为归一化音量到分贝的转换系数
- 也可在初始化时显式传入：`AudioService.Initialize(instanceRoot, audioMixer, audioGroupConfigs)`

## 注意事项

- `Play` 返回 `0UL` 表示播放失败（无可用代理、音轨未配置或编辑器禁用了音频）
- `DoNotAutoRecycleIfNotDonePlaying` 为 `false` 时（`new AudioPlayOptions` 的默认值），超过最大发声数会淡出打断播放最久的音频；`Default` 与 `Create` 系列工厂默认为 `true`
- `ForEachAgentByID` / `ForEachHandleByID` 为回调式遍历（零分配），回调在遍历期间同步执行，勿在其中修改枚举结构
- 加载新场景时服务会自动 `StopAllButPersistent`，需要跨场景的音频设置 `Persistent = true`
- 编辑器下服务会在根节点挂载 `AudioDebugger` 供 Inspector 调试；编辑器禁用音频（`unityAudioDisabled`）时所有接口静默失效
- 主音量经 `AudioListener.volume` 生效，音轨音量经 AudioMixer 参数生效，两者机制不同

---
[« 返回主 README](../../README.md)
