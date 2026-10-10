# Testing 测试规范

> 框架测试的分层归属、用例规范、运行通道、门禁阈值与发布出口准则。本文件是测试相关约束的**唯一权威源**；`CLAUDE.md` 中的《测试规范》《AI 测试流程》两节是面向代理的执行摘要，冲突时以本文件为准。

## 定位

框架的测试不是"补作业"，而是**回归锁**：每一次被修掉的缺陷、每一条对外契约、每一处性能承诺，都要有一格用例把它钉住。判据是"删掉这格用例，缺陷能不能悄悄回来"——能，就是有价值的用例；不能，就该删掉它。

三条基本原则：

1. **用例断言行为，不断言实现。** 断言"调 X 之后 Y 成立"，而不是"X 内部调了 Z"。绑定实现的用例会在重构时成批变红，却抓不到真缺陷。
2. **失败必须能归因。** 一格红必须能直接指向一个明确的原因；需要人肉复现才能判断的用例等于没有。
3. **确定性优先于覆盖率。** 一个 flaky 用例对信号价值的破坏，远大于它贡献的那点覆盖率。

## 测试分层与归属

四层，各司其职。**新写用例前先按下表选层**——选错层是用例质量问题的第一来源。

| 层 | 程序集 | 位置 | 覆盖对象 | 判据 |
|---|---|---|---|---|
| **L1 单元 / 契约** | `Moirai.Atropos.Tests.EditorMode` | `Tests/EditorMode/` | 纯逻辑、数据结构、状态机、契约形状、降级路径 | 不需要 Unity 运行期（PlayerLoop、真实帧、场景、真实 IO）即可判定 |
| **L2 集成** | `Moirai.Atropos.Tests.PlayMode` | `Tests/PlayMode/` | 跨组件协作、真实帧驱动、场景/宿主生命周期、真实 IO 往返 | 结论依赖"跑起来"——进播放态才能观察到 |
| **L3 玩家验收** | `Moirai.Atropos.Tests.Player` | `Tests/Player/` | 0-GC 热路径、托管分配计量、IL2CPP 行为差异 | 事件口径（`GC.Alloc` 采样）在编辑器 PlayMode 也推进（2026-10-10 实证：校准格绿并当场逮到开窗路径的委托分配），但**发布出口以玩家构建的报告为准** |
| **L4 基准** | `[Explicit]` 标记，随所在程序集 | 与模块同目录或 `Tests/Benchmarks/` | 吞吐/延迟/分配基线 | 不参与常规套件；由人工或 benchmark CI 通道触发 |

### 选层判据

- 能在 EditMode 判定 → **必须**放 L1。不要为了"更真实"把纯逻辑塞进 PlayMode（慢、难归因、易 flaky）。
- 需要 `Awake`/`OnDestroy`/`DontDestroyOnLoad`/协程/真实 `Update` → L2。
- 结论依赖**托管分配计量** → L3（编辑器 Mono 的 `GC.GetAllocatedBytesForCurrentThread()` 恒返回 0，见下文《0-GC 验收》）。
- 只是"想知道现在有多快" → L4，且必须 `[Explicit]`。

### 当前分布（2026-10-10 复算，测试方法数口径）

| 层 | 文件 | 用例 |
|---|---|---|
| L1 | 180（含支撑文件） | 2215 |
| L2 | 23（测试 15：Audio×11 + Kernel + Tasks + Timer + Scene；其余为支撑/host） | 122（另 1 格 skipped） |
| L3 | 10 | 28 |

「用例」列取当日整趟运行回执（含 `[TestCase]` 展开），复现口径是运行 `run g-l1-1 | passed 2215 | failed 0`、`run g-l2-1 | passed 122 | failed 0 | skipped 1`；按源码声明数另用 `grep -rh '\[Test' Tests/<层> --include=*.cs | grep -c '^\s*\[Test\]'`（该口径不含 `[TestCase]` 行，故比回执小）。

## 覆盖目标

按「用例价值判据」（删掉这格缺陷能不能回来）滚动补齐，不追覆盖率数字。当前缺口账本（2026-09-27）：

| 缺口 | 状态 | 去处 |
|---|---|---|
| 事件传播契约（Bubbles/TricklesDown 路径） | 延后——与 P1 缺陷纠缠（传播路径丢目标），修复批次携带先红后绿 | 修复批 A |
| GameApp 发布期分支（RETHROW=false） | 延后——编辑器编译期不可达，需 L3+发布构建配置 | L3 |
| Scene 异步编排（挂起/取消→分离收尾守卫/失败恢复） | 待补；不重复 SceneRegistryTests 的同步注册表判定 | L2 |
| UI 窗口栈生命周期（现行绿语义） | 待补；幽灵窗口（加载失败静默）断言留修复批 | L2 |
| Debugger OnlyOpenWhenDevelopment×非调试构建注册分支 | 延后——`ResolveActivation` 直读 `Debug.isDebugBuild`（编辑器恒 true、无注入接缝），与 GameApp 发布分支同类环境不可达 | 注入接缝或 L3 |
| Save 维护门互锁 / 日志等级过滤 / 跨线程契约 | **已覆盖**（审计证实，勿重建） | — |
| Timer 0-GC | **已补** TimerHotPathAllocationTests | L3 |
| UI 开窗族 0-GC | **已补** UIOpenWindowAllocationTests（2026-10-10：换上事件口径当场逮到「每次停放重取一个委托」，修后稳态 0 事件） | L3 |
| 取池地址归一化 0-GC | **已补** PoolLocationAllocationTests（同批从 L1 的字节尺迁来）；同夹具剩下的 `HotPath_ZeroGcAlloc_WarmPoolRoundtrip` 因依赖池夹具机器（假装载器/注册表/调度器/根节点）**仍在用恒 0 的字节尺**，未迁 | L1（待迁 L3） |
| 加密与路径工具契约（AES 往返/防篡改、XOR 自逆、路径规范化/远程前缀） | **已补** EncryptionUtilityTests / PathUtilityTests（2026-10-06） | L1 |
| `CommandLineUtility` / `VersionUtility` / `FileUtility` 等其余无直接用例的工具 | 待补（按用例价值判据滚动，不追数字） | L1 |
| `PathUtility.CommonPath` / `TruncatePath` | 运行时零调用方且 CommonPath 对更短路径会越界——死 API，不锁用例；复活前先修实现 | — |

