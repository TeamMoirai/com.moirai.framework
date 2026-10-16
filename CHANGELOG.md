# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 资源

- SpriteReference 精灵引用门面（自玩法层上移为框架类型）：数据为 `AssetReference<Sprite>` 弱引用 + 打包方式判别（Common 常规散图/合图、Config 经 `ConfigTableService.LoadSpriteByID` 取配置表精灵）+ 子图名/配置表 ID 各占一字段；`SetSprite(Image/SpriteRenderer)` 异步取像并赋给目标（普通与配置表精灵走保活窗口，合图子精灵按名经绑定服务挂目标元素），`Clone`/`Matches`/`DataSave`/`DataLoad` 存档块自带版本号；检视面为属性驱动（引用行拖入实捕子图名、包名行由 `[ResourcePackageDropdown]` 接管），编辑器面 `LoadEditorSubSprite` 按名直取子精灵
- SpriteReferenceTests：存档块往返与线格式（与玩法侧 IOExtensions 字节对齐）、版本守卫、等值比较与克隆独立契约用例
- 资源内核诊断面（仅编辑器）：`Profiler/Resource` 新增 LEASE PROVENANCE 段——开关打开后为每条新租约记一次取用来源（类型全名.方法名 + 源文件 + 行号，跳过资源内核自身帧），段内先列在途租约（带来源的成行、可按句柄答回同一来源，开关没开时取的那批只计入读数），再看有界卸载账（地址/类型/UTC 时刻，满 64 条覆盖最旧并计入 Dropped）；开关默认关闭，关掉时既不建调用栈也不建字符串

#### 核心

- `[ProviderHandlerHost]` 新增默认处理器声明（第二构造参数）：`ProviderHandlerHostGenerator` 据此生成 `CreateDefaultHandler` 兜底工厂，19 个宿主的手写兜底工厂移除；非法声明（未继承契约/抽象/无可访问无参构造）编译期 MIRAI103 报错，声明默认与手写工厂并存提示 MIRAI104

#### 编辑器

- 编辑器窗口异步数据装载管线 `EditorLoadPipeline`：即时步、主线程分帧切片步（编辑器 update 每拍预算毫秒内连吃条目）、后台任务步顺序执行，装载中可在主线程步内继续登记后续步、每步带完成回调，取消与进度计数齐备；起跑经编辑器 update 自摘除延一拍（不走 `delayCall`——它不随 EditMode 测试的模拟泵派发）
- `FrameworkMenuWindowBase` 装载接入面：`StartDataLoad`/`BuildDataLoadSteps` 登记装载步骤，`IsDataLoading`/`IsDataFailed`/`IsDataReady`（虚属性，派生窗可按热缓存扩展）供呈现装载态；冷装载（无可展示数据）默认绘制整窗遮罩（居中装载进度 + 进度条）并禁用输入，数据在屏的重扫不遮罩不锁交互；装载完成经 `ShouldRebuildTreeAfterDataLoad` 裁决单次重建树再置就绪（在屏数据与新结果一致时可跳过重建免无谓抖动；遮罩与树同拍切换不露空树帧）、`OnDisable` 取消在途装载，不接入异步装载的派生窗零成本
- EditorLoadPipelineTests：步序与动态登记、切片跨拍分摊与计数、后台线程与主线程续体、后台/切片取消观察、失败面用例
- 包管理器插件清单新增必填 `minFrameworkVersion`（与 `minUnityVersion` 同款硬性要求：不满足即拒绝安装，规划期报 `FrameworkFloorUnmet`）；插件作者侧口径写进 `plugin` 分支 README 的字段表与守则（要 NuGet 包就把安装前置写进 `description`）
- 包管理器插件清单新增可选上限 `maxUnityVersion` / `maxFrameworkVersion`（空或缺省=无限制；区间 `[min, max)`，等于上限即已停止提供，Unity 侧写内部版本号如 Unity 6.3=`6000.3.0`），配 `PlanConflictKind.UnityCeilingExceeded` / `FrameworkCeilingExceeded` 在规划期与地板同款中止整批；上限不高于同侧地板判作空区间并点名拒签，框架版本读不出时地板与上限一起跳过。有限生命周期的插件由此自己声明停止的那一档（`unity-logging` 按官方弃用公告取 `6000.3.0`），不必框架代记谁何时失效
- 包管理器详情面 `操作` 面：安装类按钮改大号并按徽标同一条判据置灰，置灰时按钮下出一行 `✕ 原因`（能装则整行不画），`卸载` 不受此限；`Update All` 按同判据剔除装不了的并在结果里逐条点名，不静默少一个
- 包管理器（编辑器窗口 `Tools/Moirai/Package Manager`）：以可配 git 分支作插件包源，部分克隆 + cone 稀疏检出使未勾选的包既不物化也不落盘；插件清单 `<id>.plugin.json` 的字段与目录名规则在此定形，依赖闭包、拓扑序、成环、版本不满足、引擎地板与框架地板在规划期判出且任一冲突整批中止；外部 UPM 依赖按归属台账行级写入工程 `Packages/manifest.json`（用户手写条目既不覆写也不删除，同键只留版本最高的要求），脚本宏按使用者计数成对回收；已装状态与依赖归属落 `ProjectSettings/MoiraiPackageManager.json`，动作面为安装 / 卸载（含连带在场依赖方）/ 更新 / 历史修订安装；落点工作树有未提交改动、或落点分支领先 origin（有未推送提交）时一律拒绝装卸；分支名、源地址与清单标签进 git 或上界面前先过形状守卫；模块文档 `Documentation~/zh|en/PackageManager.md`

