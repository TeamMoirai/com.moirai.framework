# 测试细则（框架包）

本块是本包该主题的唯一权威落点：`CLAUDE.md` 与命令正文只留摘要与指针，同一段话在两边各写一遍即视为缺陷。

## 测试规范

完整规范见 [`Documentation~/zh/Testing.md`](Documentation~/zh/Testing.md)（英文对照 [`en/Testing.md`](Documentation~/en/Testing.md)）。以下是必须遵守的硬约束清单。

### 分层与归属

| 层 | 程序集 | 位置 | 放什么 |
|---|---|---|---|
| L1 单元/契约 | `Moirai.Atropos.Tests.EditorMode` | `Tests/EditorMode/` | 纯逻辑、数据结构、状态机、契约形状、降级路径 |
| L2 集成 | `Moirai.Atropos.Tests.PlayMode` | `Tests/PlayMode/` | 跨组件协作、真实帧驱动、场景/宿主生命周期、真实 IO |
| L3 玩家验收 | `Moirai.Atropos.Tests.Player` | `Tests/Player/` | 0-GC 热路径、托管分配计量（发布出口以玩家侧报告为准） |
| L4 基准 | 入口住 `Tests`（`[Explicit]` 薄壳）；Debugger 窗口双通道基准的矩阵核心在运行程序集（`XxxBenchmarkRunner`，public static） | `Tests/<层>/` 镜像被测模块 | 必须 `[Explicit]`，不进常规套件 |

能在 EditMode 判定的**必须**放 L1；不要为了"更真实"把纯逻辑塞进 PlayMode（慢、难归因、易 flaky）。

- **配置数据不进测试断言（强制）：** 用例只验**代码逻辑**（判据分支、契约形状、状态机、降级路径），不验**具体配置资产填得对不对**——既不枚举真实 `.asset`/断某字段的值，也不拿 `typeof(生产配置类型)` 当夹具（配置侧改名删类会把用例打成无关红）。数据层的结论由**校验器**（Odin Validator 的 `GlobalValidator`/`RootObjectValidator` 一类）在编辑期出：它按资产逐条给结论、可指向资产与一键修复，其显示属交互面按"待目测"报。因此同一条判据要拆两层——**不吃校验框架的纯判据层**（只收快照、路径字符串与注入的注册表，单测得动）+ **把结论搬到校验器报告面的薄层**；只在存量说明里报"当前有几个资产踩线"，别把它写成用例。

### 命名与结构

- 文件/类 `<被测>Tests`（`AudioClipCacheTests`）；**禁止 `XxxTest` 单数式**。
- 夹具基座 `XxxFixture`、共享支撑 `XxxTestSupport`/`XxxTestHost`、基准 `XxxBenchmark`。
- 用例方法 `场景_条件_期望` 三段式（`RetainRelease_CycleAllocatesZeroBytes`）。
- 命名空间用**短名**（`Service.Audio`、`Core.MemoryPool`、`Utility`），不带 `Moirai` 根；引用框架子命名空间类型**必须 `using` 别名**（`using Res = Moirai.Atropos.Resource;`），禁止裸限定名（会撞全局命名空间或 `UnityEngine` 类型，报 CS0246/CS0426）。
- 测试树**镜像到模块档为止**：`Runtime/Services/<Module>/…` 的用例落 `Tests/EditorMode/Service/<Module>/`，`Runtime/Core/<功能>/…` 落 `Tests/EditorMode/Core/<功能>/`。服务层内部的**角色档（`Handler/`、`Models/`、`Objects/`、`Internal/`、`Support/`）不在测试树里复刻**——本轮目录归位没有搬动 `Tests/**`，两侧同形只到模块/功能档，别按生产侧档名去测试树找目录。按 R3 分过服务档的模块连**模块档名**也不镜像：`Runtime/Services/Pooling/**` 的用例在 `Tests/EditorMode/Service/ObjectPool/`（测试树里没有 `Service/Pooling/` 这一目），池的用例按池找，不按模块名找。一个文件一个公开测试类；测试专用类型一律 `internal`。
- **禁止在测试里创建 `[Serializable]` 框架基类的子类**（`LogHandler`/`JsonHandler`/`TweenHandler`/`XxxServiceHandler` 等）——`[SerializeReference]` 类型扫描会把它们塞进生产资产的 Inspector 下拉框。捕获日志用内置实现 + `LogUtility.onMessageLogged`。
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

1. 行为契约守卫——断成员标注、经反射触达条件编译成员（`YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`、`AddressableHandlerFailFastTests`）；
2. 唤起 Unity 生命周期回调（`Awake`/`OnEnable`/`OnInit`）；
3. 产码字段探针（`MemoryPoolFixture.StaticField`）。

### 日志断言

`LogUtility` 的 Handler 可插拔：`DefaultLogHandler`/`ZLoggerHandler` 对 UTF 可见（须 `LogAssert.Expect`），`UnityLoggingHandler` **不可见**（声明 Expect 反报 "Expected log did not appear"）。

