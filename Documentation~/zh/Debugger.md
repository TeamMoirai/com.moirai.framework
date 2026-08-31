# Debugger 服务

> 运行时调试器：基于 UI Toolkit 的游戏内调试面板——控制台（虚拟化日志流）、运行环境信息、内存与对象池剖析、常驻统计 HUD，以及一行注册的流式调试面板构建器。

Debugger 服务由 `DebuggerService` 静态外观负责窗口注册表与轮询，由处理器在首个 Tick 懒建的 `DebuggerRuntimeHost`（`DontDestroyOnLoad`）承载 UI——**无需在场景中摆放任何组件或资产**。运行时以左上角 FPS 悬浮入口出现（可拖拽、边缘吸附、按日志级别着色），点按展开完整窗口；窗口为侧边栏树导航 + 内容区结构，支持搜索过滤、标题栏拖动与右下角缩放。全部面板（`PanelSettings` / `UIDocument`）运行时构建，仅依赖包内 `Resources/DebuggerPanelSettings.asset`（内嵌默认主题引用）。

## 核心特性

- **UI Toolkit 渲染**：脱离 IMGUI 逐帧重绘——`ListView` 虚拟化日志列表（makeItem/bindItem 仅渲染可视行），信息窗口按 0.25s 节流重建
- **Console 日志台**：分级过滤芯片（增量计数，零遍历刷新）、日志搜索、锁定滚动、堆栈详情与一键复制
- **信息窗口**：System / Environment / Screen / Graphics / Input（Input System 设备与传感器）/ Scene / Time / Quality / Path
- **Profiler 窗口**：Summary、Memory Summary、Memory 明细（All / Texture / Mesh / Material / Shader / AnimationClip / AudioClip / Font / TextAsset / ScriptableObject）、Object Pool / GameObject Pool / Memory Pool、Service System（服务容器诊断）
- **服务调试面板**：Timer / Resource / Audio / Procedure / Localization 各服务模块自带的调试视图，经服务 OnInit 自动注册（见「服务调试面板」章节）
- **游戏应用设置**：目标帧率/游戏速度实时控制与本地设置键值清单（`Other/Game Settings`，原 GameAppEditor 整合）
- **常驻统计 HUD**：FPS / Tris / Batches / DrawCall / SetPass / Mono / Alloc / GfxDrv（`ProfilerRecorder` 按需启停 + 0.25s 节流）
- **Operations**：GameObject 池冲刷、资源卸载 / GC、Time Scale 滑条、框架关停（None / Restart / Quit）
- **流式面板构建器**：`RegisterPanel` 一行注册自定义调试面板（滑条 / 开关 / 按钮 / 折叠组 / 只读字段 / 进度条，Getter/Setter 闭包绑定 + 200ms 轮询刷新）
- **线程安全日志捕获**：`logMessageReceivedThreaded` 任意线程入队、主线程排空的池化环形缓冲
- **可配置激活策略**：总是打开 / 仅开发构建 / 仅编辑器 / 总是关闭，命令行 `-showdebugger` 强制开启
- **布局持久化**：悬浮入口与窗口的位置、尺寸、缩放经 `SettingUtility` 记忆；分辨率自适应（参考 1920×1080）

## 核心类型

命名空间：`Moirai.Atropos.Debugger`

