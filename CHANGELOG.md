# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### `Audio`

- 中间件接入面：clip → 事件的映射表（事件路径由配置给出，不再从 `clip.name` 推导）、声音库加载结果三态 `EAudioBankLoadResult`、实时参数绑定。
- Clip 租约缓存 `AudioClipCache`：按地址播放经窄接缝 `IAudioClipLeaseSource` 取还租约，策略 `EAudioCachePolicy`（`Default` / `None` / `Ttl` / `Pin`），加载失败带负冷却，留池视图 `PoolReadOnly` 只读。
- Voice 驱动的自动 Ducking `AudioVoiceDucking`：活跃声部数由各后端实算。
- 通道扩展上限由写死常量改为按轨可配的 `MaxChannelCeiling`。
- 中间件失败可归因：声音库加载失败与事件播不出各报一条可定位告警。
- 上线收口四项：切后台冻结（`AudioServiceHandler.OnApplicationPaused`）、主线程不变量 `AudioMainThread`、`AudioService.Tick` 分段故障隔离、缺 `AudioMixer` / `AudioMixerSnapshot` 的启动期校验。
- 游戏内调试器 `Profiler/Audio` 的「Clip 缓存」段：条目/容量、在途、常驻、失败冷却、TTL 与默认策略、留池可见数、混音快照与 Ducking 占用。

#### `Localization`

- 首启语言兜底：检测链给出的语言没随这批词条发行时按语言表首项落。
- 常驻规模以 `ResidentChars` 可观测。
- 运行时覆盖层 `SetStringOverlay`（按来源摘除）：不改表、不重出包就能换掉某语言的若干词条。
- 配置表后端可选接缝 `SupportsPerLanguageLocalizationLoad` / `GetLocalizedStringsByLanguage`：按语言单独取一列词条。
- 句柄式语言变更订阅、不装箱取文、单趟取值 `TryGetTextFromId`（外观 + 处理器）：资源模式本地化器的「有则注、无则报」判断不再 `Has` + `GetTextFromId` 双查；文本展示仍走 `GetTextFromId` 的缺译即露 key。
- 预览收成单一入口 `ResolvePreviewText`（状态 `EPreviewResolveStatus`）：分得开「表内无此 ID / 该语言留空 / 数据未就绪」，取不到时给 `null` 而不是 key。
- 组件预览报到资产一层：非播放态把资源模式的 location 解析成资产，播放态改读服务已解析的真值；类型判据收成接缝 `IInjectorAssetPreview`。

#### `Resource`

- 空闲资源记录容量上限 `IdleAssetCapacity`（默认 256）。
- 销毁态槽位兜底回收：`ResourceOwner` 的注销不再全押在 `OnDestroy` 上，每帧预算化轮扫补上场景卸载与关停路径，预算取 `ResourceServiceSettings.DestroySweepBudget`（默认 64）。
- 编辑器预览取资产 `ResourceService.LoadAssetForEditor`：非播放态唯一的「地址 → 资产」入口，直读 `AssetDatabase`、不建租约，后端可覆写 `ResourceServiceHandler.LoadAssetForEditor`。
- 配置自检判据 + 构建期复用同一份判据的门禁 `ResourceSettingsBuildValidator`：构建期默认只告警，设 `MOIRAI_RESOURCE_SETTINGS_STRICT=1` 才拦停。

#### `Kernel` 与工具面

- 统一随机源 `RandomSource` / `RandomUtility`（可复现，不再全类共享一个 `System.Random`）。
- 洗牌原语 `ShuffleUtility` 与轮次策略 `ShuffleBag<T>`（音频侧 `ShuffleIndexBag` 收敛为它的适配器）。
- `MathsUtility` 的 `RandomPointInsideUnitCircle` / `RandomPointOnUnitSphere` / `RandomPointInsideUnitSphere`。
- `MemoryPoolInfo.MaxUsingCount` 与结构自检，使"漏还"在数据上看得见。

#### 测试与门禁