#### 本地化

- `LocalizationServiceHandler.SelfReportedLanguageCodes`（protected virtual）：数据源自报有哪几门语言的覆写点，换自定义数据源的后端各自实现这一个方法。基类只在解析那趟问后端一次，原样序列缓存为 `ExportedLanguageCodes`、去空去重后给出 `ExportedLanguages`（后端抛出时按不自报降级并只报一次），换批与整批重载自动作废缓存，自报集合自己变了调 `InvalidateExportedLanguages`。`ConfigTableLocalizationHandler` 的覆写在非播放态直读 Settings 里那份配置表实例。`LocalizationService.ExportedLanguages` / `ExportedLanguageCodes` 转发到当前数据源，不触发装载

### Changed

#### 核心
- 图形设置的确认入口落盘：`SetResolutionSettings` / `SetFullScreenSettings` / `SetWindowModeSettings` / `SetVSyncSettings` 与 `RemoveSetting` 各在写完后补一次 `SettingUtility.Save()`（此前只写内存，重启回到旧分辨率）


- ⚠ 移除默认后端的安装探测自动优选：`LogUtility`/`StringUtility`/`TweenUtility`/`ObjectUtility` 的 `#if` 优选链删除，settings 未配置时一律回退声明默认（DefaultLogHandler/DefaultStringHandler/DefaultTweenHandler/UnityObjectHandler），新建 settings 资产的播种值同步变为声明默认；迁移口径 = 已安装 ZLogger/UnityLogging/ZString/LitMotion/PrimeTween/Fusion 的工程在 `GameAppSettings` 的 Provider 下拉框显式选择后端（已配置工程无感；`InputService` 保留引擎宏分派手写工厂不受影响）
- ⚠ ZLogger 日志后端移出框架，改由 `plugin` 分支的 `zlogger` 插件提供：`ZLoggerHandler` 与 `ZLoggerBypassUnityDebugLoggerProvider` 从 `Moirai.Atropos` 删除，Runtime asmdef 的 `com.cysharp.zlogger` versionDefine（宏 `ZLOGGER_INSTALLED`）与 `ZLogger.Unity` 引用一并移除；迁移口径 = 经 `Tools/Moirai/Package Manager` 装 `zlogger` 插件（清单自带 `com.cysharp.zlogger` UPM 依赖与自有宏），装完在 `GameAppSettings` 的 Provider 下拉框重选后端（`SerializeReference` 的 `asm:` 随类型迁到插件装配而变），未装插件时日志回落 `DefaultLogHandler`
- ⚠ 其余 14 支「装了第三方包才编进来」的兼容后端同样改由 `plugin` 分支按插件发行（一 `id` 一顶层目录）：日志 `serilog`、`unity-logging`；字符串 `zstring`、`newtonsoft-json`；补间 `litmotion`、`primetween`；对象 `photon-fusion`；资源 `addressables`；音频 `fmod`、`wwise`；存档 `memorypack`、`messagepack`、`protobuf`、`unity-cloud-save`。类型名与命名空间逐字不变（按全名反射的判据照旧命中），装配名从 `Moirai.Atropos` 变为各自的 `Moirai.Plugin.*`；框架里只剩默认/内置后端——`DefaultLogHandler`、`DefaultJsonHandler`、`DefaultStringHandler`、`DefaultTweenHandler`、`UnityObjectHandler`、`UnityAudioHandler`、`YooAssetHandler`、`RestCloudSaveKvStore`、`JsonSaveSerializer` 等。Runtime asmdef 随之摘掉 `com.cysharp.zstring`、`com.annulusgames.lit-motion`、`com.unity.logging`、`com.unity.addressables`、`com.unity.services.cloudsave` 五条 versionDefines 与对应 GUID 引用（现存 9 条），`org.nuget.k4os.compression.lz4` 那条无代码的空宏槽位一并删除；PrimeTween 的 `.asmref` 扩展随 `primetween` 插件走，框架那份 `#if !PRIMETWEEN_INSTALLED` 的 stub 不再存在
- ⚠ 上述后端的迁移口径 = 经 `Tools/Moirai/Package Manager` 装对应插件，再在 Provider 下拉里重选后端：日志/字符串/JSON/对象/补间在 `GameAppSettings`，音频在 `AudioServiceSettings`，资源在 `ResourceServiceSettings`，存档序列化与云端 KV 在 `SaveServiceSettings`。插件未装时该后端在下拉里根本不存在、各门面回落内置默认；`[SerializeReference]` 资产与 `[SaveData(Backend = …)]` 按类型全名解析的后端随迁到插件装配而改 `asm:` 归属，存量配置要在下拉里重选一次。FMOD/Wwise/Photon Fusion/MemoryPack/MessagePack/protobuf-net/Serilog 的宏是手写档（`FMOD_INSTALLED`、`WWISE_INSTALLED`、`FUSION2`、`MEMORYPACK_INSTALLED`、`MESSAGEPACK_INSTALLED`、`PROTOBUF_INSTALLED`、`SERILOG_INSTALLED`），前置与宏一律以各插件清单的 `description` 为准；`com.unity.nuget.newtonsoft-json` 仍是框架 `package.json` 的硬依赖（事件基类 `[JsonIgnore]`、任务基类与事件调试器、存档迁移的 JSON 改写都无守卫地用它），搬走的只是 `JsonHandler` 实现类
- 契约基类可见性按契约面定性补正：`AudioServiceHandler`、`MiddlewareAudioHandler`、`ResourceRecordHostHandler` 取 `public`（同为契约面的音频桥接口 `IAudioMiddlewareBridge`/`IAudioMiddlewareBankControl`/`IAudioMiddlewareRtpcControl` 与 `EAudioBankLoadResult` 亦然）——它们是 settings 的 Provider 下拉与装配外派生后端的接缝，与其余 `XxxServiceHandler` 同规格；资源记录内核（`ResourceRecordStore`、`ResourceKeyCodec`、`LoadingOperationState`、`ResourceBindingService`、`IResourceLeaseSource`、`IResourceRecordHost`）保持 `internal`，插件侧派生后端经 `ResourceRecordHostHandler` 上的 `protected internal` 接缝取内核能力，签名里不出现内核类型；`IAudioFadeTarget` 仍是 `internal`，由 `MiddlewareAudioHandler` 的嵌套 `FadeSink` 承接。跨装配的能力判定改由被判定方自报：`ResourceServiceHandler.ConsumesPackageNames`（`protected internal virtual`），Editor 侧以友元身份读取，不再按具体后端类型 `switch`。全仓不为插件新增 `InternalsVisibleTo`；插件清单的 `minFrameworkVersion` 按发行计划取 `1.0.2`，框架自身版本仍由发版流程写
- 日志后端的扩展面由 `internal` 改为 `protected internal`，且不新增公共类型：`LogHandler.Log`／`MinimumLevel` 与新增的 `LogHandler.BypassUnityHandler`（转发 `LogUtility.GetBypassUnityHandler`，静态类不能带 protected 成员故由处理器基类转出）对派生类开放，日志等级词表嵌为 `LogHandler.ELogLevel`（不再是命名空间级类型，框架侧引用改为经 `using static Moirai.Atropos.LogHandler;`）；`Runtime/AssemblyInfo.cs` 的 `InternalsVisibleTo("Moirai.Plugin.ZLogger")` 随之删除——外置插件不再需要白名单；迁移口径 = 跨程序集的后端覆写 `Log` 须声明 `protected`（覆写 `protected internal` 成员不能收窄成 `internal`），框架与 `plugin` 分支的插件须同批更新
- ⚠ 后端实现类可见性统一为 `internal`：`DefaultGameObjectPoolHandler`、`DefaultSettingHandler`（含两处 partial）、`PlayerPrefsSettingHandler`、`FileOffsetEncryptorHandler`、`FileStreamEncryptorHandler` 由 `public` 收窄，与其余内置后端一致；契约基类（各 `XxxServiceHandler`/`SettingHandler`/`YooAssetEncryptorHandler` 等）保持 `public`——它们是 settings 的 Provider 下拉与外部派生后端的接缝；迁移口径 = 外部若直接 `new`/引用过这些实现类，改为在对应 settings 资产里选择后端或派生契约基类（`[SerializeReference]` 资产与 Provider 下拉按类型名工作，与可见性无关，已配置资产无需迁移）

