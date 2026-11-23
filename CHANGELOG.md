# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### Kernel

- 新增 `EventBusAnalyzer`（`SourceGenerators/EventBusAnalyzer.dll`，源码 `Source~/EventBusAnalyzer/`，随包自动生效，**纯分析器不生成任何代码**）：MA7000 禁止匿名函数订阅/退订 EventBus（匿名委托无法值等价匹配 Unsubscribe，订阅即泄漏）、MA7001 `OnXxxEvent` 带参回调必须 `in` 修饰（含一键 CodeFix；方法组转换要求 ref-kind 精确匹配）、MA7002 参数类型与方法名后缀不匹配告警、MA7003 订阅缺少成对退订（按「事件类型 + 处理器」在同一宿主类型内配对，跨 partial 分部，多出的 Subscribe 报 Warning）、MA7004–MA7006 `[GameEvent]` 契约校验；**不再生成 SubscribeEvents()/UnsubscribeEvents() 胶水，无参回调也不再要求 partial，订阅时机与退订配对全部由用户自行控制**，详见 `Documentation~/EventBusAnalyzer.md`
- EventBus 作用域订阅：新增 `EventBus.SubscribeScoped(kind, handler)`——订阅并登记进作用域台账，`ServiceWorld` 关停作用域（`ShutdownScope(Async)`）或整体 `Dispose(Async)` 时自动批量退订（在该作用域全部服务 `OnShutdown` 之后执行）；`Subscribe`/`Unsubscribe` 对称无句柄 API 不变，未登记的作用域订阅照旧手动成对退订

#### Input

- 新增按键重绑定（Input System 后端）：`IInputRebindable` 能力窄接口 + `InputService.Rebinding` 能力探测（旧后端访问抛 `GameException`）、`RebindAsync` 交互式监听（冲突三策略 Fail/Override/Cancel、复合绑定按部件落点、默认屏蔽鼠标触屏）、覆写走 `InputBinding.overridePath` 并经 `GameSettings`（键 `Setting.Input.Rebind.{Group/Name}`）设备级持久化、启动自动恢复；重绑终态发布 `InputRebindEvent` 供 UI 提示层订阅刷新，详见 `Documentation~/Input.md` 的「按键重绑定（Rebinding）」

#### UI

- `UIService` 定型为多缓存静态门面（终态口径）：`IUIService` 定位为后端能力契约 + 门面多缓存引用类型，不承诺容器可解析；跨服务消费走门面静态 API 与静态事件（`onWindowShown`/`onWindowClosed`），不立容器契约绑定

#### Tests

- 新增 `AllocationCapture` 字节级分配计量夹具（GC 线程字节差值主口径 + Profiler 稳态对照），并挂 Timer 万次 add/remove、事件稳态发布与订阅退订三组 0-Alloc 门禁

#### Kernel

- 事件发布热路径消分配：`EventChannel` 派发快照改缓存数组复用（容量按订阅数对齐 2 的幂），稳态发布零分配，扩容与重入发布（嵌套 Publish）走新数组保语义不变

#### UI

- Widget 绑定纳入 UI 生成器产线：widget 根节点语法 `{WidgetClassName}@{InstanceTag}`（类名取 @ 前段，多实例共享一个类与一份 `<Class>.g.cs`；window 收集器在 widget 根停递归，只拿到 widget-typed 字段 `m_{Class}{Instance}`），生成与多实例回绑流程见包根 `.agent/commands/new-ui.md` 的「Widget 控件（可复用组件）」

#### Editor

- `UIBindDataCollector.CollectWidget` 对 `@` 分隔的 widget 根节点改按「@ 前段作类名 + 全名后段作实例段」派生字段名（`m_{Class}{Instance}`），无 `@` 的单实例命名不变；`UIIdentifierFormatter.GetClassName` 同口径取 @ 前段

- 抽出 `PackageManagerWindowBase` 及其嵌套 `PackageManagerProfile`：菜单树、详情面、版本历史与装卸编排收进基类，派生包只需给出窗口标题、状态文件、检出落点、源缺省、宿主包类型与界面文案，即可在同一工程里管理另一条分支上的可安装包；`PackageManagerWindow` 改为框架插件源的首个实现，状态账本与宿主版本量取目标按包源注入

#### Localization

- 新增 `LanguageIndexedAttribute`（`Runtime/Services/Localization/Support/`）与编辑器抽屉（`Editor/Services/Localization/Drawers/LanguageIndexedArrayDrawer`）：标在「下标 = 语言列下标」的数组字段上，Inspector 每个元素行前缀显示语言码（语言清单取 `LocalizationService.ExportedLanguages`，编辑态直读 Settings 后端实例）；清单为空整列原样显示，越界行不猜语言。`TextLocalizer`/`ImageLocalizer`/`AudioLocalizer` 的六个按语言数组字段已标注

