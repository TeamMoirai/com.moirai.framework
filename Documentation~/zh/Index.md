# Moirai Framework 文档

> Unity 全平台商业化游戏框架——服务化架构、高性能异步、热更新就绪。

欢迎使用 Moirai Framework。本文档集覆盖框架全部功能服务与核心工具，每篇文档包含核心特性、核心类型、快速上手与进阶用法。

- 英文文档见 [`Documentation~/en/`](../en/Index.md)
- 项目主页与安装指南见 [主 README](../../README.md)

---

## 功能服务

| 文档 | 说明 |
|------|------|
| [Core](Core.md) | 服务系统基座：`ServiceWorld` 服务世界、`GameServices` 注册/查找/作用域、`[ServiceDependency]` 依赖拓扑初始化 |
| [Resource](Resource.md) | 基于 YooAsset 的资源管理：同步/异步加载、引用计数、加密、子精灵 |
| [UI](UI.md) | 商业化 UI 框架：栈式窗口、五层层级、Widget 子控件、绑定代码生成 |
| [Audio](Audio.md) | 音频系统：分类管理、AudioAgent 代理播放、混音器、淡入淡出、句柄控制 |
| [Localization](Localization.md) | 本地化：文本/图片/音频/Timeline 多类型注入、Google 翻译集成 |
| [ConfigTable](ConfigTable.md) | Luban 配置表集成：表加载与懒加载访问、转表工具链 |
| [Procedure](Procedure.md) | 游戏流程管理：启动链、可配置流程、自包含状态机 |
| [Input](Input.md) | 多平台输入抽象：Input System / 旧版输入 / 移动端 UI 触控、按键提示 |
| [Save](Save.md) | 可插拔存档系统：多数据块容器、四后端序列化、AES 加密、版本迁移、无代码组件保存、云同步 |
| [Scene](Scene.md) | 场景管理：基于 YooAsset SceneHandle 的异步加载/激活/卸载 |
| [Timer](Timer.md) | 四级时间轮计时器：版本化句柄、预热、统计信息 |
| [ObjectPool](ObjectPool.md) | 服务级对象池：单次/多次 Spawn 池、GameObject 池 |
| [Debugger](Debugger.md) | 运行时调试器：可注册调试窗口、日志回放 |

## 核心工具

| 文档 | 说明 |
|------|------|
| [Singleton](Singleton.md) | 单例系统：纯 C# / MonoBehaviour / 注册式单例基类家族 |
| [MemoryPool](MemoryPool.md) | 零 GC 页式内存池：非托管元数据、EWMA 自适应水位线 |
| [UpdateDriver](UpdateDriver.md) | Unity 生命周期代理：协程托管、帧更新注入、引擎事件注入 |
| [StringUtility](StringUtility.md) | 字符串格式化与构建：可插拔 Handler、池化 StringBuilder |
| [JsonUtility](JsonUtility.md) | JSON 序列化/反序列化：可插拔 Handler、字节快速通路 |
| [ObjectUtility](ObjectUtility.md) | 对象实例化/销毁：可插拔 Handler、联网感知 |
| [TweenUtility](TweenUtility.md) | 缓动动画：可插拔引擎（自研/PrimeTween/LitMotion）、统一缓动参数 |

---

[« 返回主 README](../../README.md)
