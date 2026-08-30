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
- **UpdateDriver** - 更新驱动

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

Unity AAA 生产级 C# 编码规范（强制执行）。**Why:** 用户要求所有 Unity C# 系统编写、优化、重构时严格按照此规范执行，确保 AAA 商业化代码质量。**How to apply:** 所有 Unity C# 代码编写任务均以下述规范为基线。

- **核心原则：** 性能即特性（热路径 0-Alloc，帧预算内完成）；确定性（避免反射/动态生成，确保 IL2CPP 一致）；可读性即维护性；Fail-Fast（Editor 断言优先，Runtime 防御性检查）。
- **命名：** 命名空间 Pascal（{Org}.{Product}.{Module}）；类/结构体/接口 PascalCase；公有属性 Auto-Property；序列化私有字段 m_PascalCase（强制）；非序列化私有字段 _camelCase（强制）；静态私有字段 s_PascalCase（强制）；方法/事件 PascalCase；局部变量/参数 camelCase；常量 ALL_UPPER（强制）；静态只读 PascalCase。字段前缀区分：m_=序列化、_=非序列化、s_=静态，杜绝 this. 冗余。var 仅当右侧类型明确时使用。Allman 大括号，4 空格缩进。
- **0-Alloc 热路径：** 禁止 new/LINQ/foreach 非泛型；禁止 lambda/匿名委托（缓存方法组）；禁止 + 拼字符串（用零分配字符串工具或 StringBuilder 池）；禁止每帧 ToUpper/ToLower；禁止 params；返回空集合用 Array.Empty<T>()；临时缓冲区优先 stackalloc + Span<T>。
- **内存布局与 Cache 友好：** struct ≤ 16 bytes 且 readonly；批量数据优先 SoA 提升 cache locality；高频值类型实现 IEquatable<T>；互操作场景用 [StructLayout(LayoutKind.Sequential)]；多线程写入字段防 false sharing。
- **Span 与 unsafe：** 字符串/JSON/二进制解析用 ReadOnlySpan&lt;char&gt;/ReadOnlySpan&lt;byte&gt; 避免 substring 分配；unsafe 仅限性能关键场景（指针操作/直接内存拷贝），须注释说明；allowUnsafeCode 按 asmdef 粒度开启。
- **防装箱：** 通用工具必须泛型接口（IEquatable&lt;T&gt;）；禁止 ArrayList/Hashtable/非泛型 Queue/Stack；禁止 Enum 传 object（用泛型 Enum.Parse&lt;T&gt;）；禁止 object 参数函数（用泛型）；禁止热路径 Debug.Log（用封装日志工具）。
- **线程安全：** 跨线程共享字段用 volatile/Interlocked；Unity API 仅主线程调用，异步续体须 EnsureMainThread() 守卫或 Dispatcher 入队；锁仅限非热路径初始化，热路径用 lock-free；CancellationToken 贯穿所有异步操作。
- **对象池化：** 高频创建/销毁对象（事件、任务、缓冲区、GameObject）必须池化；池接口统一 Acquire/Release；池对象实现状态重置；容量按场景配置，支持运行时回收。
- **Unity 引擎：** GetComponent 必须 Awake/Start 缓存；禁止 GameObject.Find/SendMessage/BroadcastMessage；序列化字段 m_ 前缀；yield return 缓存静态只读或用协程工具；高频异步用 UniTask（禁止同步 IO 和 Coroutine 做 IO）；用 Mathf 不用 Math；ScriptableObject 做数据驱动配置并运行时缓存引用。
- **异常与错误处理：** 禁止 try-catch 做逻辑控制；热路径严禁 try-catch；用 Debug.Assert/Assert.IsTrue（仅 Editor）；非热路径公共 API 做参数校验抛 ArgumentException；异常不吞——要么处理要么上抛。
- **代码组织：** 一文件一顶层类；类/接口/公有方法/枚举必须 &lt;summary&gt;（内容独占行）；严禁 TODO 入主干；#region 用于小范围分组（双语标签），严禁大段折叠掩盖 SRP 违例（违反则拆类）；asmdef 最小化依赖、禁止循环引用。
- **AOT/IL2CPP 兼容：** 禁止 Reflection.Emit/动态代码生成；反射仅限序列化/编辑器，运行时避免；泛型 AOT 预编译缺失时需预生成元数据或用非泛型路径；Type/enum 缓存为静态只读字段避免反复 GetType。
- **工具链与质量门：** 启用 Roslyn Analyzers；.editorconfig indent_size=4；提交前通过 ZeroAlloc 性能测试；PR 须通过编译 + Analyzer + 测试三重门。
- **执行等级：** Mandatory（违反打回：命名前缀、0-Alloc、防装箱、AOT 兼容）/ Prefer（性能敏感区必须，非热路径可放宽：Span/unsafe/池化/线程安全）/ Reference（逐步优化遗留）。

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
- **DOTween** - 动画系统
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
