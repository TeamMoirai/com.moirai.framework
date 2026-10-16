# 编码细则（框架包）

本块是本包该主题的唯一权威落点：`CLAUDE.md` 与命令正文只留摘要与指针，同一段话在两边各写一遍即视为缺陷。

## 编码规范

生产级 C# 编码规范（强制执行）。**Why:** 用户要求所有 Unity C# 系统编写、优化、重构时严格按照此规范执行，确保 AAA 商业化代码质量。**How to apply:** 所有 Unity C# 代码编写任务均以下述规范为基线。

- **核心原则：** 性能即特性（热路径 0-Alloc，帧预算内完成）；确定性（避免反射/动态生成，确保 IL2CPP 一致）；可读性即维护性；Fail-Fast（Editor 断言优先，Runtime 防御性检查）。
- **命名：** 以 IDE 实拦的口径为准——见下方《命名规范》表（规则源 `Client/Client.sln.DotSettings`，表内右列记录本仓实测分布与历史偏差）。字段前缀分档：`m_` = 私有家族序列化字段（private/internal/protected）、`_` = 私有家族非序列化实例字段、`s_` = 私有家族静态字段；公共家族（public/protected internal/file-local）序列化字段无前缀 `lowerCamelCase`、其余公共字段无前缀 `PascalCase`，据此杜绝 `this.` 冗余。`var` 仅当右侧类型明确时使用。Allman 大括号，4 空格缩进。
- **0-Alloc 热路径：** 禁止 new/LINQ/foreach 非泛型；禁止 lambda/匿名委托（缓存方法组）；禁止 + 拼字符串（用零分配字符串工具或 StringBuilder 池）；禁止每帧 ToUpper/ToLower；禁止 params；返回空集合用 Array.Empty<T>()；临时缓冲区优先 stackalloc + Span<T>。
- **内存布局与 Cache 友好：** struct ≤ 16 bytes 且 readonly；批量数据优先 SoA 提升 cache locality；高频值类型实现 IEquatable<T>；互操作场景用 [StructLayout(LayoutKind.Sequential)]；多线程写入字段防 false sharing。
- **Span 与 unsafe：** 字符串/JSON/二进制解析用 ReadOnlySpan&lt;char&gt;/ReadOnlySpan&lt;byte&gt; 避免 substring 分配；unsafe 仅限性能关键场景（指针操作/直接内存拷贝），须注释说明；allowUnsafeCode 按 asmdef 粒度开启。
- **防装箱：** 通用工具必须泛型接口（IEquatable&lt;T&gt;）；禁止 ArrayList/Hashtable/非泛型 Queue/Stack；禁止 Enum 传 object（用泛型 Enum.Parse&lt;T&gt;）；禁止 object 参数函数（用泛型）；禁止热路径 Debug.Log（用封装日志工具）。
- **线程安全：** 跨线程共享字段用 volatile/Interlocked；Unity API 仅主线程调用，异步续体须 EnsureMainThread() 守卫或 Dispatcher 入队；锁仅限非热路径初始化，热路径用 lock-free；CancellationToken 贯穿所有异步操作。
- **对象池化：** 高频创建/销毁对象（事件、任务、缓冲区、GameObject）必须池化；池接口沿用各池家族既定词汇——内存池 `Acquire`/`Release`、事件 `Acquire`/`Dispose` 配对、对象池服务 `Spawn`/`Despawn`，新池先对齐同家族词汇不另造第三套；池对象实现状态重置；容量按场景配置，支持运行时回收。
- **Unity 引擎：** GetComponent 必须 Awake/Start 缓存；禁止 GameObject.Find/SendMessage/BroadcastMessage；私有序列化字段 m_ 前缀（公共序列化字段无前缀 lowerCamelCase）；yield return 缓存静态只读或用协程工具；高频异步用 UniTask（禁止同步 IO 和 Coroutine 做 IO）；用 Mathf 不用 Math；ScriptableObject 做数据驱动配置并运行时缓存引用。
- **异常与错误处理：** 禁止 try-catch 做逻辑控制；热路径严禁 try-catch（**例外**：`PlayerLoopDriver.HandlerSlot/CallbackSlot.Drive` 与内核 `ServiceScope` 轮询循环内的 per-subscriber try/catch 属有意隔离——订阅/服务抛出不得截断同阶段其余项；异常本身仍按分级上抛或隔离，不吞）；用 Debug.Assert/Assert.IsTrue（仅 Editor）；非热路径公共 API 做参数校验抛 ArgumentException；异常不吞——要么处理要么上抛。
- **代码组织：** 一文件一顶层类；类/接口/公有方法/枚举必须 &lt;summary&gt;（内容独占行，见《XML 文档注释》）；严禁 TODO 入主干；#region 用于小范围分组（双语标签），严禁大段折叠掩盖 SRP 违例（违反则拆类）；asmdef 最小化依赖、禁止循环引用。
- **AOT/IL2CPP 兼容：** 禁止 Reflection.Emit/动态代码生成；反射仅限序列化/编辑器，运行时避免；泛型 AOT 预编译缺失时需预生成元数据或用非泛型路径；Type/enum 缓存为静态只读字段避免反复 GetType。
- **测试可见性（强制）：** 测试不得用反射读写字段/属性（`GetField("m_…", BindingFlags.NonPublic)`）——需要触达的成员把访问级别 `private`→`internal`，`Runtime/AssemblyInfo.cs` 已对 `Moirai.Atropos.Editor` 与三个测试程序集（`.Tests.EditorMode`/`.Tests.PlayMode`/`.Tests.Player`）开了 `InternalsVisibleTo`。反射把字段名变成测试依赖：改名不报编译错，只在运行期 `GetField` 返回 null 后 NRE；`internal` 由编译器把关。序列化字段改 `internal` 不影响 Unity 序列化（`[SerializeField]` 不要求 `private`），前缀仍走 `m_`/`s_`/`_` 私有家族口径。反射只留两类正当用途：行为契约守卫（断成员标注、经反射触达条件编译成员——`YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`、`AddressableHandlerFailFastTests`，这类只能反射，别当违例删掉）、唤起 Unity 生命周期回调（`Awake`/`OnEnable`/`OnInit`）。API 形状不进测试登记（签名快照、成员计数、名单冻结类用例不建），破坏性更新在 `CHANGELOG.md` 记迁移口径。已有窄接缝的成员不为此放开字段：换入换出走 `Internal_PeekHandler()`/`Internal_UseHandler(next)`（常规情形由 `[ProviderHandlerHost]` 生成），`s_Handler` 保持 `private`。`UIService` 是例外：它不声明 `[ProviderHandlerHost]`，每支后端各一个**具体类型**的处理器槽（`s_UGUIHandler`/`s_UITKHandler`，都是 `private`），只读接缝是手写的 `Internal_PeekUGUIHandler()`/`Internal_PeekUITKHandler()`，**没有换入接缝**——后端实现固定，加一支后端是加一个 `UIService.<轨>.cs` partial（自登记一个 `UITrack` 进门面目录，主文件零改动）；夹具要回到干净域状态走 `Internal_ResetHandlerSlots()`（不收实参、不能把对象放进槽；它同时清掉目录里各轨的关停回调认领）；合成轨登记的处置走 `Internal_UnregisterTrack()`。
- **工具链与质量门：** 启用 Roslyn Analyzers；.editorconfig indent_size=4；提交前通过 ZeroAlloc 性能测试；PR 须通过编译 + Analyzer + 测试三重门。
- **公开面收敛（强制）：** 框架非公开 API 不向外扩可见性——能 `internal` 就别 `public`，公共面每多一个类型/成员都是在引导使用方依赖不该依赖的接缝（用户 2026-10-16 口径）。外部插件（`plugin` 分支）要触达的扩展面只按需开给派生方：优先契约基类上的 `protected internal` 成员，类型则嵌进契约基类（如 `LogHandler.ELogLevel`）。实测约束：命名空间级类型不能写 `protected internal`（CS1527，只能 `public`/`internal`）→ 嵌成嵌套类型；静态类不能带 protected 成员（CS1057，`LogUtility.GetBypassUnityHandler` 这类由可派生的基类以 `LogHandler.BypassUnityHandler` 转出）；`protected internal` 是「protected **或** internal」的并集，对外部程序集等价于 `protected`（`private protected` 才是「且」，方向相反插件反而用不了）；跨程序集覆写须声明 `protected`（写 `internal override` 报 CS0507，**友元身份不豁免**）。`InternalsVisibleTo` 只留给框架自家与产品程序集（Editor/三个测试集/Clotho/Lachesis），不逐插件放行。
插件外置时按**使用者是谁**给扩展面定档（2026-10-16 十四支后端外置实测得出的三分法）：仅供**派生后端**取用的成员写 `protected internal`，
内核类型一律**不出现在** `public`/`protected` 签名里——要传能力就开方法级接缝（`ResourceRecordHostHandler` 上 26 枚 `protected internal`，
`ResourceRecordStore`/`ResourceKeyCodec`/`IResourceLeaseSource` 等内核件保持 `internal` 原样）；只在派生侧出现的**类型**嵌进契约基类
（`LogHandler.ELogLevel` 那种）；而**非派生类也要实现**的类型（音频桥 `IAudioMiddlewareBridge`/`IAudioMiddlewareBankControl`/
`IAudioMiddlewareRtpcControl` 与 `EAudioBankLoadResult`）既嵌不进去（插件里的桥类不是派生方，`protected internal` 对它不可见）
也躲不开 CS0050/CS0051，按契约面定性取 `public`，并**留在命名空间级不改名**——嵌进基类会改类型全名，撞"迁移不改类型名"这条
（按全名反射的判据与 `[SerializeReference]` 资产都认全名）。反过来，框架自用而插件不碰的面（`IAudioFadeTarget`、
`IsBackendInert`、`VoicePoolCount` 这类）保持 `internal`：基类转 public 不等于把它基列表里的内部接口一起放出去，
CS0061 由"嵌套一个 private 实现类 + 一枚 `internal` 窄接缝"解掉（`MiddlewareAudioHandler.FadeSink` + `Internal_PeekFadeTarget()`）。
跨装配的能力判据用 `protected internal virtual` 让**被判定方自报**（`ResourceServiceHandler.ConsumesPackageNames`），
Editor 侧以友元身份读，绝不在框架里 `is 具体插件类型` 或按插件宏分支——那种判定会被预处理静默裁成恒假。框架侧引用嵌套类型走 `using static Moirai.Atropos.LogHandler;`（派生类内裸名可用），不改公共签名。后端家族统一口径（2026-10-16 全包盘点结论）：**契约基类一律 `public`**——各 `XxxServiceHandler`/`SettingHandler`/`YooAssetEncryptorHandler` 等是 settings 的 Provider 下拉与外部派生后端的接缝（`Testing.md` 明说这些基类就是给所有程序集扫描派生用的；已证外部消费者 = `InputServiceHandler` 作参数类型、`ConfigTableServiceHandler` 被 `Moirai.GameProto.LubanHandler` 派生）；**实现类一律 `internal`**（含带 `[ProviderDisplay]` 的内置后端：`DefaultLogHandler`/`YooAssetHandler`/`DefaultSettingHandler`/`DefaultGameObjectPoolHandler` 等），例外是 `Internal/` 目录里**被用户程序集生成代码或用户实现反向调用**的注册器与接口（`UIWindowRegistry`/`SaveSerializerRegistry`/`ISaveSerializer` 之类），它们必须 `public`；`SourceGenerators` 的生成器/分析器类保持 `public`——实测 internal 也能被 Roslyn 加载（探针 PROBEINT001/002 命中），但入库 DLL 不可字节复现（同源码重建哈希即变），不为可见性去动发行二进制、免得源码与产物脱节。后端装配一律走 settings 的 Provider 下拉（`Handler` 属性由生成器发出、为 `internal`，只限框架与友元程序集赋值），别在文档里写"游戏侧 `XxxService.Handler = new …` 安装"。
- **执行等级：** Mandatory（违反打回：命名前缀、0-Alloc、防装箱、AOT 兼容、测试可见性、公开面收敛）/ Prefer（性能敏感区必须，非热路径可放宽：Span/unsafe/池化/线程安全）/ Reference（逐步优化遗留）。

