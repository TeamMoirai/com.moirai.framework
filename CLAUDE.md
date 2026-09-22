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
Project/
├── Packages/
│   ├── com.moirai.framework/     # 核心框架
│   │   ├── Runtime/              # 运行时代码
│   │   │   ├── Core/             # 核心系统
│   │   │   └── Modules/          # 功能服务
│   │   ├── Editor/               # 编辑器代码
│   │   └── Tests/                # 测试代码
│   ├── Plugins/                  # 第三方插件
│   └── Settings/                 # 项目设置
├── Packages/                     # Unity 包
└── ProjectSettings/              # 项目配置
```

## 核心服务

### Runtime Services
- **AudioService** - 音频管理
- **DebuggerService** - 调试工具
- **FsmService** - 有限状态机
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

### Core 系统
- **Attributes** - 自定义特性
- **Events** - 事件系统
- **Extension** - 扩展方法
- **GameConfig** - 游戏配置
- **GameLog** - 日志系统
- **MemoryPool** - 内存池
- **Pool** - 通用池
- **Singleton** - 单例模式
- **Tasks** - 任务系统
- **Tween** - 缓动系统
- **Utility** - 工具类

## 编码规范

生产级 C# 编码规范（强制执行）。**Why:** 用户要求所有 Unity C# 系统编写、优化、重构时严格按照此规范执行，确保 AAA 商业化代码质量。**How to apply:** 所有 Unity C# 代码编写任务均以下述规范为基线。

- **核心原则：** 性能即特性（热路径 0-Alloc，帧预算内完成）；确定性（避免反射/动态生成，确保 IL2CPP 一致）；可读性即维护性；Fail-Fast（Editor 断言优先，Runtime 防御性检查）。
- **命名：** 以 IDE 实拦的口径为准——见下方《命名规范》表（规则源 `Client/Client.sln.DotSettings`，表内右列记录本仓实测分布与历史偏差）。字段前缀三分：`m_`=序列化私有、`_`=非序列化实例字段、`s_`=静态私有，据此杜绝 `this.` 冗余。`var` 仅当右侧类型明确时使用。Allman 大括号，4 空格缩进。
- **0-Alloc 热路径：** 禁止 new/LINQ/foreach 非泛型；禁止 lambda/匿名委托（缓存方法组）；禁止 + 拼字符串（用零分配字符串工具或 StringBuilder 池）；禁止每帧 ToUpper/ToLower；禁止 params；返回空集合用 Array.Empty<T>()；临时缓冲区优先 stackalloc + Span<T>。
- **内存布局与 Cache 友好：** struct ≤ 16 bytes 且 readonly；批量数据优先 SoA 提升 cache locality；高频值类型实现 IEquatable<T>；互操作场景用 [StructLayout(LayoutKind.Sequential)]；多线程写入字段防 false sharing。
- **Span 与 unsafe：** 字符串/JSON/二进制解析用 ReadOnlySpan&lt;char&gt;/ReadOnlySpan&lt;byte&gt; 避免 substring 分配；unsafe 仅限性能关键场景（指针操作/直接内存拷贝），须注释说明；allowUnsafeCode 按 asmdef 粒度开启。
- **防装箱：** 通用工具必须泛型接口（IEquatable&lt;T&gt;）；禁止 ArrayList/Hashtable/非泛型 Queue/Stack；禁止 Enum 传 object（用泛型 Enum.Parse&lt;T&gt;）；禁止 object 参数函数（用泛型）；禁止热路径 Debug.Log（用封装日志工具）。
- **线程安全：** 跨线程共享字段用 volatile/Interlocked；Unity API 仅主线程调用，异步续体须 EnsureMainThread() 守卫或 Dispatcher 入队；锁仅限非热路径初始化，热路径用 lock-free；CancellationToken 贯穿所有异步操作。
- **对象池化：** 高频创建/销毁对象（事件、任务、缓冲区、GameObject）必须池化；池接口统一 Acquire/Release；池对象实现状态重置；容量按场景配置，支持运行时回收。
- **Unity 引擎：** GetComponent 必须 Awake/Start 缓存；禁止 GameObject.Find/SendMessage/BroadcastMessage；序列化字段 m_ 前缀；yield return 缓存静态只读或用协程工具；高频异步用 UniTask（禁止同步 IO 和 Coroutine 做 IO）；用 Mathf 不用 Math；ScriptableObject 做数据驱动配置并运行时缓存引用。
- **异常与错误处理：** 禁止 try-catch 做逻辑控制；热路径严禁 try-catch（**例外**：`PlayerLoopDriver.HandlerSlot/CallbackSlot.Drive` 与内核 `ServiceScope` 轮询循环内的 per-subscriber try/catch 属有意隔离——订阅/服务抛出不得截断同阶段其余项；异常本身仍按分级上抛或隔离，不吞）；用 Debug.Assert/Assert.IsTrue（仅 Editor）；非热路径公共 API 做参数校验抛 ArgumentException；异常不吞——要么处理要么上抛。
- **代码组织：** 一文件一顶层类；类/接口/公有方法/枚举必须 &lt;summary&gt;（内容独占行）；严禁 TODO 入主干；#region 用于小范围分组（双语标签），严禁大段折叠掩盖 SRP 违例（违反则拆类）；asmdef 最小化依赖、禁止循环引用。
- **AOT/IL2CPP 兼容：** 禁止 Reflection.Emit/动态代码生成；反射仅限序列化/编辑器，运行时避免；泛型 AOT 预编译缺失时需预生成元数据或用非泛型路径；Type/enum 缓存为静态只读字段避免反复 GetType。
- **工具链与质量门：** 启用 Roslyn Analyzers；.editorconfig indent_size=4；提交前通过 ZeroAlloc 性能测试；PR 须通过编译 + Analyzer + 测试三重门。
- **执行等级：** Mandatory（违反打回：命名前缀、0-Alloc、防装箱、AOT 兼容）/ Prefer（性能敏感区必须，非热路径可放宽：Span/unsafe/池化/线程安全）/ Reference（逐步优化遗留）。

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
| 序列化字段（`private`/`internal`/`protected`） | `m_PascalCase` | ~332 处；公开面一律包 `PascalCase` 属性（`public int ID { get => m_ID; internal set => … }`） | 同；别为省事写 `public` 裸序列化字段 |
| 序列化字段（`public`/`protected internal`/file-local） | `PascalCase`，无 `m_` | 一致 | 同 |
| 静态字段 / 静态 readonly（`private`/`internal`/`protected`） | `s_PascalCase` | ~87 处 | 同 |
| 静态字段 / 静态 readonly（`public`/`protected internal`） | `PascalCase`，无 `s_` | ~156 处 | 同 |
| `const`（任何可见性） | `UNIFORM_CASE` | 主流（~453）；~122 处 `PascalCase`（`MemoryPool.Core`、`Save`、`Audio` 的量纲/上限/位域）；2 处 `k_Default…` 为外来写法 | 新 `const` 一律 `UNIFORM_CASE`；想要 `PascalCase` 就写 `static readonly`（Rider 不把它算作常量，两者语义也确实不同）；`k_` 不再引入 |
| `event` 成员 | `On` + `PascalCase`（另登记 `on` 变体；该规则未开前后缀告警，缺前缀不报红） | 带 `On` 7 处、无 `On` 约 20 处（`BlockSaved`、`LoadFailed` 式过去分词） | 新事件一律 `On`；旧事件不顺手改名（外部订阅面） |
| 泛型参数 | 表内未配（走 Rider 默认） | `T`、`TKey`/`TValue`、语义式 `TVoice`/`TLexer` | 单参数 `T`，多参数 `T` + 名词 |

前缀由**访问级别**决定，不看是否"真私有"：`internal` 走 `private` 口径（带 `m_`/`s_`/`_`），`public`/`protected internal`/file-local 走无前缀 `PascalCase`。

缩略词表里只登记了 `FSM` 与 `GOAP`（供 Rider 按整词切分，加前缀/自动重命名时不拆成 `F`+`S`+`M`）；`UI` 没登记而仓内一律写成 `UILayer`/`UIService`。要用新缩略词前先决定"登记"还是"照抄现状"，别两边各写一半。

下列是**仓库既定词汇**，DotSettings 管不到，但新模块照抄、不另造同义词：

- **服务三件套**：`XxxService.cs`（门面）+ `XxxServiceHandler.cs`（后端契约，派生 `FrameworkHandler`）+ `XxxServiceSettings.cs`（配置资产）；默认实现 `DefaultXxxHandler`，按后端的 `<Backend>XxxHandler`（`YooAssetHandler`、`UnityAudioHandler`、`MiddlewareAudioHandler`）。
- **生命周期**：覆写点一律 `protected virtual On*`（`OnInit`/`OnShutdown`/`OnInitAsync`/`OnShutdownAsync`/`OnUpdate`/`OnLowMemory`），框架内部门面一律 `internal Internal_*`（`Internal_Init`/`Internal_Shutdown`）。调用方只碰 `Internal_*`，不要在 `On*` 上直接互调。
- **SDK 适配层**：`IXxxBridge` 主接口 + `XxxBridgeStub`（无 SDK 也要能编译）+ `XxxBridgeNative`（真 SDK，整文件由 `*_INSTALLED` 宏门控）；可选能力另开窄接口按能力探测（`IAudioMiddlewareBankControl`、`IAudioMiddlewareRtpcControl`），不扩主接口。
- **目录与文件**：`Runtime/Services/<Module>/{Handler,Mix,Models,Spatial,Support}/`，后端进 `Handler/<Backend>/`；`Core/` 放无服务契约的基础件。数据结构在 `Models/`（`XxxEntry`/`XxxLease`/`XxxRequest`/`XxxOptions`/`XxxConfig`/`XxxSlot`），场景组件与辅助件在 `Support/`（`AudioEmitter`、`BgmPlaylist`、`AudioFault`、`ShuffleIndexBag`），契约/类型面按 `<Module>Types.cs`/`Abstractions`/`Callbacks`/`Contract` 分档。
- **partial 拆文件**：`Xxx.<职责>.cs`，职责名是首字母大写的单个英文名词（`.Core`/`.Slots`/`.Maintenance`/`.Bindings`/`.Async`/`.IO`，在仓 66 个）；拆文件不破坏"一文件一顶层类型"。
- **通用后缀**：静态工具 `XxxUtility`（单数）、扩展方法 `XxxExtensions`、账本 `XxxRegistry`、缓存 `XxxCache`、调度 `XxxScheduler`/`XxxStateMachine`、调试器面板 `XxxServiceDebuggerWindow`。
- **键名常量**：Mixer 参数与设置键按 `<域>_<对象>_<属性>` 全大写（`AUDIO_MASTER_VOLUME`、`GRAPHICS_FULLSCREEN_MODE`）；存档 schema 字段是 `PascalCase` + `Key` 后缀（`LocalPositionKey`、`SpawnsKey`）。两套并存是历史，改到哪个文件就跟哪个，不新造第三种。
- **程序集与命名空间**：本包自带 asmdef 为 `Moirai.Atropos`、`Moirai.Atropos.Editor`、`Moirai.Atropos.Tests.EditorMode`/`.PlayMode`（`Templates~` 下的 `GameLib`/`GameLogic`/`GameProto` 是工程侧模板，不属本包）。运行期与编辑器代码一律 `namespace Moirai.Atropos[.<Module>[.<Sub>]]`；测试用与被测模块对齐的**短命名空间**（`Service.Audio`、`Core.Events`、`Core.MemoryPool`），不带 `Moirai` 根——这正是 `CheckNamespace` 降级要护住的写法。
- **测试**：类 `<被测>Tests`（`AudioClipCacheTests`）、基准 `<被测>Benchmark`（一律 `[Explicit]`，不随常规套件跑）、夹具 `XxxTestSupport`/`XxxTestHost`/`MemoryPoolFixture`（派生式基座）。方法名 `场景_条件_期望` 三段式（`RetainRelease_CycleAllocatesZeroBytes`、`PauseGame_NestedSources_OnlyLastResumeRestoresSpeed`）。异常断言沿 `InnerException`/`AggregateException` 链判定，不用 `Assert.Throws<T>` 硬匹配（泛型 `new T()` 实走 `Activator.CreateInstance<T>()`，原始异常会被包装）。

**冲突怎么判**：DotSettings 与代码打架时以 DotSettings 为准（它是门禁，也是评审依据）；表里没写、仓内已成词汇的那一档（`Handler`/`Bridge`/`Registry`/`Support`）按仓库现状走。两类冲突都不许用 `// ReSharper disable` 或规则抑制绕过——要改先改规则，再改代码。

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
（焦点切过去即可，通常在几秒内）；正在编译、正在导入时不接新单。`mode` 支持 `EditMode`/`PlayMode`；PlayMode 进出场的域重载由驱动落盘 `Temp/MoiraiTestRunState.json` 自动续跑。可选 `timeoutSeconds` 是墙钟上限（秒，`0`/缺省不限时，编译、导入与域重载的等待计入），超时按 ABORTED 收口；`assemblies` 与 `tests` 均为空的请求会被直接拒绝收口——空过滤器会让 Test Runner 重跑上一次的选择集。

### 3. 代码优化
1. 使用 `/optimize` 分析性能
2. 识别瓶颈
3. 实施优化
4. 使用 `/review` 验证优化

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

- [Moirai Framework GitHub](https://github.com/Lx34r/com.moirai.framework)
- [YooAsset 文档](https://www.yooasset.com/)
- [HybridCLR 文档](https://hybridclr.doc.code-philosophy.com/)
- [Luban 文档](https://focus-creative-games.github.io/luban-doc/)
- [UniTask 文档](https://github.com/Cysharp/UniTask)
