# Coverage 覆盖率门禁

> 覆盖率怎么跑、阈值怎么判、报告落在哪。规范正文见 [`Documentation~/zh/Testing.md`](../../Documentation~/zh/Testing.md) 的《覆盖率与门禁》一节。

## 工具与配置

工具是 `com.unity.testtools.codecoverage`（1.3.0，已进 `Packages/manifest.json`）。

**过滤口径**（两条通道共用同一组值）：

```
+Moirai.Atropos
-Moirai.Atropos.Editor
-Moirai.Atropos.Tests.*
-Moirai.Atropos.SourceGenerators.*
-*.Generated
```

**为什么这样切**：只测运行时代码。Editor 工具与代码生成器是开发期设施，用测试行覆盖率衡量它们收益极低；测试程序集自身不该被统计；生成代码（`*.Generated`）的行不是人写的，计入会稀释信号。

## 两条运行通道

### 通道一：编辑器窗口（人工/本地）

`Window > Analysis > Code Coverage` → 勾选 **Enable Code Coverage** → 在 Filters 里填上面的过滤串 →
切到 **Test Runner** 窗口点 **Run with Coverage**（或 Code Coverage 窗口的 Run with Coverage）。

> 注意：勾选 Enable Code Coverage 会切换 `ENABLE_CODE_COVERAGE` 编译符号并触发**全量重编译**。
> 本地验完记得取消勾选，否则后续所有编译都带着插桩开销。

### 通道二：命令行 / CI（自动化）

```bash
Unity -batchmode -quit -projectPath . \
  -runTests -testPlatform EditMode \
  -testResults ./TestResults/editmode.xml \
  -enableCodeCoverage \
  -coverageResultsPath ./TestResults/Coverage \
  -coverageOptions "generateAdditionalMetrics;generateHtmlReport;generateBadgeReport;assemblyFilters:+Moirai.Atropos,-Moirai.Atropos.Editor,-Moirai.Atropos.Tests.*,-Moirai.Atropos.SourceGenerators.*,-*.Generated"
```

`-coverageOptions` 的分号分隔项与 CLI 参数名均取自包内 `CommandLineManager.cs` 的 `case` 分支
（`ASSEMBLYFILTERS` / `GENERATEADDITIONALMETRICS` / `GENERATEHTMLREPORT` / `GENERATEBADGEREPORT` / `PATHFILTERS` …），
不是照文档猜的。

## 产物

| 路径 | 内容 |
|---|---|
| `TestResults/Coverage/Report/index.html` | HTML 报告（人工看） |
| `TestResults/Coverage/Report/Summary.json` | 机器可读汇总（门禁脚本读它） |
| `TestResults/Coverage/Report/Summary.md` | Markdown 汇总 |

> `Tests/Coverage/latest/` 若被本地用作输出目录，**不入版本控制**；只有 `baseline-<date>.md` 进仓库。

## 分级阈值

| 档 | 范围 | 行覆盖 | 分支覆盖 |
|---|---|---|---|
| 核心服务 | Resource / Save / Audio / UI / Kernel | ≥ 80% | ≥ 70% |
| 其余服务 | ConfigTable / Debugger / Input / Localization / ObjectPool / Procedure / Scene / Timer | ≥ 70% | — |
| Editor 工具与生成代码 | `Moirai.Atropos.Editor`、SourceGenerators | ≥ 50% | — |

阈值是**下限**不是目标；新代码不得让所在模块覆盖率下降。

## 门禁脚本

```powershell
# 在仓库根（Client/）执行
powershell -ExecutionPolicy Bypass -File "Packages\com.moirai.framework\Tests\Coverage\coverage-gate.ps1" `
  -SummaryPath "TestResults\Coverage\Report\Summary.json"
```

脚本按上面的分级阈值判定，逐模块打印实测值，任何一档不达标即非零退出（CI 直接红）。

**它不会静默通过**：读不到文件、JSON 结构不认识、某模块在报告里找不到——一律按失败处理并打印原因。
「测不出来」不等于「达标」，这是本项目对 0-GC 计量一贯的口径，覆盖率同样适用。

## 当前基线

**待首次插桩运行产出。** 本仓库尚未跑过覆盖率（编辑器被占用时 batchmode 打不开同一工程，
而窗口通道需要全量重编译）。首次产出后：

1. 把各模块实测值填进下表；
2. 报告快照存为 `baseline-<date>.md` 并提交；
3. 差距表（当前 → 目标）随快照一起维护。

| 模块 | 档 | 行覆盖（待填） | 分支覆盖（待填） | 差距 |
|---|---|---|---|---|
| Resource | 核心 | — | — | — |
| Save | 核心 | — | — | — |
| Audio | 核心 | — | — | — |
| UI | 核心 | — | — | — |
| Kernel | 核心 | — | — | — |
| ConfigTable | 其余 | — | — | — |
| Debugger | 其余 | — | — | — |
| Input | 其余 | — | — | — |
| Localization | 其余 | — | — | — |
| ObjectPool | 其余 | — | — | — |
| Procedure | 其余 | — | — | — |
| Scene | 其余 | — | — | — |
| Timer | 其余 | — | — | — |
| Editor / SourceGenerators | 工具 | — | — | — |

## 已知覆盖空洞（2026-09-24 静态盘点）

基线未出，先按"有没有用例"列一份结构性空洞——这些是首次覆盖率报告里最可能出现大面积红的区域：

| 区域 | 现状 |
|---|---|
| `Core/Tasks`、`Core/Attributes`、`Core/Extensions`、`Core/GameProfiler`、`Core/Models`、`Core/Pool` | 零用例 |
| `Core/Utilities` 的 `FileUtility` / `PathUtility` / `HttpUtility` / `EncryptionUtility` / `AssemblyUtility` / `ReflectionUtility` / `MarshalUtility` / `GameVersion` / `GameTime` / `SettingSave` | 无直接用例 |
| `Editor` 程序集（ReleaseTools / HybridCLR / AtlasMaker …） | 零用例 |
| PlayMode 层（Resource / Save / UI / Scene / Input / Localization / Procedure） | 仅 Audio 有集成用例 |
| `Debugger`（42 文件 / 39 用例）、`UI`（20 文件 / 8 用例） | 偏薄 |

补齐优先级与顺序由 `Documentation~/zh/Testing.md` 的《覆盖目标》与各阶段提交记录决定。