## 目录结构

```
Tests/
├── EditorMode/                  # L1
│   ├── Moirai.Atropos.Tests.EditorMode.asmdef
│   ├── TestRequestRunner.cs     # 测试桥（基础设施，非用例）
│   ├── EditorStateBridge.cs     # 编辑器状态桥（基础设施，非用例）
│   ├── Core/                    # 对应 Runtime/Core/<模块>
│   │   ├── MemoryPool/
│   │   ├── Singleton/
│   │   ├── Events/
│   │   └── GameApp/
│   ├── DataStructure/           # 对应 Runtime/Core/Foundation/DataStructure（纯数据结构用例）
│   ├── Service/                 # 对应 Runtime/Services/<模块>
│   │   ├── Audio/  Save/  Resource/  UI/  ...
│   │   └── Kernel/
│   └── Utility/                 # 对应 Runtime/Core/Utilities
├── PlayMode/                    # L2
│   └── Service/<模块>/
└── Player/                      # L3
    ├── PlayerTestBootstrap.cs   # 关掉 GameApp.AutoBoot（基础设施）
    └── Service/<模块>/
```

规则：

- **测试目录镜像被测目录**。`Runtime/Services/Save/Container/X.cs` 的用例落在 `Tests/EditorMode/Service/Save/`。模块内再细分的子目录不强制镜像。
- 基础设施（桥、Bootstrap、共享支撑）与被测模块无关的，放测试根目录或就近的模块目录。
- 一个被测类型可以有多个用例文件（按行为簇拆分），但**一个文件只放一个公开测试类**（与"一文件一顶层类"一致）；夹具基座与测试专用类型是例外，见《夹具与隔离》。

## 命名规范

| 元素 | 口径 | 示例 |
|---|---|---|
| 测试文件 / 类 | `<被测>Tests` | `AudioClipCacheTests`、`SaveMigrationBusTests` |
| 夹具基座 | `XxxFixture` | `MemoryPoolFixture` |
| 共享支撑 | `XxxTestSupport` / `XxxTestHost` | `AudioCacheTestSupport`、`AudioServiceTestHost` |
| 基准 | `XxxBenchmark`，且必须 `[Explicit]` | `GenericObjectPoolBenchmark` |
| 用例方法 | `场景_条件_期望` 三段式 | `RetainRelease_CycleAllocatesZeroBytes`、`PauseGame_NestedSources_OnlyLastResumeRestoresSpeed` |
| 命名空间 | **短名**，与被测模块对齐，不带 `Moirai` 根 | `Service.Audio`、`Core.MemoryPool`、`Utility` |

> 命名空间用短名是**有意为之**（`CheckNamespace` 规则已降级为 SUGGESTION 正是为此）。测试程序集自身声明了 `Resource`/`ObjectPool`/`Utility` 等全局命名空间，与被测子命名空间撞名；且 `UnityEngine` 的 `Audio`/`UI`/`Input` 类会遮蔽框架子命名空间。**在测试文件里引用框架子命名空间类型必须用 `using` 别名**（`using Res = Moirai.Atropos.Resource;`），禁止裸限定名（`Resource.Xxx` 会解析成全局命名空间或 `UnityEngine` 类型而报 CS0246/CS0426）。

### 测试专用类型的三条禁令

1. **不得创建 `[Serializable]` 框架基类的自定义子类**——`LogHandler`、`JsonHandler`、`TweenHandler`、各 `XxxServiceHandler` 等基类都以 `[SerializeReference]` 字段使用，Unity 会扫描**所有程序集**查找派生类并填入 Inspector 下拉框，测试里的假实现会污染生产资产的下拉列表。要捕获日志用框架内置实现 + 事件回调（`LogUtility.onMessageLogged`）。确需派生框架基类的行为替身（Handler 探针、云存档假件）：派生类**不带 `[Serializable]`** 且一律 `internal`——`[Serializable]` 不被继承，SerializeReference 的 Inspector 下拉只收录带该特性的派生，替身因此不进生产资产（Save / Localization 两侧同口径）。
2. **测试专用类型一律 `internal`**，且只放在测试程序集内。
3. **`Test` / `Editor` / 非运行时脚本中的日志一律用 `Debug.LogXX`**，不用 `LogUtility`（`LogUtility` 是带分类过滤与 Handler 管道的运行时基础设施，测试不需要，且会让"这条日志算不算测试失败"变得不可控）。

