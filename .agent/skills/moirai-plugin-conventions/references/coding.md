# 编码细则（本包）

适用面：15 个插件目录（`addressables/` … `zstring/`）。分级：**Mandatory = 违反要打回**，Prefer = 性能敏感区必须、非热路径可放宽，Reference = 逐步优化遗留。

## 命名与结构

| 元素 | 规则 | 本仓依据 |
|---|---|---|
| 插件目录 | 小写 kebab-case，与 manifest `id` 一致 | `newtonsoft-json/`、`unity-cloud-save/` |
| 插件 asmdef | `Moirai.Plugin.<名>` | 21 个 asmdef 的命名 |
| 命名空间 | 多数直接扩展 `Moirai.Atropos.*` | 实测 13 个 `Moirai.Atropos`、8 个 `.Resource`、5 个 `.Serilog` |
| manifest 字段 | 与 `zlogger/zlogger.plugin.json` 同形 | 见下 |
| 测试目录 | `*/Tests/` + 独立测试 asmdef | 6 个插件 |

## 程序集与 asmref

| 事实 | 值 |
|---|---|
| 根 asmdef | 无（根 package.json 只描述目录分支） |
| 插件 asmdef | 21 个，分布在 15 个插件目录 |
| 测试 asmdef | 6 个：addressables、fmod、memorypack、messagepack、protobuf、wwise |
| 依赖方向 | 插件引用框架 `Moirai.Atropos.*` 与外部 SDK；插件之间不互相引用 |
| 宏门控 | 各插件 asmdef 的 `versionDefines` 决定可选依赖 |
| asmref | `primetween/PrimeTween/PrimeTween.Runtime.asmref`（vendored PrimeTween 并入插件程序集） |

## 领域模板与硬约束

### 插件清单

以 `zlogger/zlogger.plugin.json` 为完整样本：`id` 与目录名一致；`version` 是插件自身版本；`minUnityVersion`/`minFrameworkVersion` 是安装地板；`upmDependencies` 只列 UPM 依赖，外部 SDK 写进 `description` 的安装前置。

### 插件 asmdef

- 名称 `Moirai.Plugin.<名>`，引用框架程序集与外部 SDK；可选依赖用 `versionDefines` + `#if`。
- 插件目录内不新建根 asmdef；测试放 `Tests/` 独立装配。
- 新增插件先对照 `zlogger/`（最小后端）或 `messagepack/`（带测试的后端）。

### 适配面

插件实现框架的契约基类（资源/音频/日志/序列化/存档等），不复制框架服务。具体基类以所属框架模块的契约声明为准，引用前先 grep 框架包。

## 热路径与分配

- 日志后端的每帧写入与格式化
- 序列化后端的读写路径
- 资源/音频桥接的每帧调用
- 插件初始化与注册

## 序列化与资产

- 插件 manifest 是 JSON，字段名与值直接参与包管理器解析；改字段名要同步框架包管理器的读取面。
- 插件自己的设置资产/序列化字段以所属框架模块为准；改字段名要给迁移口径。
- 插件目录与 manifest `id` 必须一致，否则包管理器找不到。

## 注释、日志与检查面板

- 注释与 XML 文档用中文；日志正文英文，走框架 `LogUtility`（若本包不依赖框架，则用本包既有日志通道）；热路径禁 `Debug.Log`。
- 给人看的类型名用 `nameof(Type)`。
- Inspector 属性先确认归属：框架 OdinExtends 属性（`Moirai.Atropos.Attributes`）与 Odin 原生属性（`Sirenix.OdinInspector`）不是同一套；新增字段优先属性驱动。
- 分组标题保持 `中文 [ENGLISH]`。

## 规范射程外

| 路径 | 处理 |
|---|---|
| 各插件内的第三方 SDK 源码/DLL | vendored 或外部包，保持原样 |
| 未安装的外部 SDK 宏分支 | 不描述其运行期行为 |

## 已知文档漂移（只记录，不顺手改）

- `package.json` 的 `repository.url` 指向框架仓的 `#plugin` 分支，不是独立仓；不要按独立仓的发版流程推断。
- 根 `package.json` 版本 0.1.0 与各插件 manifest 版本不同；两者不能互相当作发版判据。
