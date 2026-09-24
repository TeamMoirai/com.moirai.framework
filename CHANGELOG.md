# Changelog

本项目的所有重要变更都会记录在此文件中。

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件按**后覆盖**维护，且**只有 `[Unreleased]` 一段**：这里只记尚未发布的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases)，文件本身清空重写，不留已发布的 release notes。被后续变更推翻的中间态（加了又删的开关、改到一半的命名、逐轮刷新的测试格数与成员计数、当时判为"不采纳"的观察）也不留条目——同一件事被推翻时改掉或删掉原条目，不要再追加一条把它推翻。改动为什么这样做的完整推演在 commit message 里。

标记 ⚠ 的是破坏性变更。

排版约定：`###` 是变更类型，段内用 `####` 按模块分组；一条只说一个事实，一条里的分号与句号就是拆分线；同一主题的多个事实用缩进子条并列，不把它们挤进一句。

## [Unreleased]

1.1.0 之后的全部变更。

### Added

#### `Audio`

- 中间件接入面：
  - clip → 事件的映射表：事件路径由配置给出，不再从 `clip.name` 推导 FMOD 的 `event:/` 与 Wwise 的 `wwise:/` 前缀。
  - 声音库加载结果三态 `EAudioBankLoadResult`。
  - 实时参数绑定。
- 通道扩展上限由写死常量改为按轨可配的 `MaxChannelCeiling`。
- Clip 租约缓存 `AudioClipCache`：
  - 按地址播放经窄接缝 `IAudioClipLeaseSource` 取还资源租约。
  - 策略 `EAudioCachePolicy`（`Default` / `None` / `Ttl` / `Pin`）。
  - 加载失败带负冷却。
  - 留池视图 `PoolReadOnly` 是只读投影。
- Voice 驱动的自动 Ducking `AudioVoiceDucking`：各后端实算音轨上的活跃声部数，不再有"播放 +1 / 结束 -1"漏减之后混音被永久压低。
- 中间件失败可归因：声音库加载失败与事件播不出各报一条可定位告警，不再静默返回 `false` / `0` 句柄。
- 上线收口四项：切后台冻结（`AudioServiceHandler.OnApplicationPaused`）、主线程不变量 `AudioMainThread`、`AudioService.Tick` 分段故障隔离、缺 `AudioMixer` / `AudioMixerSnapshot` 的启动期校验。
- 游戏内调试器 `Profiler/Audio` 的「Clip 缓存」段：条目/容量、在途、常驻、失败冷却数、TTL 与默认策略、留池可见数、当前混音快照与 Ducking 占用。

#### `Localization`

- 缺译回退链 `FallbackLanguageCodes` 与首启语言兜底：当前语言该列留空不再把 `UI.Shop.Title` 这样的 key 直接印到界面上。
- 常驻规模以 `ResidentChars` 可观测。
- 运行时覆盖层 `SetStringOverlay`（按来源摘除）：不改表、不重出包就能换掉某语言的若干词条。
- 句柄式语言变更订阅、不装箱取文、编辑器内预览。

#### `Resource`

- 同步加载同 key 在途时只接力赢家已落地的那条记录，绝不另开第二次后端加载（双句柄会双计引用，且后完成的赢家会把先落地的句柄 Dispose 掉）；仍在途则 fail-fast 返回空，调用方改用异步 API。
- 租约热路径 key 一次打包、按 key 直查：`GetOrCreateAssetRecordByKey` / `TryGetCachedAssetRecordByKey` 跳过三条名称轴字典往返；`AcquireDirect` / 同步异步加载共用同一 packed key。
- 后端接缝 `internal abstract` 清零：租约取用族与维护族升为 `public abstract`，`EResourceLeaseOption` 随之公开；程序集外后端可派生实现，`ResourceSeamShapeGuardTests` 基线 11→0。
- 空闲资源记录容量上限 `IdleAssetCapacity`（默认 256），与 `IdleAssetExpireTime` 一起挡住长时间运行下的记录堆积。
- 销毁态槽位兜底回收：`ResourceOwner` 的注销原本全押在 `OnDestroy` 上，场景卸载与关停路径上的槽位会永久占住租约。
  - 每帧查验数量由 `ResourceServiceSettings.DestroySweepBudget`（默认 64）给出。
