# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### UI

- Widget 绑定纳入 UI 生成器产线：widget 根节点语法 `{WidgetClassName}@{InstanceTag}`（类名取 @ 前段，多实例共享一个类与一份 `<Class>.g.cs`；window 收集器在 widget 根停递归，只拿到 widget-typed 字段 `m_{Class}{Instance}`），生成与多实例回绑流程见包根 `.agent/commands/new-ui.md` 的「Widget 控件（可复用组件）」

#### Editor

- `UIBindDataCollector.CollectWidget` 对 `@` 分隔的 widget 根节点改按「@ 前段作类名 + 全名后段作实例段」派生字段名（`m_{Class}{Instance}`），无 `@` 的单实例命名不变；`UIIdentifierFormatter.GetClassName` 同口径取 @ 前段

- 抽出 `PackageManagerWindowBase` 及其嵌套 `PackageManagerProfile`：菜单树、详情面、版本历史与装卸编排收进基类，派生包只需给出窗口标题、状态文件、检出落点、源缺省、宿主包类型与界面文案，即可在同一工程里管理另一条分支上的可安装包；`PackageManagerWindow` 改为框架插件源的首个实现，状态账本与宿主版本量取目标按包源注入

### Changed

#### UI

- ⚠ 删除按名/按路径查找 API：`UIBase.FindChild` / `UIBase.FindChildComponent<T>`（含 `Transform` 重载）与 `GameObjectExtensions.FindChildByName` 不再存在——窗口与 Widget 的节点引用一律走 `UIBindComponent` binder 生成面（框架内置 `LogUI` 已迁序列化 binder `LogUIBinder`）；迁移口径见 `.agent/commands/new-ui.md`

#### Audio

- 新增 `AudioBackendCapabilities`（由后端自报的 `SupportsDelayedStart` / `SupportsPlaybackTrim` / `SupportsSoloControl`，缺省为不支持）与构建期校验器 `AudioBackendCapabilityBuildValidator`：播放选项资产用到当前音频后端不消费的能力时逐条报出资产与字段，设 `MOIRAI_AUDIO_SETTINGS_STRICT=1` 改为挡包；运行期热路径不新增任何判定
- ⚠ `AudioPlayOptions` 的 `SoloSingleTrack` / `SoloAllTracks` 合并为 `EAudioSoloMode SoloMode`（`None` / `SingleTrack` / `AllTracks`），`AudioPlayOptionsSO` 与 `BackgroundMusic` 上的同名序列化字段一并合一；旧资产键不再读取，原 `SoloAllTracks` 为真的配置需重设为 `AllTracks`

#### Core

- `ProviderHandlerHost` 的处理器改为初始化成功后才发布：并发读不再取到半初始化实例，`OnInit` 抛错不再把污染实例永久留在缓存里（下次访问重试），同宿主初始化期递归取用改为明确报错而非栈溢出
- 后端切换赋值时，同一实例重复赋值幂等；新实例初始化失败则保留并继续服务旧实例，成功替换才关停旧实例一次
- `MainThreadDispatcher.Send<TState>(state, action)`：状态化 Send——已在主线程同步直调（零分配、异常直抛调用方），后台复用 `Post<TState>` 池化工作项（稳态零分配），与 `Post<TState>`/`Send(Action)` 语义对称
- `MainThreadDispatcher` 停机改为主线程优雅排水：已入队任务在 `BeginShutdown` 内联执行完毕（存档落盘回执等停机收尾不再被静默截杀），挂起可等待操作的取消语义不变；非主线程停机维持丢弃并告警
- `MainThreadDispatcher` 任务异常与 `PlayerLoopDriver` 订户策略对齐：开发构建 Fatal 后上抛中止本次泵（fail-fast），发布构建隔离续跑并对连续失败 300 次告警一次（任一任务成功归零重计）
- `MainThreadDispatcher` 入队路径零分配收尾：`Post(IEnumerator)` 改经池化工作项 + 静态 lambda 转发（稳态零闭包分配），取消注册改用 `CancellationToken.Register` 状态重载，泵内毫秒换算提为静态字段

#### Editor