- `HandlerHost` 生成器多发无损换入接缝 `Internal_PeekHandler()` / `Internal_UseHandler(next)`：测试换入换出处理器不再反射私有字段。
- 编辑器运行环路：`Tests/EditorMode/EditorStateBridge.cs` 每 ~1s 把编辑器状态落盘并收 `focus` / `refresh` / `recompile` 命令，`Tests/EditorMode/TestRequestRunner.cs` 让开着的编辑器自己跑 Test Runner。
- 玩家专用测试程序集 `Moirai.Atropos.Tests.Player` 承载热路径 0-GC 验收（音频、资源绑定层）；托管分配计数器在编辑器 Mono 下不推进，这几格不进编辑器套件。
- 测试规范 `Documentation~/zh|en/Testing.md`：分层归属（L1 EditMode 契约 / L2 PlayMode 集成 / L3 Player 验收 / L4 基准）、用例与夹具规范、确定性纪律、反射与日志断言政策、覆盖率阈值、发布出口五门、运行通道；`CLAUDE.md` 收两节执行摘要。
- 反射策略守卫 `ReflectionPolicyGuardTests`：允许使用非公开反射的文件钉成白名单并**双向**断言。
- 覆盖率工具与门禁：`com.unity.testtools.codecoverage` 1.3.0，`coverage-gate.ps1` 按分级阈值判定。
- PR 门禁三条（均仅 `pull_request`）：`tests.yaml`（EditMode + PlayMode + 玩家程序集形状校验）、`coverage.yaml`、`metas.yaml`。
- 回归补齐：配置表契约 `ConfigTableServiceContractTests`、真实设置资产门禁 `ResourceSettingsAssetRegressionTests`（钉住 `[SerializeReference]` 后端静默失效）、内存池夹具基座、对象池异常路径、时间轮时钟污染、后端接缝形状基线、空闲容量淘汰现状、Clip 缓存热路径 CPU 基准 `AudioCacheBenchmark`。

### Changed

#### `ConfigTable`

- ⚠ 转表入口只有一个 `gen.sh`（`gen.bat` 只是 Windows 下找到 bash 的启动器）；目标由参数选（无参数 = 客户端，另有 `server` / `all`）。
- ⚠ 转表配置是单文件 `config.ini`（分节 + 正斜杠路径），键名全局唯一；「更新配置路径」按这些键改写路径。
- ⚠ 生成的 `Tables` 缺省是懒加载：构造期不取数，每张表首次访问才装载。靠覆盖 `tables.sbn` 实现，按 code target 各一份（`Templates/Client_LazyLoad/<codeTarget>/`）。
- 两条生成路线：`bin`（`cs-bin`+`bin`）与 `json`（`cs-simple-json`+`json`）；缺省值在 `config.ini`（`DATA_FORMAT` / `LAZY_LOAD`），`--format=` / `--load=` 只做当次覆盖。
- 生成码样式与框架一致：无 file header 前导空行、私有字段 `_小驼峰`、注释与 bean comment 为中文。

#### `Resource`

