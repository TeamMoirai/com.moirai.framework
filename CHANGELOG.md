# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 测试

- 测试架构规范化批次落地，基线门禁全绿（L1 1928 过 0 失败、L2 47 过 0 失败）。
- `GameTimeHandler` 新增 `RealtimeNow` 虚拟时钟接缝：默认回落 `UnscaledNow`（虚拟时钟实现自动获得确定性墙钟），`DefaultGameTimeHandler` 覆写为引擎墙钟。
- `AudioClipCache` 的 TTL 驱逐与失败冷却改走可注入时间源，TTL 用例脱离真实墙钟等待。
- 五基准（MemoryPool/Timer/Kernel/ObjectPool/AudioCache）跑完经 `BenchmarkReport` 落 XML 至统一文件夹 `<工程根>/Benchmarks/`。
- 新增与反射守卫同构的可执行政策守卫 `TestLogChannelPolicyGuardTests`：测试日志发射统一 `Debug.Log*`，白名单=被测本体/替身复刻生产发射。
- `AudioLeakAcceptance` 改经 `AudioServiceTestHost` 自建最小组——宿主工程未配 AudioGroupConfigs 也可全量执行。
- 0-GC 分配验收迁 Player 程序集（原 EditorMode 死格：编辑器内计数器不推进恒 Ignore、玩家构建不含 Editor 程序集，任何环境都不执行）。
- 新增 `TimerHotPathAllocationTests`（Timer 热路径 0-GC，L3）。

### Changed

#### 测试

- `Tests/Player` 程序集的 `defineConstraints` 去掉 `!UNITY_EDITOR`：UTF 玩家测试运行（GUI 与 CLI 同机制）只收录编辑器可见的测试程序集——旧组合「编辑器不编译 + Run all in Player」在任何环境都不执行（2026-09-28 实证，L3 门禁此前从未真正跑过 0-GC 格）。
- `AllocationCapture` 计量换 `GC.Alloc` 采样事件数（UTF 官方 AllocatingGCMemory 同机制、同款 API）：`GC.GetAllocatedBytesForCurrentThread` 在编辑器 Mono、Mono 玩家、IL2CPP 玩家三处实测恒 0、`GC.GetTotalAllocatedBytes` 在 Unity profile 不存在——字节口径无实现，旧口径 0-GC 断言全部假绿；「0 事件」断言比「0 字节」更强。
- ⚠ 编辑器 PlayMode 门禁（L2）基线位移：`Tests/Player` 约 23 格进入编辑器套件、经计数器能力探针整组跳过——新基线以重跑为准。

#### 基准

- 所有 Benchmark 归一住 `Tests/`（`[Explicit]`，目录镜像被测模块）：`JsonUtilityBenchmark` 自 Editor 菜单工具迁 `[Explicit]` 用例（测量内核逐字保留，去菜单/进度条/结果窗）；Timer 基准拆双通道——同步矩阵核心 `TimerBenchmarkRunner`（运行程序集，隔离 handler 直驱）+ Debugger 的 Timer 调试窗口基准区 + Tests `[Explicit]` 薄壳共用同一矩阵，fire/burst 帧依赖用例住 PlayMode `[UnityTest]`；`BenchmarkReport.ResolveXmlPath` 修统一文件夹根推导（原以 `temporaryCachePath` 推工程根，Unity 6 编辑器下指系统临时目录——改经 `Application.dataPath` 父目录）。
- ⚠ 移除 `Window/Moirai/JSON Benchmark` 编辑器菜单与结果对比窗（基准迁 `Tests/EditorMode/Utility/JsonUtilityBenchmark`，经 Test Runner 按名执行）。
- ⚠ 移除 `Window/Moirai/Timer Benchmark` 菜单与场景 MonoBehaviour（其 m_* 序列化配置随独立入口一并消亡、矩阵常量化；双通道入口见上条）。
- ⚠ 移除 `AudioCacheBenchmark` 的私有导出环境变量 `MOIRAI_AUDIO_BENCH_FILE`（统一收口至 `BenchmarkReport` XML，`MOIRAI_BENCH_XML` 可覆盖路径）。

### Fixed

#### 音频

- `AudioMainThread.AssertMainThread` 的断言消息插值挪进失败分支：此前 `UnityEngine.Assertions.Assert.IsTrue` 的消息参数在断言通过时也每次求值——播放入口（Preload/Unload 等）每次调用恒 1 个 GC 分配，L3 实测逮到（Preload 命中路径每调用 1 个 GC.Alloc 事件）；修后主线程快路径零分配。

#### 测试

- `Tests/Player` 程序集补 `UniTask` 引用、Player 版 `AudioCacheTestSupport` 补 `using NUnit.Framework`——该程序集编辑器从不编译（`!UNITY_EDITOR` 约束），玩家构建首次真编译时暴露 CS0246/CS0012/CS0103。
- `PlayerTestBootstrap` 掐 `AutoBoot` 收进 `#if !UNITY_EDITOR` 守卫——程序集转编辑器可见后，无守卫会连带掐掉编辑器 PlayMode 测试域的框架自动启动（L2 门禁前提）。
- 0-GC 计量用例的 NUnit 断言移出测量窗：`Constraint` 链自身每格 5~9 个 GC.Alloc 事件，窗内断言把产品计数淹成 7~26 事件/次（2026-09-28 L3 首次有牙实测）；Eviction 预算随产品修复同步收紧为 0。

#### 构建

- `LocalizationChannelBuildHook` 无 `-CustomArgs:` 前缀时按「缺省不动」静默早退——`CommandLineReader` 的缺参 LogError 在预处理钩子里会直接判构建失败，GUI 发起与 UTF 测试玩家构建此前全部被挡。

#### 存档

- 非 Windows 平台写档不再「先删旧档再改名」：`File.Replace` 抛 `PlatformNotSupportedException` / `NotImplementedException` 后，旧写法直接删掉主档再改名到位，这两步之间崩溃或断电就是存档消失——而 Android / iOS / WebGL 上这条回退正是常态路径。现改为旧档先改名到 `xxx.sav.journal`、再把临时文件改名到位，到位失败当场抬回；进程真崩在两步之间时由 `RecoverInterruptedWrites` 在下次初始化抬回（排在孤儿临时文件清扫之前）。回滚只在 journal 仍在时动主档——主档位置上可能是并发恢复刚抬回来的旧档；抬回也失败时 journal 与原样主档一并留着，交给下次初始化按「主档非空才算已提交」裁决，原异常照常上抛。
- 上述中转日志位与项目侧 `CreateBackup` / `RestoreBackup` 的单槽 `.bak` 分开：借 `.bak` 中转会让玩家「恢复上一版」捞到一份写入中途的快照。`DeleteFile` 先清同路径 `.journal` 再删主档——反过来的话 journal 被云同步/杀软锁住就留下「主档已没、journal 尚存」，删掉的档下次开机又复活。启动期的恢复与清扫持根级串行门，与所有存档 IO 互斥（否则恢复会在两步改名中间把 journal 抬成主档）。`CloudSaveStorageBackend` 的本地镜像同步转发 `RecoverInterruptedWrites`。`SupportsAtomicRename` 的语义改准为「替换时不出现半写窗口，且中断后旧档必可恢复」，不再是「底层用过一次原子 rename」；代价是中转期间主档路径短暂缺席，此时 `Exists()` / `EnumerateFiles()` 会把该槽报成不存在。