## 夹具与隔离

池、注册表、单例、静态配置都是**进程级全局状态**。一个用例漏还一个对象，下一个用例会读到虚高的计数，表现为"单独跑绿、整套跑红"。夹具基座的职责就是把这种污染**当场钉死在用例自己的红上**。

### 基座契约

```csharp
public abstract class XxxFixture
{
    [SetUp]
    public void SetUpFixture()
    {
        // 1. 快照所有将被改动的全局旋钮
        // 2. 复位被测对象到已知状态（清空内容、给足容量、统计归零）
        // 3. 断言初始状态干净（带上个用例的名字，便于归因）
    }

    [TearDown]
    public void TearDownFixture()
    {
        // 1. 断言无残留（未归还的租约、未注销的订阅、未释放的句柄）
        // 2. 清理被测对象
        // 3. finally 中还原全部全局旋钮（即使上面抛了也要还原）
        // 4. 把收集到的多个异常聚合成 AggregateException 抛出，不要吞
    }
}
```

要点：

- **`TearDown` 里必须还原全局旋钮，且必须放在 `finally`**。否则一次失败的用例会把污染外溢到后续所有用例。
- **`TearDown` 里的清理异常要收集后聚合抛出**，不能因为第一个异常就跳过后续还原。
- **断言初始状态干净**（如 `Assert.AreEqual(0, Info<T>().UsingCount, ...)`）。只清理不断言，会让上一个用例的泄漏变成这个用例的莫名其妙。

### EditMode 与 PlayMode 的生命周期差异（高频坑）

| 行为 | EditMode | PlayMode |
|---|---|---|
| 非 `[ExecuteInEditMode]` 组件的 `Awake` / `OnDestroy` | **不执行**（含活跃物体上 `AddComponent`） | 执行 |
| `DontDestroyOnLoad` | **抛 `InvalidOperationException`** | 正常 |
| `Time.frameCount` | **不推进** | 推进 |
| 协程 | 需要外部逐帧驱动 | 由引擎驱动 |
| `UniTask.SwitchToMainThread` | 可用（UniTask 经 `EditorApplication.update` 泵 PlayerLoop） | 可用 |

由此推出三条硬约束：

1. 运行期依赖 `Awake` 注册的管线，必须提供**显式注册兜底**（`EnsureActivated` 幂等模式），不能假设 EditMode 用例里 `Awake` 跑过。
2. 任何 `DontDestroyOnLoad` 调用点必须加 `Application.isPlaying` 守卫，否则该模块的 EditMode 用例全红。
3. EditMode 下需要"跨帧"的逻辑，必须**自带帧号游标**（`Tick(++Frame)`）而不是依赖 `Time.frameCount`。

### `[UnityTest]` 与线程

- 需要"在主线程上跨帧做某事"（托管分配计量、Unity API 时序、`ProfilerRecorder`）→ **必须用 `[UnityTest]` + `IEnumerator`**，`yield return null` 逐帧驱动。`async Task` 的续体在**线程池线程**执行（Unity 无 `SynchronizationContext` 捕获），在那里触碰 Unity API 即主线程违例，结论全部无效。
- 框架外观方法在 `await` 一个内部走线程池的 Handler 任务之后、触碰 Unity API 之前，必须 `if (!MainThreadDispatcher.IsMainThread) await UniTask.SwitchToMainThread(ct);`。评审"外观 await Handler 后干活"的代码时，**线程归属是必查项**。

### 禁止反射调用字段

`Runtime/AssemblyInfo.cs` 已对 `Moirai.Atropos.Editor` 与三个测试程序集开了 `InternalsVisibleTo`，所以：

- **需要触达的成员把访问级别 `private` → `internal`**，不要用反射。
- 反射把字段名变成测试依赖：改名不报编译错，只在运行期 `GetField` 返回 null 后 NRE；`internal` 由编译器把关。
- 序列化字段改 `internal` 不影响 Unity 序列化（`[SerializeField]` 不要求 `private`），前缀仍走 `m_`/`s_`/`_` 私有家族口径。
- 已有窄接缝的成员不为此放开字段：换处理器走生成的 `Internal_PeekHandler()` / `Internal_UseHandler(next)`，`s_Handler` 保持 `private`。

**反射白名单**（仅这三类正当用途，且必须在文件头写明理由）：

1. **行为契约守卫**——断成员标注、经反射触达条件编译成员（`YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`：序列化边界；`AddressableHandlerFailFastTests`：条件编译后端的 fail-fast 面）。这类**只能**反射，别当违例删掉。
2. **唤起 Unity 生命周期回调**——`Awake` / `OnEnable` / `OnInit`。
3. **产码字段探针**——读取代码生成器产出的字段（`MemoryPoolFixture.StaticField`）。

> 白名单不是免罪符：新增一条反射必须能明确归入以上三类之一，否则一律改 `internal`。

## 确定性纪律

**一个 flaky 用例比十个没写的用例更糟**——它让人开始忽略红色。