### 命名规范

规则源是解决方案级的 `Client/Client.sln.DotSettings`（Rider / ReSharper 实拦）。两个前提要先知道：

- `ApplyAutoDetectedRules=False`——关掉的是"从现有代码里自动学出来的"命名规则（Rider 会照着仓库里的高频写法生成告警），本仓的口径由表内这些显式规则自己承担。内置预定义规则（类型/成员 `PascalCase`、参数 `camelCase`、接口 `I`、泛型 `T` 等）仍按 IDE 默认生效，所以表里"未配"不等于"随便写"。
- `CheckNamespace` 降为 `SUGGESTION`——命名空间与目录不一致只提示不报红，这是给下面《程序集与命名空间》那条留的口子。**目录与命名空间解耦是有意设计**，不是待修的历史偏差：目录按职责归位只搬文件位置，命名空间、程序集归属与公共 API 面一律不动（前置条件是这条停在 SUGGESTION，否则整棵树一起报黄）。

改前缀或风格必须连这张表一起改，否则规范与 IDE 各说各话。表右列"本仓实测"是 grep 出来的约数（字段与常量取 `Runtime`，事件取 `Runtime`+`Editor`），只用于说明历史，**不是可复制的写法**。

| 元素 | DotSettings 规则 | 本仓实测 | 新代码口径 |
|---|---|---|---|
| 类型 / 方法 / 属性 / 命名空间 | `PascalCase` | 一致 | 同 |
| 接口 | `I` + `PascalCase` | 全部（`IAudioClipLeaseSource`、`IAudioMiddlewareBridge`） | 同 |
| 枚举类型 | `PascalCase` | 主流 `E` 前缀（`EAudioTrack`、`EResourceAssetKind`）；历史无前缀（`UIType`、`TaskStatus`、`TimerPhase`） | 一律 `E` 前缀；运行期状态枚举显式 `: byte`；非运行期状态的配置档枚举（如 `EUILayer`，其整数值进特性与深度算术）保持原底层类型 |
| 枚举成员 | `PascalCase` | 一致 | 同 |
| 局部变量 / 参数 | `camelCase` | 一致 | 同 |
| 实例字段（`private`/`protected`/`internal`，非序列化） | `_camelCase` | ~650 处 | 同 |
| 序列化字段（`private`/`internal`/`protected`） | `m_PascalCase` | ~332 处；公开面一律包 `PascalCase` 属性（`public int ID { get => m_ID; internal set => … }`） | 同；`public` 裸序列化字段允许（走下方 `lowerCamelCase` 口径） |
| 序列化字段（`public`/`protected internal`/file-local） | `lowerCamelCase`，无 `m_` | 一致（`ImageLocalizer.localizedTextID`、`AudioLocalizer.clips`） | 同 |
| 静态字段 / 静态 readonly（`private`/`internal`/`protected`） | `s_PascalCase` | ~87 处 | 同 |
| 静态字段 / 静态 readonly（`public`/`protected internal`） | `PascalCase`，无 `s_` | ~156 处 | 同 |
| `const`（任何可见性） | `UNIFORM_CASE` | 主流（~453）；~122 处 `PascalCase`（`MemoryPool.Core`、`Save`、`Audio` 的量纲/上限/位域）；2 处 `k_Default…` 为外来写法 | 新 `const` 一律 `UNIFORM_CASE`；想要 `PascalCase` 就写 `static readonly`（Rider 不把它算作常量，两者语义也确实不同）；`k_` 不再引入 |
| `event` 成员 | `on` + `PascalCase`（DotSettings 主前缀 `on`，`On` 作容错变体登记；该规则未开前后缀告警） | 31 处全部 `on`（`Runtime`+`Editor` 实测） | 新事件一律 `on` |
| 泛型参数 | 表内未配（走 Rider 默认） | `T`、`TKey`/`TValue`、语义式 `TVoice`/`TLexer` | 单参数 `T`，多参数 `T` + 名词 |

