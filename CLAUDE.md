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
│   ├── Core/                 # 核心系统，不依赖服务层；按功能分组平铺，不套服务层的角色档
│   │   ├── <功能组>/         # Attributes / Constant / DataStructure / Extensions / GameException /
│   │   │                     # Events / GameApp / GameProfiler / FrameworkSettings / Graphics /
│   │   │                     # MemoryPool / Obfuz / SettingPersistence / Singleton / Tasks
│   │   └── Utilities/        # 横切工具，一个关注点一个子目录（Algorithm / Json / GameLog / Tween …）
│   └── Services/             # 各功能服务（13 个模块，每个服务一份文档，见 Documentation~）
│       └── <Module>/         # 模块根平铺门面四件套 + 封闭角色档（见《目录与文件》）
├── Editor/                   # 编辑器代码（Core / Services / Tools / Utilities）
├── Tests/                    # EditorMode / PlayMode / Player 三套测试
├── SourceGenerators/         # ProviderHandlerHost 等代码生成器
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

服务注册与生命周期由 `Runtime/Services/Kernel` 承接：门面 `GameServices.cs`（+ `GameServices.DiagnosticInfo.cs`）、容器与轮询 `Internal/ServiceScope.cs`、服务基类 `Objects/ServiceBase.cs`。

### Core 系统
按功能分组平铺在 `Runtime/Core/<功能>/`（没有 `Foundation/`、`Infrastructure/` 层级壳，也不用服务层的角色档）：
- **Attributes** - 自定义特性（`BooleanButtonAttribute`、`ConditionAttribute`、`CustomLabelAttribute` 等编辑器绘图/校验用特性；`[ProviderHandlerHost]` 不在这一组，它随设置基座住 `FrameworkSettings/`）
- **Constant** - 常量
- **DataStructure** - 数据结构
- **Events** - 事件系统
- **Extensions** - 扩展方法
- **FrameworkSettings** - 框架设置基座（`FrameworkSettings<T>`、`FrameworkHandler`、`[ProviderHandlerHost]` 宿主侧）
- **GameApp** - 启动、帧驱动与运行期开关（`PlayerLoop/` 次层）
- **GameException** - 框架异常约定
- **GameProfiler** - 性能采样
- **Graphics** - 图形设置资产
- **MemoryPool** - 内存池与通用池件（原 `Infrastructure/Pool/` 已并入本组）
- **SettingPersistence** - 设置的落盘与读回
- **Singleton** - 单例模式
- **Tasks** - 任务系统（`Components/`、`Models/` 次层）
- **Obfuz** - 混淆虚拟机初始化（`OBFUZ_INSTALLED && ENABLE_OBFUZ` 门控）
- **Utilities** - 工具类（算法、随机、JSON、Tween、日志 `LogUtility` 等；一个关注点一个子目录）

## 编码规范



细则（`### 命名规范`、`### XML 文档注释` 及其分级条款）的权威落点在 `.claude/skills/moirai-conventions/references/coding.md`，本文不复述第二套。

执行等级只有一个含义：Mandatory 违反要打回，Prefer 性能敏感区必须、非热路径可放宽，Reference 逐步优化遗留。



## 测试规范



细则（分层与归属、命名与结构、夹具与隔离、确定性、反射政策、日志断言、基准、公共 API 变更口径、覆盖率与出口准则、可执行守卫）

的权威落点在 `.claude/skills/moirai-conventions/references/testing.md`，本文不复述第二套。出口准则以「0 失败才算通过」为准。



## AI 测试流程



六步流程与两条验证环路（测试桥、编辑器状态桥）的权威落点在 `.claude/skills/moirai-conventions/references/verification.md`。

三行硬条件：只认内容等于本次 `id` 的配对完成标记；结论取落盘报告首行计数；0 失败才算通过。



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

### 自动触发的技能（Agent Skills，`.claude/skills/`）

上表是显式 `/` 命令；下面两个放在 `.claude/skills/`，由 Claude 按各自 `SKILL.md` 的 `description` 自动选用，
也可显式点名。每个技能自带 `references/`（按需加载）与 `scripts/`（量具，读的是**装机版 dll 与宿主配置资产**）。

| 技能 | 管什么 |
|------|--------|
| `odin-inspector` | Odin 属性与自定义抽屉做检视面板；写回必须走 `ValueEntry`、布局优先属性驱动 |
| `odin-validator` | Odin Validator 校验器与规则：基类取舍、注册两型、`ref` 报告面、窗口/配置生效值、CI 驱动 |

两份知识都以"先量装机版读数再下笔"为前提——官方文档站描述最新版，dll 里自带的模板原文也不是可编译证据。

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

### 3. 代码优化
1. 使用 `/optimize` 分析性能
2. 识别瓶颈
3. 实施优化
4. 使用 `/review` 验证优化

### 4. 提交时的文档与 CHANGELOG



提交、文档与 CHANGELOG、发版与 PR 的权威落点在 `.claude/skills/moirai-conventions/references/release.md`。

口径：只写净结果与现行事实，破坏性变更前置 ⚠ 给迁移口径，版本号与 Release 由工作流写入。



## 依赖项

### 运行时核心依赖（Client 实装）
- **UniTask** - 异步编程
- **YooAsset** - 资源管理
- **Luban** - 配置表生成器（构建期工具，运行时消费其生成的表代码）

### 可选依赖（`*_INSTALLED` 宏门控，未装也能编译）
- **HybridCLR** - 热更新（`HYBRIDCLR_INSTALLED`）
- **R3** - 响应式编程（`R3_INSTALLED`）
- 其余可选包见 Runtime asmdef 的 16 条 versionDefines（LitMotion/PrimeTween/ZString/com.unity.logging/Obfuz/Addressables/LZ4/CloudSave 等）。asmdef 对未安装包的 GUID 引用按 Unity 惯例静默跳过（Runtime 18 引用中 11 个 Client 未装而照常编译）——引用缺失不报错，缺的是宏。ZLogger 日志后端由 `plugin` 分支的插件提供，框架不再持有其宏与引用。

### 开发工具
- **Odin Inspector** - 编辑器增强
- **TextMesh Pro** - 文本渲染

## 注意事项

1. **Unity 版本**：推荐 Unity 2022.3.x
2. **.NET 版本**：Api Compatibility Level 为 .NET Framework（`apiCompatibilityLevel: 6`，等效 4.8），非 .NET Standard 2.1
3. **平台支持**：Windows、macOS（Standalone——L3 玩家验收基座，IL2CPP 实测）、Android、iOS、WebGL
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
