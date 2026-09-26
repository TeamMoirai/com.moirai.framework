# Moirai Framework - Claude Code 配置

## 项目概述

**Moirai Framework** 是一个 Unity 游戏开发框架，提供服务化、高性能的开发解决方案。

### 核心特性
- 🚀 开箱即用 - 5 分钟快速上手
- 🔥 高性能 - 基于 UniTask 的异步系统，零 GC 事件分发
- 🧩 高内聚低耦合 - 服务化设计
- 🔄 热更新支持 - 集成 HybridCLR
- 📦 资源管理 - 集成 YooAsset
- 📊 配置表系统 - 集成 Luban
- 🎨 UI 框架 - 商业化 UI 开发流程

## 项目结构

```
com.moirai.framework/
├── Runtime/                  # 运行时代码（Moirai.Atropos.asmdef）
│   ├── Core/                 # 核心系统，不依赖服务层
│   └── Services/             # 各功能服务
├── Editor/                   # 编辑器代码
├── Tests/                    # EditorMode / PlayMode / Player 三套测试
├── SourceGenerators/         # HandlerHost 等代码生成器
├── Documentation~/           # 模块文档，zh / en 双语成对维护
├── Samples~/                 # 示例（InputSystem Action Prompts）
├── Templates~/               # 代码生成模板
└── Plugins/                  # 第三方插件
```

（`~` 后缀的目录 Unity 不导入包内；工程另在 `Client/` 下，与包分开归属。）

## 核心服务

### Runtime Services
- **AudioService** - 音频管理
- **ConfigTableService** - 配置表（Luban）读写与本地化表查询
- **DebuggerService** - 调试工具
- **InputService** - 输入系统
- **LocalizationService** - 本地化
- **ObjectPoolService** - 通用对象池（任意 ObjectBase 派生对象，opt-in 注册）
- **GameObjectPoolService** - 游戏对象池（GameObject 实例，opt-in 注册，依赖 ResourceService）
- **ProcedureService** - 流程管理
- **ResourceService** - 资源管理
- **SaveService** - 存档系统
- **SceneService** - 场景管理
- **TimerService** - 定时器
- **UIService** - UI 框架

服务注册与生命周期由 `Runtime/Services/Kernel`（`GameServices` / `ServiceScope` / `ServiceBase`）承接。

### Core 系统
- **Attributes** - 自定义特性（`[HandlerHost]`、`BooleanButtonAttribute` 等）
- **Constant** / **Models** - 常量与共享数据模型
- **DataStructure** - 数据结构
- **Events** - 事件系统
- **Extensions** - 扩展方法
- **GameApp** - 启动、帧驱动与运行期开关
- **GameException** - 框架异常约定
- **GameProfiler** - 性能采样
- **MemoryPool** / **Pool** - 内存池与通用池
- **Singleton** - 单例模式
- **Tasks** - 任务系统
- **Obfuz** - 混淆虚拟机初始化（`OBFUZ_INSTALLED && ENABLE_OBFUZ` 门控）
- **Utilities** - 工具类（算法、随机、JSON、Tween、日志 `LogUtility` 等）

## 编码规范

生产级 C# 编码规范（强制执行）。**Why:** 用户要求所有 Unity C# 系统编写、优化、重构时严格按照此规范执行，确保 AAA 商业化代码质量。**How to apply:** 所有 Unity C# 代码编写任务均以下述规范为基线。