前缀由**访问级别**决定，不看是否"真私有"：`internal` 走 `private` 口径（带 `m_`/`s_`/`_`）；`public`/`protected internal`/file-local 无前缀——序列化字段 `lowerCamelCase`，其余字段与静态成员 `PascalCase`。

缩略词表里只登记了 `FSM` 与 `GOAP`（供 Rider 按整词切分，加前缀/自动重命名时不拆成 `F`+`S`+`M`）；`UI` 没登记而仓内一律写成 `EUILayer`/`UIService`。要用新缩略词前先决定"登记"还是"照抄现状"，别两边各写一半。

下列是**仓库既定词汇**，DotSettings 管不到，但新模块照抄、不另造同义词：

- **服务三件套**：`XxxService.cs`（门面）+ `XxxServiceHandler.cs`（后端契约，派生 `FrameworkHandler`）+ `XxxServiceSettings.cs`（配置资产）；默认实现 `DefaultXxxHandler`，按后端的 `<Backend>XxxHandler`（`YooAssetHandler`、`UnityAudioHandler`、`MiddlewareAudioHandler`）。
- **生命周期**：覆写点一律 `protected virtual On*`（`OnInit`/`OnShutdown`/`OnInitAsync`/`OnShutdownAsync`/`OnUpdate`/`OnLowMemory`），框架内部门面一律 `internal Internal_*`（`Internal_Init`/`Internal_Shutdown`）。调用方只碰 `Internal_*`，不要在 `On*` 上直接互调。
- **SDK 适配层**：`IXxxBridge` 主接口 + `XxxBridgeStub`（无 SDK 也要能编译）+ `XxxBridgeNative`（真 SDK，整文件由 `*_INSTALLED` 宏门控）；可选能力另开窄接口按能力探测（`IAudioMiddlewareBankControl`、`IAudioMiddlewareRtpcControl`），不扩主接口。
- **目录与文件**：`Runtime/Services/<Module>/` 顶层只允许下面这套**封闭角色词表**，其余目录名一律视为同义词归并，不许临时开档；新文件归位的判据 = 这张表 + R1–R8。

