# Core 服务系统（@Service）

> 框架的服务化基座：以纯 C# 类管理所有子服务的生命周期、轮询与作用域，并由 `GameApp`（MonoBehaviour）驱动。

`@Service` 是整个框架的服务基础设施。所有功能服务（资源、UI、音频、计时器等）均为继承 `Service` 的普通 C# 类，由静态类 `ServiceSystem` 统一注册、查找与销毁；`GameApp` 作为引擎入口，在 `Update`/`FixedUpdate`/`LateUpdate` 中驱动服务轮询，并提供 `GameApp.Timer` 等静态访问器。服务支持 App/Scene/Gameplay 三级作用域，场景卸载时可自动清理场景与玩法级服务。

## 核心特性

- 纯 C# 服务：非 MonoBehaviour，无场景依赖，生命周期由框架精确控制
- 三级作用域（`ServiceScope.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 遮蔽查找
- 生命周期接口按需实现：`IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable`、`IServiceGizmoDrawable`
- `Priority` 优先级控制轮询顺序（高优先先轮询、后关闭）
- 异步初始化：实现 `IAsyncInitService` 的服务在 `ServiceSystem.InitializeAsync()` 中异步初始化
- 服务事件：`ServiceRegistered`/`ServiceUnregistered` 事件支持热替换通知
- 迭代安全：轮询期间的注册/注销延迟到本轮结束后统一应用
- 主线程亲和守卫：编辑器与开发构建下断言调用线程，发布版零开销
- 静态访问器懒加载：`GameApp.Resource`、`GameApp.Timer` 等首次访问时创建并缓存

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `Service` | 服务抽象基类，定义 `OnInit()` / `Shutdown()` / `Priority` / `Scope`，并提供 `Require<T>()` / `TryGet<T>(out T)` 跨服务依赖解析 |
| `ServiceSystem` | 静态服务管理中心：注册、获取、注销、轮询驱动与作用域关闭 |
| `ServiceScope` | 服务作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | 轮询接口，方法签名 `Tick(float elapseSeconds, float realElapseSeconds)` 等 |
| `IServiceGizmoDrawable` | 编辑器 Gizmos 绘制接口 `OnDrawGizmos()` |
| `IAsyncInitService` | 异步初始化接口，实现 `OnInitAsync()` 的服务在 `ServiceSystem.InitializeAsync()` 中统一驱动 |
| `MonoServiceBehaviour<TScope>` | MonoBehaviour 服务基类，Awake 自动注册，App 作用域自动 DontDestroyOnLoad |
| `GameApp` | MonoBehaviour 入口（`[DefaultExecutionOrder(-1000)]`），持有全部内置服务静态访问器并驱动 `ServiceSystem` |
| `MessageEvent` / `EMessageEventType` | 命名空间 `Moirai.Atropos.Events`，框架级池化事件（对焦/失焦/退出、SDK 回调） |

## 快速上手

```csharp
// 1. 通过 GameApp 静态访问器获取内置服务（懒加载）
ITimerService timer = GameApp.Timer;
IResourceService resource = GameApp.Resource;

// 2. 通过 ServiceSystem 按接口获取（未注册时按 IXxxService -> XxxService 反射回退）
var service = ServiceSystem.GetService<ITimerService>();

// 3. 定义自定义服务
public interface IMyService { void DoSomething(); }

public class MyService : Service, IMyService, IUpdateService
{
    public override int Priority => 10;              // 高优先级先轮询
    public override ServiceScope Scope => ServiceScope.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Update(float elapseSeconds, float realElapseSeconds) { }
}

// 4. 显式注册（不遵循 IXxxService -> XxxService 命名约定时必须）
IMyService my = ServiceSystem.RegisterService<IMyService>(new MyService());