- **核心原则：** 性能即特性（热路径 0-Alloc，帧预算内完成）；确定性（避免反射/动态生成，确保 IL2CPP 一致）；可读性即维护性；Fail-Fast（Editor 断言优先，Runtime 防御性检查）。
- **命名：** 以 IDE 实拦的口径为准——见下方《命名规范》表（规则源 `Client/Client.sln.DotSettings`，表内右列记录本仓实测分布与历史偏差）。字段前缀分档：`m_` = 私有家族序列化字段（private/internal/protected）、`_` = 私有家族非序列化实例字段、`s_` = 私有家族静态字段；公共家族（public/protected internal/file-local）序列化字段无前缀 `lowerCamelCase`、其余公共字段无前缀 `PascalCase`，据此杜绝 `this.` 冗余。`var` 仅当右侧类型明确时使用。Allman 大括号，4 空格缩进。
- **0-Alloc 热路径：** 禁止 new/LINQ/foreach 非泛型；禁止 lambda/匿名委托（缓存方法组）；禁止 + 拼字符串（用零分配字符串工具或 StringBuilder 池）；禁止每帧 ToUpper/ToLower；禁止 params；返回空集合用 Array.Empty<T>()；临时缓冲区优先 stackalloc + Span<T>。
- **内存布局与 Cache 友好：** struct ≤ 16 bytes 且 readonly；批量数据优先 SoA 提升 cache locality；高频值类型实现 IEquatable<T>；互操作场景用 [StructLayout(LayoutKind.Sequential)]；多线程写入字段防 false sharing。
- **Span 与 unsafe：** 字符串/JSON/二进制解析用 ReadOnlySpan&lt;char&gt;/ReadOnlySpan&lt;byte&gt; 避免 substring 分配；unsafe 仅限性能关键场景（指针操作/直接内存拷贝），须注释说明；allowUnsafeCode 按 asmdef 粒度开启。
- **防装箱：** 通用工具必须泛型接口（IEquatable&lt;T&gt;）；禁止 ArrayList/Hashtable/非泛型 Queue/Stack；禁止 Enum 传 object（用泛型 Enum.Parse&lt;T&gt;）；禁止 object 参数函数（用泛型）；禁止热路径 Debug.Log（用封装日志工具）。
- **线程安全：** 跨线程共享字段用 volatile/Interlocked；Unity API 仅主线程调用，异步续体须 EnsureMainThread() 守卫或 Dispatcher 入队；锁仅限非热路径初始化，热路径用 lock-free；CancellationToken 贯穿所有异步操作。
- **对象池化：** 高频创建/销毁对象（事件、任务、缓冲区、GameObject）必须池化；池接口统一 Acquire/Release；池对象实现状态重置；容量按场景配置，支持运行时回收。
- **Unity 引擎：** GetComponent 必须 Awake/Start 缓存；禁止 GameObject.Find/SendMessage/BroadcastMessage；私有序列化字段 m_ 前缀（公共序列化字段无前缀 lowerCamelCase）；yield return 缓存静态只读或用协程工具；高频异步用 UniTask（禁止同步 IO 和 Coroutine 做 IO）；用 Mathf 不用 Math；ScriptableObject 做数据驱动配置并运行时缓存引用。
- **异常与错误处理：** 禁止 try-catch 做逻辑控制；热路径严禁 try-catch（**例外**：`PlayerLoopDriver.HandlerSlot/CallbackSlot.Drive` 与内核 `ServiceScope` 轮询循环内的 per-subscriber try/catch 属有意隔离——订阅/服务抛出不得截断同阶段其余项；异常本身仍按分级上抛或隔离，不吞）；用 Debug.Assert/Assert.IsTrue（仅 Editor）；非热路径公共 API 做参数校验抛 ArgumentException；异常不吞——要么处理要么上抛。
- **代码组织：** 一文件一顶层类；类/接口/公有方法/枚举必须 &lt;summary&gt;（内容独占行）；严禁 TODO 入主干；#region 用于小范围分组（双语标签），严禁大段折叠掩盖 SRP 违例（违反则拆类）；asmdef 最小化依赖、禁止循环引用。
- **AOT/IL2CPP 兼容：** 禁止 Reflection.Emit/动态代码生成；反射仅限序列化/编辑器，运行时避免；泛型 AOT 预编译缺失时需预生成元数据或用非泛型路径；Type/enum 缓存为静态只读字段避免反复 GetType。
- **测试可见性（强制）：** 测试不得用反射读写字段/属性（`GetField("m_…", BindingFlags.NonPublic)`）——需要触达的成员把访问级别 `private`→`internal`，`Runtime/AssemblyInfo.cs` 已对 `Moirai.Atropos.Editor` 与三个测试程序集（`.Tests.EditorMode`/`.Tests.PlayMode`/`.Tests.Player`）开了 `InternalsVisibleTo`。反射把字段名变成测试依赖：改名不报编译错，只在运行期 `GetField` 返回 null 后 NRE；`internal` 由编译器把关。序列化字段改 `internal` 不影响 Unity 序列化（`[SerializeField]` 不要求 `private`），前缀仍走 `m_`/`s_`/`_` 私有家族口径。反射只留两类正当用途：遍历 API 形状与断成员标注做契约守卫（`ResourceSeamShapeGuardTests`、`ResourceMethodSetContractTests`、`YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`——这类只能反射，别当违例删掉）、唤起 Unity 生命周期回调（`Awake`/`OnEnable`/`OnInit`）。已有窄接缝的成员不为此放开字段：换处理器走生成的 `Internal_PeekHandler()`/`Internal_UseHandler(next)`，`s_Handler` 保持 `private`。
- **工具链与质量门：** 启用 Roslyn Analyzers；.editorconfig indent_size=4；提交前通过 ZeroAlloc 性能测试；PR 须通过编译 + Analyzer + 测试三重门。
- **执行等级：** Mandatory（违反打回：命名前缀、0-Alloc、防装箱、AOT 兼容、测试可见性）/ Prefer（性能敏感区必须，非热路径可放宽：Span/unsafe/池化/线程安全）/ Reference（逐步优化遗留）。

### 命名规范

规则源是解决方案级的 `Client/Client.sln.DotSettings`（Rider / ReSharper 实拦）。两个前提要先知道：

- `ApplyAutoDetectedRules=False`——关掉的是"从现有代码里自动学出来的"命名规则（Rider 会照着仓库里的高频写法生成告警），本仓的口径由表内这些显式规则自己承担。内置预定义规则（类型/成员 `PascalCase`、参数 `camelCase`、接口 `I`、泛型 `T` 等）仍按 IDE 默认生效，所以表里"未配"不等于"随便写"。
- `CheckNamespace` 降为 `SUGGESTION`——命名空间与目录不一致只提示不报红，这是给下面《程序集与命名空间》那条留的口子。

改前缀或风格必须连这张表一起改，否则规范与 IDE 各说各话。表右列"本仓实测"是 grep 出来的约数（字段与常量取 `Runtime`，事件取 `Runtime`+`Editor`），只用于说明历史，**不是可复制的写法**。