| 档 | 装什么 | 不该进这里 |
|---|---|---|
| 模块根（平铺） | 服务门面四件套：`XxxService.cs` + 其 `.` 分段 partial、`XxxServiceHandler.cs`（宿主）、`XxxServiceSettings.cs`（含 `.` 分段 partial）、`XxxServiceDebuggerWindow.cs`。门面**按实名认不按 glob**——Kernel 模块的门面实名是 `GameServices.cs`（+ `GameServices.DiagnosticInfo.cs`），不以 `Service` 结尾但计入根档 | 领域类型、后端实现 |
| `Handler/` | 可换后端缝：跨后端抽象与宿主 partial，每支后端一个 `Handler/<后端>/` | 后端之间的共享缓存/注册表（→ `Internal/`） |
| `Models/` | 数据面与类型面：`XxxEntry`/`Lease`/`Request`/`Options`/`Config`/`Slot`、枚举、struct/class 数据、事件与 Args 类型、Attribute，以及 `*Types.cs`/`*Abstractions.cs`/`*Callbacks.cs`/`*Contract.cs` 这类纯声明文件 | 带引擎行为的机制类；有实现的接口（见 R4） |
| `Objects/` | 服务所管理的对象类型、项目侧要继承的基类（`UIWindow`/`UIWidget`/`UIBase`、`ServiceBase`/`ServiceMono`、`ObjectBase`） | 只被模块自己 `new` 的机制件（→ `Internal/`） |
| `Internal/` | 模块私有机制：注册表、缓存、编解码、调度、状态机、迁移、容器、压缩、序列化、存储与云后端 | 用户会 `new`/继承的类型 |
| `Support/` | 挂场景或 GameObject 的 MonoBehaviour、运行时配套件与其域次层 | 纯 C# 数据类型 |
| `Extension/`、`Views/` | 按需档：确有扩展方法面 / 确有调试窗集合时才出现（`Resource/Extension/`、`Debugger/Views/`） | 为对齐而造的空档 |
| `Resources/` | Unity 路径语义目录（按名解析） | **不参与归位** |