- ⚠ **包初始化 API 按真实语义改名**：`InitPackage` → `InitializePackageAsync`（原语，返回 `ResourcePackageInitResult`，`needInitManifest` 可选拉清单），`InitPackageAsync` → `TryInitializePackageAsync`（在其上写远程地址并收成 `bool`）。旧名直接替换、不挂 `[Obsolete]`，模板侧唯一调用点 `ProcedureInitPackage` 已跟上。
- ⚠ **包管理 API 名实一致**：`RequestPackageVersionAsync` → `RequestPackageVersion`、`ClearCacheAsync` → `StartClearCache`（二者同步返回结果结构体、`Operation` 字段供轮询，不是 `UniTask`）；YooAsset 自身的 `package.*Async` 未动。
- ⚠ **后端接缝对程序集外敞开**：`EResourceLeaseOption` 由 internal 升 public，`internal abstract` 成员清零、租约取用族与维护族升为 `public abstract`，程序集外后端第一次能真正派生落地。
- ⚠ 每帧维护入口 `ProcessKeepAlive` 更名 `ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)`；到期与销毁两条预算刻意不合并，`ProcessDestroyedObjects` 删除默认参。
- 热路径 key 一次打包、按 key 直查（租约族与子资源族各一对入口），跳过三条名称轴的字典往返；loadingKey 与 recordKey 分账不混用。
- 加载去重等待改完成源一次唤醒（`LoadingOperationState.WaitAsync` + `Preserve`），不再轮询 `IsDone`。
- `GC.Collect` 改 `GCCollectionMode.Optimized`（时序仍在 `UnloadUnusedAssets` 完成之后）；卸载/GC 日志降 `Verbose`。
- 空闲容量淘汰候选表改按 `IdleExpireTick` 的最小堆，淘汰 O(log n)。
- 两座时间轮（KeepAlive / Idle）合并为一套桶算法（`ScheduleOnWheel` / `RemoveFromWheel` / `ProcessDueWheelBuckets`），仅队列种类与过期刻度不同。
- 契约锁：Handler 上返回 `IResourceOperation` 的方法冻结为 `LoadPackageManifestAsync`，新成员一律 `UniTask`。
- 绑定 cache-only：`TryAcquireBindingCached` / `TryBindSpriteCached` / `Image.TrySetSprite`，未命中不进后端加载。
- 绑定服务只握 internal `IResourceLeaseSource`，不再拿后端全契约；后端与绑定服务因此能各自构造、各自被测。
- packed key 三条名称轴合成一份 `ResourceNameRegistry`，登记与回收只剩一条路径；`Release` 与 `DecrementOnly` 刻意分开。
- 记录槽与后端断开第一根线：`AssetSlot` 两个具名句柄字段合成 `object RawHandle`（引用类型不装箱），`Records` / `Keys` / `Expiry` 已无后端类型名。
- 记账内核成形为 `ResourceRecordStore`（`Runtime/Services/Resource/Kernel/`，由后端持有）：记录槽、租约、索引表、在途去重与两座时间轮整体搬出 `YooAssetHandler`，两侧只剩一面 `IResourceRecordHost`。
- 缓存命中不再为"已经完成的结果"造异步状态机，命中路径直接 `UniTask.FromResult`。
- `ResourceBindingService.Shutdown` 拆为终态关停与可复用重置。
- 外观读写成员统一走 `RequireHandler()`：未就绪不再伪装成"资源不存在"，也不再静默丢写。
- 配置自检判据从 `ResourceService` 搬进 `ResourceServiceSettings`（`GetConfigurationIssues` / `ReportConfigurationIssues`）；轮盘上限由写死的 255 改读 `ResourceRecordStore.IdleWheelSpanSeconds`。
- Addressables 后端接上同一套 `ResourceRecordStore`：异步租约 / 绑定 / 预制体实例化 / 图集子精灵 / 场景加载 / 缓存维护不再抛错，两后端共用记账与过期。剩余缺口是能力差：无同步加载与两步式下载族（统一 fail-fast）、`IsNeedDownloadFromRemote` / `GetAssetInfo` / 按标签 `GetAssetInfos` 退化为恒定值、`HasAsset` 分不清"已缓存"与"待下载"故 `AssetOnline` 永不出现。该层仍留在主程序集按文件级 `#if ADDRESSABLES_INSTALLED` 剔除。

#### `Audio`

- ⚠ **音量值域统一为线性 0..1**（主音量与音轨音量）：Unity 侧原允许 0..10，中间件侧却在落总线前 `Clamp01`，换后端就把上限从 10 变成 1。现契约写 0..1（`AudioGroupConfig.MAXIMAL_VOLUME` 为 1），夹取只在契约入口发生一次。**迁移**：工程内音轨 `m_DefaultVolume` 实测均为 1 不受影响，持久化过的 >1 旧值在 `LoadSettings` 读回时夹到 1。
- 后端整体失效（`IsBackendInert`）时音量面统一为读 0、写无效。
- 总线过渡族（`FadeMasterTrack` / `StopFade` 等）与音轨暂停标记上移到契约基类。
- 桥侧 `LoadBank` 由 `bool` 改三态。
- `AudioHandleRegistry` 去掉全部字典，句柄改打包值（代次 + 槽号）。
- Clip 缓存的寻址表换定长开址槽表。