- 内容断言一律走 `LogUtility.onMessageLogged`（Handler 无关，唯一稳定通道）。
- `LogAssert.Expect` 的正则一律 `".*"`，只承担消除未处理日志的职责，不耦合 Handler 的渲染前缀。
- **消除未处理日志一律经 `UtfLogExpect`**（`Tests/EditorMode/Support/UtfLogExpect.cs`，PlayMode 侧有同名本地副本）：处理器可见性判定收在那一处，用例侧不写 `#if`、不提处理器类型。API 面：`Error()`/`Warning()`/`Exception()`（正则固定 `.*`）、`ErrorWithException(fragment)`（带异常对象的 Error 重载按处理器自述判级别）、`ScopedIgnore()`（`ignoreFailingMessages` 的 using 快照窗口，仅供错误集不可枚举的故障注入夹具，是唯一允许触碰该全局开关的入口）。不要在用例里自写 `LogAssert.Expect` + 处理器判定（未装 com.unity.logging 的工程里 `UnityLoggingHandler` 不存在，会逼出每处一个 `#if`）。**例外**：被测走 `Debug.Log*` 直发、不经 LogUtility 时（如 `DebuggerLogCaptureTests`），LogAssert 是唯一正确通道——该场景登记进 `TestLogChannelPolicyGuardTests.LogAssertAllowlist`。

### 基准

- 一律 `[Explicit]`，不进常规套件；命名 `XxxBenchmark`。
- 性能结论必须同工具同数据 before/after A/B；编辑器 Mono 基准 ±2× 噪声，只做同轮内比较。
- 菜单驱动/场景 MonoBehaviour 的手动基准已全部废止（含原 `TimerServiceBenchmark` 与 JSON Benchmark 菜单工具）：基准统一住 `Tests/`（`[Explicit]`），入口与双通道政策见《Testing 规范》基准政策（L4）节。

### 公共 API 变更口径（强制）

测试不把 API 形状（签名、成员数、名单）钉成基线常数——公共面的有意变更不在测试里登记，那类守卫要求每次变更先同步测试基线，把破坏性变更的登记义务搬进了测试。正确口径：

1. **破坏性更新（增删公共成员、改签名/约束/枚举编号）在 `CHANGELOG.md` 记迁移口径**：收掉了什么、旧调用方怎么迁；UI 载荷/事件类另记 `UIMigration.md`。
2. 反射白名单里的「契约守卫」只覆盖成员标注与运行行为的断言（如 `[NonSerialized]` 序列化边界），不承担形状登记。

### 覆盖率与出口准则

工具 `com.unity.testtools.codecoverage`（**2026-09-28 口径：Client 现未装该包**——覆盖率门须装包后才可执行；基线缺失情况已记入 Testing.md 治理账本）；assemblyFilters `+Moirai.Atropos`，排除 `Moirai.Atropos.Editor`/`Moirai.Atropos.Tests.*`/生成代码；报告落盘 `Tests/Coverage/`。

| 档 | 范围 | 行覆盖 | 分支覆盖 |
|---|---|---|---|
| 核心服务 | Resource/Save/Audio/UI/Kernel | ≥ 80% | ≥ 70% |
| 其余服务 | ConfigTable/Debugger/Input/Localization/ObjectPool/Procedure/Scene/Timer | ≥ 70% | — |
| Editor 工具与生成代码 | `Moirai.Atropos.Editor`、SourceGenerators | ≥ 50% | — |

覆盖率是**找空洞的工具，不是质量指标**；评审用例看断言强度，不看百分比。新代码不得让所在模块覆盖率下降。

**发布出口五门**（缺一不可）：编译 0 error → L1 全量 0 失败 → L2 全量 0 失败 → L3 `Run all in Player` 0 失败 → 覆盖率不低于分级阈值与上一版基线。2026-09-28 起 L2 套件包含 `Tests/Player` 的 0-GC 计量格真跑，其红同打穿 L2 门与 L3 门——两门不再互斥。**基线必须绿**——套件有红时"全绿"信号失效，必须先修红再继续开发。

### 可执行守卫与基准归一（2026-09-27）

- 反射白名单钉成可执行守卫 `ReflectionPolicyGuardTests`（双向断言：未登记不得出现、已登记必须仍命中）；测试日志通道同构落 `TestLogChannelPolicyGuardTests`——测试日志发射统一 `Debug.Log*`，禁 `LogUtility.Verbose/Debug/Info/Warning/Error/Fatal/Assert(`，白名单=被测本体/替身复刻生产发射，断言通道（onMessageLogged/UtfLogExpect）不受限；同一守卫另钉 **LogAssert 通道**：用例侧禁直用 `LogAssert.Expect`/`ignoreFailingMessages`/`NoUnexpectedReceived`（一律经 UtfLogExpect，后者只经 `ScopedIgnore()`），白名单=两份 UtfLogExpect 副本+Debug 直发场景，双向断言防名单腐烂；守卫按原文扫描，注释写「LogUtility 的 Error」规避字面命中。
- 所有基准住 `Tests/`（`[Explicit]`，KernelBenchmark 范式），跑完经 `BenchmarkReport` 落 XML 至统一文件夹 `<工程根>/Benchmarks/`（`MOIRAI_BENCH_XML` 可覆盖）。需 Debugger 窗口跑的基准走双通道：矩阵核心 `XxxBenchmarkRunner`（运行程序集，public static）+ 窗口基准区 + Tests `[Explicit]` 薄壳，两入口同一份矩阵；帧依赖 fire 用例住 PlayMode `[UnityTest]`。
- 反膨胀三原则（存量不追改增量强制 / 夹具基座触发条件 / 用例价值映射）与教训账本见 `Documentation~/zh/Testing.md`《可执行政策守卫与治理原则》。