### Changed

#### Core

- ⚠ 服务架构回摆——后端即注册实体、门面退回纯静态缓存：12 个服务门面全部转 `static partial class`（删除 `ServiceBase` 派生、`[AutoRegisterService]` 与实例生命周期，门面实例化 API 废弃），可序列化后端改为 `XxxHandler : ServiceBase, IServiceTickable?, IXxxService` 注册进容器（绑定即注册：懒链解析 → 容器注册 → 注册结果回填门面缓存槽，槽位 ≡ 容器实例；组合根 `RegisterAll` 对 static 宿主发射 `WarmHandler` 预热）；契约键改绑真实后端实现（原「门面自报契约但未实现」的隐性错绑随之消除），`[ServiceDependency]`/`Priority` 从门面移到后端，后端进容器轮询、拓扑排序与诊断表；`Tick`/`Initialize` 移出业务契约（`IAudioService`/`IDebuggerService`/`IObjectPoolService`/`IProcedureService`/`ITimerService`/`IUIService` 的 Tick 与 `IAssetService.Initialize`/`IsInitialized`/`IProcedureService.Initialize` 不再是契约成员，由 `IServiceTickable`/容器生命周期承接；业务性 `Restart` 保留）；消费政策三车道 = 业务走门面静态 API、框架互调走 `Context` 契约注入、测试/编辑器走 `Handler` setter + `Internal_Peek/UseHandler` 接缝——门面槽 ≡ App 级注册实例（作用域遮蔽只影响契约解析通道）
- ⚠ UI 双轨多缓存收口：`UGUIHandler`/`UITKHandler : UIServiceHandler : ServiceBase, IUIService, IServiceTickable` 由组合根 `UIService.RegisterEnabledHandlers()` 按目录关停档降序注册（契约 = 各自具体类型，宿主轨最先注册），容器反序拆除 = 档位升序收口（宿主轨最后拆根）；共享栈结算加帧守卫幂等（任一轨 Tick 先到先结算、顺序无关）；`GameAppSettings` 的 IUIService 手工补登删除，Input/Procedure 等消费方改订门面静态事件/调门面静态开窗；门面级广播（`onWindowShown`/`onWindowClosed`）与跨轨归零门挂门面静态，域重载复位接缝保留
- ⚠ 服务间引用范式统一：内置服务的跨服务依赖一律「后端 `[ServiceDependency]` 声明 + `OnInit` 中 `Context.GetRequiredService<IXxxService>()` 解析到实例字段」——Timer/Save/Procedure/ObjectPool/Asset/Localization/Audio 的调试窗注册与窗口事件订阅不再经静态门面直调；UI 消费例外走门面静态事件（见 UI 双轨条目）；自定义服务以门面具体类型声明依赖的需同步改契约接口
- ⚠ 机制类不再调静态门面：`AssetLeaseScope` 改持注入/容器解析的 `IAssetService`（公共无参构造保留，首次 `Load` 经容器解析一次并缓存——boot 前调用从「自动注册资产服务」变为抛 `GameException`）；Audio 的 clip 租约来源（`AssetClipLeaseSource`）与 AudioAgent 自动解除独奏计时器同改容器契约引用
- 服务间引用与查找热路径内联：门面 one-liner 转发与显式接口实现、`GameServices.GetRequiredService/GetService/TryGetService`、`ServiceWorld.TryGet` 族与跨作用域绑定 `TryGetBest` 加 `[MethodImpl(AggressiveInlining)]`，稳态跨服务互调的转发开销由 JIT 折叠为零
- ⚠ 门面懒装配不再自举世界：`[ProviderHandlerHost]` 服务宿主的懒链只在**世界已初始化或初始化在途**的窗口放行，世界未建、已建未初始化、`GameApp` 已关闭三种情形一律抛 `GameException`（正文带门面名），不再静默返回未初始化的后端；预热唯一入口是组合根 `WarmHandler`。`GameServices.EnsureRegistered<T>` 随之删除（外部无引用面）。迁移口径 = 组合根跑到之前不要访问服务门面，需要自造世界时走显式 `RegisterService`
- 门面槽位一致性改由写侧推送：装配路径向内核登记清槽回调，`GameServices.Shutdown`/`ShutdownAsync` 逐个落槽；`Handler` 读侧不再每次回查容器绑定（原「任一契约」线性扫描判据删除），`Internal_PeekHandler`/`Internal_UseHandler` 的无损换入换出也不再被读侧静默弃槽
- ⚠ 生命期钩子收严到 `HandlerBase`：`OnInit`/`OnShutdown` 改为 `protected virtual`，包外覆写处 `public override` 改 `protected override`
- 状态机、就绪判据与转换语义收进 `LifecycleMachine`；转换只由框架驱动——容器经 `IServiceLifecycle`，生成宿主经 `Internal_Init`/`Internal_Shutdown`；就绪判据保持严格等号，关停体内的编排收尾另读 `HandlerBase.WasInitialized`（机器落 `Initialized` 时武装的单向锁存，关停期仍可读）
- `FrameworkHandler` 派生 `HandlerBase`：`IsInitialized` 读共用的就绪判据，状态位只由 `LifecycleMachine` 写
- 直驱关停只留「停在 `Created`（从未装配成功）即跳过 `OnShutdown`」这一枚前置；重复驱动与复活口径并到 `LifecycleMachine` 一处，`AllowsRevival` 取假保留 Mono 不复活
- 工具处理器的直驱关停承接 `OnShutdown` 抛错：由生命期机器记 Error、实例照常落 `Disposed`，不再打断换提供者本身
- ⚠ `ServiceMono<TScope>` 生命期钩子收严为 `protected abstract`，与容器侧同口径：包外子类覆写处 `public override` 改 `protected override`