#### `Kernel` 与工具面

- ⚠ 运行期随机全面改走 `RandomUtility`，`UnityEngine.Random` 退出 `Runtime`。
- ⚠ `AlgorithmUtility.RandomRange(long, long)` 的上界口径由"含"改"不含"。

#### `Localization`

- 词条交付改由「批」自带语言头，存储与解析搬进 `LocalizationStore`。
- 全局语言注册表删除，可用语言随表自报（`ConfigTableService.GetLocalizationLanguageCodes`）。
- 配置表数据源在自报支持按语言取列的后端下改走按语言列模式：常驻与取值都只有语言头 + 当前语言列。
- 语言切换的事件时序与查询热路径一并收口：注册去重改并行 `HashSet`，重注入与变更广播的遍历快照改 `ArrayPool` 租用。
- ⚠ 资源注入器按载荷类型唯一派发（`string` = 资源 location / `int` = 语言下标 / `AudioClip` = 直注），不在注入器内二次解析；构造不再收文本 ID，`SetLocalizedId` 删除（旧「int+资源模式 / 空载荷+资源模式」调用形态迁移为传入 `TryGetTextFromId` 解析出的 location）。

#### 池与内存

- `MemoryPool<T>` 的页调度改侵入式双向链表。
- 池维护调度改"采集 / 派发"两段式。
- 主线程守卫不再随正式构建整条消失，池维护异常按房内 `RETHROW_*` 约定分级。
- `TickAll` 在每帧边界自收口并带单轮异常采集上限，单条异常不再连带冻住同帧的输入、UI 与存档。

#### `GameApp` 事件

- `GameAppMessageEvent` 的专用事件枚举内聚进 `EEventType`。

#### 测试 [Testing]

- 26 个遗留 `*Test.cs` 统一为 `*Tests.cs`（类名同步，`.meta` GUID 不变），`TweenTest.Easing.cs` 更名 `TweenEaseTests.Easing.cs`，`GameDictionaryTests` 拆出 `GameSortedDictionaryTests`。
- 测试不再靠字段反射读写状态，改走 `internal` 直接访问；为此放宽的成员覆盖 `ObjectBase`、`SaveServiceSettings`、`AudioGroupConfig`、`AudioServiceSettings`、`AudioEmitter`、`BgmPlaylist`、`UnityAudioHandler`、`InputButton` / `InputAxes`、`PreventInputOnEnable`、`SingletonMono` 与 `MiddlewareAudioHandler`。
- UTF 预期判定内聚到 `Tests/EditorMode/Support/UtfLogExpect.cs`，`UNITY_LOGGING_INSTALLED` 版本宏只这一处依赖。

### Fixed

#### `ConfigTable`

- 编辑器「更新配置路径」原本指向两个不存在的文件，点击只在控制台留一行警告；现在确实改写 `config.ini` 与 `Templates/LubanHandler_Init.cs`。

#### `Resource`