规则：

- **R1** 一支后端一个 `Handler/<后端>/`，后端私有件（桥、平台文件系统、引擎实现）住在该后端目录内部；**单支默认后端不建 `<后端>/` 子目录**（否则给 ConfigTable/Scene/Procedure/Timer/Debugger/Localization 造六个只装一个文件的空档）。
- **R2** 域目录不作废，但只作角色档的次层：`Save/Internal/Container/`、`Localization/Support/Text/` 合法，`Save/Container/` 不合法。
- **R3** 一个模块含两支以上服务时先按服务分档、档内再按角色切：`Pooling/ObjectPool/{四件套,Handler,Models,Objects,Internal}/`、`Pooling/GameObjectPool/{…}`，两支共享的机制件在 `Pooling/Internal/`。**R3 只在两支队伍各自成套时生效**：判据是有没有第二套 `Handler`/`Settings`/`DebuggerWindow`，不是根上有几个 `XxxService.cs`——`Resource` 根的 `ResourceBindingService` 是 `internal sealed partial class ResourceBindingService : IResourceBindingService`（经 `ResourceService.BindingService` 取用，没有独立三件套），属同一服务的第二个门面族，**不拆**。
- **R4** 归属拿不准按消费者判：项目侧继承/`new` → `Objects/`，只有模块内部走 → `Internal/`，只是数据 → `Models/`，挂在 GameObject 上 → `Support/`；接口、枚举、Attribute、委托一律算类型面 → `Models/`（无论有无实现、实现在哪个档）；判"内部还是对外"不许凭名字猜，在 `Packages/` 全域 grep 类型名，命中包外即对外。
- **R5** 路径语义区不参与：`Resources/` 与两处 `.asmref` 射程保持原路径。
- **R6** UI 两腿例外：`Runtime/Services/UI/Handler/{UGUI,UITK}/UIService.<轨>.cs` 是后端自包含形状（见上文 UIService 例外条），留在后端目录，不搬回模块根。
- **R7** 一个文件只整体归一档，档位由**文件名所指的那个类型**决定，文件内共存的次要类型随之同行（不做"一个文件里两个类型拆两档"，本刀严禁改文件内容）。
- **R8** 整目录优先：内容同档的子目录整目录搬（连它的 `.meta`），不拆散同目录文件。