- 配置自检判据表（7 条）+ 构建期复用同一份判据的门禁：
  - 启动期由 `ResourceService.OnInit` 打一次 Warning；构建期由 Editor 侧 `ResourceSettingsBuildValidator`（`IPreprocessBuildWithReport`）原样再走一遍。
  - 构建期默认也只告警，设 `MOIRAI_RESOURCE_SETTINGS_STRICT=1` 才把构建拦停——本包被他人消费，因一项配置拦停别人的构建是工单不是提醒。
  - 判据从 3 条扩到 7 条，新增每帧过期预算非正数（时间轮不推进）、卸载上限非正数（每帧卸载）、下限高于上限（预约档形同废弃）、GC 节流为负（每次请求都真收）。
  - 政策仍是**只报不改**：夹取会把配置错误洗成"看起来本来就对"的值，与 `PlayMode` 读取只归一返回值、不回写资产同一条取向。

#### `Kernel` 与工具面

- 统一随机源 `RandomSource` / `RandomUtility`（可复现，不再全类共享一个 `System.Random`）。
- 洗牌原语 `ShuffleUtility`、轮次策略 `ShuffleBag<T>`（音频侧 `ShuffleIndexBag` 收敛为它的适配器）。
- `MathsUtility` 的 `RandomPointInsideUnitCircle` / `RandomPointOnUnitSphere` / `RandomPointInsideUnitSphere`。
- `MemoryPoolInfo.MaxUsingCount` 与结构自检，使"漏还"在数据上看得见。

#### 接缝与测试基座

- `HandlerHost` 生成器多发无损换入接缝 `Internal_PeekHandler()` / `Internal_UseHandler(next)`：测试换入换出处理器不再反射私有字段，框架成员也不为此放宽访问级别。
- `Tests/EditorMode/TestRequestRunner.cs` 让开着的编辑器自己跑 Test Runner（跨域重载续跑、请求先改名后读取、作业句柄判活与取消）。
- `Tests/EditorMode/EditorStateBridge.cs` 把编辑器状态每 ~1s 落盘 `Temp/MoiraiEditorState.json`（心跳、域重载序号、编译/导入/播放态、前台与否、脏场景数、Console 错误数、四份产物的 mtime），并收 `Temp/MoiraiEditorCommand.json` 的 `focus` / `refresh` / `recompile`：让"要不要重编、域新不新"由状态判定，而不是喊人按 `Ctrl+R`。
- 玩家专用测试程序集 `Moirai.Atropos.Tests.Player` 承载热路径 0-GC 验收：音频之后，绑定层也搬了进去（`ResourceBindingAllocationTests` 4 格：稳态重绑、空闲轮转扫描、诊断读表、注销+重登记往返）。
  - 编辑器套件里不再出现这几格：托管分配计数器在编辑器 Mono 下不推进，零分配断言在那里无条件成立，加它等于往套件里放一个恒绿的假阳性。
- 真实设置资产的回归门禁 `ResourceSettingsAssetRegressionTests`（3 格）：读的是工程里那份 `ResourceServiceSettings.asset`，不是测试自造的实例。
  - `[SerializeReference]` 的后端引用一旦静默失效（改名、挪程序集、类型下线），Unity 不报错、只读成 null，服务照样初始化成功——这是这条失效路径上唯一的自动防线。
  - 另两格钉"按路径读到的那份就是 `Instance`"与"每个公开读数都等于资产里那个字段"：属性层加过夹取、换过默认值或忘了转发都会在这里露出来。