- `ProviderDropdown` 集合字段（数组 / `List<T>`，元素为 `[SerializeReference]`）由特性抽屉整体接管：字段行与全部子元素包在一层分组 box 里标示从属关系（同 Odin 分组盒），字段行整行点击收展、展开箭头紧贴字段标签左侧（同 Unity / Odin foldout 样式），右侧 `+` 弹同一类型弹窗新增元素，子元素以浅黑底色的 box 包裹（同 Odin 元素盒观感，与字段背景区分），行尾 `✕` 竖条纵贯元素盒高度且图标纵向居中（同 Odin 列表删除条）；非 managed reference 元素与只读集合维持 Odin 原生列表 UI

#### Input

- `InputServiceHandler.SetUIModal` 由 `internal abstract` 改为对派生方开放，非友元程序集的自定义输入后端现可按 `protected` 覆写实现（此前无法完成该契约）

### Removed

#### Pooling

- ⚠ 删除通用对象池服务 `ObjectPoolService`/`IObjectPool<T>`/`ObjectBase` 及其调试窗口与设置资产：全仓无生产消费面，C# 对象复用走 `MemoryPool`、GameObject 复用走 `GameObjectPoolService`，无需迁移（外部引用者请改用上述两套）

#### Kernel

- `Runtime/Services/Kernel` 独立为根级 `Runtime/Kernel`：物理目录迁移，命名空间与公共 API 不变；Core 依赖棘轮豁免目录名册同步改判据（`ContainerKernelFileRoster` 已留账）

### Changed

#### Timer

- ⚠ 删除 `TimerServiceHandler` 抽象基类：计时器后端改为直接实现唯一契约 `ITimerService`（`DefaultTimerHandler` 签名变为 `FrameworkHandler, ITimerService`，原 `internal override` 全部转 public）；自定义后端迁移口径 = 直接实现 `ITimerService` + 继承 `FrameworkHandler` 生命周期基座，`TimerServiceSettings.TimerServiceHandler`、`Internal_UseHandler` 等签名同步改按接口类型

#### Editor

- `[ProviderHandlerHost]` 源生成器支持接口契约：默认实现校验放宽为「实现处理器契约（继承类或实现接口）+ 继承 FrameworkHandler 生命周期基座」，生成代码的生命周期调用按基座强转；类契约宿主（Log/GameTime/Json 等 Utility 族）行为不变

#### Audio

- ⚠ 删除 `AudioServiceHandler` 抽象基类：音频后端改为直接实现唯一契约 `IAudioService`（`UnityAudioHandler`/`MiddlewareAudioHandler` 签名变为 `FrameworkHandler, IAudioService`）；原基类共享语义（总线 Fade 六方法、`SetVoicePitch`、音轨暂停标记越界守卫、场景切换停音判据）收进 internal `AudioBackendPolicy`（this + 自持调度表作参数的组合替代继承）；`AudioServiceSettings` 字段、`AudioCategory.Handler`、`AudioAgent._audioHandler` 等签名改按接口类型。自定义后端迁移口径 = 直接实现 `IAudioService` + 继承 `FrameworkHandler`；过渡调度表（internal `AudioFadeScheduler`）仍留在各后端自持

### Fixed

#### PackageManager

- 勾选「安装/更新前先清目录」时，安装目标位上的外来同名目录视同已批准覆盖、随清目录一并先删再装；原实现仍走覆盖确认回调，回调缺席即整批中止，勾了清目录也装不下去。未勾清目录的拒绝路径行为不变

#### Editor

- `ProviderCatalogIndexTests` 归行判据夹具的 providerBase 默认值改取 `typeof(FakeBase).FullName` 运行时全名；原硬编码字符串随测试命名空间改名而静默失配，清单行整片铺不出来（3 个用例红）

#### Editor

- API Compatibility Level 切换为 .NET Standard 2.1：`IAudioService` 的可选能力/共享语义默认实现与 `ITimerService.GetLeftFrames/WaitAsync` 回收为接口默认方法（DIM），`UnityAudioHandler`/`MiddlewareAudioHandler`/`DefaultTimerHandler` 的显式补齐与纯默认转发行删除——DIM 仅在 .NET Standard 2.1 profile 下生效（.NET Framework 4.x profile 不支持）

#### ConfigTable / Debugger / Scene