| 类/接口 | 说明 |
|---------|------|
| `DebuggerService` | 静态外观（`[HandlerHost]`，`IServiceTickable`）：`ActiveWindow` / `ShowFullWindow` / `ActiveWindowType` / `WindowRegistry` / `LogCapture`；`RegisterDebuggerWindow` / `UnregisterDebuggerWindow` / `GetDebuggerWindow` / `SelectDebuggerWindow` / `RegisterPanel` / `RegisterDebugView` / `GetRecentLogs`。经 `s_Handler` 转发（未注册时静默降级——仅主动注册方可使用服务） |
| `DebuggerServiceHandler` | 处理器抽象基类（契约）：`ActiveWindow` / `ShowFullWindow` / `WindowRegistry` / `LogCapture` / `Tick` / 注册族四方法；配置由 `DebuggerServiceHandlerConfig` 纯数据类承载 |
| `DefaultDebuggerHandler` | 内置后端：持有窗口注册表与日志捕获器，按激活策略解析可见性，首个 Tick 懒建运行时宿主 |
| `DefaultDebuggerHandlerConfig` | 后端配置（`[SerializeReference]` 存于设置资产）：`ConsoleCapacity`（环形缓冲容量，默认 256）/ `FpsUpdateInterval` / `StatsOverlayVisible` / `WindowOpacity` |
| `DebuggerServiceSettings` | 设置资产（`[FrameworkSetting]`）：`ActiveWindowType` 激活策略 + `m_HandlerConfig` 处理器配置 |
| `IDebuggerWindow` | 窗口接口：`Initialize(params object[])` / `Shutdown()` / `OnEnter()` / `OnLeave()` / `OnUpdate(float, float)` / **`CreateView()` → `VisualElement`** |
| `DebuggerWindowRegistry` | 窗口注册表（纯数据）：扁平字典 O(1) 检索 + 路径树导航模型（`DebuggerWindowNode`），结构版本号驱动侧边栏重建 |
| `DebuggerLogCapture` | 日志捕获器：线程安全入队 + 主线程 `Drain()` 排空的池化环形缓冲；增量分级计数 + 内容版本号 |
| `LogNode` | 池化日志结点：`LogTime` / `LogFrameCount` / `LogType` / `LogMessage` / `StackTrack` |
| `DebuggerRuntimeHost` | 运行时宿主（MonoBehaviour）：运行时构建 PanelSettings/UIDocument、悬浮 FPS 入口、主窗口 chrome、布局持久化、OS 回退字体（含 CJK）；单例 `Instance` |
| `DebuggerStatsOverlay` | 常驻统计 HUD（`ProfilerRecorder` + StringBuilder 复用，稳态零分配） |
| `DebugPanelBuilder` | 流式面板构建器：`AddLabel` / `AddSection` / `AddFoldout` / `AddButton` / `AddToggle` / `AddSlider` / `AddIntSlider` / `AddReadOnlyField` / `AddProgressBar` |
| `ScrollableDebuggerWindowBase` | 可滚动窗口基类（UI Toolkit）；`PollingDebuggerWindowBase` 节流轮询重建基类 |
| `DebuggerActiveWindowType` | 激活策略枚举：`AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor` / `AlwaysClose` |
| `Constant.Debug` | 布局与控制台筛选的设置键常量 |
| `CommandLineUtility` | 静态工具类：`GetShowDebugger()` 读取 `-showdebugger` 强制开启参数 |
| `ServiceDebugView` | IMGUI 调试视图抽象基类（实现 `IDebuggerWindow`）：`Title` / `IsReady` / `OnDrawContent()`（GUILayout）+ 默认 `CreateView()`（`IMGUIContainer` 嵌入 UI Toolkit 面板）——游戏侧快速 IMGUI 视图的兼容扩展路径（框架内置面板均为原生 UI Toolkit） |
| `Windows/*` | 内置窗口实现：`ConsoleWindow`、`*InformationWindow`、`RuntimeMemorySummaryWindow`、`RuntimeMemoryInformationWindow<T>`、`*PoolInformationWindow`、`ServiceSystemInformationWindow`、`OperationsWindow`、`SettingsWindow` 等 |

## 快速上手

无需场景配置——组合根注册 `DebuggerService` 后（`GameEntry` 预制体已内置），运行即出现左上角 FPS 悬浮入口，点按展开完整窗口。激活策略在 `Assets/Settings/Framework/Resources/DebuggerServiceSettings.asset` 中配置。

代码控制：

```csharp
using Moirai.Atropos.Debugger;

DebuggerService.ActiveWindow = true;          // 悬浮入口可见开关
DebuggerService.ShowFullWindow = true;        // 完整窗口 <-> 悬浮入口
DebuggerService.SelectDebuggerWindow("Profiler/Memory/Texture");  // 选中窗口
IDebuggerWindow window = DebuggerService.GetDebuggerWindow("Console");
```

获取运行期间记录的日志：

```csharp
using System.Collections.Generic;
using Moirai.Atropos.Debugger;

var logs = new List<LogNode>();
DebuggerService.GetRecentLogs(logs);       // 全部（环形缓冲内）
DebuggerService.GetRecentLogs(logs, 100);  // 最近 100 条

foreach (LogNode node in logs)
{
    UnityEngine.LogType type = node.LogType;
    string message = node.LogMessage;
    string stack = node.StackTrack;
}
```

## 流式调试面板（推荐扩展方式）

自定义调试面板无需手写 UI Toolkit 视图——一行注册，构建器声明控件：

```csharp
using Moirai.Atropos.Debugger;

DebuggerService.RegisterPanel("Game/Player", builder => builder
    .AddLabel("Player runtime tweaks")
    .AddSlider("Move Speed", 0f, 20f, () => player.MoveSpeed, v => player.MoveSpeed = v)
    .AddToggle("God Mode", () => player.Invulnerable, v => player.Invulnerable = v)
    .AddIntSlider("Max Health", 1, 999, () => player.MaxHealth, v => player.MaxHealth = v)
    .AddReadOnlyField("Current Position", () => player.transform.position)
    .AddProgressBar("Stamina", 0f, 100f, () => player.Stamina)
    .AddFoldout("Combat", b => b
        .AddButton("Kill All Enemies", player.KillAll)
        .AddReadOnlyField("Damage", () => player.Damage, "{0:F1}"))
    .AddSection("Danger Zone")
    .AddButton("Respawn", player.Respawn));
```