- 回归补齐：
  - 内存池夹具基座
  - 对象池异常路径
  - 时间轮时钟污染（`WheelTimerClockPoisonTests`）
  - 后端接缝形状基线（`ResourceSeamShapeGuardTests`）
  - 空闲容量淘汰的现状（`ResourceRecordStoreIdleTrimTests`）：受害者按空闲过期刻度从旧到新挑、每趟不超预算、超限没摘完时请求位留回下一帧、被租约握着的记录不参与。
    - 这三格只带一面假 `IResourceRecordHost` 就能直接驱动内核——记账从 `YooAssetHandler` 抽出来之后第一次成立，此前这套锁在"不初始化 YooAsset 就进不去"的位置上。
- Clip 缓存热路径的 CPU 预算基准 `AudioCacheBenchmark`（3 格 `[Explicit]`，与 `KernelBenchmark` 同一范式，量的是单次调用的纳秒数而不是条目数）。

### Changed

#### `Resource`

- ⚠ **包初始化 API 按真实语义改名**：`InitPackage` → `InitializePackageAsync`（原语，返回 `ResourcePackageInitResult`，`needInitManifest` 可选拉清单）；`InitPackageAsync` → `TryInitializePackageAsync`（在前者之上写远程地址并收成 `bool`，不更新清单）。旧名直接替换、不挂 `[Obsolete]`（接缝守卫钉死 `[Obsolete]=0`）；模板侧唯一调用点 `ProcedureInitPackage` 已跟上。
- 加载去重等待改完成源一次唤醒（`LoadingOperationState.WaitAsync` + `Preserve`），不再 `while (!IsDone) await UniTask.Yield()` 空转。
- `GC.Collect` 改 `GCCollectionMode.Optimized`（时序仍在 `UnloadUnusedAssets` 完成之后）；卸载/GC 日志降 `Verbose`。
- ⚠ **`EResourceLeaseOption` 从 internal 升为 public**：租约取用族签名要在程序集外被后端实现，参数类型不得再低于方法可见性。
- 后端接缝 11 个 `internal abstract` 成员升为 `public abstract`（含 `AcquireBinding*` / `AcquireSubAssetsBindingAsync` / `AcquirePrefabSourceLease*` / `TryGetSubSpriteAsset` / `TryGetLeaseAssetId` / `SetLeaseOptions` / `ProcessResourceMaintenance` / `ReleaseAllUnusedAssetRecords` / `ForceReleaseAllAssetRecords`）：程序集外派生类第一次能真正落地后端。

#### `Audio`

- ⚠ **音量值域统一为线性 0..1**（主音量与音轨音量）：
  - Unity 侧原先允许 0..10（写 Mixer 时按 `log10(v) * MixerValuesMultiplier` 换算），中间件侧存同样的 0..10 却在落总线前 `Mathf.Clamp01`——同一份 `AudioServiceSettings` 换后端就把上限从 10 变成 1。
  - 现在契约写成 0..1，`AudioGroupConfig.MAXIMAL_VOLUME` 为 1，夹取只在契约入口发生一次。
  - **迁移**：工程内音轨的 `m_DefaultVolume` 实测均为 1，不受影响；持久化过的 >1 旧值在 `LoadSettings` 读回时夹到 1。
- 跨后端契约第 6 条：后端整体失效（`IsBackendInert`）时音量面统一为读 0、写无效。
- 总线过渡族（`FadeMasterTrack` / `StopFade` 等）与音轨暂停标记上移到契约基类，两后端各删约 50 行。
- 桥侧 `LoadBank` 由 `bool` 改三态，Tick 侧不再每轮造临时集合。
- `AudioHandleRegistry` 去掉全部字典，句柄改打包值（代次 + 槽号）。
- Clip 缓存的寻址表换定长开址槽表。

#### `Resource`