- ⚠ 删除 `DebuggerServiceHandler`/`ConfigTableServiceHandler`/`SceneServiceHandler` 抽象基类：三服务后端改为直接实现唯一契约 `IDebuggerService`/`IConfigTableService`/`ISceneService`（`DefaultXxxHandler` 签名变为 `FrameworkHandler, I{Service}`）；`ConfigTableServiceSettings.InjectConfigTableHandler<T>` 泛型约束改 `where T : class, IConfigTableService, new()`（DIM 需 .NET Standard 2.1 API profile）。可选能力（按语言取列）走接口默认方法，自定义后端不覆写即回默认

#### Input / Procedure / GameObjectPool

- ⚠ 删除 `InputServiceHandler`/`ProcedureServiceHandler`/`GameObjectPoolServiceHandler` 抽象基类：三服务后端改为直接实现唯一契约 `IInputService`/`IProcedureService`/`IGameObjectPoolService`（3 个输入后端 UIMobile/UnityInputManager/UnityInputSystem 与 DefaultProcedureHandler/DefaultGameObjectPoolHandler 签名变为 `FrameworkHandler, I{Service}`）；`ProcedureServiceHandler` 的切换历史/广播承载收进 internal `ProcedureBackendPolicy`（后端自持状态 + 一行转发）；`GameObjectPoolService.TryResolveInstance` 因参数类型 internal 移入 internal `IGameObjectPoolInternals`；下游消费方（CharacterActions/PlayerActions）签名同步接口化。`UIServiceHandler` 保留——它是 uGUI/UITK 双轨并存的家族基座（多实例托管，非单选策略契约）

#### Asset（原 Resource）

- ⚠ 服务整体由 `Resource` 更名为 `Asset`：命名空间 `Moirai.Atropos.Resource` → `Moirai.Atropos.Asset`，类型族（`ResourceService`/`IResourceService`/`ResourceServiceHandler`/`ResourceServiceSettings`/`ResourceOwner`/`ResourceKey`/`ResourceAssetLease` 等）改按 `Asset*` 命名（复合名并入既有资产类型族：`ResourceAssetInfo`→`AssetInfo`、`ResourceAssetLease`→`AssetLease`、`EResourceAssetKind`→`EAssetKind`），服务方法（`ProcessResourceMaintenance`→`ProcessAssetMaintenance`、`RetainResourceKey`→`RetainAssetKey` 等）与目录 `Services/Resource`→`Services/Asset`、文档 `Resource.md`→`Asset.md`、设置资产 `ResourceServiceSettings.asset`→`AssetServiceSettings.asset`（GUID 不变、托管引用同步）一并迁移；`ResourceServiceSettings.asset`/测试字符串全名已同步，已配置自定义后端实例无损。迁移动机：`Resource` 只描述「资源」而未贴合服务本体（YooAsset 资产加载 + Lease/Binding 生命周期的资产服务），`Asset` 与类型族 `AssetReference`/`AssetSlot`/YooAsset 对齐。Unity `Resources` API、`Resources/` 特殊目录与 YooAsset `package.CreateResourceDownloader` 等外部 API 不在迁移范围，保持原名；下游需同步替换 using 与类型引用
- 新增唯一契约接口 `IAssetService`（60 个公共契约成员收编，语义文档与门面同源）；`AssetServiceHandler` 抽象基座保留并实现接口（abstract 成员满足契约义务，72 成员实现链零改动）——Asset 采用「契约接口 + 引擎基座」混合形态，addressables 等外部插件兼容性无损；internal `IAssetLeaseSource` 租约窄缝提取独立文件，internal `IAssetLeaseDiagnostics` 诊断附属接口（`GetLeaseSourceInfos` 显式实现，internal 参数类型不进公共契约）；`AssetServiceSettings` 字段、`IPrefabLoader`/`OperationsWindow`/`AssetBindingExtension`/编辑器包名桥与探针、消费方测试签名全部接口化

#### Localization