- **禁止真实墙钟等待**（`Thread.Sleep`、`await Task.Delay` 做时序断言、`DateTime.Now` 做判据）。时间必须**注入**：计时器用例自带帧号游标与 `Advance(delta)`。 <br />
  边界：**给替身注入延迟**（fake HTTP handler 的 `Task.Delay` 模拟网络延迟、工作负载里 `Thread.Sleep(3)` 压超预算）不属此列——延迟是被注入的输入而非等待判据；跨线程**阻塞门探测**允许有界等待，但等待方向必须是「期望未完成」（超时只导致弱化信号，不会假红——范例：Save 文件级门的并发删除用例）。
- **随机必须定种**。`RandomSource` / `RandomUtility` 的用例固定种子；禁止依赖默认随机。
- **禁止跨用例共享可变静态**。需要共享的只读数据用 `static readonly`。
- **禁止依赖用例执行顺序**。整套跑绿 ≠ 单跑绿；两者都必须绿。
- **`Assert.ThrowsAsync<T>` 对 async lambda 会因 `TaskCanceledException` 精确类型不匹配而失败**——用 `task.GetAwaiter().GetResult()` 取原始异常类型。
- **异常断言沿 `InnerException` / `AggregateException` 链判定**，不用 `Assert.Throws<T>` 硬匹配（泛型 `new T()` 实走 `Activator.CreateInstance<T>()`，原始异常会被包装）。
- **断言值类型结果先取字段**：`TryLoadBlock` 返回 `SaveResult<T>` 时写 `Assert.AreEqual(SaveError.None, result.Error)`，直接比较会报全限定类型名而非枚举值。

### flaky 处置流程

1. **隔离重跑**：单跑该夹具 / 该用例。绿 → 是跨夹具串扰或顺序依赖，按上文修；仍红 → 是真实缺陷或环境依赖。
2. **确认归因**：`git diff` 排除并行改动；确认是不是自己引入的。
3. **禁止用 `Assert.Ignore` 掩盖**。Ignore 只能用于"能力探测失败"这类**环境性**原因（见《0-GC 验收》），且必须在注释里写明探测的是什么能力、在什么条件下会恢复。
4. 修不掉且确认是既有断裂 → 记录在案（issue 或 CHANGELOG 的已知问题），**不要留在套件里红着**。

## 日志断言政策

框架的 `LogUtility` 是**可插拔**的：测试域激活的 Handler 会随 `GameAppSettings.asset` 的 `m_LogHandler` 变化。这直接决定了断言写法。

| Handler | Unity Test Framework 是否可见 | 后果 |
|---|---|---|
| `DefaultLogHandler` | 可见 | 必须 `LogAssert.Expect` |
| `ZLoggerHandler` | 可见（仍经 `Debug` 通路） | 必须 `LogAssert.Expect` |
| `UnityLoggingHandler` | **不可见**（`ConsoleWindow.AddMessage` 直写） | 声明 `Expect` 反而报 "Expected log did not appear" |

因此：

- **内容断言一律走内部事件** `LogUtility.onMessageLogged`——它与 Handler 无关，是唯一稳定的断言通道。
- **`LogAssert.Expect` 只承担"消除未处理日志"的职责，正则一律用 `".*"`**，不要在正则里耦合 Handler 的渲染前缀（`[ERR]`/`[FAT]` 三字符前缀与文档里的 `[ERROR]`/`FATAL` 不一致，会成批假红）。
- **消除未处理日志一律经 `UtfLogExpect`**（`Tests/EditorMode/Support/UtfLogExpect.cs`；PlayMode 侧有同名本地副本）：处理器可见性判定收在那一处，用例侧不写 `#if`、不提处理器类型。**不要**在用例里自己写 `LogAssert.Expect` 加处理器判定——那会把「未装 com.unity.logging 的工程里 `UnityLoggingHandler` 根本不存在」扩散成每处一个 `#if`。API 面：
  - `Error()` / `Warning()` / `Exception()`：三条基础级别各一个声明，正则固定 `.*`；
  - `ErrorWithException(fragment)`：带异常对象的 Error 重载——级别随处理器自述（`ErrorWithExceptionUsesExceptionChannel`），ZLogger 下转 Exception 通道、DefaultLogHandler 下仍是 Error；
  - `ScopedIgnore()`：`ignoreFailingMessages` 的 using 形态窗口（构造快照、Dispose 还原），供**错误集不可枚举的故障注入夹具**使用。取舍：窗口内所有未处理日志（含真缺陷的）都不判红，相当于放弃「意外错误也要红」这层信号——只在夹具确实不需要该信号时选用；错误集可枚举的用例逐条声明。它是唯一允许触碰该全局开关的入口。
  - 测试程序集因此保留 `com.unity.logging` → `UNITY_LOGGING_INSTALLED` 的 `versionDefines`：全仓只有 `UtfLogExpect` 一处需要该宏，别在别处再依赖它。
- **被测走 `Debug.Log*` 直发、不经 LogUtility 的场景，`LogAssert` 是唯一正确通道**——该链路对 UTF 恒可见，声明预期不涉及处理器判定，用 `UtfLogExpect` 反而会在 UnityLoggingHandler 下漏声明而假红（范例：`DebuggerLogCaptureTests` 测的就是 Unity 控制台捕获器）。这类文件登记进 `TestLogChannelPolicyGuardTests.LogAssertAllowlist` 并写明归类；除此之外的直用一律按上一条走 `UtfLogExpect`。

## 0-GC 验收（L3）