- 绑定服务只握 internal `IResourceLeaseSource`（八个成员），不再拿后端全契约：后端与绑定服务第一次能各自构造，绑定层测试第一次能 mock 后端。
  - 接缝的抽象成员基线由 `ResourceSeamShapeGuardTests` 钉在 66（19 个抽象属性 + 47 个抽象方法，其中 11 个 `internal abstract`、0 个 `[Obsolete]`）。
- packed key 三条名称轴合成一份 `ResourceNameRegistry` 实现，15 个字段收为 3，登记与回收只剩一条路径。
  - `Release` 与 `DecrementOnly` 刻意分开：整表清空时逐条回收既白做，也会在遍历一张表时反向改动另一张表。
- 绑定路径少两趟与调用者数量无关的开销：注册路径省掉重复的原生 id 往返，异步子精灵绑定的两个孪生成员统一为直接转发。
- 记录槽与后端断开第一根线：
  - `AssetSlot` 的两个具名句柄字段合成一个 `object RawHandle`（YooAsset 句柄是引用类型，存进去不装箱），取用只剩 `IsHandleValid` / `DisposeHandle` / `GetSubSprite` 三个操作。
  - 图集加载流程随之由 `Records` 移到 `Loading`。
  - `Records` / `Keys` / `Expiry` 三个文件里已无一个后端类型名。
- 记账内核成形为 `ResourceRecordStore`（`Runtime/Services/Resource/Kernel/`，由后端持有）：记录槽、租约、两条索引表、在途去重与两座时间轮整体搬出 `YooAssetHandler`，后端与内核之间只剩一面 `IResourceRecordHost`——三个原生句柄算子加三个配置读数。`YooAssetHandler.Keys.cs` / `Expiry.cs` 随之退役。
- 缓存命中不再为"已经完成的结果"造异步状态机：`GetOrLoadAssetAsync` 与 `AcquireSubAssetsBindingAsync` 剥成同步前缀 + 在途段，命中路径直接 `UniTask.FromResult`。
- ⚠ 每帧维护入口 `ProcessKeepAlive` 更名 `ProcessResourceMaintenance(float unscaledTime, int expireBudget, int destroySweepBudget)`。
  - 到期与销毁两条预算刻意不合并：到期记录多的帧不该饿死销毁回收。
  - `ProcessDestroyedObjects` 的默认参删除，让漏传在编译期报出来。
- `ResourceBindingService.Shutdown` 拆为终态关停与可复用重置。
- 外观写成员改走 `RequireHandler()`，未就绪不再伪装成"资源不存在"。
- 配置自检的判据从 `ResourceService` 私有方法搬进 `ResourceServiceSettings` 自己（`GetConfigurationIssues` / `ReportConfigurationIssues`）：只有设置类知道自己的字段与取值，搬过来之后构建期那一份才能原样复用，而不是两处各写一遍规则。轮盘上限也从写死的 255 改成读内核的 `ResourceRecordStore.IdleWheelSpanSeconds`。
- Addressables 后端接上同一套 `ResourceRecordStore`：异步租约 / 绑定 / 预制体实例化 / 图集子精灵 / 场景加载 / 缓存维护不再抛错，两后端共用记账、在途去重与过期，各自只实现一面 `IResourceRecordHost`。
  - 剩余缺口按原因分两类，都不是待办：Addressables 没有同步加载 API，同步取用族与两步式 Check→Update 的下载族统一 fail-fast；`IsNeedDownloadFromRemote` / `GetAssetInfo` / 按标签的 `GetAssetInfos` 要么只有异步版本、要么 `IResourceLocation` 不带对应信息，退化为恒定值。
  - 语义分歧一处：`HasAsset` 区分不出"已缓存"与"待远端下载"，`AssetOnline` 在该后端永不出现。
  - 该层仍留在主程序集里按文件级 `#if ADDRESSABLES_INSTALLED` 整体剔除，不拆卫星 asmdef——内核类型是 `Moirai.Atropos` 的 `internal`，拆开只多换来一行 `InternalsVisibleTo`。