| 元素 | DotSettings 规则 | 本仓实测 | 新代码口径 |
|---|---|---|---|
| 类型 / 方法 / 属性 / 命名空间 | `PascalCase` | 一致 | 同 |
| 接口 | `I` + `PascalCase` | 全部（`IAudioClipLeaseSource`、`IAudioMiddlewareBridge`） | 同 |
| 枚举类型 | `PascalCase` | 主流 `E` 前缀（`EAudioTrack`、`EResourceAssetKind`）；历史无前缀（`UILayer`、`UIType`、`TaskStatus`、`TimerPhase`） | 一律 `E` 前缀；运行期状态枚举显式 `: byte` |
| 枚举成员 | `PascalCase` | 一致 | 同 |
| 局部变量 / 参数 | `camelCase` | 一致 | 同 |
| 实例字段（`private`/`protected`/`internal`，非序列化） | `_camelCase` | ~650 处 | 同 |
| 序列化字段（`private`/`internal`/`protected`） | `m_PascalCase` | ~332 处；公开面一律包 `PascalCase` 属性（`public int ID { get => m_ID; internal set => … }`） | 同；`public` 裸序列化字段允许（走下方 `lowerCamelCase` 口径） |
| 序列化字段（`public`/`protected internal`/file-local） | `lowerCamelCase`，无 `m_` | 一致（`ImageLocalizer.localizedTextID`、`AudioLocalizer.clips`） | 同 |
| 静态字段 / 静态 readonly（`private`/`internal`/`protected`） | `s_PascalCase` | ~87 处 | 同 |
| 静态字段 / 静态 readonly（`public`/`protected internal`） | `PascalCase`，无 `s_` | ~156 处 | 同 |
| `const`（任何可见性） | `UNIFORM_CASE` | 主流（~453）；~122 处 `PascalCase`（`MemoryPool.Core`、`Save`、`Audio` 的量纲/上限/位域）；2 处 `k_Default…` 为外来写法 | 新 `const` 一律 `UNIFORM_CASE`；想要 `PascalCase` 就写 `static readonly`（Rider 不把它算作常量，两者语义也确实不同）；`k_` 不再引入 |
| `event` 成员 | `On` + `PascalCase`（另登记 `on` 变体；该规则未开前后缀告警，缺前缀不报红） | 带 `On` 7 处、无 `On` 约 20 处（`BlockSaved`、`LoadFailed` 式过去分词） | 新事件一律 `On`；旧事件不顺手改名（外部订阅面） |
| 泛型参数 | 表内未配（走 Rider 默认） | `T`、`TKey`/`TValue`、语义式 `TVoice`/`TLexer` | 单参数 `T`，多参数 `T` + 名词 |

前缀由**访问级别**决定，不看是否"真私有"：`internal` 走 `private` 口径（带 `m_`/`s_`/`_`）；`public`/`protected internal`/file-local 无前缀——序列化字段 `lowerCamelCase`，其余字段与静态成员 `PascalCase`。

缩略词表里只登记了 `FSM` 与 `GOAP`（供 Rider 按整词切分，加前缀/自动重命名时不拆成 `F`+`S`+`M`）；`UI` 没登记而仓内一律写成 `UILayer`/`UIService`。要用新缩略词前先决定"登记"还是"照抄现状"，别两边各写一半。

下列是**仓库既定词汇**，DotSettings 管不到，但新模块照抄、不另造同义词：

- **服务三件套**：`XxxService.cs`（门面）+ `XxxServiceHandler.cs`（后端契约，派生 `FrameworkHandler`）+ `XxxServiceSettings.cs`（配置资产）；默认实现 `DefaultXxxHandler`，按后端的 `<Backend>XxxHandler`（`YooAssetHandler`、`UnityAudioHandler`、`MiddlewareAudioHandler`）。
- **生命周期**：覆写点一律 `protected virtual On*`（`OnInit`/`OnShutdown`/`OnInitAsync`/`OnShutdownAsync`/`OnUpdate`/`OnLowMemory`），框架内部门面一律 `internal Internal_*`（`Internal_Init`/`Internal_Shutdown`）。调用方只碰 `Internal_*`，不要在 `On*` 上直接互调。
- **SDK 适配层**：`IXxxBridge` 主接口 + `XxxBridgeStub`（无 SDK 也要能编译）+ `XxxBridgeNative`（真 SDK，整文件由 `*_INSTALLED` 宏门控）；可选能力另开窄接口按能力探测（`IAudioMiddlewareBankControl`、`IAudioMiddlewareRtpcControl`），不扩主接口。
- **目录与文件**：`Runtime/Services/<Module>/{Handler,Mix,Models,Spatial,Support}/`，后端进 `Handler/<Backend>/`；`Core/` 放无服务契约的基础件。数据结构在 `Models/`（`XxxEntry`/`XxxLease`/`XxxRequest`/`XxxOptions`/`XxxConfig`/`XxxSlot`），场景组件与辅助件在 `Support/`（`AudioEmitter`、`BgmPlaylist`、`AudioFault`、`ShuffleIndexBag`），契约/类型面按 `<Module>Types.cs`/`Abstractions`/`Callbacks`/`Contract` 分档。
- **partial 拆文件**：`Xxx.<职责>.cs`，职责名是首字母大写的单个英文名词（`.Core`/`.Slots`/`.Maintenance`/`.Bindings`/`.Async`/`.IO`，在仓 66 个）；拆文件不破坏"一文件一顶层类型"。
- **通用后缀**：静态工具 `XxxUtility`（单数）、扩展方法 `XxxExtensions`、账本 `XxxRegistry`、缓存 `XxxCache`、调度 `XxxScheduler`/`XxxStateMachine`、调试器面板 `XxxServiceDebuggerWindow`。
- **键名常量**：Mixer 参数与设置键按 `<域>_<对象>_<属性>` 全大写（`AUDIO_MASTER_VOLUME`、`GRAPHICS_FULLSCREEN_MODE`）；存档 schema 字段是 `PascalCase` + `Key` 后缀（`LocalPositionKey`、`SpawnsKey`）。两套并存是历史，改到哪个文件就跟哪个，不新造第三种。
- **程序集与命名空间**：本包自带 asmdef 为 `Moirai.Atropos`、`Moirai.Atropos.Editor`、`Moirai.Atropos.Tests.EditorMode`/`.PlayMode`/`.Player`（`Templates~` 下的 `GameLib`/`GameLogic`/`GameProto` 是工程侧模板，不属本包）。其中 `.Player` 是**玩家专用**测试程序集（`defineConstraints: ["UNITY_INCLUDE_TESTS", "!UNITY_EDITOR"]`）：编辑器里根本不编译，只随玩家构建的测试运行执行，且只引用玩家安全的程序集（`UnityEngine.TestRunner` + `Moirai.Atropos`），不引 Editor-only 的 `UnityEditor.TestRunner`（按"玩家侧只依赖玩家安全程序集"收窄；2026-09-22 实测引用它**并未**硬阻断玩家构建——`.PlayMode` 原样打进 IL2CPP 测试玩家时也进了包，故此处不写"引用即报错"）。运行期与编辑器代码一律 `namespace Moirai.Atropos[.<Module>[.<Sub>]]`；测试用与被测模块对齐的**短命名空间**（`Service.Audio`、`Core.Events`、`Core.MemoryPool`），不带 `Moirai` 根——这正是 `CheckNamespace` 降级要护住的写法。
- **测试**：类 `<被测>Tests`（`AudioClipCacheTests`）、基准 `<被测>Benchmark`（一律 `[Explicit]`，不随常规套件跑）、夹具 `XxxTestSupport`/`XxxTestHost`/`MemoryPoolFixture`（派生式基座）。方法名 `场景_条件_期望` 三段式（`RetainRelease_CycleAllocatesZeroBytes`、`PauseGame_NestedSources_OnlyLastResumeRestoresSpeed`）。异常断言沿 `InnerException`/`AggregateException` 链判定，不用 `Assert.Throws<T>` 硬匹配（泛型 `new T()` 实走 `Activator.CreateInstance<T>()`，原始异常会被包装）。