#### 源生成器

- ⚠ 诊断 ID 段号方案统一迁移为 `MA + 4 位段号（ABCD）`：A=模块（1 ProviderHandlerHost / 2 ServiceDependency / 3 SaveServiceCodegen / 5 UIWindowCodegen / 6 HotPathAnalyzer / 7 EventBusAnalyzer）、B=功能面、CD 沿用原分配（Save 3xx/4xx 归并为 A=3）；旧 `MIRAI` + 3 位 ID 全部停用，`.editorconfig` 中的 `dotnet_diagnostic.MIRAIxxx.severity` 需按 `SourceGenerators/README.md` 分配表改为对应 `MAxxxx`
- `ProviderHandlerHost` 生成的薄访问器加内联：`CreateDefaultHandler`/`RequireHandler`/`IsValid`（`[method: ...]`）与 `Handler` 属性快路径加 `[MethodImpl(AggressiveInlining)]`，宿主稳态取用零转发开销

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
- ⚠ 运行时目录重组：`Runtime/Core/` 一分为二——框架运行底座（`GameApp` 含 `PlayerLoop`、`MemoryPool`、`GameException`、`FrameworkSettings`、`GameSettings`、`Singleton`、`Tasks`、`GameProfiler`，连同事件总线 `Events/`）归入 `Runtime/Kernel/`；剩余基础件（`Attributes`、`Constant`、`DataStructure`、`Extensions`、`Obfuz`、`Utilities`）所在目录由 `Runtime/Core/` 改名为 `Runtime/Foundation/`。生产侧命名空间与公共 API 全部不变（仍为 `Moirai.Atropos`），所有 `.meta` GUID 原样保留；测试树同步拆分/改名为 `Tests/{EditorMode,PlayMode}/Kernel/` 与 `Tests/EditorMode/Foundation/`，测试命名空间一并改（`Core.MemoryPool` → `Kernel.MemoryPool`、`Utility` → `Foundation.Utility`、`GameSettings` → `Kernel.GameSettings`）；按源码相对路径定位文件的判据与文档路径已同步

#### Kernel

- `EventBus` 调试窗去服务化并补齐参数查看/重播：移除 Runtime 侧的 `EventBusDebuggerWindow`（原继承 `ScrollableDebuggerWindowBase`）与 `EventBusDebuggerRegistrar`（原经 `IDebuggerService.RegisterDebuggerWindow` 挂窗），调试改为纯编辑器 `OdinEditorWindow`（`Editor/Kernel/Events/EventBusDebuggerWindow.cs`，菜单 `Window/Event Debugger`，形态对齐 `PlayerLoopDebuggerWindow`：控制条/状态区由 Odin 特性渲染，事件流行可点选、回放条目 [R] 着色），经 `#if UNITY_EDITOR` 门控的 `EventBusDiagnostics.OnPublished` 钩子经 `InternalsVisibleTo` 挂接；内核侧不引入任何 Debugger 服务接口或窗口注册依赖
- 诊断钩子 `EventBusDiagnostics.OnPublished` 由 `Action&lt;Type&gt;` 升为 `Action&lt;Type, object&gt;`：随事件携带 struct 值快照（调试窗未挂接时 `?.` 短路、零装箱），每次发布仅触发一次（原实现按订阅者数放大）；编辑器调试窗据此支持点选事件行查看传参（Odin 检视器只读渲染 struct 快照）、单条重播与按记录时间差+倍速整段回放（反射闭合 `EventBus.Publish&lt;T&gt;` 走真实发布路径）
- ⚠ `Editor/Core/` 镜像生产侧一分为二——`Events`/`GameSettings`/`Tasks` 归入 `Editor/Kernel/`，`Attributes`/`Tween`/`Inspector`/`MenuEditorWindow`/`Models` 归入 `Editor/Foundation/`；`Core.meta` GUID 延续给 `Kernel.meta`。对应测试 `Tests/EditorMode/Editor/Core/` 同步拆为 `Foundation/{Attributes,MenuEditorWindow}/`，测试短命名空间一并改（`Attributes` → `Foundation.Attributes`、`MenuEditorWindow` → `Foundation.MenuEditorWindow`）。编辑器侧命名空间与公共 API 全部不变，`.meta` GUID 原样保留；按名解析的 `Attributes/...` Resources 相对路径不随父目录改变