#### `Kernel` 与工具面

- ⚠ 运行期随机全面改走 `RandomUtility`，`UnityEngine.Random` 退出 `Runtime`。
- ⚠ `AlgorithmUtility.RandomRange(long, long)` 的上界口径由"含"改"不含"。

#### `Localization`

- 词条交付改由「批」自带语言头，存储与解析搬进 `LocalizationStore`。
- 全局语言注册表删除，可用语言随表自报（`ConfigTableService.GetLocalizationLanguageCodes`）。
- 语言切换的事件时序与查询热路径一并收口。

#### 池与内存

- `MemoryPool<T>` 的页调度改侵入式双向链表。
- 池维护调度改"采集 / 派发"两段式。
- 主线程守卫不再随正式构建整条消失，池维护异常按房内 `RETHROW_*` 约定分级。
- `TickAll` 在每帧边界自收口并带单轮异常采集上限——一条音的异常不再连带冻住同帧的输入、UI 与存档。

#### `GameApp` 事件

- `GameAppMessageEvent` 的专用事件枚举内聚进 `EEventType`。

### Fixed

#### `Resource`

- 玩家构建里 `EditorSimulate` 的入库设置退回离线模式，从整片沉默改为启动时报一次 Error；三项"配了但永不参与决策"的设置同样各报一次。
- 发起即忘的绑定把抛出整个吞掉（扩展层 7 处 `Binding….Async().Forget()`），"没反应"查不出原因；现在失败原因进日志。
- 销毁态轮转的扫描被组件清理的抛出截断，租约永久泄漏且每帧重复抛异常：改为先记账后清理。
- 异步绑定在"取用租约"一步抛出时把预约位永久留在表里，现已回收。
- 调用方取消不再冒充"加载失败"：`EResourceBindStatus.Cancelled` 与加载失败分道，上层能判要不要重试。
- 子资源（图集）绑定绕开加载去重与卸载代次。
- `WaitForLoadingAsync` 的等待者计数不归还、失败原因被丢弃。
- `LoadGameObject` / `LoadGameObjectAsync` 不防实例化期间的回收与关停。
- `UnloadUnusedAssets` / `ForceUnloadAllAssets` 不校验包是否仍有有效清单。
- YooAsset 后端的实例状态与 YooAssets 的静态表不同一条命：同域重启（容器重启、或关掉脚本域重载进 Play）时 `[SerializeReference]` 里那份处理器原封不动，`Initialize()` 却直接抛 `YooAssets is already initialized`；即使不抛，`PackageMap` 里的 `ResourcePackage` 也全是孤儿，而 `InitPackage` 的快路径恰恰按它的 `InitializeStatus` 判"这个包已就绪"。现在 `Initialize()` 先走一次 `ResetReloadUnsafeState()`：静态已初始化就先 `Destroy`，随后清包表、`AssetInfo` 缓存与两本包初始化字典。
- Addressables 后端把 `Application.lowMemory` 这条链整个吞掉：`SetForceUnloadUnusedAssetsAction` 丢掉委托、`OnLowMemory` 是空方法体，内存吃紧时既不收记录也不请求系统回收，而调用方看到的是"正常返回"。现按 YooAsset 的形状接上，`AddressableHandlerFailFastTests` 钉住委托真的以 force=true 被调用。
  - 同处的强制档 `UnloadUnusedAssets(true)` 同样空转，现在走记录释放；非强制档刻意仍为空——那一档在 YooAsset 侧推进 bundle 卸载操作，本后端没有对应物。
