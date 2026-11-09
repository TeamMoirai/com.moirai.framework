# 编码细则（本包）

适用面：`Editor/`。分级：**Mandatory = 违反要打回**，Prefer = 性能敏感区必须、非热路径可放宽，Reference = 逐步优化遗留。

## 命名与结构

| 元素 | 规则 | 本仓依据 |
|---|---|---|
| 命名空间 | `Moirai.Atropos.Installer.Editor` | `FrameworkInstallerWindow.cs` |
| 主类 | `FrameworkInstallerWindow : EditorWindow` | `FrameworkInstallerWindow.cs:14` |
| 私有字段 | `_camelCase` | `_registryResolveRequest`、`_installCoreRequest` |
| 常量 | `UPPER_SNAKE` | `MENU_PATH`、`TEMPLATES_PATH`、`INSTALL_STATE_PATH` |
| 编辑器守卫 | 整文件 `#if UNITY_EDITOR` | 文件首行 |

## 程序集与 asmref

| 事实 | 值 |
|---|---|
| asmdef | 无 |
| 编译归属 | `Editor/` 下的脚本由宿主工程编入预定义编辑器程序集（`Assembly-CSharp-Editor` 一类），不是独立包 DLL |
| 运行期 | 无；安装器只在编辑器里运行 |
| asmref | 无 |
| 测试装配 | 无 |

## 领域模板与硬约束

### 安装流程

- 环境检查：确保 OpenUPM 作用域注册表存在（`EnsureRequiredScopedRegistry`），缺则写 `Packages/manifest.json`。
- 核心安装：用 `UnityEditor.PackageManager` 的 `AddRequest` 安装 `com.moirai.framework`，轮询 `IsCompleted`。
- 模板导入：从框架包 `Templates~` 复制 `NormalTemplate` / `HybridTemplate`，跳过占位文件，按模板类型写 scripting define 与 PlayerSettings。
- 安装状态：写 `ProjectSettings/MoiraiFrameworkInstaller.json`；读取时兼容旧格式。

### 硬约束

- `Packages/manifest.json` 是宿主资产：先读全文再改，保留用户已有条目与缩进风格；不要整文件重写。
- `ProjectSettings/` 是宿主设置：只在安装器明确需要时改，并记录改了什么。
- 模板复制不覆盖用户已改文件；占位文件按 `IsTemplatePlaceholder` 跳过。
- 不新增 asmdef；安装器靠宿主预定义编辑器程序集。

## 热路径与分配

- 编辑器 `OnGUI` 重绘
- Package Manager 请求轮询
- 模板目录复制（一次性）

## 序列化与资产

- 安装状态 JSON 字段是跨版本兼容面；新增字段要给旧档默认值，读取失败要能降级。
- `Packages/manifest.json` 的 scopedRegistries 结构必须保持合法 JSON 与用户条目。
- `ProjectSettings/MoiraiFrameworkInstaller.json` 不在包内，不提交。

## 注释、日志与检查面板

- 注释与 XML 文档用中文；日志正文英文，走框架 `LogUtility`（若本包不依赖框架，则用本包既有日志通道）；热路径禁 `Debug.Log`。
- 给人看的类型名用 `nameof(Type)`。
- Inspector 属性先确认归属：框架 OdinExtends 属性（`Moirai.Atropos.Attributes`）与 Odin 原生属性（`Sirenix.OdinInspector`）不是同一套；新增字段优先属性驱动。
- 分组标题保持 `中文 [ENGLISH]`。

## 规范射程外

| 路径 | 处理 |
|---|---|
| 宿主 `Packages/manifest.json` | 只按安装流程读写，不作为包内源码 |
| 宿主 `ProjectSettings/` | 只在安装器明确需要时改 |
| `Templates~/`（在框架包内） | 本包只读取，不修改 |
| 第三方 URP/HybridCLR | 外部包/服务 |

## 已知文档漂移（只记录，不顺手改）

- `package.json` 的 `repository.url` 指向框架仓的 `#installer` 分支，不是独立仓。
- README 的安装步骤与脚本实现可能漂移；以 `FrameworkInstallerWindow.cs` 为准。
