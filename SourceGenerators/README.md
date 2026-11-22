# 源生成器与分析器总览

本目录收录框架的分发产物（`*.dll`，Unity 以 `RoslynAnalyzer` label 应用于引用方程序集）与源码（`Source~/`，`~` 后缀目录 Unity 不导入）。

## 诊断 ID 段号方案（MA + 4 位段号 ABCD）

全部分析器/生成器的诊断 ID 统一为 **`MA` + 4 位数字**，按位分区：

| 位 | 含义 | 约定 |
| --- | --- | --- |
| **A**（首位） | 模块 | 同一模块的所有分析器共用一个首位；新模块取未占用的最小首位 |
| **B**（次位） | 功能 | 同一模块内的不同功能面（不同分析器/生成器文件）使用不同次位；单一功能模块取 0 |
| **CD**（后两位） | 具体诊断 | 顺序分配，发布后冻结——已对外出现的 ID 不复用、不重排 |

完整 ID 形如 `MA3007`，`.editorconfig` 调级示例：`dotnet_diagnostic.MA3007.severity = error`。

> 历史：v2.0.1 前曾使用 `MIRAI` + 3 位段号（1xx~7xx），v2.0.1 起统一迁移至 `MA+ABCD`——首位与旧段号一一对应（Save 3xx/4xx 归并为 A=3，B 区分生成器与架构校验）。

## 模块与段位分配表

| A | 模块（程序集） | B | 功能 | CD 段占用 |
| --- | --- | --- | --- | --- |
| 1 | ProviderHandlerHost | 0 | 宿主契约/默认工厂 | 01–04（05 已退役，不复用） |
| 2 | ServiceDependency | 0 | `[ServiceDependency]` 依赖校验 | 01–02（B=1 段 03–05 已退役，不复用） |
| 3 | SaveServiceCodegen | 0 | 字段捕获/序列化注册/迁移注册 | 00–12 |
| | | 1 | 二进制后端键序冻结校验（SaveSchemaAnalyzer） | 00–01 |
| 4 | _（预留：随 Save 归并释放，后续模块可复用）_ | | | |
| 5 | UIWindowCodegen | 0 | UI 窗口注册/绑定 | 00–03 |
| 6 | HotPathAnalyzer | 0 | 热路径纪律（装箱/匿名函数） | 00–01 |
| 7 | EventBusAnalyzer | 0 | EventBus 订阅规范（纯分析器，无胶水生成） | 00–06 |
| 8+ | 未分配 | | | |

### 当前 ID 明细

- **A=1 ProviderHandlerHost**
  - `MA1001` 工厂全缺（懒加载无来源，Warning）
  - `MA1002` 仅声明 GetHandlerFromSettings（settings-only，Info）
  - `MA1003` 声明的默认处理器不可实例化（Error）
  - `MA1004` 声明默认与手写工厂并存（Info）
  - ~~`MA1005`~~ **已退役**：原「服务后端仍继承 FrameworkHandler」的过渡提示。接口契约的默认后端只认 `ServiceBase`，`FrameworkHandler` 派生改由 `MA1003` 判红，生成代码的服务侧装配路径也不再按实例类型分派；按本文《诊断 ID 段号方案》该号位不再复用
- **A=2 ServiceDependency**
  - `MA2001` 依赖类型未实现 IService/契约接口（Warning）
  - `MA2002` 未声明任何依赖类型（Info）
  - ~~`MA2103`/`MA2104`/`MA2105`~~ **已退役**：原 `[AutoRegisterService]` 实例自注册的约束诊断。该特性已整体删除（特性类型、生成器采集支路一并移除——框架零标记后项目侧自建服务汇入通道不再保留，改走 `onServicesComposing` 显式注册或 `[ProviderHandlerHost]` 门面）；按本文《诊断 ID 段号方案》该号段不再复用
- **A=3 SaveServiceCodegen**
  - B=0 生成器：`MA3000` 字段类型不支持 · `MA3001` 存档键重复 · `MA3002` 迁移器无法生成自注册 · `MA3003` 包含类型须 partial class · `MA3004` 须为实例字段 · `MA3005` 场景引用指引 · `MA3006` 引用类型不明 · `MA3007` 嵌套数据类型无效 · `MA3008` 集合元素/键值类型不支持 · `MA3009`–`MA3012` 序列化器编号/撞号（Error）
  - B=1 架构校验：`MA3100` 键序重排（Warning）· `MA3101` 成员删除缺迁移钩子（Warning）
- **A=5 UIWindowCodegen**
  - `MA5000` [Window] 非 UIWindow 派生 · `MA5001` 缺公共无参构造 · `MA5002` 抽象/泛型 · `MA5003` 嵌套在私有类型内（均 Error）
- **A=6 HotPathAnalyzer**
  - `MA6000` 热路径值类型装箱（Warning）
  - `MA6001` 热路径匿名函数（Warning）
- **A=7 EventBusAnalyzer**（纯分析器，不产出任何生成代码；Subscribe/Unsubscribe 由用户自行编写）
  - `MA7000` 禁止匿名函数订阅/退订 EventBus（Warning）
  - `MA7001` OnXxxEvent 带参回调必须 `in` 修饰（Warning，含 CodeFix）
  - `MA7002` OnXxxEvent 参数类型与方法名后缀不匹配（Warning）
  - `MA7003` 订阅缺少成对退订——同宿主类型内按「事件类型 + 处理器」配对，多出的 Subscribe 报警（Warning）
  - `MA7004` `[GameEvent]` 必须为 struct（Error）· `MA7005` 必须实现 IGameEvent（Error）· `MA7006` 不得含实例方法（Warning）

## 新增分析器的申请流程

1. 选定模块：同模块沿用既有 A；新模块取分配表中未占用的最小首位（当前下一个为 8）。
2. 选定功能：同模块既有 B 则沿用，新功能面取该模块未占用的最小次位（单一功能取 0）。
3. CD 顺序分配，从 00 起编号；仅在描述符集中定义（各项目 `Diagnostics.cs` / 分析器文件顶部）。
4. 更新本 README 的分配表与明细；模块文档（`Documentation~/`）同步引用新 ID。

## 构建

```bash
# 各源码项目构建后 DLL 直出本目录（OutputPath=..\..\）
cd Source~/<项目名>
dotnet build -c Release

# EventBusAnalyzer 自检
cd Source~/EventBusAnalyzer.Probe && dotnet run -c Release

# ProviderHandlerHost 自检（装配路径发射形状 + 默认声明判据 + MA 支持集）
cd Source~/ProviderHandlerHost.Probe && dotnet run -c Release
```

探针经 `InternalsVisibleTo` 直调生成项目的 internal 入口（`GenerateCodeForTesting` / `HandlerContract`），不用反射读私有成员；
`dotnet run` 会连带重建 `SourceGenerators/ProviderHandlerHost.dll`，源码与分发产物不会各走各路。

技术口径：目标框架 `netstandard2.0`、`Microsoft.CodeAnalysis.CSharp 4.3.0`（Unity 6000 内置 Roslyn 版本）、`IsRoslynComponent=true`、DLL 输出 `SourceGenerators/` 根并配 `RoslynAnalyzer` label 的 `.meta`。
