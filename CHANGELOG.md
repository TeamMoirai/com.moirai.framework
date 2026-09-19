# Changelog

本项目的所有重要变更都会记录在此文件中。

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。
已发布版本的完整对比见 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases)。

## [Unreleased]

### Added

- `PlayerLoopDriver`：`OnDrawGizmos` / `OnDrawGizmosSelected` 静态事件表，Gizmos 订阅从宿主实例事件迁出，宿主销毁不再丢订阅。
- `GameAppHost`：框架唯一的轻量 MonoBehaviour 宿主（`SingletonMono_Persistent`），承接协程与 `OnDrawGizmos(Selected)` / `OnApplicationPause` 三类只能在 MonoBehaviour 上派发的消息，统一转发到 `PlayerLoopDriver` 静态表。
- `PlayerLoopDriver` 注册/注销接入主线程 fail-fast 断言，并在类型文档中写明线程契约。
- `Tests/EditorMode/Core/PlayerLoop/PlayerLoopDriverTests.cs`：驱动架构验收测试。

### Changed

- **`GameApp` 不再含任何 MonoBehaviour 成员**：1.0.2 里嵌套的 `GameApp.MainBehaviour` 宿主（连同 `s_Entity` / `s_Behaviour`）被移除，协程与引擎事件一律经 `GameAppHost`。
- **宿主 GameObject 改名**（相对已发布的 1.0.2）：`[UpdateDriver]` → `[GameAppHost]`。
- **帧时钟采样时点**：`GameTime.StartFrame()` 上移到 `DriveUpdate` / `DriveFixedUpdate` / `DriveLateUpdate` 各阶段入口。此前接口 `IUpdateHandler` 读到的是上一帧的 `deltaTime`（采样排在回调循环之后），现在 Handler 与 Action 回调读到同一帧的快照。
- **驱动中注册/注销的延迟缓冲按阶段隔离**：此前 `AddLateUpdateCallback` 在驱动中被调用会被兜底注册成 Update 回调；`Register(单阶段接口)` 在驱动中被调用会被"升级"注册进该对象实现的其余阶段。
- `PlayerLoopDriver` 内部：六份按阶段复制的注册表（数组 + 计数 + 延迟缓冲 + 容量增长 + 去重 + 注销搬移）收敛为 `HandlerSlot<T>` / `CallbackSlot`，三份插入排序合一；同一收敛也消除了"新增一个阶段就得复制一遍"的出错面。

### Fixed

- **`GameApp.AddOnApplicationPauseListener` 单独使用时永不触发**：此前只有宿主因协程 / Gizmos 等原因被创建后才会挂上 Pause 转发。
- **订阅方抛异常会永久卡死驱动器**：`Drive*` 缺少 `finally`，异常路径下 `s_IsDriving` 残留为 `true`，此后所有注册滞留在延迟缓冲且当帧不提交。

### Deprecated

- 无。

## [1.0.2] - 2026-09-12

见 [1.0.2 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.2)。

## [1.0.1] - 2026-09-01

见 [1.0.1 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.1)。

## [1.0.0] - 2026-08-20

首个正式版本。见 [1.0.0 发布页](https://github.com/TeamMoirai/com.moirai.framework/releases/tag/1.0.0)。