`Runtime/Core/` **不套这套角色词表**，按功能分组平铺 `Core/<功能>/`：`Attributes`、`Constant`、`DataStructure`、`Extensions`、`GameException`、`Events`、`GameApp`、`GameProfiler`、`FrameworkSettings`、`Graphics`、`MemoryPool`、`Obfuz`、`SettingPersistence`、`Singleton`、`Tasks` + `Utilities/`（横切工具，一个关注点一个子目录）。`Foundation/`、`Infrastructure/` 两个层级壳与 `Utilities/GameSetting/` 已解除；Core 没有 `Internal/` 这一档。层级名消失后依赖方向只剩一条机械判据：`Tests/EditorMode/Policy/CoreDependencyRatchetTests.cs`——扫 `Runtime/Core/**` 对服务类型的整词取用、豁免 `Kernel/**` 容器与同名类型、基线钉 3 站点只准降不准升（键用 `文件名|类型名`，与路径无关）。

`Editor/` 四域——`Tools/`（单点工具窗与构建工具）、`Foundation/`（编辑器侧基础件）、`Services/`（运行时服务的编辑器配套）、`Utilities/`；其中 `Editor/Services/<Module>/` 用编辑器侧的**五档封闭集合** + 模块根平铺配套件：`Drawers/`（属性绘制与自定义检视器）、`Windows/`（工具窗）、`BuildPipeline/`（构建期：转表、bake、BuildValidator、快照导出）、`Generator/`（产码，其配套件在 `Generator/Support/`）、`Support/`（编辑器侧桥与预览失效器等）。纯资产**不参与角色档**：单件的留在模块根（`Input/template-input-actions.txt`），成套的自成一目（`Input/Gizmos/`，8 张 `.psd` 加一份 `README.md`）——九件平铺在模块根比自成一目更糟，所以这里不硬套「留模块根」。五档之外、模块根上的目次只有两处：`Input/Gizmos/` 与 `Resource/YooAsset/`（后者是 `.asmref` 射程的禁碰区，不是资产堆）。