- 精灵绑定族的四个入口把资源包写死成空串（材质族早已透传），DLC 包里的精灵绑不上：`SetSprite` / `SetSubSprite` 全部 8 个重载补上末位可选 `packageName`，留空即走默认包，既有调用行为不变。
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
- `BgmPlaylist` 的 Shuffle 收尾误用顺序下标回绕（`_index + 1 >= Count` 判停），随机落到末位时只播一首就 `Stop()`；现走洗牌袋，一轮全覆盖不重复。
- `MiddlewareAudioHandler.Restart` 归还声部后立刻清空声部池，热复用全部白做；现只在终态关停时丢弃。
- FMOD 短名 `LoadBank(..., loadSamples: false)` 让首播因样本未载入而无声，失败时还伪造 `ERR_EVENT_NOTFOUND`；改 `loadSamples: true` 并去掉误导的错误码。

#### `Localization`

- 数据源抛异常被"先生成配置"顶替，读表缺陷被念成配表缺失。
- 首次查询把 key 当译文返回。
- 检测语言未发行时整套界面露 key。
- 占位符写坏把查询抛出。

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
- 编辑器脚本重载漏掉整份非托管页元数据。
- `Spawn<T>` / `SpawnAsync<T>` 取不到组件时把实例丢在场景里。
- `Despawn(T obj)` 只认目标键不认对象。
- `PoolCatalog` 的规则次序随构建漂移（`Array.Sort` 不稳定）。

#### `Timer`

- 时间轮被污染的时间输入（NaN / Infinity / 溢出）打穿并永久冻结，恢复与重启路径的取值校验补齐。
- 帧计时器在进度回调后沿用回调前的剩余帧数，并把完成回调打到复用槽位的新计时器上。
- 两条泳道回收 executing 标记的形式统一。

#### 其余

- 事件派发链缺 `finally`：订户异常截断整条派发，热路径订阅与异常策略不一致。
- `AlgorithmUtility` 随机面与 `MathsUtility` 概率取值的四处静默缺陷：
  - `RandomRange(length, min, max)` 在 `length = 1` 配 `[100, 200]` 或 `min < 0` 时取不到值只能空转、`length >= 10` 时 `Int32.Parse` 直接抛 `FormatException`——现改为在"位数可表达的值域 ∩ 区间"上取一次，空集抛 `ArgumentException`。
  - `RandomRange(min, max)` 每次 `new Random(Guid)`，且 `min == max` 抛的是 `ArgumentNullException`。
  - `AverageRandom` 乘 10000 取整使第 5 位小数恒为 0、溢出时静默取乱数。
  - `MathsUtility.Chance(percent)` 的 `Range(0, 100) <= percent` 让 0% 也有 1% 成功率（对既有数值是整体 +1% 偏置）。
- `ShuffleBag<T>` 三处：换手必连点（本轮最后一手必撞上轮最后一手）、本轮中途 `Add` 倒拨轮次、空袋取用越界。
- Serilog 后端把日志上下文整个丢掉（`LogHandler.Log` 的 `context` 参数只有一个后端透传）。
- 缓存窗口重开后被上一轮的关闭动画重新隐藏（`_isCreate` 保持 `true`）。
- 测试自证两处：派发用例里残留的故障回调、反射注入不判空。

### Removed

#### `Resource`

- ⚠ `[Obsolete]` 遗留加载族整族删除，连带它下面那条无法补救的引用计数轴。
  - **迁移**：一次性加载走 `ResourceService` 的现役成员，带生命周期的取用走 `ResourceBindingService` 与绑定扩展。
- 死码：`RegisteredTarget` 整套子系统、两个恒零的数据维度、`TryAcquireDirect`。

#### `Runtime/Core/Utilities`

- 上线前精简：五个整类与一批零引用成员约 1.2k 行（`TimeUtility` / `NetUtility` / `XmlUtility` / `ProgramUtility` 等）。
- `AlgorithmUtility` 的正态分布两件套随之删除。

#### `Timer`

- `TimerServiceBenchmark` 移出 `Runtime`，落 `Tests/EditorMode/Service/Timer/`。