#### Editor

- `ProviderDropdown` 集合字段（数组 / `List<T>`，元素为 `[SerializeReference]`）由特性抽屉整体接管：字段行与全部子元素包在一层分组 box 里标示从属关系（同 Odin 分组盒），字段行整行点击收展、展开箭头紧贴字段标签左侧（同 Unity / Odin foldout 样式），右侧 `+` 弹同一类型弹窗新增元素，子元素以浅黑底色的 box 包裹（同 Odin 元素盒观感，与字段背景区分），行尾 `✕` 竖条纵贯元素盒高度且图标纵向居中（同 Odin 列表删除条）；非 managed reference 元素与只读集合维持 Odin 原生列表 UI

#### Input

- `InputServiceHandler.SetUIModal` 由 `internal abstract` 改为对派生方开放，非友元程序集的自定义输入后端现可按 `protected` 覆写实现（此前无法完成该契约）

#### Pooling

- ⚠ GameObject 池服务整体更名为 `ObjectPool`：目录 `Runtime/Services/Pooling/GameObjectPool/` → `Runtime/Services/ObjectPool/`（命名空间 `Moirai.Atropos.ObjectPool`），`GameObjectPoolService` → `ObjectPoolService`、`IGameObjectPoolService` → `IObjectPoolService`、`DefaultGameObjectPoolHandler` → `DefaultObjectPoolHandler`、internal `IGameObjectPoolInternals` → `IObjectPoolInternals`、`ServicePriorityOrder.GAME_OBJECT_POOL` → `OBJECT_POOL`，同族其余类型现均带 `ObjectPool*` 前缀（`ObjectPoolServiceSettings`/`ObjectPoolSource`/`ObjectPoolServiceDebuggerWindow`），文档改 `Documentation~/ObjectPool.md`。迁移口径 = 池类型的 `GameObjectPool*` 前缀统一换成 `ObjectPool*`，成员与语义不变；腾出的 `ObjectPoolService` 这个名字此前属已删除的通用对象池服务（见 Removed 的 Pooling 条目），现由 GameObject 池独占

### Removed

#### Pooling

- ⚠ 删除通用对象池服务（拆除时的名字 `ObjectPoolService`/`IObjectPool<T>`/`ObjectBase`）及其调试窗口与设置资产：全仓无生产消费面，C# 对象复用走 `MemoryPool`、GameObject 复用走 `ObjectPoolService`（该名已由 GameObject 池服务更名接管，见 Changed 的 Pooling 更名条目），无需迁移（外部引用者请改用上述两套）

#### Kernel

- `Runtime/Services/Kernel` 独立为根级 `Runtime/Kernel`：物理目录迁移，命名空间与公共 API 不变；Core 依赖棘轮豁免目录名册同步改判据（`ContainerKernelFileRoster` 已留账）

#### Core

- 门面读路径未就绪可观测：`[ProviderHandlerHost]` 生成的 `Internal_PeekHandler()` 在编辑器/开发构建对「处理器未就绪即读」一次性告警（文案前缀 `Facade unready read:`，每门面每会话至多一条，Release 静默零成本）；11 个服务门面共 239 处 `s_Handler?.X() ?? default` 降级站点改走 `Internal_PeekHandler()?.`，写路径 `RequireHandler` fail-fast 口径不变
- ⚠ 删除 `[AutoRegisterService]` 及其全部支撑面（特性类型、生成器实例注册支路、MA2103/204/205）：框架程序集已零标记（12 门面 static 化退役全部使用点），这条与「服务装配单一路径」相抵的项目侧自建服务汇入通道不再保留——迁移口径 = 项目侧服务在 `GameApp.onServicesComposing` 订阅方内显式 `GameServices.RegisterService`，或门面挂 `[ProviderHandlerHost]` 走 settings 后端选型；MA2001 契约白名单不再收 `[AutoRegisterService]` 类的实现接口，特例注册服务（UIService 双轨）的依赖声明以具体后端类型为准，号段不复用
- ⚠ 删除服务契约上的轮询成员 `IObjectPoolService.Tick`、`ITimerService.FixedTick`/`LateTick`：逐帧驱动只认能力接口 `IServiceTickable`/`IServiceFixedTickable`/`IServiceLateTickable`——实现业务契约本身不再带来任何驱动，后端在实现契约之外按需实现能力接口（内建后端已全部实现，全仓无按契约类型调用这些成员的站点）
- ~~`MA1005`~~ 退役：原「服务后端仍继承 `FrameworkHandler`」的过渡告警随之取消——接口契约宿主的默认后端只认 `ServiceBase`，非法声明由 `MA1003` 在编译期判红，号位不再复用