- ⚠ 命名空间 `Moirai.Atropos.Localization` 迁移为 `Moirai.Atropos.Localization`（类型与目录不变；`LocalizationServiceSettings.asset` 的托管引用 ns 已同步迁移，已配置的自定义后端实例无损；下游 using 需同步替换）
- 新增唯一消费契约 `ILocalizationService`（门面公共 API 收编，语义文档同源）：引擎基座 `LocalizationServiceHandler` 保留（1500+ 行共享实现豁免契约化），后端经派生基座落地；internal 协作面（偏好解析/编辑器预览）不进公共契约，框架内部按引擎基座引用访问；`Settings` 字段与消费方测试签名按契约类型持有

#### Save

- 新增唯一消费契约 `ISaveService`（28 个门面公共 API 收编，语义文档同源）：引擎基座 `SaveServiceHandler` 保留（2600+ 行共享实现豁免契约化），后端经派生基座落地；internal 协作面（存储后端选择/原始块读写/截图写入）不进公共契约，框架内部按引擎基座引用访问；`Settings` 字段、调试窗口与消费方测试签名按契约类型持有

#### Localization

- ⚠ 回滚 `Moirai.Atropos.L10n` 命名空间迁移：类型命名空间改回 `Moirai.Atropos.Localization`（`LocalizationServiceSettings.asset` 托管引用 ns 同步回迁，实例无损；下游 using 同步替换）；`ILocalizationService` 薄契约保留。顺带修复：反射守卫 Allowlist 两条 Resource 路径同步 Asset 目录更名（Resource→Asset 更名的遗留腐烂）

#### Kernel / 注入互调（Phase 2 任务①）

- `[ServiceDependency]` 依赖声明改指契约接口（20 处具体类依赖清零，`UIService` 双轨豁免保留具体类）：`BuiltinServiceRegistrationGenerator` 为带 `[ProviderHandlerHost]` 的服务生成契约接口附加绑定行（同实例多契约注册），`MIRAI201` 放宽为「实现 IService 或为 [ProviderHandlerHost] 自报的契约接口」（跨程序集契约接口收集）；容器注册键（具体类）不变，`EnsureRegistered<T>` 调用习惯零变更

#### Kernel / 注入互调（Phase 2 任务②·基础设施）

- 新增 `IServiceContext`（容器公共解析面：`GetRequiredService/GetService/TryGetService`，`ServiceWorld` 实现它）与 `IServiceContextAware`（注入标记）：容器在三条注册路径（两阶段入图/附加契约/运行时注册）注入上下文，`ServiceBase`/`ServiceMono` 暴露 `protected Context` 属性——派生服务在 `OnInit` 中按契约接口解析依赖，替代经静态门面的间接互调。决定记录：不改 `IService.OnInit/OnInitAsync` 签名（契约面含异步变体，改签名是三接口 breaking），采用注入接口加法演进
- 注册期依赖校验失败回滚守卫测试（校验失败不残留契约条目/实现条目；Runtime 注册路径 throw-before-mutate 天然满足，测试固化）
- 反射棘轮名册同步：`IServiceContext.cs` 入 ContainerKernelFileRoster（容器新组成部分）

#### Kernel / 注入互调（Phase 2 任务②·消费改造）

- AudioService（TimerService.WaitFrame / DebuggerService 注册注销调试窗）、AssetService（DebuggerService 注册）、LocalizationService（DebuggerService 注册）三服务的 IService 层互调点改为 `OnInit` 经上下文按契约接口解析依赖存字段、调用点走字段——替代经静态门面的间接互调；依赖契约由 `[ServiceDependency]` 声明经拓扑序保证先行就绪。Handler 层与深层组件的门面消费（UI 深层组件/扩展层/调试窗）属"组件→服务"消费，由任务③（门面静态→实例转发）自然收敛，本轮不动

#### Kernel / IEventBus（Phase 3 任务 3.1）

- 新增事件总线体系（决策 B v4）：`IGameEvent`（struct 约束标记）+ `IEventBus` 契约（`Subscribe` 返回可 Dispose 句柄 / `Publish(in evt)` in 参数零拷贝 / `BeginScope` 作用域批量退订）+ `EventBus` 默认实现（经 [AutoRegisterService] 注册，派生 ServiceBase 获得生命周期）
- 发布期安全：派发前拷贝回调快照，发布期内的订阅不派发本次事件（下次发布生效）、退订排队至派发循环结束后应用
- 异常分级：发布期逐回调隔离（Debug.LogException 记日志不中断其他订阅者）；Debugger 对接预留 `EventBusDiagnostics.OnPublished` 钩子
- 作用域批量退订：`BeginScope` 登记作用域内全部订阅句柄，Dispose 批量退订（Scene/Gameplay 级清理防跨场景泄漏）
- 不挪用 DOM 五阶段树形传播（留在 EventBase 服务 UI/Task）；`EventBase<T>` 池化 class 事件体系保留，垫片迁移（任务 3.2）后续落地
- 8 个行为契约测试（订阅序/句柄退订/幂等/发布期安全/作用域/异常隔离/无订阅者 no-op）

