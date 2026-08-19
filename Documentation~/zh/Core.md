# Core 模块系统（@Core）

> 框架的模块化基座：以纯 C# 类管理所有子模块的生命周期、轮询与作用域，并由 `GameModule`（MonoBehaviour）驱动。

`@Core` 是整个框架的模块基础设施。所有功能模块（资源、UI、音频、计时器等）均为继承 `Module` 的普通 C# 类，由静态类 `ModuleSystem` 统一注册、查找与销毁；`GameModule` 作为引擎入口，在 `Update`/`FixedUpdate`/`LateUpdate` 中驱动模块轮询，并提供 `GameModule.Timer` 等静态访问器。模块支持 App/Scene/Gameplay 三级作用域，场景卸载时可自动清理场景与玩法级模块。

## 核心特性

- 纯 C# 模块：非 MonoBehaviour，无场景依赖，生命周期由框架精确控制
- 三级作用域（`ModuleScope.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 遮蔽查找
- 生命周期接口按需实现：`IUpdateModule`、`IFixedUpdateModule`、`ILateUpdateModule`、`IGizmoModule`
- `Priority` 优先级控制轮询顺序（高优先先轮询、后关闭）
- 迭代安全：轮询期间的注册/注销延迟到本轮结束后统一应用
- 主线程亲和守卫：编辑器与开发构建下断言调用线程，发布版零开销
- 静态访问器懒加载：`GameModule.Resource`、`GameModule.Timer` 等首次访问时创建并缓存

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `Module` | 模块抽象基类，定义 `OnInit()` / `Shutdown()` / `Priority` / `Scope`，并提供 `Require<T>()` / `TryGet<T>(out T)` 跨模块依赖解析 |
| `ModuleSystem` | 静态模块管理中心：注册、获取、注销、轮询驱动与作用域关闭 |
| `ModuleScope` | 模块作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `IUpdateModule` / `IFixedUpdateModule` / `ILateUpdateModule` | 轮询接口，方法签名 `Update(float elapseSeconds, float realElapseSeconds)` 等 |
| `IGizmoModule` | 编辑器 Gizmos 绘制接口 `OnDrawGizmos()` |
| `GameModule` | MonoBehaviour 入口（`[DefaultExecutionOrder(-1000)]`），持有全部内置模块静态访问器并驱动 `ModuleSystem` |
| `MessageEvent` / `EMessageEventType` | 命名空间 `Moirai.Atropos.Events`，框架级池化事件（对焦/失焦/退出、SDK 回调） |

## 快速上手

```csharp
// 1. 通过 GameModule 静态访问器获取内置模块（懒加载）
ITimerModule timer = GameModule.Timer;
IResourceModule resource = GameModule.Resource;

// 2. 通过 ModuleSystem 按接口获取（未注册时按 IXxxModule -> XxxModule 反射回退）
var module = ModuleSystem.GetModule<ITimerModule>();

// 3. 定义自定义模块
public interface IMyModule { void DoSomething(); }

public class MyModule : Module, IMyModule, IUpdateModule
{
    public override int Priority => 10;              // 高优先级先轮询
    public override ModuleScope Scope => ModuleScope.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Update(float elapseSeconds, float realElapseSeconds) { }
}

// 4. 显式注册（不遵循 IXxxModule -> XxxModule 命名约定时必须）
IMyModule my = ModuleSystem.RegisterModule<IMyModule>(new MyModule());

// 5. 注销（按接口注销当前最高优先作用域中的绑定，或按实例注销）
ModuleSystem.UnregisterModule<IMyModule>();
```

## 进阶用法

### 生命周期与作用域

- `Module.OnInit()` 在注册完成（含接口绑定、优先级排序）后立即调用；`Shutdown()` 在注销或作用域关闭时调用。
- `ModuleSystem.Shutdown()` 按 Gameplay -> Scene -> App 逆序关闭全部模块；`ModuleSystem.ShutdownScope(ModuleScope scope)` 只关闭指定作用域。
- `GameModule` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域的模块。
- 同一接口可在不同作用域注册不同实现，`GetModule<T>()` 查找顺序为 Gameplay > Scene > App（跨作用域遮蔽），可用于战斗内临时替换全局实现。

### 跨模块依赖

```csharp
public class BattleModule : Module
{
    public override void OnInit()
    {
        // 获取失败抛 GameException（同作用域向上回退到 App 查找）
        var timer = Require<ITimerModule>();

        // 可选依赖
        if (TryGet<IDebuggerModule>(out var debugger)) { /* ... */ }
    }
}
```

### 内置模块注册

内置模块实现类型在 `AppSettings.Initiation()`（`RuntimeInitializeLoadType.AfterAssembliesLoaded` 阶段）由配置注册到 `ModuleSystem`，可在 Inspector 中替换为自定义实现（如替换 `ITimerModule` 的实现类）。配置注册早于任何游戏代码，因此优先于反射回退。

### 框架事件（MessageEvent）

`GameModule` 在引擎回调中触发框架事件（命名空间 `Moirai.Atropos.Events`）：

```csharp
// 获取/失去焦点、退出时由 GameModule 自动触发：
// EMessageEventType.ApplicationFocus / NotApplicationFocus / ApplicationQuit
MessageEvent.Trigger(EMessageEventType.ApplicationQuit);

// 通过 EventManager 订阅（池化事件，零 GC 分发）
EventManager.RegisterCallback<MessageEvent>(OnMessageEvent);
```

### 编辑器工具

菜单 `Tools/Moirai/Module System` 打开模块系统窗口，可查看已注册模块的接口、实现、作用域、优先级与生命周期接口实现情况（数据来自 `ModuleSystem.GetDiagnosticInfo()`）。

## 注意事项

- `ModuleSystem` 仅允许主线程调用；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Dispatch`/`DispatchAsync` 切回主线程。
- `GetModule<T>()` 与 `UnregisterModule<T>()` 必须传入接口类型，传入具体类会抛出 `GameException`。
- `RegisterModule<T>` 快速失败校验：模块必须实现所注册的接口；同一作用域内重复注册同一接口仅告警并返回已有实例。
- 编辑器下退出 Play 模式时 `GameModule` 会自动调用 `ModuleSystem.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回主 README](../../README.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