#### 资源

- 真机下 `ResourceServiceSettings.PlayMode` 把 `EditorSimulate` 归一为 `OfflinePlay` 时不再打 Error：静默降级、不回写资产原值也不落一行日志，远程热更随之整体关闭；发行前该字段必须是 `HostPlay`/`WebPlay`（口径已写进 zh/en Resource.md）

#### 音频
- 音量设置真正落盘：`AudioService.SetSettings()` 与 `RemoveSetting()` 各在写完/清完后补一次 `SettingUtility.Save()`；两处的 `categories == null` 早退会连 flush 一起跳过，已改成条件块。滑杆与静音开关等连续量路径不落盘


- 播放链消除 `AudioPlayOptions` 的整份防御性拷贝：全部属性 getter 标 `readonly`，经 `in` 参数读属性不再隐式复制约 190 字节结构体（修复前单次 Play 全链 17-18 处拷贝），序列化形状与公共 API 不变。
- 并发检查与按 clip 遍历改经声部托管镜像（`PlayingClip`）预过滤：声部全表扫描不再逐个原生读 `AudioSource.clip`（命中才读 `isPlaying`，按 clip 遍历零原生读）；`AudioPlayOptionsSO` 的 `MaximumConcurrentInstances` 检查默认即走此路径。
- ⚠ 移除 4 个传统巨型 `Play` 签名（`AudioService`×2 门面与 `AudioServiceHandler`×2 virtual，各 41 参）及其翻译点 `BuildOptions`：常用形态由新入口 `Play(clip, EAudioTrack, Vector3)` 与 `AudioPlayOptions` 工厂（`Create`/`CreateLooping`/`CreateWithFade`）承接，全量定制走 `Play(clip, in AudioPlayOptions)` / `Play(clip, in AudioPlayRequest, cold)`；外部自定义后端若覆写过 virtual 巨兽，改为覆写 Options/Request 抽象成员。
- ⚠ `Play(path, in AudioPlayOptions, bAsync, bInPool)` 移除 `bInPool` 形参：留池语义统一由 `AudioPlayOptions.CachePolicy` 表达（旧 `bInPool: true` 等价 `CachePolicy = Ttl`）。