禁碰区（搬了就换装配或断引用）：`Editor/Services/Resource/YooAsset/Internal/**`（`YooAsset.Editor.asmref` 射程）、`Runtime/Core/Utilities/Tween/Handler/PrimeTween/AssemblyReference/`（`PrimeTween.Runtime.asmref`）、四处 `Resources/`（`Runtime/Services/{Debugger,UI}/Resources/`、`Editor/Foundation/{Attributes,Events}/Resources/`）、`Tests/**`（两份 Policy 门禁的白名单按 Tests 相对路径为键）、`Samples~`/`Templates~`/`Plugins`/`SourceGenerators`。新增文件先按上面归位再提交，不制造新一轮粒度割裂。
- **partial 拆文件**：`Xxx.<职责>.cs`，职责名是首字母大写的单个英文名词（`.Core`/`.Slots`/`.Maintenance`/`.Bindings`/`.Async`/`.IO`，在仓 66 个）；拆文件不破坏"一文件一顶层类型"。
- **通用后缀**：静态工具 `XxxUtility`（单数）、扩展方法 `XxxExtensions`、账本 `XxxRegistry`、缓存 `XxxCache`、调度 `XxxScheduler`/`XxxStateMachine`、调试器面板 `XxxServiceDebuggerWindow`。
- **键名常量**：Mixer 参数与设置键按 `<域>_<对象>_<属性>` 全大写（`AUDIO_MASTER_VOLUME`、`GRAPHICS_FULLSCREEN_MODE`）；存档 schema 字段是 `PascalCase` + `Key` 后缀（`LocalPositionKey`、`SpawnsKey`）。两套并存是历史，改到哪个文件就跟哪个，不新造第三种。
- **程序集与命名空间**：本包自带 asmdef 为 `Moirai.Atropos`、`Moirai.Atropos.Editor`、`Moirai.Atropos.Tests.EditorMode`/`.PlayMode`/`.Player`（`Templates~` 下的 `GameLib`/`GameLogic`/`GameProto` 是工程侧模板，不属本包）。测试侧编辑器工具（测试桥 `TestRequestRunner`/`EditorStateBridge` 与 Test Player Runner 窗口）住 `Moirai.Atropos.Tests.EditorMode`——`includePlatforms: Editor` + `UNITY_INCLUDE_TESTS` 门控：缺 Test Framework 包整程序集不编译、工具自动消失，常驻 Editor 程序集不背测试程序集依赖（单脚本独立程序集不再单设）。其中 `.Player` 是**玩家验收专用**测试程序集（`defineConstraints: ["UNITY_INCLUDE_TESTS"]`，2026-09-28 起编辑器可见——UTF 的玩家测试运行只收录编辑器可见程序集，旧组合「编辑器不编译 + Run all in Player」经实证在任何环境都不执行，L3 门禁此前从未真正跑过）：编辑器 PlayMode 套件会真跑其中的 0-GC 计量格（`GC.Alloc` 采样编辑器同样有牙；仅 AudioPerformance 3 格按宿主未配 `AudioGroupConfigs` 探针跳过），发布出口的 L3 验收仍以玩家侧报告为准；玩家构建语义不变——生产包不含它（`UNITY_INCLUDE_TESTS` 未定义即不编译），测试玩家构建原样编入。引用面收窄至玩家安全程序集（`UnityEngine.TestRunner` + `Moirai.Atropos` + `UniTask`），不引 Editor-only 的 `UnityEditor.TestRunner`——玩家域没有该程序集，历史容错（`.PlayMode` 曾原样进包）不代表被支持。运行期与编辑器代码一律 `namespace Moirai.Atropos[.<Module>[.<Sub>]]`；测试用与被测模块对齐的**短命名空间**（`Service.Audio`、`Core.Events`、`Core.MemoryPool`），不带 `Moirai` 根——这正是 `CheckNamespace` 降级要护住的写法。
- **测试**：类 `<被测>Tests`（`AudioClipCacheTests`）、基准 `<被测>Benchmark`（一律 `[Explicit]`，不随常规套件跑）、夹具 `XxxTestSupport`/`XxxTestHost`/`MemoryPoolFixture`（派生式基座）。方法名 `场景_条件_期望` 三段式（`RetainRelease_CycleAllocatesZeroBytes`、`PauseGame_NestedSources_OnlyLastResumeRestoresSpeed`）。异常断言沿 `InnerException`/`AggregateException` 链判定，不用 `Assert.Throws<T>` 硬匹配（泛型 `new T()` 实走 `Activator.CreateInstance<T>()`，原始异常会被包装）。

