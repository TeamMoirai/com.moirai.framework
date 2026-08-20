# Debugger 服务

> 运行时调试器：基于 IMGUI 的游戏内调试面板，提供控制台、运行环境信息、内存与对象池剖析等窗口。

Debugger 服务由纯 C# 的 `DebuggerService`（经 `GameApp.Debugger` 访问）负责窗口树的注册与轮询，由场景组件 `DebuggerComp` 负责绘制。运行时以左上角漂浮框形式出现，点按后展开完整窗口；窗口布局（位置、大小、缩放）通过 `SettingUtility` 持久化。全部窗口基于 `IDebuggerWindow` 接口实现，业务可注册自己的调试窗口。本服务为纯运行时 IMGUI 实现，无编辑器专属代码（Editor 目录下的 Events / Scheduler 调试窗口属于其他服务）。

## 核心特性

- 运行时面板：Console 日志台（信息/警告/错误/致命筛选、锁定滚动）、FPS 计数
- 信息窗口：System / Environment / Screen / Graphics / Scene / Path / Time / Quality
- 输入信息：Summary / Touch / Location / Acceleration / Gyroscope / Compass
- Profiler 窗口：Summary、Memory（All / Texture / Mesh / Material / Shader / AnimationClip / AudioClip / Font / TextAsset / ScriptableObject）、Object Pool、Reference Pool
- 可配置激活策略：总是打开 / 仅开发构建 / 仅编辑器 / 总是关闭，且可用命令行参数强制开启
- 窗口树架构：路径式注册（如 `"Profiler/Memory/Texture"`），支持自定义窗口与窗口组
- 布局持久化：漂浮框与窗口的位置、尺寸、缩放记忆在本地设置中

## 核心类型

命名空间：`Moirai.Atropos.Debugger`

| 类/接口 | 说明 |
|---------|------|
| `IDebuggerService` | 调试器管理器接口：`ActiveWindow`、`DebuggerWindowRoot`、`RegisterDebuggerWindow` / `UnregisterDebuggerWindow` / `GetDebuggerWindow` / `SelectDebuggerWindow` |
| `DebuggerService` | 默认实现（`internal sealed`），`Priority = -1`，实现 `IUpdateService`，仅当窗口激活时轮询窗口树 |
| `IDebuggerWindow` | 调试器窗口接口：`Initialize(params object[] args)` / `Shutdown()` / `OnEnter()` / `OnLeave()` / `OnUpdate(float, float)` / `OnDraw()` |
| `IDebuggerWindowGroup` | 窗口组接口（继承 `IDebuggerWindow`）：`DebuggerWindowCount` / `SelectedIndex` / `SelectedWindow` / `GetDebuggerWindowNames()` / `RegisterDebuggerWindow(string, IDebuggerWindow)` |
| `DebuggerComp` | 调试器组件（`public sealed partial`，MonoBehaviour），挂接场景后绘制全部面板，单例 `DebuggerComp.Instance` |
| `DebuggerActiveWindowType` | 激活策略枚举：`AlwaysOpen` / `OnlyOpenWhenDevelopment` / `OnlyOpenInEditor` / `AlwaysClose` |
| `DebuggerComp.LogNode` | 日志结点：`LogTime` / `LogFrameCount` / `LogType` / `LogMessage` / `StackTrack` |
| `Constant.Debug` | 布局与控制台筛选的设置键常量（如 `WINDOW_SCALE`、`LOCK_SCROLL`） |
| `CommandLineUtility` | 静态工具类：`GetShowDebugger()` 读取命令行强制开启参数 |
| `Component/*` | 各信息窗口实现，如 `ConsoleWindow`、`ProfilerInformationWindow`、`RuntimeMemoryInformationWindow<T>`、`ObjectPoolInformationWindow`、`ScrollableDebuggerWindowBase` 等 |

## 快速上手

在场景中放置挂有 `DebuggerComp` 的 GameObject（Inspector 可配置 `GUISkin`、激活策略 `m_ActiveWindow`、`m_ShowFullWindow`），运行后点击左上角漂浮框展开调试器。

代码中控制开关与选中窗口：