- 音频设置的键名、缺省值与音量值域钳制收进 `AudioSettingsPolicy`：两个后端与轨级配置不再各自拼装键名。⚠ Unity 后端因此与中间件同口径——越界存量读回来钳到 0..1（此前直接透传给混音器），写入侧同样先钳再存

#### 本地化

- ⚠ `LocalizationServiceSettings` 的「编辑器语言」候选由内置语言全集收窄为 Unspecified 加配置表导出的语言，落盘值为语言 Code；迁移口径 = Unspecified 的 Name 与 Code 都判为未选，资产里已存的 Name 写法照常解析到同一门语言
- `Tools/Config/烘焙渠道默认语言` 窗口的候选改取导出语言：表里没有的语言不再能烘进包；数据源没自报语言时禁用烘焙，而当前烘焙值、失配告警与清除烘焙照旧可用来（没转表又要清掉上一次烘焙的那一档不再被摘掉入口）
- 编辑器预览的取数改由数据源给：新增 `LocalizationServiceHandler.LoadEditorPreviewBatch`（internal virtual，基类不提供），由包内配置表数据源实现，`LocalizationService` 的预览分部不再触达配置表。Inspector 里换成本地化数据源后，预览取的就是那个数据源的词条；程序集外的自定义数据源不实现该钩子时预览为不可用（取数调用收口到后端，`[ProviderHandlerHost]` 与 `[ServiceDependency]` 的默认后端声明仍留在外观文件里，那是装配层布线）
- ⚠ 本地化处理器的当前语言不再回身问服务外观，也没有静态注册点：检测链的原值与来路由装配侧经 `ApplyPreference(rawValue, source)` 交进来，可用性判定按处理器自己的语言批。不经服务直接 new 处理器的代码要先交接，没交接过时当前语言报 `Language.Unspecified`；`ReloadTexts`／`PreloadAsync`／五个切换入口／`GetHandlerFromSettings`／编辑器语言 setter 都先交接再用处理器。「检测出的语言不在本批」落 `LocalizationService.DefaultLanguage` 而不是语言表首项，这一档不变
- 语言可用性的判定实现收成一份：公共 `ToLanguage(str, onlySupported)` 在判定档委派处理器的 `ResolvePreference`，同一件事不再有两份实现；`onlySupported` 为真且没有注册处理器时仍退化为身份解析，公共语义逐档不变
- 「编辑器语言」留空不再当作用户选了一门语言：空串照旧往下走存档、渠道烘焙、系统语言，而它此前会提前返回并把整条链跳掉；切回 Unspecified 也不再走一次语言切换（不白烧该语言的一次性告警闸门）
- ⚠ 语言存档的写入点从本地化处理器上移到服务：处理器只负责切换，直接调 `handler.ChangeLanguage(...)` 的代码不再写存档；键名与读写口径的定义处只剩 `LocalizationSettingsPolicy`，外观的检测链也从那里读存档
- 显式切换在数据未就绪时也落存档（按处理器记下的切换意图当场写，旧口径要等装载完成才写，那次切换最终不在批内时则从不落盘）；首启检测链给的语言因此没有写入路径。存档里存着空值不再算「写过」，渠道烘焙与系统语言照旧参与解析
- 语言存档真正落盘：服务的显式切换入口写完键后补一次 `SettingUtility.Save()`——此前全仓只有调试器会 flush，文件后端下用户选的语言重启即丢。直接调处理器换语言、以及首启检测链给出的语言都不落盘