### Changed

#### Timer

- ⚠ 删除 `TimerServiceHandler` 抽象基类：计时器后端改为直接实现唯一契约 `ITimerService`（`DefaultTimerHandler` 签名变为 `ServiceBase, IServiceTickable..., ITimerService`，原 `internal override` 全部转 public）；自定义后端迁移口径 = 直接实现 `ITimerService` + 继承 `ServiceBase`（绑定即注册进容器），`TimerServiceSettings.TimerServiceHandler`、`Internal_UseHandler` 等签名同步改按接口类型

#### Editor

- `[ProviderHandlerHost]` 源生成器接口契约定型（绑定即注册）：默认实现校验改为「实现处理器契约（继承类或实现接口）+ 继承 ServiceBase 基座」（`FrameworkHandler` 派生的服务后端判非法，服务侧只有一条经容器的装配路径，生成代码不按实例类型分派），生成代码的生命周期调用改为「注册进容器（契约绑定）→ 注册结果回填缓存槽」，新增 `InvalidateHandler` 缓存失效钩子与 static 宿主 `WarmHandler` 预热发射，宿主按 `static` 写（生成器读静态与否决定发射 `static partial class` 与要不要发 `WarmHandler`，实例宿主只是拿不到预热入口——没有任何诊断拦截非静态宿主，`MA1003` 判的是声明的默认处理器可否实例化）；类契约宿主（Log/GameTime/Json 等 Utility 族）行为不变（`Internal_Init`/`Internal_Shutdown` 直驱）

#### Audio

- ⚠ 删除 `AudioServiceHandler` 抽象基类：音频后端改为直接实现唯一契约 `IAudioService`（`UnityAudioHandler`/`MiddlewareAudioHandler` 签名变为 `ServiceBase, IServiceTickable, IAudioService, IAudioFadeTarget`）；原基类共享语义（总线 Fade 六方法、`SetVoicePitch`、音轨暂停标记越界守卫、场景切换停音判据）收进 internal `AudioBackendPolicy`（this + 自持调度表作参数的组合替代继承）；`AudioServiceSettings` 字段、`AudioCategory.Handler`、`AudioAgent._audioHandler` 等签名改按接口类型。自定义后端迁移口径 = 直接实现 `IAudioService` + 继承 `ServiceBase`（FMOD/Wwise 插件经 `MiddlewareAudioHandler` 家族基座自动跟随）；过渡调度表（internal `AudioFadeScheduler`）仍留在各后端自持

### Fixed

#### Audio

- `AudioHandleRegistry.RegisterUser` 头表插入触发扩容后返回旧表槽位，链头被写进新表里别的 key 的位置：第 8 个唯一 userId 起（初始头表 16 半载即扩）按 ID 链错位/丢链，`StopByID`/`ForEachHandleByUser`/`Release` 解链失效。扩容后改按 key 重查新表槽位返回

#### Asset

- `AssetService.IsInitialized` 与 `AssetServiceHandler.IsInitialized` 按「≥ `EServiceState.Initialized`」判就绪，把关停中与已关闭（`Disposed`）的后端一并报成就绪，与内核 `ServiceWorld.IsServiceReady` 的严格判据分叉；`AssetReference` 的租约释放与加载核心路径据此对已拆除的后端发起取用。两处收紧为严格 `== EServiceState.Initialized`

#### 测试（反膨胀治理）

- Audio 测试夹具的 16 处反射直调 `OnInit` 全部改走 `IServiceLifecycle` 显式转换接缝（8 个 PlayMode/Player 文件；架构回摆前曾短暂走 `Internal_Init()`，后端改挂 `ServiceBase` 后统一为容器生命周期接口）：反射绕过初始化闸使关闭空转，每次 SetUp 泄漏 `sceneLoaded` 订阅与一棵 DDOL 树；接缝化后生命周期门与生产同路，顺带修掉该错位。删掉名不副实的 `PriorityStealSelection_StaysWithinBudget` 基准格（首轮抢占后通道转 free，后续 999 发全走空闲路径，测的并非抢占选择）与 `AudioCacheTestSupport.CompleteNext` 的死参数 `address`（文档声称覆盖回调地址，实现从未使用）；`AudioVolumeParityTests` remarks 的旧值域口径 0..10 更正为 0..1（契约 `AudioGroupConfig.MAXIMAL_VOLUME`）

#### Localization

- `LocalizationService.ChangeLanguage(Language)` 重载补上 `HandPreferenceToHandler()` 交接：三个切换重载此前只有它漏交接检测链读数，经该重载首切的处理拿不到偏好（报 `Language.Unspecified`），与文档「每次取用处理器之前交接」不符