- 值控件经 Getter/Setter 闭包绑定（构建期一次性分配），运行时由 `schedule` 以 200ms 间隔轮询刷新；元素脱离面板时调度自动暂停
- 窗口标题取路径末段（上例为 "Player"）
- `AddSlider` 拖动期间暂停外部回写（避免与用户输入打架）

## 自定义窗口与 IMGUI 调试视图

实现 `IDebuggerWindow`（UI Toolkit 视图）注册自定义窗口：

```csharp
using UnityEngine.UIElements;

public class MyWindow : IDebuggerWindow
{
    public void Initialize(params object[] args) { }
    public void Shutdown() { }
    public void OnEnter() { }
    public void OnLeave() { }
    public void OnUpdate(float elapseSeconds, float realElapseSeconds) { }

    public VisualElement CreateView()
    {
        var root = new VisualElement();
        root.Add(DebuggerUI.CreateSectionTitle("Hello Debugger"));
        root.Add(DebuggerUI.CreateRow("Answer", "42"));
        return root;
    }
}

DebuggerService.RegisterDebuggerWindow("Other/My", new MyWindow());
```

既有 IMGUI 调试视图（`ServiceDebugView` 派生）零改动接入——`CreateView()` 默认经 `IMGUIContainer` 包装 `OnDraw()` 的 GUILayout 内容：

```csharp
// 便捷注册（内部经 IMGUIDebuggerWindow 适配）
DebuggerService.RegisterDebugView("Profiler/Timer Service", new TimerServiceDebugView());
```