```csharp
using Moirai.Atropos;

GameApp.Debugger.ActiveWindow = true;        // 打开/关闭调试器窗口
bool active = GameApp.Debugger.ActiveWindow;

// DebuggerComp 上的等价与扩展控制
DebuggerComp.Instance.ActiveWindow = true;      // 同时启停组件
DebuggerComp.Instance.ShowFullWindow = true;    // 完整窗口 <-> 漂浮框
DebuggerComp.Instance.ResetLayout();            // 还原默认布局（位置/大小/缩放）

// 选中某个窗口（路径来自注册时的字符串）
GameApp.Debugger.SelectDebuggerWindow("Profiler/Memory/Texture");
IDebuggerWindow window = GameApp.Debugger.GetDebuggerWindow("Console");
```

注册自定义调试窗口：

```csharp
using Moirai.Atropos.Debugger;
using UnityEngine;

public class MyWindow : IDebuggerWindow
{
    public void Initialize(params object[] args) { }
    public void Shutdown() { }
    public void OnEnter() { }
    public void OnLeave() { }
    public void OnUpdate(float elapseSeconds, float realElapseSeconds) { }

    public void OnDraw()
    {
        GUILayout.Label("Hello Debugger");
    }
}

// 经 DebuggerComp 或服务接口注册，路径以 "/" 分层，自动归入窗口组
DebuggerComp.Instance.RegisterDebuggerWindow("Other/My", new MyWindow());
// 也可以直接使用服务接口：GameApp.Debugger.RegisterDebuggerWindow("Other/My", new MyWindow());
```

获取运行期间记录的日志：

```csharp
using System.Collections.Generic;
using Moirai.Atropos.Debugger;

var logs = new List<DebuggerComp.LogNode>();
DebuggerComp.Instance.GetRecentLogs(logs);     // 全部
DebuggerComp.Instance.GetRecentLogs(logs, 100); // 最近 100 条

foreach (DebuggerComp.LogNode node in logs)
{
    UnityEngine.LogType type = node.LogType;
    string message = node.LogMessage;
    string stack = node.StackTrack;
}
```

## 配置与扩展

- 激活策略（`DebuggerComp` Inspector 中的 `m_ActiveWindow`）：
  - `AlwaysOpen`：无条件打开（默认）
  - `OnlyOpenWhenDevelopment`：`UnityEngine.Debug.isDebugBuild` 时打开
  - `OnlyOpenInEditor`：`Application.isEditor` 时打开
  - `AlwaysClose`：默认关闭
  - 以上除 `AlwaysOpen` 外，均可用 `CommandLineUtility.GetShowDebugger()` 对应的启动参数强制打开
- 布局相关属性：`IconRect`（漂浮框区域）、`WindowRect`（窗口区域）、`WindowScale`（缩放，默认 1.5）；修改后由 `Constant.Debug` 中的设置键持久化
- 控制台筛选状态（信息/警告/错误/致命、锁定滚动）同样持久化，键见 `Constant.Debug.INFO_FILTER` 等
- 扩展信息窗口时可继承 `Component` 目录下的 `ScrollableDebuggerWindowBase` 获得滚动绘制能力，再注册到 `"Information/..."` 路径下
- `DebuggerService.Update` 仅在 `ActiveWindow == true` 时调用窗口树 `OnUpdate`，关闭状态下无轮询开销

## 注意事项

- `DebuggerComp.Start` 中一次性注册全部内置窗口（Console、Information/...、Profiler/...、Other/Settings、Other/Operations），自定义窗口请在之后注册
- `RegisterDebuggerWindow` 的 `path` 不能为空、`debuggerWindow` 不能为 null，否则抛出 `GameException`；`args` 会透传给窗口的 `Initialize`
- `ShowFullWindow` 切换时会连带启停场景中 `"UIRoot/EventSystem"` 事件系统对象
- FPS 计数器（`FpsCounter`）为 `DebuggerComp` 内部类型，按固定间隔（0.5 秒）统计，不对外暴露
- 窗口绘制基于 `OnGUI`/`GUILayout`，`DebuggerComp.OnDestroy` 时会调用 `SettingUtility.Save()` 保存布局设置

---
[« 返回主 README](../../README.md)