#### UI

- `UIService.CloseUI(type, windowId)` 带标识路径补类型守卫：标识与类型指向不同窗口（如 `CloseUI<A>("B的id")`）此前静默误关；现按空操作处理并 Error 告警，与 `GetWindow<T>` 的类型守卫同口径

#### Save（测试）

- `SaveMigrationBusTests` 订阅/退订改用具名方法组：原两端各自编译的匿名 lambda 不是同一委托实例，退订永不匹配，订阅跨用例泄漏进程级 EventBus

#### 测试

- `AudioHandleRegistryTests` 补头表扩容回归用例（唯一 userId 数越过半载阈值，逐 ID 验链完整）并修两处永真断言（`AreSame(registry, registry)`、压测桥 `PlayCount >= 0`）；`LocalizationServiceHandlerTests` 补三重载交接齐一用例；`UIServiceHandlerOrchestrationTests` 补标识与类型不匹配不误关用例——三处均按先红后绿验证

#### Kernel

- 契约键绑定语义修正：容器把 `[ProviderHandlerHost]` 自报契约绑定到**真实后端实现**（`IXxxService` → 已迁移的 `XxxHandler : ServiceBase, IXxxService`；未迁移过渡期按实例类型分派）——原实现里契约绑在未实现该接口的门面实例上（`GetRequiredService<IXxxService>()` 解析坍缩为 null、boot 在首个接口互调即中断），随架构回摆一并消除

#### PackageManager

- 勾选「安装/更新前先清目录」时，安装目标位上的外来同名目录视同已批准覆盖、随清目录一并先删再装；原实现仍走覆盖确认回调，回调缺席即整批中止，勾了清目录也装不下去。未勾清目录的拒绝路径行为不变

#### Editor

- `ProviderCatalogIndexTests` 归行判据夹具的 providerBase 默认值改取 `typeof(FakeBase).FullName` 运行时全名；原硬编码字符串随测试命名空间改名而静默失配，清单行整片铺不出来（3 个用例红）

#### Editor

- API Compatibility Level 切换为 .NET Standard 2.1：`IAudioService` 的可选能力/共享语义默认实现与 `ITimerService.GetLeftFrames/WaitAsync` 回收为接口默认方法（DIM），`UnityAudioHandler`/`MiddlewareAudioHandler`/`DefaultTimerHandler` 的显式补齐与纯默认转发行删除——DIM 仅在 .NET Standard 2.1 profile 下生效（.NET Framework 4.x profile 不支持）

#### ConfigTable / Debugger / Scene

- ⚠ 删除 `DebuggerServiceHandler`/`ConfigTableServiceHandler`/`SceneServiceHandler` 抽象基类：三服务后端改为直接实现唯一契约 `IDebuggerService`/`IConfigTableService`/`ISceneService`（`DefaultXxxHandler` 签名变为 `ServiceBase, I{Service}`）；`ConfigTableServiceSettings.InjectConfigTableHandler<T>` 泛型约束改 `where T : class, IConfigTableService, new()`（DIM 需 .NET Standard 2.1 API profile）。可选能力（按语言取列）走接口默认方法，自定义后端不覆写即回默认

#### Input / Procedure / ObjectPool

- ⚠ 删除 `InputServiceHandler`/`ProcedureServiceHandler`/`GameObjectPoolServiceHandler` 抽象基类（池服务随后整体更名为 `ObjectPool`，见 Changed 的 Pooling 更名条目）：三服务后端改为直接实现唯一契约 `IInputService`/`IProcedureService`/`IObjectPoolService`（3 个输入后端 UIMobile/UnityInputManager/UnityInputSystem 与 DefaultProcedureHandler/DefaultObjectPoolHandler 签名变为 `ServiceBase, I{Service}`）；`ProcedureServiceHandler` 的切换历史/广播承载收进 internal `ProcedureBackendPolicy`（后端自持状态 + 一行转发）；`ObjectPoolService.TryResolveInstance` 因参数类型 internal 移入 internal `IObjectPoolInternals`；下游消费方（CharacterActions/PlayerActions）签名同步接口化。`UIServiceHandler` 保留——它是 uGUI/UITK 双轨并存的家族基座（多实例托管，非单选策略契约）

#### Asset（原 Resource）