**冲突怎么判**：DotSettings 与代码打架时以 DotSettings 为准（它是门禁，也是评审依据）；表里没写、仓内已成词汇的那一档（`Handler`/`Bridge`/`Registry`/`Support`）按仓库现状走。两类冲突都不许用 `// ReSharper disable` 或规则抑制绕过——要改先改规则，再改代码。

## 测试规范

完整规范见 [`Documentation~/zh/Testing.md`](Documentation~/zh/Testing.md)（英文对照 [`en/Testing.md`](Documentation~/en/Testing.md)）。以下是必须遵守的硬约束清单。

### 分层与归属

| 层 | 程序集 | 位置 | 放什么 |
|---|---|---|---|
| L1 单元/契约 | `Moirai.Atropos.Tests.EditorMode` | `Tests/EditorMode/` | 纯逻辑、数据结构、状态机、契约形状、降级路径 |
| L2 集成 | `Moirai.Atropos.Tests.PlayMode` | `Tests/PlayMode/` | 跨组件协作、真实帧驱动、场景/宿主生命周期、真实 IO |
| L3 玩家验收 | `Moirai.Atropos.Tests.Player` | `Tests/Player/` | 0-GC 热路径、托管分配计量（编辑器测不出来） |
| L4 基准 | 所在程序集 | 模块目录 | 必须 `[Explicit]`，不进常规套件 |

能在 EditMode 判定的**必须**放 L1；不要为了"更真实"把纯逻辑塞进 PlayMode（慢、难归因、易 flaky）。

### 命名与结构

- 文件/类 `<被测>Tests`（`AudioClipCacheTests`）；**禁止 `XxxTest` 单数式**。
- 夹具基座 `XxxFixture`、共享支撑 `XxxTestSupport`/`XxxTestHost`、基准 `XxxBenchmark`。
- 用例方法 `场景_条件_期望` 三段式（`RetainRelease_CycleAllocatesZeroBytes`）。
- 命名空间用**短名**（`Service.Audio`、`Core.MemoryPool`、`Utility`），不带 `Moirai` 根；引用框架子命名空间类型**必须 `using` 别名**（`using Res = Moirai.Atropos.Resource;`），禁止裸限定名（会撞全局命名空间或 `UnityEngine` 类型，报 CS0246/CS0426）。
- 测试目录镜像被测目录；一个文件一个公开测试类；测试专用类型一律 `internal`。
- **禁止在测试里创建 `[Serializable]` 框架基类的子类**（`LogHandler`/`JsonHandler`/`TweenHandler`/`XxxServiceHandler` 等）——`[SerializeReference]` 类型扫描会把它们塞进生产资产的 Inspector 下拉框。捕获日志用内置实现 + `LogUtility.OnMessageLogged`。
- 测试/Editor/非运行时脚本的日志用 `Debug.LogXX`，不用 `LogUtility`。

### 夹具与隔离

- 夹具基座 `[SetUp]`：快照全局旋钮 + 复位被测对象 + **断言初始状态干净**（带上个用例的名字便于归因）。`[TearDown]`：断言无残留（未归还租约/未注销订阅）+ 清理 + **在 `finally` 里还原全部旋钮** + 收集后聚合抛出多个异常（不吞）。
- EditMode 与 PlayMode 的生命周期差异是高频坑：非 `[ExecuteInEditMode]` 组件的 `Awake`/`OnDestroy` **不执行**（含活跃物体 `AddComponent`）；`DontDestroyOnLoad` **抛 `InvalidOperationException`**；`Time.frameCount` **不推进**。相应地——运行期靠 `Awake` 注册的管线必须有 `EnsureActivated` 幂等兜底；`DontDestroyOnLoad` 调用点必须有 `Application.isPlaying` 守卫；EditMode 跨帧逻辑自带帧号游标 `Tick(++Frame)`。
- 主线程敏感探针（托管分配计量、Unity API 时序、`ProfilerRecorder`）必须用 `[UnityTest]` + `IEnumerator` 逐帧驱动——`async Task` 的续体在线程池线程，结论全部无效。

### 确定性