**字节口径的 GC 计数 API 在 Unity 内无实现**：`GC.GetAllocatedBytesForCurrentThread()` 在编辑器 Mono、Mono 玩家、IL2CPP 玩家三处实测恒返回 0（含主线程一次 64MB 且被真实读写的分配，delta 仍为 0）；`GC.GetTotalAllocatedBytes()` 在 Unity .NET profile 不存在（CS0117）。**可用的托管分配计量是 `GC.Alloc` 采样事件数**（`Recorder.Get("GC.Alloc")` + `sampleBlockCount`，UTF 官方 `AllocatingGCMemory` 同机制）——2026-09-28 实证三处都有牙：编辑器 PlayMode 套件真跑 0-GC 格并逮到过产品热路径分配。**绝不能把"测不出分配"当成"没有分配"；探不到时按能力探针整组 `Ignore`——Ignore 既不是假绿也不是绿。**

因此 0-GC 用例的正确形态是：

```csharp
// 校准：确认测量台能抓到分配（计数器不可用时 MeasureManaged 自行 Ignore；能走到这里就必须抓到）
AllocationCapture.CalibrateKnownAllocation();

// 稳态测量：内部先预热一次并丢弃（JIT/池扩容落在预热里），再计 iterations 次——事件数口径
long allocs = AllocationCapture.MeasureManaged("cached-play-stop", 200,
    () => PlayCached(),
    b => Assert.AreEqual(0, b, "热路径不应有托管分配事件"));
```

- 计数器不可用时 `MeasureManaged` 会 **`Assert.Ignore`**（测量台 `AllocationCapture` 自带能力探测缓存）。**绝不要写"前后差"计量**。
- 这条禁令有实测背书（2026-10-10）：同一把事件尺量「开窗+关窗」稳态往返读到 **1 事件/往返**，同一条路径上的字节尺前后差读 **0**；往测量窗里加一次 `new byte[4096]`，事件尺按 **+1 事件/往返** 精确跟随（同趟校准格 `CalibrateKnownAllocation` 绿）。字节尺读 0 时，`AreEqual(0, after - before)` 与 `LessOrEqual(delta, 阈值)` 两形都是必真的绿——后者还把分配写成了许可。
- 0-GC 断言的**发布出口以 L3 玩家侧报告为准**（最终判据是 IL2CPP 玩家）；编辑器 PlayMode 同机制可跑（便于日常回归），编辑器跑绿不替代玩家验收。计量走 `GC.Alloc` 采样事件数，UTF 官方 AllocatingGCMemory 同机制——字节口径 GC 计数 API 在 Unity 内无实现：`GetAllocatedBytesForCurrentThread` 三处实测恒 0，`GetTotalAllocatedBytes` 在 Unity profile 不存在。

### 玩家侧用例的运行方式（2026-09-28 按实证重写）

`Moirai.Atropos.Tests.Player` 的 `defineConstraints` 是 `["UNITY_INCLUDE_TESTS"]`——**编辑器里编译、可见**；0-GC 计量格经 GC.Alloc 采样能力探针决定真跑或整组 Ignore（可见不是死格、Ignore 不是假绿）。实证前提（2026-09-28，StandaloneOSX 双后端）：

1. **玩家测试收录以编辑器可见性为前提**：UTF 玩家测试运行（GUI 与 CLI 同机制，构建带 `BuildOptions.IncludeTestAssemblies`）只收录编辑器可见的测试程序集——`!UNITY_EDITOR` 约束的程序集永远不进玩家测试包。「编辑器不编译 + Run all in Player」的组合在任何环境都不执行。
2. **发起通道三选一**：Test Runner 窗口 PlayMode 页签 → `Run all in Player`（可用搜索框把范围缩到目标夹具，结论以玩家侧报告为准）；本包 `Window/General/Test Player Runner` 窗口（参数化一键发起：目标平台/过滤/心跳超时/报告路径 + CLI 等价命令复制，报告自动落盘）；或 CLI `Unity -batchmode -projectPath <工程> -runTests -testPlatform StandaloneOSX -testResults <xml> -logFile <log>`（退出码 0=全过 / 2=测试失败 / 3=RunError / 4=平台名错；前提是 GUI 编辑器已关——工程锁互斥）。三条通道走同一 `PlayerLauncher` 机制。
3. **不要自己 `BuildPipeline.BuildPlayer` 搭测试玩家**。玩家里的测试入口不是 `-runTests` 参数，而是构建期注入的引导场景（`CreateBootstrapSceneTask` 生成挂 `PlaymodeTestsController` 的 `Assets/InitTestScene<guid>.unity`）；手搓玩家没有这个场景，`-runTests` 什么也不会发生。且玩家**自己不写结果 XML**——结果经 `RemoteTestResultSender` 走 PlayerConnection 回传编辑器，由编辑器落盘（CLI 模式由 UTF 写入 `-testResults`）。
4. **`Tests/Player/PlayerTestBootstrap.cs` 是必需前置**：玩家默认自动启动框架（`GameApp.AutoBoot` 默认 true），测试玩家跑的是空场景，UI 后端等不到 `UIRootBinding` 登记，启动链在 `UGUIHandler` 报出「UI 根尚未绑定」后停住，测试运行永远轮不到。Bootstrap 在 `AfterAssembliesLoaded` 把 `AutoBoot` 置 false——**仅玩家域生效（`#if !UNITY_EDITOR`）**，编辑器 PlayMode 测试域依赖自动启动链（L2 门禁前提），绝不能在编辑器里掐。
5. **0-GC 真计量走 GC.Alloc 采样**：`AllocationCapture` 以 `GC.Alloc` 采样事件数计量（UTF 官方 AllocatingGCMemory 同机制、同款 API）；采样探不到的运行时整组 Ignore。字节口径 GC 计数 API 在 Unity 内无实现（`GetAllocatedBytesForCurrentThread` 在编辑器 Mono、Mono 玩家、IL2CPP 玩家三处实测恒 0，`GetTotalAllocatedBytes` 在 Unity profile 不存在）。Standalone 测试玩家后端按发布目标取——IL2CPP 增量缓存已建，改一个程序集后的测试玩家构建约 3.5 分钟。
6. **钩子与取参**：`LocalizationChannelBuildHook` 在无 `-CustomArgs:` 前缀时按「缺省不动」早退；CI 出包仍按约定传 `-CustomArgs:platform=X;localizationLanguage=Y`，前缀在而键缺失照样响亮报错。