- ⚠ 服务整体由 `Resource` 更名为 `Asset`：命名空间 `Moirai.Atropos.Resource` → `Moirai.Atropos.Asset`，类型族（`ResourceService`/`IResourceService`/`ResourceServiceHandler`/`ResourceServiceSettings`/`ResourceOwner`/`ResourceKey`/`ResourceAssetLease` 等）改按 `Asset*` 命名（复合名并入既有资产类型族：`ResourceAssetInfo`→`AssetInfo`、`ResourceAssetLease`→`AssetLease`、`EResourceAssetKind`→`EAssetKind`），服务方法（`ProcessResourceMaintenance`→`ProcessAssetMaintenance`、`RetainResourceKey`→`RetainAssetKey` 等）与目录 `Services/Resource`→`Services/Asset`、文档 `Resource.md`→`Asset.md`、设置资产 `ResourceServiceSettings.asset`→`AssetServiceSettings.asset`（GUID 不变、托管引用同步）一并迁移；`ResourceServiceSettings.asset`/测试字符串全名已同步，已配置自定义后端实例无损。迁移动机：`Resource` 只描述「资源」而未贴合服务本体（YooAsset 资产加载 + Lease/Binding 生命周期的资产服务），`Asset` 与类型族 `AssetReference`/`AssetSlot`/YooAsset 对齐。Unity `Resources` API、`Resources/` 特殊目录与 YooAsset `package.CreateResourceDownloader` 等外部 API 不在迁移范围，保持原名；下游需同步替换 using 与类型引用
- 新增唯一契约接口 `IAssetService`（60 个公共契约成员收编，语义文档与门面同源）；`AssetServiceHandler` 抽象基座保留并实现接口（abstract 成员满足契约义务，72 成员实现链零改动）——Asset 采用「契约接口 + 引擎基座」混合形态，addressables 等外部插件兼容性无损；internal `IAssetLeaseSource` 租约窄缝提取独立文件，internal `IAssetLeaseDiagnostics` 诊断附属接口（`GetLeaseSourceInfos` 显式实现，internal 参数类型不进公共契约）；`AssetServiceSettings` 字段、`IPrefabLoader`/`OperationsWindow`/`AssetBindingExtension`/编辑器包名桥与探针、消费方测试签名全部接口化

#### Localization

- ⚠ Localization 支撑组件公共序列化字段收编为私有 `m_` 前缀 + PascalCase 属性（`ImageLocalizer` 的 `localizedTextID`/`propertyName`/`texture2Ds`/`sprites`/`textures`、`AudioLocalizer` 的 `localizedTextID`/`clips`，`PlayFromSamePositionWhenInject` 改为只读属性；Timeline `TextLocalizerPlayableAsset.textId` → `m_TextId`、`TextLocalizerPlayableBehaviour.textId` → `TextId`）：组件字段挂 `FormerlySerializedAs` 保旧序列化键，场景资产无损，代码引用旧字段需改用新名或属性
- ⚠ 命名空间 `Moirai.Atropos.Localization` 迁移为 `Moirai.Atropos.Localization`（类型与目录不变；`LocalizationServiceSettings.asset` 的托管引用 ns 已同步迁移，已配置的自定义后端实例无损；下游 using 需同步替换）
- 新增唯一消费契约 `ILocalizationService`（门面公共 API 收编，语义文档同源）：引擎基座 `LocalizationServiceHandler` 保留（1500+ 行共享实现豁免契约化），后端经派生基座落地；internal 协作面（偏好解析/编辑器预览）不进公共契约，框架内部按引擎基座引用访问；`Settings` 字段与消费方测试签名按契约类型持有

#### Save

- 新增唯一消费契约 `ISaveService`（28 个门面公共 API 收编，语义文档同源）：引擎基座 `SaveServiceHandler` 保留（2600+ 行共享实现豁免契约化），后端经派生基座落地；internal 协作面（存储后端选择/原始块读写/截图写入）不进公共契约，框架内部按引擎基座引用访问；`Settings` 字段、调试窗口与消费方测试签名按契约类型持有

#### Localization

- ⚠ 回滚 `Moirai.Atropos.L10n` 命名空间迁移：类型命名空间改回 `Moirai.Atropos.Localization`（`LocalizationServiceSettings.asset` 托管引用 ns 同步回迁，实例无损；下游 using 同步替换）；`ILocalizationService` 薄契约保留。顺带修复：反射守卫 Allowlist 两条 Resource 路径同步 Asset 目录更名（Resource→Asset 更名的遗留腐烂）

#### Kernel / 注入互调（Phase 2 任务①）

- `[ServiceDependency]` 依赖声明改指契约接口（20 处具体类依赖清零，`UIService` 双轨豁免保留具体类）：`BuiltinServiceRegistrationGenerator` 为带 `[ProviderHandlerHost]` 的服务生成契约接口附加绑定行（同实例多契约注册），`MA2001` 放宽为「实现 IService 或为 [ProviderHandlerHost] 自报的契约接口」（跨程序集契约接口收集）；容器注册键（具体类）不变，`EnsureRegistered<T>` 调用习惯零变更

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

#### Phase 5 / T5 + T6 剥离（快赢两项）