- 禁真实墙钟（`Thread.Sleep`、`Task.Delay` 做时序断言、`DateTime.Now` 做判据）；时间必须**注入**（计时器用例自带帧号游标 + `Advance(delta)`）。
- 随机必须定种；禁跨用例共享可变静态；禁依赖用例执行顺序（单跑绿与整套绿必须同时成立）。
- `Assert.ThrowsAsync<T>` 对 async lambda 会因 `TaskCanceledException` 精确类型不匹配而失败——用 `task.GetAwaiter().GetResult()`。
- 异常断言沿 `InnerException`/`AggregateException` 链判定；`SaveResult<T>` 等值类型结果先取 `.Error` 字段再断言。
- flaky 处置：隔离重跑 → `git diff` 排除并行改动 → 修。**禁止用 `Assert.Ignore` 掩盖**（Ignore 只留给能力探测失败这类环境性原因，且须注释说明探测的能力与恢复条件）。

### 反射政策

`Runtime/AssemblyInfo.cs` 已对 `Moirai.Atropos.Editor` 与三个测试程序集开 `InternalsVisibleTo`，所以**需要触达的成员把 `private` 改 `internal`，不要用反射**——反射把字段名变成测试依赖，改名不报编译错、只在运行期 `GetField` 返回 null 后 NRE。序列化字段改 `internal` 不影响 Unity 序列化，前缀仍走 `m_`/`s_`/`_` 私有家族口径。

白名单（必须能归入其一，且在文件头写明理由）：

1. 契约形状守卫（`ResourceSeamShapeGuardTests`、`ResourceMethodSetContractTests`、`YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`）；
2. 唤起 Unity 生命周期回调（`Awake`/`OnEnable`/`OnInit`）；
3. 产码字段探针（`MemoryPoolFixture.StaticField`）。

### 日志断言

`LogUtility` 的 Handler 可插拔：`DefaultLogHandler`/`ZLoggerHandler` 对 UTF 可见（须 `LogAssert.Expect`），`UnityLoggingHandler` **不可见**（声明 Expect 反报 "Expected log did not appear"）。

- 内容断言一律走 `LogUtility.OnMessageLogged`（Handler 无关，唯一稳定通道）。
- `LogAssert.Expect` 的正则一律 `".*"`，只承担消除未处理日志的职责，不耦合 Handler 的渲染前缀。
- **消除未处理日志一律经 `UtfLogExpect.Error()` / `UtfLogExpect.Warning()`**（`Tests/EditorMode/Support/UtfLogExpect.cs`）：处理器可见性判定收在那一处，用例侧不写 `#if`、不提处理器类型。不要在用例里自写 `LogAssert.Expect` + 处理器判定（未装 com.unity.logging 的工程里 `UnityLoggingHandler` 不存在，会逼出每处一个 `#if`）。

### 基准

- 一律 `[Explicit]`，不进常规套件；命名 `XxxBenchmark`。
- 性能结论必须同工具同数据 before/after A/B；编辑器 Mono 基准 ±2× 噪声，只做同轮内比较。
- 非 NUnit 的手动基准（`TimerServiceBenchmark`，MonoBehaviour + 菜单驱动）必须明确标注为非自动基准。

### 契约守卫维护（强制）

把 API 形状钉成基线常数的用例（`ResourceSeamShapeGuardTests` 等）**必须有人维护，否则退化成常年红**——那时它既不防回归，还掩盖真缺陷：

1. API 有意变更时**同一提交内**同步基线常数，不留到"下次一起改"。
2. 基线注释写清数字来源（`2026-09-24 基线：19 个抽象属性 + 47 个抽象方法；……`）。
3. `CHANGELOG.md` 写明收掉了哪些成员（常数是"现在的形状"，CHANGELOG 是"为什么变成这样"）。
4. 守卫红了必须判断「有意变更（同步基线）」还是「意外收敛（修代码）」；**不允许直接改常数让它变绿**。

### 覆盖率与出口准则

工具 `com.unity.testtools.codecoverage`；assemblyFilters `+Moirai.Atropos`，排除 `Moirai.Atropos.Editor`/`Moirai.Atropos.Tests.*`/生成代码；报告落盘 `Tests/Coverage/`。

| 档 | 范围 | 行覆盖 | 分支覆盖 |
|---|---|---|---|
| 核心服务 | Resource/Save/Audio/UI/Kernel | ≥ 80% | ≥ 70% |
| 其余服务 | ConfigTable/Debugger/Input/Localization/ObjectPool/Procedure/Scene/Timer | ≥ 70% | — |
| Editor 工具与生成代码 | `Moirai.Atropos.Editor`、SourceGenerators | ≥ 50% | — |

覆盖率是**找空洞的工具，不是质量指标**；评审用例看断言强度，不看百分比。新代码不得让所在模块覆盖率下降。

**发布出口五门**（缺一不可）：编译 0 error → L1 全量 0 失败 → L2 全量 0 失败 → L3 `Run all in Player` 0 失败 → 覆盖率不低于分级阈值与上一版基线。**基线必须绿**——套件有红时"全绿"信号失效，必须先修红再继续开发。

## AI 测试流程

面向代理的执行流程；规范全文见 [`Documentation~/zh/Testing.md`](Documentation~/zh/Testing.md)。

### 1. 何时必须写测试

**必须**：新增或修改框架对外契约（服务外观、Handler 契约、公共 API 语义）；修 bug（先构造可复现失败用例）；性能承诺（0-GC/帧预算）；状态机与生命周期；序列化/迁移/编解码。

**不必**：纯注释与文档改动；无行为变化的内部重命名；`Editor` 下一次性的手动工具脚本（除非含可复现的纯函数逻辑）。

### 2. 写作顺序（不许跳步）