### IL2CPP 玩家的验证判据

- **`_Data/Managed/` 不存在**。用 `File.Exists(.../Managed/X.dll)` 判断"程序集有没有进包"会得到**假阴性**。
- 正确判据：`_Data/ScriptingAssemblies.json` 里是否列出程序集名；或 ISO-8859-1 解码 `il2cpp_data/Metadata/global-metadata.dat` 搜类型名（UTF-8 标识符，命中即已编入）。
- 成本参考（StandaloneWindows64）：冷构建 ≈ 22 分钟（3.0 GB Development 包）；只改一个程序集后的增量构建 ≈ 3.5 分钟。所以"改完再验一轮"并不昂贵。

## 基准政策（L4，2026-09-27 归一后）

**所有基准住 `Tests/`（KernelBenchmark 范式：`[Explicit]` NUnit，目录镜像被测模块），跑完经 `BenchmarkReport` 输出 XML 到统一文件夹 `<工程根>/Benchmarks/<name>-benchmark.xml`**（`MOIRAI_BENCH_XML` 环境变量可覆盖；编辑器经 `Application.dataPath` 父目录推工程根——`temporaryCachePath` 在 Unity 6 编辑器指系统临时目录，推不出工程根）。

- 一律 `[Explicit]`，**不随常规套件跑**；经测试桥按名显式执行。基准数值受机器负载影响，混进回归套件只会制造噪音。
- **双通道基准**（需 Debugger 窗口也能跑的，如 MemoryPool/Timer）：矩阵核心（`XxxBenchmarkRunner`，public static，**住运行程序集**——运行时调试器窗口够不到测试程序集）+ Debugger 窗口基准区（Run/Export 按钮）+ Tests `[Explicit]` 薄壳（调 `Runner.Run()` 后写 XML）——两个入口跑同一份矩阵代码。同步矩阵直驱隔离 handler（不依赖门面懒加载的活服务世界）；依赖真实帧的 fire/burst 用例住 PlayMode `[Explicit]` `[UnityTest]`。
- 软校验口径：矩阵内不变量命中只累加 `failures` 计数并 LogWarning，不抛出——正确性回归归测试族，基准是测量不是验收。
- **性能结论必须用同一工具、同一数据做 before/after A/B 实测**（跨工具数据不可比）。编辑器 Mono 基准有 ±2× 噪声，只做同轮内比较。
- 回调一律缓存方法组字段（C# 9 不缓存方法组转换，裸写每次分配一个委托，污染 0-GC 基准）。
- CI 基准通道见 `Packages/GitHubActions~/README.BENCHMARK.md`。

## 公共 API 变更口径

测试不把 API 形状（签名、成员数、名单）钉成基线常数——公共面的有意变更不在测试里登记基线，那类守卫要求每次变更先同步测试，把破坏性变更的登记义务搬进了测试。正确口径：

1. **破坏性更新（增删公共成员、改签名/约束/枚举编号）在 `CHANGELOG.md` 记迁移口径**：收掉了什么、旧调用方怎么迁；UI 载荷/事件类另记 `UIMigration.md`。
2. 反射白名单里的「契约守卫」只覆盖成员标注与运行行为的断言（如 `[NonSerialized]` 序列化边界），不承担形状登记。

## 覆盖率与门禁

工具：`com.unity.testtools.codecoverage`（1.3.0）。**运行配方（编辑器窗口 / batchmode CLI）、过滤口径与门禁脚本见 [`Tests/Coverage/README.md`](../../Tests/Coverage/README.md)**，本节只写判定口径。

CI 侧由 `.github/workflows/coverage.yaml` 执行同一套：插桩跑一轮 EditMode，再用 `Tests/Coverage/coverage-gate.ps1` 判定；脚本读不到报告、XML 结构不认识、没有任何 class 行，一律非零退出——**「测不出来」不等于「达标」**。测试与 PlayMode 回归见 `.github/workflows/tests.yaml`，`.meta` 完整性见 `.github/workflows/metas.yaml`（三者均仅 `pull_request` 触发）。

> 本仓库是 UPM 包，没有 `ProjectSettings/`，Unity 测试必须在**宿主工程**里跑；workflow 因此 checkout 宿主工程仓库（`HOST_REPOSITORY`）再执行。接入时需按实际情况调整该值并配置 `HOST_REPO_TOKEN`。

### 配置口径