- 同步加载同 key 在途时会另开第二次后端加载：双句柄双计引用，后完成的赢家还会把先落地的句柄 Dispose 掉。现只接力赢家已落地的那条记录；仍在途则 fail-fast 返回空。
- `GetAssetInfos(tag/tags)` 在 handler 未就绪时返回 `null` 而非空数组，调用方 `foreach` 直接 NRE；读降级口径与 `HasAsset→NotExist` 对齐。
- `[SerializeReference]` 后端解析为 null 时静默换 `CreateDefaultHandler()`，构建里完全看不见；现打一条 Error 留下痕迹。
- Addressables 后端的 `BindingOwnerCapacity` / `BindingSlotCapacity` setter 不触发 Warmup（Yoo 侧会），绑定槽位预热静默 no-op；补齐字段夹取与 `WarmupBindingRecords`。
- 玩家构建里 `EditorSimulate` 的入库设置退回离线模式，从整片沉默改为启动时报一次 Error；三项"配了但永不参与决策"的设置同样各报一次。
- 发起即忘的绑定把抛出整个吞掉（扩展层 7 处 `Binding….Async().Forget()`），失败原因现进日志。
- 销毁态轮转的扫描被组件清理的抛出截断，租约永久泄漏且每帧重复抛异常：改为先记账后清理。
- 异步绑定在"取用租约"一步抛出时把预约位永久留在表里，现已回收。
- 调用方取消不再冒充"加载失败"：`EResourceBindStatus.Cancelled` 与加载失败分道，上层能判要不要重试。
- 子资源（图集）绑定绕开加载去重与卸载代次。
- `WaitForLoadingAsync` 的等待者计数不归还、失败原因被丢弃。
- `LoadGameObject` / `LoadGameObjectAsync` 不防实例化期间的回收与关停。
- `UnloadUnusedAssets` / `ForceUnloadAllAssets` 不校验包是否仍有有效清单。
- YooAsset 后端实例状态与 `YooAssets` 静态表不同一条命：同域重启时 `Initialize()` 直接抛 `YooAssets is already initialized`，`PackageMap` 里的包全是孤儿。现 `Initialize()` 先走 `ResetReloadUnsafeState()`。
- Addressables 后端把 `Application.lowMemory` 这条链整个吞掉（`SetForceUnloadUnusedAssetsAction` 丢委托、`OnLowMemory` 空方法体）：现按 YooAsset 形状接上，强制档走记录释放。
- 精灵绑定族把资源包写死成空串（材质族早已透传），DLC 包里的精灵绑不上：`SetSprite` / `SetSubSprite` 全部重载补末位可选 `packageName`。
- `RemoteService.GetRemoteUrls` 把内部字段数组直接交给调用方。
- `ResourceOwner.ReleaseBindingsInHierarchy` 的共用缓冲被嵌套释放踩掉。

#### `Audio`

- `CurrentlyPlayingCount` 绕过事件映射表，命中映射的 clip 恒查到 0。
- 中间件后端初始化失败后仍在每帧驱动那个没起来的引擎。
- `AudioWarnOnce` 的带参告警整条打成 `System.Object[]`（`LogUtility` 缺格式化重载）。
- 留池视图每次刷新重新装箱一次租约。
- 暂停会丢弃进行中的音量斜坡。
- `AudioGroupConfig.RemoveSetting` 的回落音量写死 1。
- 同步加载不作废在途异步续体（`_loadGeneration` 原先只在异步分支自增）。
- 上线门禁收口：`BgmPlaylist` 的分层 ID 写死、四处 `StopByID` 误停他轨等。
- `BgmPlaylist` 的 Shuffle 收尾误用顺序下标回绕，随机落到末位时只播一首就 `Stop()`；现走洗牌袋。
- `MiddlewareAudioHandler.Restart` 归还声部后立刻清空声部池，热复用全部白做；现只在终态关停时丢弃。
- FMOD 短名 `LoadBank(..., loadSamples: false)` 让首播因样本未载入而无声、失败时还伪造 `ERR_EVENT_NOTFOUND`；改 `loadSamples: true`。

#### `Localization`

- 非播放态的 Inspector 预览取不到表：读的是运行期查询入口，`s_Handler` 未注册时恒为空；现未注册时经 Settings 里配置的处理器取数。
- 数据源抛异常被"先生成配置"顶替，读表缺陷被念成配表缺失。
- 首次查询把 key 当译文返回。
- 检测语言未发行时整套界面露 key。
- 占位符写坏把查询抛出。
- `TextLocalizer` 把空白格/缺译打成「Text ID 不可用」拒注，旧文案留在界面上；现按「缺译即露 key」注入 ID 原文，漏翻在界面可见。