样式辅助统一收口 `DebuggerUI`（仅构建结构与挂 USS 类）：`CreateSection` / `CreateCard` / `CreateRow`（值区域点击复制，2/3 宽行重载）/ `CreateActionButton` / `CreateToggle` / `CreateFilterChip` / `CreateSlider` / `CreateReadOnlyMultilineText` / `StyleScrollView` 等；视觉样式（色板/尺寸/三态）统一定义于共享样式库「`Runtime/Modules/Debugger/Resources/Debugger UI.uss`」（经「`Debugger UI Theme.tss`」挂载到 `DebuggerPanelSettings.themeStyleSheet`，悬停/按下/选中由 USS 伪类驱动）——与 [DebugUI](https://github.com/annulusgames/DebugUI) 共用同一主题结构；侧边栏组节点使用内置 `Foldout`（自带旋转箭头与内容折叠）。

## 服务调试面板（框架内置）

各框架服务模块在其自身目录下持有原生 UI Toolkit 调试视图（实现 `IDebuggerWindow`），由**服务自己的 `OnInit` 经 `DebuggerService.RegisterDebuggerWindow` 自动注册**——组合根中调试器先行注册（顺序契约），服务初始化即完成面板挂载，无需场景组件。侧边栏路径与承载内容：

| 路径 | 视图（模块目录） | 内容 |
|------|-----------------|------|
| `Profiler/Timer` | `TimerServiceDebugView`（Timer 模块） | 活跃/容量/峰值统计与占用率、活跃计时器采样、僵尸一次性计时器检测（0.5s 节流） |
| `Profiler/Resource` | `ResourceServiceDebugView`（Resource 模块） | 运行模式、已加载资产快照（状态/引用计数，0.5s 节流） |
| `Profiler/Audio` | `AudioServiceDebugView`（Audio 模块） | 主音量与 Sfx/UI/Music/Voice 四轨音量/静音实时控制 |
| `Profiler/Procedure` | `ProcedureServiceDebugView`（Procedure 模块） | 当前流程状态与持续时长（0.5s 节流） |
| `Profiler/Localization` | `LocalizationServiceDebugView`（Localization 模块） | 当前语言展示与一键切换（1s 节流） |
| `Other/Game Settings` | `GameAppInformationWindow`（Debugger 内置） | 目标帧率/游戏速度实时控制（预设 0x-8x）、本地设置键值清单与保存/清除 |

新增服务调试面板的固定模式：

```csharp
// 1) 视图类放在服务模块自己的目录下（如 Runtime/Modules/Audio/AudioServiceDebugView.cs），
//    继承 PollingDebuggerWindowBase（数据型）或 ScrollableDebuggerWindowBase（控制型），内容经 DebuggerUI 主题化辅助构建；
// 2) 服务 OnInit 末尾注册（组合根已保证 DebuggerService 先行——外观未就绪时静默跳过）：
public override void OnInit()
{
    _ = Handler;
    DebuggerService.RegisterDebuggerWindow("Profiler/<Service>", new XxxServiceDebugView());
}
```

注册表结构版本驱动宿主侧边栏自动重建——晚于宿主创建的注册同样即时生效。

### IMGUI 调试视图适配（兼容扩展路径）

游戏侧已有或偏好 GUILayout 的调试视图（`ServiceDebugView` 派生）零改动接入——默认 `CreateView()` 经 `IMGUIContainer` 包装（自动提亮皮肤文字色保证深底可读）：

```csharp
DebuggerService.RegisterDebugView("My/IMGUI View", new MyIMGUIDebugView());
```

自定义弹窗等任意 OnGUI 上下文也可直接 `view.OnDraw()`。

## 配置与扩展

- **激活策略**（`DebuggerServiceSettings.ActiveWindowType`）：
  - `AlwaysOpen`：无条件打开
  - `OnlyOpenWhenDevelopment`：`Debug.isDebugBuild` 时打开（默认）
  - `OnlyOpenInEditor`：`Application.isEditor` 时打开
  - `AlwaysClose`：默认关闭
  - 非 `AlwaysOpen` 策略均可用启动参数 `-showdebugger` 强制开启
- **后端配置**（`DefaultDebuggerHandlerConfig`）：`ConsoleCapacity` / `FpsUpdateInterval` / `StatsOverlayVisible` / `WindowOpacity`
- **扩展信息窗口**：继承 `PollingDebuggerWindowBase`（节流重建）或 `ScrollableDebuggerWindowBase`，注册到 `"Information/..."` 路径
- **自定义后端**：继承 `DebuggerServiceHandler` + 配对 `DebuggerServiceHandlerConfig`，在设置资产中替换
- `DebuggerService.Tick` 仅在 `ShowFullWindow` 展开时轮询可见窗口；日志捕获的排空始终执行（捕获不依赖 UI 状态）

## 注意事项

- 内置窗口（28 个）由 `DefaultDebuggerHandler.OnInit` 注册，自定义窗口请在服务初始化后注册；`RegisterDebuggerWindow` 的路径不能为空 / 不能与已注册窗口或目录冲突，否则抛出 `GameException`
- 运行时面板**必须携带主题**：宿主从包内 `Resources/DebuggerPanelSettings.asset` 克隆（内嵌 `UnityDefaultRuntimeTheme` 引用）——`ScriptableObject.CreateInstance<PanelSettings>()` 在 Play 模式下 `themeStyleSheet` 为 null，全部内置控件将失去基础 USS（布局完全错位）
- MonoBehaviour 字段初始化器中禁止创建 `VisualElement`（UnityException）——一律在构建方法内创建
- 悬浮入口拖拽松手后自动吸附最近屏幕边缘；布局经 `SettingUtility` 持久化，标题栏 Reset 按钮还原默认
- 控制台筛选状态（分级 + 锁定滚动）同样持久化，键见 `Constant.Debug`
- `GetRecentLogs` 返回的 `LogNode` 为池化对象，由捕获器持有——仅读取，勿长期保存（环形淘汰后结点被复用）
- 服务为 opt-in 注册（组合根手动注册 `DebuggerService`），未注册时外观调用静默降级（日志检索返回空、注册不生效）

## 从旧版迁移（IMGUI DebuggerComp）

- 场景 / 预制体中的 `DebuggerComp` 组件已移除（`GameEntry.prefab` 不再包含 Debugger 节点）——宿主由服务运行时自建
- **`ServiceDebuggerComponent`（Inspector 宿主组件）已弃用移除**：派生组件（如 `TimerServiceDebugger`）与通用 Inspector 一并删除——各服务调试视图改为 OnInit 自动注册进游戏内调试器（原生 UI Toolkit，位于各模块目录）
- **`GameAppEditor`（GameApp Inspector）已删除**：其调试信息（帧率/游戏速度/本地设置清单）整合进内置 `Other/Game Settings` 窗口（GameObject 池内容本就由 `Profiler/GameObject Pool` 承载）
- `DebuggerComp.Instance.GetRecentLogs(...)` → `DebuggerService.GetRecentLogs(...)`
- `DebuggerComp.LogNode` → `Moirai.Atropos.Debugger.LogNode`（顶级类型）
- `IDebuggerWindow.OnDraw()`（IMGUI）→ `CreateView()`（UI Toolkit）；IMGUI 内容经 `RegisterDebugView` / `IMGUIDebuggerWindow` 适配接入
- `IDebuggerWindowGroup` / `DebuggerWindowRoot`（窗口组嵌套工具栏）→ `DebuggerWindowRegistry`（路径树仅作导航数据）
- 激活策略从 `DebuggerComp` Inspector 序列化字段迁移至 `DebuggerServiceSettings` 资产（`ActiveWindowType`）；`UGUIHandler` 的错误日志开关改读 `DebuggerService.ActiveWindowType`
- 输入信息窗口由旧 `UnityEngine.Input` API（仅 Input System 构建下抛异常）重写为 Input System 设备模型读取

---
[« 返回主 README](../../README.md)