1. **读被测**：读实现与相邻用例，弄清契约与既有夹具基座；确认要断的是**行为**而非实现。
2. **选层**：按上表选 L1/L2/L3/L4；能在 EditMode 判定就 L1。
3. **先写失败用例**：新增用例必须**先红后绿**——先跑一次确认它真的能失败（否则它可能什么都没断），再改代码让它变绿。这是"用例有效"的唯一证据。
4. **实现 / 修复**。
5. **验证**：编译 0 error → 目标夹具过滤回归 → 全量回归 0 失败。
6. **同步文档**：公共 API 变更同步 `Documentation~/zh|en`；契约守卫基线变更同步常数与 `CHANGELOG.md`。

### 3. 选通道

| 场景 | 通道 |
|---|---|
| EditMode / PlayMode 全量或过滤 | **测试桥**（`Temp/MoriaiTestRequest.json` 文件协议，见《验证：让开着的编辑器自己跑测试》） |
| 桥不可用（编辑器刚重载、桥未进域） | `TestRunnerApi` 直跑（`exec_editor_script` + `ICallbacks` 宿主） |
| 判编辑器是否空闲 / dll 是否新鲜 | **状态桥**（`Temp/MoriaiEditorState.json`，见《验证：编辑器状态桥》） |
| L3 玩家验收 | **只能** Test Runner 窗口 PlayMode 页签 → `Run all in Player`；不要自己 `BuildPipeline.BuildPlayer` 搭测试玩家 |

### 4. 证据纪律

- **唯一 `id` + 只认配对的 `.done`**：否则会把上一轮旧报告当本轮结论。
- **`assemblies` 与 `tests` 不能都为空**：空过滤器会重跑"上一次窗口选择集"，看似成功实则文不对题。
- **投单前先查状态桥**：`testRunActive` 为 0 且 `isCompiling`/`isUpdating`/`isChangingPlayMode` 皆 false 才接单；编辑器是共享的，别的会话随时占住它。
- **以落盘报告为准**：结论取自 `report.txt`（含 ABORTED 的 `collected passed/failed/skipped`），不以"工具调用返回成功"为结论。
- **客户端超时 ≠ 失败**：`exec_editor_script`/编译管线的超时只说明编辑器忙；用状态桥心跳与 `Library/ScriptAssemblies/*.dll` 时间戳判活，**不要重复提交**（会叠加运行）。

### 5. 判绿门禁

**0 失败才算通过。** 新增用例必须"先红后绿"；既有失败必须归因——先 `git diff` 排除并行改动（本项目用户会与代理并行编辑，也会 rebase/amend），确认是本次引入才动手修。

**不允许**：用 `Assert.Ignore` 掩盖 flaky；直接改契约守卫常数让它变绿；把"测不出"当成"没问题"（编辑器 Mono 无托管分配计量，0-GC 只能 L3 验证）。

### 6. 忙碌期与域重载处置

- 编辑器编译/域重载期间测试桥与状态桥都会停摆——**请求文件留着，空闲后自动消费**，不要重复投单。
- 域重载后桥脚本可能引用旧程序集（报 CS1061/CS0117 而成员确实存在）——改用反射调用，或等一次真正的重载。
- 收尾前确认编辑器空闲（状态桥 `unix` 心跳新鲜、`isCompiling` 为 false）。

## Claude Code Skills

项目提供以下 Skills（通过 `/` 命令调用）：

| 命令 | 功能 |
|------|------|
| `/new-service` | 创建新服务 |
| `/new-ui` | 创建新 UI |
| `/review` | 代码审查 |
| `/explain` | 解释代码 |
| `/refactor` | 重构代码 |
| `/fix-bug` | 修复 Bug |
| `/add-event` | 添加事件 |
| `/generate-docs` | 生成文档 |
| `/test` | 生成和运行测试 |
| `/optimize` | 性能优化 |
| `/migrate` | 代码迁移 |

## 开发流程

### 1. 新功能开发
1. 确定功能需求
2. 设计服务结构
3. 使用 `/new-service` 创建服务
4. 实现功能逻辑
5. 使用 `/review` 审查代码
6. 使用 `/test` 生成测试

### 2. Bug 修复
1. 使用 `/fix-bug` 分析问题
2. 定位根本原因
3. 实施修复
4. 使用 `/test` 验证修复

### 验证：让开着的编辑器自己跑测试

`Client/Temp/UnityLockfile` 在时 batchmode 打不开同一工程，而"改完要证据"不该每次都等人去点 Test Runner。
`Tests/EditorMode/TestRequestRunner.cs`（测试程序集内的调试桥）轮询 `Client/Temp/MoiraiTestRequest.json`：
出现请求就按过滤器执行一轮，把逐格进度与结果回写。协议是单向文件，调用方只轮询：

```json
{"id":"<唯一串>","mode":"EditMode","output":"<绝对路径>/report.txt","timeoutSeconds":180,
 "assemblies":["Moirai.Atropos.Tests.EditorMode"],"tests":["<命名空间.类名.方法名>", "..."]}
```

产物：`report.txt`（`run <id> | passed N | failed N | skipped N | 耗时`，后附逐格失败详情）、`report.txt.progress`
（正在跑的用例全名，可判卡死；收口时删除）、`report.txt.done`（内容是请求里的 `id`）。**必须自带唯一 `id` 并只认配对的
`.done`**，否则会把上一轮的旧报告当成这次的结论。前提是该程序集已编译过一次且编辑器有过一次 `update`
（焦点切过去即可，通常在几秒内）；正在编译、正在导入、正在切 PlayMode 或编辑器里有任意 run 在跑（含窗口手动发起）时不接新单——请求文件留着，空闲后自动消费。`mode` 支持 `EditMode`/`PlayMode`；PlayMode 进出场的域重载由驱动落盘 `Temp/MoiraiTestRunState.json` 自动续跑。可选 `timeoutSeconds` 是墙钟上限（秒，`0`/缺省不限时，编译、导入与域重载的等待计入），超时按 ABORTED 收口并尽力取消 Test Runner 作业；ABORTED 报告（超时/孤儿单/执行失败）附带已收集的 `collected passed/failed/skipped` 与墙钟时长，已跑完的格子不白跑；`assemblies` 与 `tests` 均为空的请求会被直接拒绝收口——空过滤器会让 Test Runner 重跑上一次的选择集。