- **assemblyFilters**：`+Moirai.Atropos`；`-Moirai.Atropos.Editor`、`-Moirai.Atropos.Tests.*`、`-*.Generated`、`-Moirai.Atropos.SourceGenerators.*`。
- 开启 `GenerateAdditionalMetrics`（分支/复杂度）。
- 报告落盘 `Tests/Coverage/`（`baseline-<date>.md` 记录基线，`latest/` 为当次产物，`latest/` 不入版本控制）。

### 分级阈值（2026-09-24 定）

| 模块档 | 范围 | 行覆盖 | 分支覆盖 |
|---|---|---|---|
| **核心服务** | Resource / Save / Audio / UI / Kernel | ≥ 80% | ≥ 70% |
| **其余服务** | ConfigTable / Debugger / Input / Localization / ObjectPool / Procedure / Scene / Timer | ≥ 70% | — |
| **Editor 工具与生成代码** | `Moirai.Atropos.Editor`、SourceGenerators | ≥ 50% | — |

说明：

- 阈值是**下限**，不是目标。核心服务应当显著高于 80%。
- **覆盖率是发现空洞的工具，不是质量指标**。100% 行覆盖 + 全是 `Assert.IsNotNull` 的用例，价值为零。评审用例看断言强度，不看百分比。
- 新代码不得让所在模块的覆盖率**下降**（CI 上对比基线）。
- 差距表（当前基线 → 目标）随基线报告一起维护。

## 发布出口准则

一次发布必须**五门全绿**，缺一不可：

| 门 | 判据 |
|---|---|
| 1. 编译 | 0 error（含 Roslyn Analyzer 无新增告警） |
| 2. L1 回归 | `Moirai.Atropos.Tests.EditorMode` 全量 0 失败 |
| 3. L2 回归 | `Moirai.Atropos.Tests.PlayMode` 全量 0 失败 |
| 4. L3 验收 | `Run all in Player` 报告 0 失败（含 0-GC 组） |
| 5. 覆盖率 | 各模块不低于分级阈值，且不低于上一版基线 |

**基线必须绿。** 任何时刻若全量套件有红，"全绿"这个信号就失效了，必须先修红再继续开发。既有断裂确认为非本次引入的，记录在案并给出修复计划——但不能长期留在套件里。

## 运行通道

### 通道一：测试桥（首选，EditMode / PlayMode）

`Tests/EditorMode/TestRequestRunner.cs` 是测试程序集内的调试桥，轮询工程根目录下的 `Temp/MoiraiTestRequest.json`（随工程，非包内），单向文件协议，调用方只轮询：

```json
{"id":"<唯一串>","mode":"EditMode","output":"<绝对路径>/report.txt","timeoutSeconds":180,
 "assemblies":["Moirai.Atropos.Tests.EditorMode"],"tests":["<命名空间.类名.方法名>"]}
```

产物：

| 文件 | 内容 |
|---|---|
| `report.txt` | `run <id> \| passed N \| failed N \| skipped N \| 耗时`，后附逐格失败详情 |
| `report.txt.progress` | 正在跑的用例全名（可判卡死；收口时删除） |
| `report.txt.done` | 内容是请求里的 `id` |

纪律：

- **必须自带唯一 `id` 并只认配对的 `.done`**，否则会把上一轮的旧报告当成这次的结论。
- **`assemblies` 与 `tests` 均为空的请求会被直接拒绝**——空过滤器会让 Test Runner 重跑"上一次在窗口里选择"的用例集，看似成功实则文不对题。
- 前提是该程序集已编译过一次且编辑器有过一次 `update`。正在编译、正在导入、正在切 PlayMode、或编辑器里有任意 run 在跑（含窗口手动发起）时不接新单——**请求文件留着，空闲后自动消费**。
- `timeoutSeconds` 是墙钟上限（编译、导入、域重载的等待都计入）；超时按 ABORTED 收口，报告附带已收集的 `collected passed/failed/skipped`。**ABORTED 报告里已跑完的格子不白跑**，可用于归因。
- **取消在途单**：往工程根目录下的 `Temp/MoiraiTestRequest.cancel.json` 写要取消的请求 `id`。UTF 取消后不再送达 `RunFinished`，受理即由驱动收口。

### 通道二：`TestRunnerApi` 直跑（桥不可用时的备选）

在编辑器脚本内直接 `ScriptableObject.CreateInstance<TestRunnerApi>()` + `ICallbacks` 宿主。要点：

- 回调宿主做成 `ScriptableObject`，收口后 `UnregisterCallbacks(host)` + `DestroyImmediate(host)`，防全局回调残留。
- `Filter` 必须显式设 `testMode`，否则默认走 PlayMode 流程并触发域重载，中断调用方脚本且结果全空。
- 统计要对 `ITestResultAdaptor` 的 `Children` **手工递归**（根节点没有 `TestCount`），且失败态字符串不保证是官方 `"Failed"`，按 `Fail`/`Error`/`Cancel`/`Inconclusive` 关键字宽匹配归类。

### 通道三：编辑器状态桥（判活与刷新）

`Tests/EditorMode/EditorStateBridge.cs` 把编辑器状态每 ~1s 覆写到工程根目录下的 `Temp/MoiraiEditorState.json`。用途：

