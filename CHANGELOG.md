# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行；本段会原样作为 release note 发布——面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 编辑器

- 新增 `Window/General/Test Player Runner` 窗口：Player 测试参数化一键发起（平台/程序集/用例过滤、心跳超时、报告路径，附 CLI 等价命令复制），护栏与测试桥同款（错误回调收口、互斥、取消、域重载孤儿判活、失败详情含堆栈）；住测试程序集 `Moirai.Atropos.Tests.EditorMode`（Editor + `UNITY_INCLUDE_TESTS` 门控，缺 Test Framework 包自动退化），与测试桥同栈。

#### 测试

- 测试架构规范化批次落地，基线全绿（L1 1928 过 0 失败、L2 47 过 0 失败）。
- 时间确定性接缝：`GameTimeHandler` 新增 `RealtimeNow` 虚拟时钟钩子（默认回落 `UnscaledNow`）；`AudioClipCache` 的 TTL 驱逐与失败冷却改走可注入时间源。
- 五基准（MemoryPool/Timer/Kernel/ObjectPool/AudioCache）跑完统一落 `BenchmarkReport` XML 至 `<工程根>/Benchmarks/`。
- 新增可执行政策守卫：`ReflectionPolicyGuardTests`（反射白名单双向断言）与 `TestLogChannelPolicyGuardTests`（测试日志统一 `Debug.Log*`）。
- 0-GC 分配验收迁 Player 程序集并新增 `TimerHotPathAllocationTests`；`AudioLeakAcceptance` 改自建最小组，宿主未配 `AudioGroupConfigs` 亦可全量执行。

### Changed

#### 场景

- 场景进度回调按值回报：`progressCallBack` 只在进度变化时触发（加载与卸载同口径），成功收尾的 1.0 仍必发一次。

#### 测试

- `Tests/Player` 程序集转编辑器可见（`defineConstraints` 去 `!UNITY_EDITOR`）：UTF 玩家测试运行只收录编辑器可见程序集，原组合在任何环境都不执行。
- `AllocationCapture` 计量改 `GC.Alloc` 采样事件数（UTF 官方 `AllocatingGCMemory` 同机制）——「0 事件」断言强于「0 字节」。
- ⚠ L2 门禁基线位移：`Tests/Player` 约 23 格进入编辑器 PlayMode 套件真跑（仅 AudioPerformance 3 格按宿主未配 `AudioGroupConfigs` 跳过），新基线以重跑为准。

#### 基准

- 基准归一住 `Tests/`（`[Explicit]`，目录镜像被测模块）；Timer 基准拆双通道——矩阵核心 `TimerBenchmarkRunner` 在运行程序集，Debugger 窗口与 Tests 薄壳共用；`ResolveXmlPath` 改经 `Application.dataPath` 父目录推工程根。
- ⚠ 移除 `Window/Moirai/JSON Benchmark` 与 `Window/Moirai/Timer Benchmark` 菜单工具——基准改经 Test Runner 按名执行。
- ⚠ 移除 `AudioCacheBenchmark` 私有导出变量 `MOIRAI_AUDIO_BENCH_FILE`——统一走 `BenchmarkReport` XML，路径可用 `MOIRAI_BENCH_XML` 覆盖。

### Fixed

#### 音频

- `AudioMainThread.AssertMainThread` 断言消息插值挪进失败分支——播放入口主线程快路径零分配（此前每调用恒 1 次 GC 分配）。

#### UI

- `UGUIHandler.CurrentModal` 与末位窗口刷新改索引取用——模态查询热路径（`UIServiceHelper` 交互前置判断）每次读取零分配（此前 `LastOrDefault`/`Last` 装箱枚举器并每次新建判定委托）。

#### 测试

- `Tests/Player` 补 `UniTask` 引用与缺失 using；`PlayerTestBootstrap` 掐 `AutoBoot` 收进 `#if !UNITY_EDITOR`（编辑器 PlayMode 域依赖自动启动链，L2 门禁前提）。
- 0-GC 计量用例的 NUnit 断言移出测量窗；Eviction 预算校准为 ≤2 事件常数。

#### 构建

- `LocalizationChannelBuildHook` 无 `-CustomArgs:` 前缀时静默早退（此前缺参 `LogError` 会把 GUI 发起与测试玩家构建整单判死）；前缀在而键缺失仍响亮报错。

#### 存档

- 非 Windows 写档回退改经 `.sav.journal` 中转 + 启动期 `RecoverInterruptedWrites` 抬回，消除「先删旧档再改名」两步之间的丢档窗口；中转位与项目侧 `.bak` 单槽分开，`DeleteFile` 先清 journal 再删主档，启动期恢复与清扫持根级串行门。
- `CloudSaveStorageBackend` 本地镜像同步转发中断恢复；`SupportsAtomicRename` 语义改准为「替换无半写窗口、中断后旧档必可恢复」——中转期间该存档槽会短暂报不存在。