**取消在途单**：往 `Client/Temp/MoiraiTestRequest.cancel.json` 写要取消的请求 `id`（裸文本或 `{"id":"..."}` 均可），
驱动匹配在途单即删除文件并经 `TestRunnerApi.CancelTestRun` 取消作业；UTF 取消后不再送达 RunFinished，
受理即由驱动收口（ABORTED 格式，附已收集计数）；拒绝受理才等 RunFinished 自然收口。
域重载后驱动按作业 guid 精确判活（不认窗口手动跑），判活不可用也有强制收口宽限——调用方永不会等不到 `.done`。

**玩家专用用例跑不了这条桥**：桥住在编辑器里，而 `Moirai.Atropos.Tests.Player` 在编辑器下不编译（`!UNITY_EDITOR`），
桥的 `assemblies` 过滤器找不到它。这类用例（音频热路径 0-GC 验收——托管分配计数器只在玩家里推进）只能用
**Test Runner 窗口的 PlayMode 页签 → `Run all in Player`**（可用搜索框把范围缩到目标夹具），结论以玩家侧报告为准；
编辑器套件里它们既不出现也不假跳。

**玩家侧测试为什么只能从窗口发起**（2026-09-22 实测，别自己 `BuildPipeline.BuildPlayer` 搭测试玩家）：① 玩家里的测试
入口不是 `-runTests` 参数，而是**构建期注入的引导场景**——编辑器侧 `CreateBootstrapSceneTask` 建一个挂着
`PlaymodeTestsController`（internal，`Code-based tests runner`）的 `Assets/InitTestScene<guid>.unity` 并把它作为构建场景，
控制器在 `Start()` 里跑测试；手搓玩家没有这个场景，`-runTests` 什么也不会发生。② 玩家**自己不写结果 XML**：运行时侧
没有 `-testResults` 解析，结果经 `RemoteTestResultSender` 走 PlayerConnection 回传编辑器，由编辑器落盘。所以
"独立玩家 + 命令行"这条路根本不存在。③ 项目侧前置：玩家默认自动启动框架（`GameApp.AutoBoot` 默认 true →
`GameAppSettings.Initiation` 里 `if (GameApp.AutoBoot) GameApp.Boot()`），测试玩家跑的是空场景，启动链会停在
`UGUIHandler.OnInit` 的 `[FAT] UIRoot not found!`（实测：带不带 `-runTests` 都停在同一行，测试运行永远轮不到）——
故 `Tests/Player/PlayerTestBootstrap.cs` 在 `AfterAssembliesLoaded` 把 `GameApp.AutoBoot` 置 false，玩家成为干净测试宿主。

### 验证：编辑器状态桥（不用人按 Ctrl+R）

`Tests/EditorMode/EditorStateBridge.cs` 把编辑器此刻的状态每 ~1s 覆写到 `Client/Temp/MoiraiEditorState.json`：
`pid`、`domainSeq`、`unix`（心跳 UTC 秒，与 `stat -c %Y` 同量纲）、`isCompiling` / `isUpdating` / `isPlaying` / `isPaused` /
`isChangingPlayMode` / `isFocused` / `isActive`、`activeScenePath`、`dirtyScenes`、`consoleErrors` / `consoleWarnings`、
`testRequestPending` / `testRunActive`（-1 探针不可用、0 空闲、1 有 run 在跑）、`domainDllUnix`（本域加载时
`Moirai.Atropos.Tests.EditorMode.dll` 的 mtime），以及 `assemblies[{name,unix}]`——
`Library/ScriptAssemblies/` 下四份 Moirai 产物的 mtime。`testRunActive` 走 `TestRunnerApi.IsRunActive()` 反射探针而不是
看 `Temp/MoiraiTestRunState.json` 在不在：实测有一单超时收口（报告与 `.done` 都落了、状态文件也删了）之后，
旧域拆走时又把运行态写回了磁盘，残留文件会把空闲报成在跑。

- **判活**：`now - unix` 大到几秒即主线程没在跑 `update`——导入中、域重载中、被原生模态框挡住（`dirtyScenes` 大于 0 时刷新/重编译
  就可能撞上"保存场景？"对话框，本桥不代存），或者 Interaction Mode 不是 `No Throttling`（那时是走得慢而不是不动）。
  真原因去 `%LOCALAPPDATA%/Unity/Editor/Editor.log` 取。
- **心跳陈旧时整份快照都作废**：里的位是"进阻塞之前"的读数，实测编辑器已经 `Compiling Scripts (busy for 09:51)`
  （进程 CPU 几乎为 0、日志不涨＝编译在等待而非在跑）时，快照里仍是 `isCompiling: false`——把它读成"没在编译"就反了。
  这时唯一可信的是 `unix` 与 `domainSeq` 本身，外加窗口标题/日志这类外部信号。
- **判新域**：`domainSeq` 递增就是域重载真发生过（`SessionState` 计数：跨域保留、随编辑器退出清空，配 `pid` 可区分重载与重启）。
- **判新鲜度**：逐源树比它归属的那份 `assemblies[].unix` 有没有越过自己的改动时刻（`Runtime/**` → `Moirai.Atropos`、
  `Tests/EditorMode/**` → `.Tests.EditorMode`），别一律比测试 dll。还要比 `assemblies[]` 与 `domainDllUnix`：
  **不相等就是"dll 已被后台代编换掉、而这个域还没重载"**，此时跑的还是旧代码——只比 `assemblies[].unix` 和自己的
  改动时刻会把这种状态误判成"已进判据"（实测踩过：dll 14:35:30 出炉、域停在 seq=5，而"无资产改动"的 `refresh`
  不会触发重载，得发 `recompile`）。`consoleErrors` 是 Console 当前条数
  （实测一轮重编译会把它清归零），刷新后 dll 没越过改动时刻且它有增量 = 编译失败；错误正文本桥不代报，仍去 `Editor.log`。
  磁盘上没东西可编时 Bee 不重写 dll，"dll 没变新"单独不构成失败判据。