- **判活**：`now - unix` 大到几秒即主线程没在跑 `update`（导入中、域重载中、被原生模态框挡住）。
- **判新域**：`domainSeq` 递增即域重载真发生过。
- **判新鲜度**：逐源树比 `assemblies[].unix` 是否越过自己的改动时刻（`Runtime/**` → `Moirai.Atropos`、`Tests/EditorMode/**` → `.Tests.EditorMode`）。**还要比 `assemblies[]` 与 `domainDllUnix`：不相等就是"dll 已被后台代编换掉、而这个域还没重载"**，此时跑的还是旧代码。
- **投测试单前**：`testRunActive` 为 0 且 `isCompiling`/`isUpdating`/`isChangingPlayMode` 皆 false 才是接单窗口。这个编辑器是共享的，别的会话随时可能占住它。

### 通道四：Test Runner 窗口（L3 唯一通道）

`Run all in Player`，见《玩家侧用例的运行方式》。

## 可执行政策守卫与治理原则（2026-09-27）

规范若只写在文档里，下一次"顺手一下"没人拦得住——以下政策已钉成可执行守卫（编辑器套件自动跑）：

- `ReflectionPolicyGuardTests`：非公开反射白名单双向断言（未登记不得出现、已登记必须仍命中）。
- `TestLogChannelPolicyGuardTests`：测试日志输出发射统一 `Debug.Log*`，禁止 `LogUtility.Verbose/Debug/Info/Warning/Error/Fatal/Assert(`——白名单两类：被测本体（LogUtilityTests）、替身复刻生产发射（Save fake loader）；断言通道（`onMessageLogged` 捕获、`UtfLogExpect` 消噪）不受限。守卫按原文扫描，注释里写「LogUtility.Error(」字面也会命中——措辞用「LogUtility 的 Error」规避。 <br />
  同一守卫还钉住 **LogAssert 通道**：用例侧禁止直用 `LogAssert.Expect` / `LogAssert.ignoreFailingMessages` / `LogAssert.NoUnexpectedReceived`（一律经 `UtfLogExpect`，`ignoreFailingMessages` 只经 `ScopedIgnore()`）——白名单为两份 `UtfLogExpect` 支撑副本与「被测走 `Debug.Log` 直发」的场景（DebuggerLogCaptureTests），双向断言防名单腐烂。

**反膨胀原则**（存量不追改、增量强制）：

- 存量方法名不批量改三段式；新增用例必须三段式。
- 夹具基座的触发条件 = 同模块 ≥2 个文件共享 setup；不 blanket 建基座、不 blanket 给 83 个 SetUp 加断言。
- 每个新增用例必须映射「契约 / 风险 / 回归锁」之一，无映射不写；新测试文件 ≤8 格，断言行为不断言实现。
- 重复的断言/理由文本抽共享 const 单点维护：同程序集进 `Support/`（如 `AudioGroupIgnoreReasons` 的探测能力与恢复条件句式），跨程序集不可共享时单文件抽私有 const——口径一处改、处处新，不得逐字复制同一段理由文本。

**教训账本**（踩过即入规）：

- 「孤儿 partial」判定必须先排除**源生成器喂养的类**——SaveFieldCapturerGenerator 编译期注入第二分部，grep 不可见（2026-09-27 审计 A-18 假阳性，执行 8 处后当场撤销）。
- `GetAllTimers(null)` 按契约返回 0——不是计数通道，活跃数走 `GetStatistics`。
- C# 9 不缓存方法组转换：基准与热路径的回调必须缓存为静态字段。
- 跨程序集测试支撑不可共享（asmdef 拓扑），复制属可接受形态（如 Player 版 AudioCacheTestSupport）。

## 常见陷阱速查

| 症状 | 根因 | 处置 |
|---|---|---|
| 单独跑绿、整套跑红 | 跨夹具全局状态污染 | 夹具基座断言初始干净 + TearDown 还原 |
| EditMode 用例报 `DontDestroyOnLoad` 异常 | EditMode 不允许 | 加 `Application.isPlaying` 守卫 |
| EditMode 用例里 `Awake` 没跑 | EditMode 不执行生命周期 | 提供 `EnsureActivated` 幂等兜底 |
| `Time.frameCount` 不推进 | EditMode 无帧 | 自带帧号游标 |
| `LogAssert` 报 "Expected log did not appear" | 当前 Handler 是 `UnityLoggingHandler`（不可见） | 经 `UtfLogExpect` 声明（判定已内聚，别自己再加判定） |
| 大量用例突然报 CS0246/CS0426 | 测试里用了裸限定名，撞全局命名空间或 `UnityEngine` 类型 | 改 `using` 别名 |
| 0-GC 断言"通过"但实际在分配 | 编辑器计量器恒 0 | 能力探测 + `Assert.Ignore`；真验证走 L3 |
| `Assert.ThrowsAsync<T>` 类型不匹配 | `TaskCanceledException` 精确类型 | `task.GetAwaiter().GetResult()` |
| 改字段名后用例 NRE | 用例用反射读字段 | 改 `internal`，别用反射 |
| 用例红了但代码没动 | 并行会话改了 API | 先 `git diff` 排除并行改动，再归因 |
| Inspector 下拉框出现陌生 Handler | 测试里建了 `[SerializeReference]` 基类的子类 | 删除该类型，改用内置实现 + 事件回调 |

---

[« 返回文档索引](Index.md) · [Core](Core.md) · [Debugger](Debugger.md)