- **T6** `JsonUtility` 错误文案改自述（"Can not convert..." → "JSON 序列化/反序列化失败"）——8 处
- **T5** `EncryptionUtility` → **`XorCipher`**（Obfuscate/ObfuscateInPlace/ObfuscateQuick 语义族——XOR 对称：同一函数即解密，不设 Deobfuscate 分立入口）；旧类 Obsolete 垫片转发（下游 juice 平滑迁移）
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

#### UI / 页面级导航

- **新增 `UIService.Navigation.cs`**：`NavigateBack()`（关闭当前顶窗走既有关闭政策 + 恢复开启序历史中前一个已停放窗口——复用停放实例）与 `NavigateBackTo(windowId)`（逐次 Back 直到目标成为顶窗，次数上限 = 导航深度防环）
- `UIWindowLedger` 补查询接缝：`PeekPreviousClosedHistoryWindow`（history 逆序找第一个未开启窗——多窗并存时跳过仍开启者）+ `IsWindowOpen`
- **天然去重**：基于既有 history 栈语义（复用/停放重取自动挪到最新）；复用窗开启序维护，零新增状态

#### 事件管线收工——原事件系统（Runtime/Core/Events）彻底移除

- **吸收清单兑现**（原系统先进设计 → EventBus）：
  - `EventDispatchPolicy` 迁入 Kernel/Events（异常分级门控——开发期上抛/发布期隔离）
  - `DispatchMode` 枚举（Immediate/Queued）+ `EventBus.Publish(in evt, DispatchMode)`——Queued 经 MainThreadDispatcher 主线程队列派发
  - `EventPool<TEvent>`：class 事件池（带引用负载的大事件保留 class 形态走池——IPoolableGameEvent.Reset 契约 + 幂等归还）
  - Debugger 接口对接（EventBusDebuggerWindow，此前完成）
- **Task 系迁移总线**：TaskCompleteEvent class 池化保留（EventPool）+ 引用包装 struct 走总线；TaskCallbackEventHandler 树传播宿主删除；R3 AsObservable 桥弃用改直接订阅
- **删除**：Runtime/Core/Events 全目录（3917 行：EventBase/EventDispatcher/EventCallbackRegistry/MonoEventCoordinator/EventManager/ChangeEvent/PropagationPaths/EventDebugger 等）+ Editor/Core/Events（旧调试窗）+ R3 CallbackEventHandler 扩展（消费者清零）+ 下游 134 文件残留 using
- **模板资产保留**：EventsDebugger.uxml/uss 迁出保留（UITK 测试夹具依赖——uxml 自足化去已删控件引用）
- 回归 **2952 用例 / 2941 过 / 0 失败 / 11 跳过**

#### Events / 事件管线收工——原事件系统彻底移除（EventBus 收编完成）

- **删除 `Runtime/Core/Events/`**（15 文件 3917 行：EventBase/EventManager/MonoEventCoordinator/EventDispatcher/EventCallbackRegistry/EventHandler/派发策略族/EventDebugger 族/ChangeEvent/PropagationPaths）——Phase 3 迁移后下游引用面已为零（gameplay/juice/platformer/Client 全部走 EventBus），死代码清退
- **删除 `Editor/Core/Events/`**；UI 测试探针夹具 `EventsDebugger.uxml/uss` 迁移至 `Editor/TestFixtures/Resources/`（3 个测试路径引用同步更新）
- **全生态残留清理**：130 文件死 `using Moirai.Atropos.Events` + 注释中 EventManager/EventBase cref → EventBus（juice/plugin 族/Client Assets 全扫）
- **吸收项补齐**：
  - EventPool 接线：EventBus 加 `RentPooled<T>/ReturnPooled<T>` 门面（大事件三步模式：Rent 负载 → Publish struct 事件携带引用 → Return 归还）
  - `[GameEvent]` 标记 + **GameEventAttributeAnalyzer**（MA7004–MA7006：标记类型必须 struct + 实现 IGameEvent + 禁实例方法——编译期约束校验，EventBusAnalyzer.dll 重建；诊断 ID 从 MA7001–MA7003 迁出以避开订阅规范占用）
- **验收**：编译零错误；回归 2952/2941/0 失败（净 -13 = 旧事件系统测试随目录清退）

#### Events / EventPool 删除——池化统一 MemoryPool

- **删除 `Runtime/Kernel/Events/EventPool.cs`**（独立事件池与 MemoryPool 职责重复——框架统一对象池归一，"三池归一"验收标准的最后一笔：MemoryPool（C# 对象）+ ObjectPool（GameObject），事件池不再单列）
- `TaskCompleteEvent` 迁移 MemoryPool：继承 `MemoryObject`（Clear() 契约），Rent/Return → `MemoryPool.Acquire/Release`
- EventBus 刚加的 RentPooled/ReturnPooled 门面一并撤除（MemoryPool 即统一入口，事件负载不设二次门面）；IGameEvent/GameEventAttribute 注释同步