#### 池与内存

- 池的关停、维护、逐项回调与整批修剪改为逐项隔离：单个坏回调不再截断整批，也不再吃掉同一实例上其余池件的 `OnPooledDestroy`。
- 池回调内可重入全局维护会换掉正在被引用的非托管页数组，现已封住。
- 关停重入导致槽位存储二次归还，现已封住。
- `MemoryPool<T>.TickAll` 的交换移除会挪错槽位。
- 构造函数抛出留下幽灵 in-use 计数。
- `Remove` / `RemoveFromType` 传 0 或负数会让池反向增长。
- `LowMemory` 阶段不清空闲储备。
- 补做延迟 Native 释放时留悬空空闲槽。
- `MemoryPoolHandle` 的池标识是死码。
- 配置过期时间的池把预热对象当"无限空闲"首轮剪光。
- `OnDespawn` 内销毁实例会抹平 inactive 链的头尾指针。
- `Spawn<T>` / `SpawnAsync<T>` 取不到组件时把实例丢在场景里。
- `Despawn(T obj)` 只认目标键不认对象。
- `PoolCatalog` 的规则次序随构建漂移（`Array.Sort` 不稳定）。

#### `Timer`

- 时间轮被污染的时间输入（NaN / Infinity / 溢出）打穿并永久冻结，恢复与重启路径的取值校验补齐。
- 帧计时器在进度回调后沿用回调前的剩余帧数，并把完成回调打到复用槽位的新计时器上。
- 两条泳道回收 executing 标记的形式统一。

#### 其余

- 事件派发链缺 `finally`：订户异常截断整条派发。
- `AlgorithmUtility` 随机面与 `MathsUtility` 概率取值四处静默缺陷：`RandomRange(length, min, max)` 的 `length = 1` 空转与 `length >= 10` 抛 `FormatException`、`RandomRange(min, max)` 每次 `new Random(Guid)` 且 `min == max` 抛 `ArgumentNullException`、`AverageRandom` 第 5 位小数恒为 0 且溢出时静默取乱数、`Chance(percent)` 的 `<=` 让 0% 也有 1% 成功率。
- `ShuffleBag<T>` 三处：换手必连点、本轮中途 `Add` 倒拨轮次、空袋取用越界。
- Serilog 后端把日志上下文整个丢掉（`LogHandler.Log` 的 `context` 参数只有一个后端透传）。
- 缓存窗口重开后被上一轮的关闭动画重新隐藏（`_isCreate` 保持 `true`）。

### Removed

#### `Localization`

- ⚠ 第三方遗留 `GoogleTranslator` / `GoogleTranslateRequest` / `GoogleTranslateResponse` 整族删除（全仓库零消费；同步版忙等、异步版走协程、API Key 经 `TextAsset` 随包分发）。

#### `Resource`

- ⚠ `[Obsolete]` 遗留加载族整族删除，连带它下面那条无法补救的引用计数轴。一次性加载走 `ResourceService` 现役成员，带生命周期的取用走 `ResourceBindingService` 与绑定扩展。
- 死码：`RegisteredTarget` 整套子系统、两个恒零的数据维度、`TryAcquireDirect`。

#### `Runtime/Core/Utilities`

- 上线前精简：五个整类与一批零引用成员约 1.2k 行（`TimeUtility` / `NetUtility` / `XmlUtility` / `ProgramUtility` 等），`AlgorithmUtility` 的正态分布两件套随之删除。

#### `Timer`

- `TimerServiceBenchmark` 移出 `Runtime`，落 `Tests/EditorMode/Service/Timer/`，并标注为非自动基准（不参与测试套件）。

#### 池与内存

- ⚠ `MemoryPoolRegistry.OnPoolStatsUpdated` 整条推送面删除（含每帧边界的 `FirePoolStatsUpdated` 与 `s_StatsBuffer`）：全工程零订阅者。池统计走拉取路径 `MemoryPool.GetAllMemoryPoolInfos`。