**冲突怎么判**：DotSettings 与代码打架时以 DotSettings 为准（它是门禁，也是评审依据）；表里没写、仓内已成词汇的那一档（`Handler`/`Bridge`/`Registry`/`Support`）按仓库现状走。两类冲突都不许用 `// ReSharper disable` 或规则抑制绕过——要改先改规则，再改代码。

### XML 文档注释

口径参考 Microsoft .NET API 文档惯例（`summary` 陈义 / `remarks` 补充 / `example` 用法 / 覆写用 `inheritdoc`）与 Google C# Style Guide「注释描述是什么，不描述怎么做与为何」。全文与 ✅/❌ 对照见 [`Documentation~/zh/CodeComments.md`](Documentation~/zh/CodeComments.md)（英文对照 [`en/CodeComments.md`](Documentation~/en/CodeComments.md)）。

- **`summary` 一句话：** **类型与方法**用三行式（`/// <summary>`、内容、`/// </summary>` 各占一行，内容 ≤2 行，断行用 `<br />`、summary 是单段不套 `<para>`）；**其余一律单行内联**（属性/字段/枚举成员/事件等，`/// <summary>一句话</summary>`）。第三人称动词短语只答「是什么 / 做什么」；行内引用用 `<c>`/`<see cref="…"/>`/`<paramref name="…"/>`。
- **`remarks` 放调用方必须知道的不变量（≤6 条，可省）：** 线程/时序约束、性能承诺（0-GC、帧预算）、生命周期配对（谁 Acquire 谁 Release）、降级与失败语义、幂等性、协议文件与调用顺序；**一条一行**短句罗列，长条目可续行（续行以 `<br />` 结尾、末行不加），分主题用 `<para>`。**精简是压缩不是删除**——调用方可感知的信息必须保留。
- **排版（换行是手段，不是违规）：** 单行 ≤120 字符（中文按 1 字符计），超限按语义断行；`<code>`/`<example>` 内是预格式、不受行宽限制。禁止把多条约束挤成 200+ 字符的长条行，也禁止把一两句话拆成碎段。
- **叙述禁入（任何标签内）：** 日期、提交号/PR 号/分支名、`CHANGELOG` 引用、「实测/实证/复现/事故/教训/评审/先红后绿/回归锁」一类过程叙事、「原为 X 现改为 Y」变更史——归宿是 `CHANGELOG.md`、提交信息与评审底稿。
- **`example` 允许且鼓励（≤15 行）：** 短用法示例进 `<example>` + `<code lang="csharp">`，与单测呼应；不把示例写成完整业务流程。
- **结构化标签与语言：** `<param>`/`<returns>` 各一行不复述类型、会抛处写 `<exception cref>`、覆写优先 `<inheritdoc/>` 只补差异；叙述用中文、类型名/成员名/路径/协议字段保持原文。