#### Events / 垫片（Phase 3 任务 3.2）

- `EventManager` 三个静态消费入口（RegisterCallback/UnregisterCallback/SendEvent）标 `[System.Obsolete]`（Phase 3 迁移窗口，行为不变）——消息内写明迁移路径（IEventBus struct 事件）。决定记录：EventBase class 事件与 struct 总线形态不同，垫片是弃用提示而非转发（转发不可能行为等价）；下游迁移（3.3）时事件 class→struct 一并转换。框架内部消费点（Save 双通道 6 个事件模型文件）以 `#pragma warning disable CS0618` 抑制，任务 3.4 收敛后移除

#### Kernel / IEventBus 静态化（用户架构指令落地）

- **EventBus 去服务化，内核管线静态调用形态**：`public static class EventBus`——`EventBus.Subscribe/Publish/Unsubscribe/BeginScope` 直接静态调用（内核管线组件，不是容器服务）；移除 `[AutoRegisterService]` 与 ServiceBase 派生；`IEventBus` 实例契约接口删除（`EventDelegate` 委托与 `EventBusDiagnostics` 诊断钩子保留）
- 分桶改泛型静态容器：`EventChannel<TEvent>.Instance`（JIT per-type 实例化，零字典查找）——发布快照/异常隔离/发布期退订排队语义不变；`ClearAllChannels` 保留注册表只清订阅（否则后续重置失去枚举入口）
- 关 DR 兜底：`EventBus.ResetStaticsForPlayModeReset` 挂 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`（与门面 ResetStatics 同口径）；反射棘轮名册补 `EventChannel.cs`
- 决定记录（同口径审计）：MemoryPool（static unsafe class）与 GameAppSettings 的 Utility 门面（StringUtility/VersionUtility/GameSettings/LogUtility/Tween 管理器等——静态类 + settings 注入可拔插 Handler）**本来就是静态调用形态**，无需改动；MainThreadDispatcher 核心静态、宿主单例留 Phase 4

#### Kernel / IEventBus API 形态定稿（用户决策）

- **对称 API 习惯**：`EventBus.Subscribe<T>(h)` / `EventBus.Unsubscribe<T>(h)` 成对直接调用（与原 EventManager 用法一致）；Subscribe 不返回句柄——`EventRuntimeHandle`、作用域批量退订（BeginScope）按用户决策移除；跨场景清理由订阅方在场景卸载回调中成对 Unsubscribe（原习惯）

#### Save / 双通道收敛（Phase 3 任务 3.4）

- 6 个 Save 事件 struct 化（SaveBlockChanged/EntityRestored/Failed/Progress/Screenshot/SlotChanged）经静态总线发布；**静态 C# event 通道删除**（`SaveService.onXxx` 九事件——下游订阅面仅框架测试，收敛为总线单一入口）；`UnsubscribeAll` 一并移除（总线 `ResetStaticsForPlayModeReset` 兜底）
- **总线退订匹配修复**：`EventChannel.UnsubscribeCore` 改 `Delegate.Equals` 值等价匹配（原 ReferenceEquals 在方法组形态下每次转换生成新实例永不匹配——退订失效致订阅泄漏计数累积）
- 消费方迁移：Save 测试套（EventTests/Incremental/Persistence/Screenshot/MigrationBus）与 Assets 侧 SaveServiceTester——聚合事件订阅带 Kind/Operation 过滤桥方法（引用稳定）

#### Events / 事件形态规范定稿（用户指令）

- ⚠ Save 6 事件规范化为 **readonly struct 且载荷字段内联**（无 Args 孪生——订阅侧 `e.Args.X` 改读 `e.X`，Kind/Operation 枚举原地保留）；`Trigger` 静态便捷发布与 5 个 `SaveXxxArgs` 类型删除，发布走构造函数 + `EventBus.Publish(in evt)`；GetPooled/对象初始化器形态移除
- **无参事件直接发布**：`EventBus.Publish<TEvent>()` 泛型无参重载（default 实例零构造）——`EventBus.Publish<EmptyArgsEvent>();`
- Save 派发入口统一 `MainThreadDispatcher.Send(evt, static e => EventBus.Publish(in e))`：主线程内联直发、后台池化工作项入队——Raise* 手写 `IsMainThread` 分支与 `Publish*` 包装删除

#### Kernel / EventBus Debugger 对接（Phase 3 任务 3.4 收尾）

- `EventBusDebuggerWindow`：事件流监视调试窗（"Profiler/EventBus" 路径）——`EventBusDiagnostics.OnPublished` 钩子挂接（进窗挂接/离窗摘除），64 条环形缓冲实时事件流 + 各事件类型发布计数（会话累计），OnUpdate 0.25s 节流刷新
- `EventBusDebuggerRegistrar`：静态总线（无服务生命周期）的注册入口——`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + `GameServices.GetService<IDebuggerService>()` nullable 解析（容器未构建静默跳过）