- **动作**：往 `Client/Temp/MoiraiEditorCommand.json` 投单（同样**必须**先写临时名再 `mv` 原子改名），
  `{"id":"<唯一串>","action":"focus|refresh|recompile"}`。`focus` 把编辑器顶到系统前台（Win32 `AttachThreadInput` +
  `SetForegroundWindow`，只实现了 Windows）；`refresh` 执行 `AssetDatabase.Refresh()`，磁盘上有改动的 `.cs` 时它自己就会起编译——
  这种时候**别再叠 `recompile`**，那会重入编译管线、和 Bee 抢同一份在途构建；`recompile` 才是要跳过磁盘检测强制重编时用。
  正在编译或导入时 `refresh`/`recompile` 直接拒（回执给原因，空闲后重投），播放中不接受 `recompile`。
- **回执**：`Temp/MoiraiEditorCommand.result.json` 配 `.done`（内容是请求 `id`）。回执只答「命令有没有被执行」，
  效果一律回状态文件读——包括 `focus` 之后 `isActive` 到底变了没有。**只认与本次 `id` 配对的 `.done`**：
  命令在编译期间会排在下一拍才消费，直接读回执文件会读到上一单的。
  `refresh`/`recompile` 的回执会写两遍——受理一遍、结论覆写一遍：动作本身可能就当场把本域拆走（编译与域重载就是这次调用发起的），
  先不落地这一单就永远没有配对回执了。读到"已受理"停在原地，是动作已发出、本域被拆走的正常形态，去看状态文件。
- **投测试单前**：`testRunActive` 为 0 且 `isCompiling`/`isUpdating`/`isChangingPlayMode` 皆 false 才是接单窗口
  （与测试桥自己的接单门同源）；这个编辑器是共享的，别的会话随时可能占住它。
- 桥与测试桥同住在测试程序集（`UNITY_INCLUDE_TESTS` 门控），关掉 Test Tools 包就没有心跳。**心跳只在桥进域之后才有**：
  新落的桥文件要等一次真正的导入 + 域重载（本机后台 `AssetImportWorker` 会代跑，本次约二十分钟后自己起来了，时长不可控），
  在那之前这条环路仍然是空的——第一次可能还得有人按一次 `Ctrl+R`。

### 3. 代码优化
1. 使用 `/optimize` 分析性能
2. 识别瓶颈
3. 实施优化
4. 使用 `/review` 验证优化

### 4. 提交时的文档与 CHANGELOG

- `CHANGELOG.md` **只有 `[Unreleased]` 一段**：已发布的内容不留在文件里。发版由 `build-release` 工作流收尾——该段切成 GitHub Release 的 notes 并 Publish，之后自动开一个清空该段的 PR。版本号与 `package.json` 的 `version` 由同一条自动化写入，不在手上改。两条硬约定：**合并清空 PR 之后才能打下一个 tag**（否则上一版条目会被再发一次）；**发版进行中不要往 `[Unreleased]` 写新条目**（清空 PR 整段删，窗口期内新写的会一并消失）。
- `CHANGELOG.md` 按**后覆盖**维护：一条只写当前仍然成立的净结果。加了又删的开关、改到一半的命名、逐轮刷新的测试格数与成员计数、当时判为"不采纳"的观察一律不立条目；同一件事被后续提交推翻时，改掉或删掉原条目，不要再追加一条把它推翻。
- 诊断过程与被删改的来龙去脉写进 commit message，不写进 CHANGELOG。破坏性变更前置 ⚠ 并给出迁移口径。
- `Documentation~/zh` 与 `Documentation~/en` 是成对副本，接口改动必须双语同步；文档里的类名、成员名与菜单路径要对着代码核真名——`E` 前缀、单复数这类差别会让照文档写出的代码直接编译不过。

## 依赖项

### 核心依赖
- **UniTask** - 异步编程
- **YooAsset** - 资源管理
- **HybridCLR** - 热更新
- **Luban** - 配置表
- **R3** - 响应式编程

### 开发工具
- **Odin Inspector** - 编辑器增强
- **TextMesh Pro** - 文本渲染

## 注意事项

1. **Unity 版本**：推荐 Unity 2022.3.x
2. **.NET 版本**：使用 .NET 4.x
3. **平台支持**：Windows、Android、iOS、WebGL
4. **热更新**：使用 HybridCLR 进行热更新
5. **资源管理**：使用 YooAsset 管理资源

## 常见问题

### Q: 如何添加新服务？
A: 使用 `/new-service` 命令，按照模板创建服务。

### Q: 如何进行热更新？
A: 参考 README 中的打包运行步骤。

### Q: 如何优化性能？
A: 使用 `/optimize` 命令分析和优化代码。

### Q: 如何修复 Bug？
A: 使用 `/fix-bug` 命令分析和修复问题。

## 相关资源

- [Moirai Framework GitHub](https://github.com/TeamMoirai/com.moirai.framework)
- [YooAsset 文档](https://www.yooasset.com/)
- [HybridCLR 文档](https://hybridclr.doc.code-philosophy.com/)
- [Luban 文档](https://focus-creative-games.github.io/luban-doc/)
- [UniTask 文档](https://github.com/Cysharp/UniTask)