// 5. 注销（按接口注销当前最高优先作用域中的绑定，或按实例注销）
ServiceSystem.UnregisterService<IMyService>();
```

## 进阶用法

### 生命周期与作用域

- `Service.OnInit()` 在注册完成（含接口绑定、优先级排序）后立即调用；`Shutdown()` 在注销或作用域关闭时调用。
- `ServiceSystem.Shutdown()` 按 Gameplay -> Scene -> App 逆序关闭全部服务；`ServiceSystem.ShutdownScope(ServiceScope scope)` 只关闭指定作用域。
- `GameApp` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域的服务。
- 同一接口可在不同作用域注册不同实现，`GetService<T>()` 查找顺序为 Gameplay > Scene > App（跨作用域遮蔽），可用于战斗内临时替换全局实现。

### 跨服务依赖

```csharp
public class BattleService : Service
{
    public override void OnInit()
    {
        // 获取失败抛 GameException（同作用域向上回退到 App 查找）
        var timer = Require<ITimerService>();

        // 可选依赖
        if (TryGet<IDebuggerService>(out var debugger)) { /* ... */ }
    }
}
```

### 内置服务注册

内置服务实现类型在 `AppSettings.Initiation()`（`RuntimeInitializeLoadType.AfterAssembliesLoaded` 阶段）由配置注册到 `ServiceSystem`，可在 Inspector 中替换为自定义实现（如替换 `ITimerService` 的实现类）。配置注册早于任何游戏代码，因此优先于反射回退。

### 框架事件（MessageEvent）

`GameApp` 在引擎回调中触发框架事件（命名空间 `Moirai.Atropos.Events`）：

```csharp
// 获取/失去焦点、退出时由 GameApp 自动触发：
// EMessageEventType.ApplicationFocus / NotApplicationFocus / ApplicationQuit
MessageEvent.Trigger(EMessageEventType.ApplicationQuit);

// 通过 EventManager 订阅（池化事件，零 GC 分发）
EventManager.RegisterCallback<MessageEvent>(OnMessageEvent);
```

### 编辑器工具

菜单 `Tools/Moirai/Service System` 打开服务系统窗口，可查看已注册服务的接口、实现、作用域、优先级与生命周期接口实现情况（数据来自 `ServiceSystem.GetDiagnosticInfo()`）。

### 异步初始化 [ASYNC INIT]

实现 `IAsyncInitService` 接口的服务可在所有服务注册完成后进行异步初始化（如预加载资源、读取配置）：

```csharp
public class MyResourceService : Service, IMyResourceService, IAsyncInitService
{
    public override void OnInit()
    {
        // 同步快速设置（注册时调用）
    }

    public async UniTask OnInitAsync()
    {
        // 异步加载（InitializeAsync 时调用）
        await LoadCatalogAsync();
    }
}

// GameApp.Awake 中自动调用：
// await ServiceSystem.InitializeAsync();
// ProcedureSettings.StartProcedure().Forget();
```

### 服务事件 [SERVICE EVENTS]

```csharp
// 监听服务注册/注销
ServiceSystem.ServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

ServiceSystem.ServiceUnregistered += (service) =>
{
    Debug.Log($"Service unregistered: {service.GetType().Name}");
};
```

### MonoBehaviour 服务 [MONO SERVICE]

继承 `MonoServiceBehaviour<TScope>` 可将 MonoBehaviour 注册为服务：

```csharp
public class MyMonoService : MonoServiceBehaviour<AppScope>, IMyService
{
    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope 自动 DontDestroyOnLoad；SceneScope/GameplayScope 随场景卸载自然销毁
}
```

## 注意事项

- `ServiceSystem` 仅允许主线程调用；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Dispatch`/`DispatchAsync` 切回主线程。
- `GetService<T>()` 与 `UnregisterService<T>()` 必须传入接口类型，传入具体类会抛出 `GameException`。
- `RegisterService<T>` 快速失败校验：服务必须实现所注册的接口；同一作用域内重复注册同一接口仅告警并返回已有实例。
- 编辑器下退出 Play 模式时 `GameApp` 会自动调用 `ServiceSystem.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回主 README](../../README.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