#### Phase 3 终态 / EventManager 垫片移除

- **Obsolete 垫片三入口删除**（RegisterCallback/UnregisterCallback 泛型版——消费面清零后按计划移除）；`SendEvent(EventBase)` 重新归类为 **树形传播入口**（EventBase 单体系 UI/Task 树传播，去 Obsolete——非跨系统总线 API）
- `Templates~` 模板 HotfixEntryEvent 同步总线口径（readonly struct + `EventBus.Publish<T>()` 无参直发）
- **跨系统事件体系终态**：EventBus 静态总线（readonly struct 事件 / 对称方法组订阅 / in 零拷贝 / 无参直发）；EventManager = EventBase 树形传播协调器（MonoEvent 单体系，与总线无交集）

#### Phase 4 / Singleton 家族加固（决策 C v4 落地）

- **Domain Reload 自重置钩子**：`SingletonMono<T>`（s_Instance/s_ShuttingDown/s_NextOrdinal）+ `SingletonRegisterMono<T>` + `SingletonRegister<T>` 三处补 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 兜底重置（关闭 DR 工作流时清上次会话异常退出的残留静态；幂等，正常路径无副作用；`SingletonMono_Persistent<T>` 无自有静态，继承基类钩子覆盖）
- **使用边界文档**：`SingletonMono<T>` 类头补"服务走 ServiceWorld 容器 / 本家族面向 UI 组件与场景级 MonoBehaviour 单例；新增全局系统优先评估服务化"边界说明（决策 C 使用边界）
- **DontDestroyOnLoad 归属登记**：11 文件 16 处调用逐处核实——全部为宿主根/服务实例根/持久单例/跨场景协调器的正当物化（无散落误用，零清理项）；归属登记表见方案产物 Phase 4 小节

#### Phase 4 / 手写单例惯例统一

- `EventManager` + `TaskRunner` 补 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` 自重置钩子——手写懒物化单例与 Singleton 家族 DR 兜底惯例对齐（保留各自特殊语义：Editor 守卫/异常分级门控不做替换——实现替换留 Phase 6 God 类分解范畴）

#### Phase 5 / T5 + T6 + T10 剥离（快赢三项）

- **T6** `JsonUtility` 错误文案改自述（"Can not convert..." → "JSON 序列化/反序列化失败"）——8 处
- **T5** `EncryptionUtility` → **`XorCipher`**（Obfuscate/ObfuscateInPlace/ObfuscateQuick 语义族——XOR 对称：同一函数即解密，不设 Deobfuscate 分立入口）；旧类 Obsolete 垫片转发（下游 juice 平滑迁移）
- **T10** `GameException` → **`MoiraiException`**（全项目 118 文件 + **源生成器模板** ProviderHandlerHostGenerator——生成器 DLL 重建后再生效，3 个 fail-fast 回归失败定位修复）+ 测试改名 MoiraiExceptionTests；`GameException` Obsolete 垫片（**继承 MoiraiException**——既有 `catch(GameException)` 继续生效）
- **工程经验**：源生成器产物（RequireHandler fail-fast）路径不在常规源码树——批量迁移必须含 SourceGenerators/Source~ 模板 + DLL 重建链路

#### Phase 6 / Top 1 GameApp 分解（partial 化，公共 API 零变化）

- `GameApp.cs`（704 行）→ **376 行**（生命周期 + 生命周期事件 + Unity 事件注入 + 私有方法）
- 新增 `GameApp.Coroutine.cs`（111 行，协程控制）+ `GameApp.FrameDriver.cs`（177 行，订阅句柄 + Unity Update 注入 + 帧逻辑 Handler 注入）+ `GameApp.EngineSettings.cs`（111 行，引擎参数 FrameRate/GameSpeed/Pause/RunInBackground/NeverSleep）
- 验收达成：全部文件 < 400 行且单一职责；公共 API 零变化（partial 纯区域移动）

#### Phase 6 / Top 2 ServiceScope 分解（partial 化，公共 API 零变化）

- `ServiceScope.cs`（1322 行）→ **306 行**（常量字段/属性/构造/查找/迭代安全/内部结构）
- 新增 `ServiceScope.Registry.cs`（332 行，服务注册与注册期校验）+ `ServiceScope.Poller.cs`（279 行，帧轮询与异常分级）+ `ServiceScope.Lifecycle.cs`（218 行，逆序关闭与销毁）+ `ServiceScope.Diagnostics.cs`（231 行，诊断与图维护工具）
- 全部文件 < 400 行单一职责；`internal sealed partial` 保持内聚（字段共享无需拆状态——类分解留待有实际改动需求时评估）

#### Phase 6 / Top 3 PlayerLoopDriver 分解（partial 化，公共 API 零变化）

- `PlayerLoopDriver.cs`（1099 行）→ **132 行**（状态 + 核心声明）
- 新增 `PlayerLoopDriver.Registration.cs`（261 行，Unity 事件/接口/Action 三类注册）+ `PlayerLoopDriver.Drive.cs`（209 行，核心钩子与三阶段驱动循环）+ `PlayerLoopDriver.Slots.cs`（92 行，生命周期 Init/Shutdown）+ `PlayerLoopDriver.SlotHandler.cs`（264 行，泛型处理器槽——钩子表独立）+ `PlayerLoopDriver.SlotCallback.cs`（202 行，回调槽）
- 全部文件 < 300 行；"核心钩子表独立"（方案 Top3 目标）达成——槽类拆出独立文件

#### Phase 6 / Top 4 SaveServiceHandler 分解（partial 化，公共 API 零变化）

- `SaveServiceHandler.cs`（2615 行）→ **143 行**（生命周期 + 类头字段）
- 8 个域文件：Blocks（383，块读写）/ BlockListing（173，块删除枚举）/ Archive（341，存档删除）/ ArchiveQuery（121，查询与备份）/ Components（383，组件块管线）/ Migration（246，版本迁移+截图）/ Paths（388，路径管理）/ ReadPipeline（396，读管线）/ WritePipeline（187，写管线）
- 全部文件 < 400 行；**行数守恒断言**（切块前后总行数校验）防内容丢失——过程中一次切块失误差点丢 394 行，靠 git HEAD 版本恢复重切

#### Procedure / Res 惰性资源作用域

- **新增 `AssetLeaseScope`**（Asset 服务侧通用设施）：惰性租约作用域——`Load<T>(location)` 按需加载并登记租约，`Dispose()` 登记逆序统一归还（幂等；IsDisposed 后 Load fail-fast）；使用者持有作用域即持有"资产所有权集合"
- **`ProcedureBase` 接入**：新增 `protected AssetLeaseScope Res`（惰性创建）——派生类在 OnEnter/OnUpdate 中 `Res.Load<T>("location")` 拉起本状态所需资产，**状态持有的资产生命周期 = 状态生命周期**
- **Owner 保证释放**：`DefaultProcedureHandler` 两处 OnLeave 调用点（正常切换 + Shutdown）之后调 `from.CleanupLeaseScope()`——不依赖派生类 override 调 base 的纪律，异常路径（catch/finally）也保证释放
