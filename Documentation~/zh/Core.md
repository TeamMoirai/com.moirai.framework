# Core 服务系统（@Service）

> 框架的服务化基座：以纯 C# 类管理所有子服务的生命周期、轮询与作用域，并由 `GameApp`（MonoBehaviour）驱动。

`@Service` 是整个框架的服务基础设施。所有功能服务（资源、UI、音频、计时器等）均为继承 `ServiceBase` 的普通 C# 类，由静态类 `GameServices` 统一注册、查找与销毁；`GameApp` 作为引擎入口，在 `Update`/`FixedUpdate`/`LateUpdate` 中驱动服务轮询，并提供 `GameApp.Timer` 等静态访问器。服务支持 App/Scene/Gameplay 三级作用域，场景卸载时可自动清理场景与玩法级服务。

## 核心特性

- 纯 C# 服务：非 MonoBehaviour，无场景依赖，生命周期由框架精确控制
- 三级作用域（`EServiceScopeKind.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 遮蔽查找
- 生命周期接口按需实现：`IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable`、`IServiceGizmoDrawable`
- `Priority` 优先级控制轮询顺序（高优先先轮询、后关闭）
- 异步初始化：实现 `IAsyncInitService` 的服务在 `GameServices.InitializeAsync()` 中异步初始化
- 服务事件：`ServiceRegistered`/`ServiceUnregistered` 事件支持热替换通知
- 迭代安全：轮询期间的注册/注销延迟到本轮结束后统一应用
- 主线程亲和守卫：编辑器与开发构建下断言调用线程，发布版零开销
- 依赖验证：服务可通过 `Dependencies` 属性声明依赖；注册时若依赖未就绪则抛出异常
- 生命周期状态机：每个服务跟踪 `EServiceState`（Created → Initialized → ShuttingDown → Disposed），关闭幂等
- Tick 异常隔离：单个服务在 `Tick` 中抛异常不会中断同帧其他服务

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `IService` | 服务核心契约：`Priority`、`Scope`、`OnInit()`、`Shutdown()` |
| `ServiceBase` | 服务抽象基类，定义 `OnInit()` / `Shutdown()` / `Priority` / `Scope` / `State` / `Dependencies`，并提供 `Require<T>()` / `TryGet<T>(out T)` 跨服务依赖解析 |
| `GameServices` | 静态服务管理中心：注册、获取、注销、轮询驱动与作用域关闭 |
| `EServiceScopeKind` | 服务作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `EServiceState` | 服务生命周期状态：`Created`、`Initialized`、`ShuttingDown`、`Disposed` |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | 轮询接口，方法签名 `Tick(float elapseSeconds, float realElapseSeconds)` 等 |
| `IServiceGizmoDrawable` | 编辑器 Gizmos 绘制接口 `OnDrawGizmos()` |
| `IAsyncInitService` | 异步初始化接口，实现 `OnInitAsync()` 的服务在 `GameServices.InitializeAsync()` 中统一驱动 |
| `ServiceMono<TScope>` | MonoBehaviour 服务基类，Awake 时通过 `RegisterAs` 合约自动注册，App 作用域自动 DontDestroyOnLoad |
| `GameApp` | MonoBehaviour 入口（`[DefaultExecutionOrder(-1000)]`），持有全部内置服务静态访问器并驱动 `GameServices` |
| `GameAppMessageEvent` / `EMessageEventType` | 命名空间 `Moirai.Atropos.Events`，框架级池化事件（对焦/失焦/退出、SDK 回调） |

## 快速上手

```csharp
// 1. 通过 GameApp 静态访问器获取内置服务
ITimerService timer = GameApp.Timer;
IResourceService resource = GameApp.Resource;

// 2. 通过 GameServices 按接口获取（未注册时抛出 GameException）
var service = GameServices.GetService<ITimerService>();

// 3. 定义自定义服务
public interface IMyService { void DoSomething(); }

public class MyService : ServiceBase, IMyService, IServiceTickable
{
    public override int Priority => 10;              // 高优先级先轮询
    public override EServiceScopeKind Scope => EServiceScopeKind.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 4. 显式注册
IMyService my = GameServices.RegisterService<IMyService>(new MyService());

// 5. 注销（按接口注销当前最高优先作用域中的绑定，或按实例注销）
GameServices.UnregisterService<IMyService>();
```

## 进阶用法

### 生命周期与作用域

- `ServiceBase.OnInit()` 在注册完成（含接口绑定、优先级排序）后立即调用；`Shutdown()` 在注销或作用域关闭时调用。
- `GameServices.Shutdown()` 按 Gameplay → Scene → App 逆序关闭全部服务；`GameServices.ShutdownScope(EServiceScopeKind scope)` 只关闭指定作用域。
- `GameApp` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域的服务。
- 同一接口可在不同作用域注册不同实现，`GetService<T>()` 查找顺序为 Gameplay > Scene > App（跨作用域遮蔽），可用于战斗内临时替换全局实现。

### 生命周期状态机

每个服务通过 `ServiceBase.State`（`EServiceState`）跟踪生命周期状态：

| 状态 | 说明 |
|------|------|
| `Created` | 实例已创建但未注册 |
| `Initialized` | `OnInit()` 已调用，服务运行中 |
| `ShuttingDown` | 正在调用 `Shutdown()` |
| `Disposed` | 服务已完全关闭并移除 |

关闭幂等：对已销毁的服务再次调用 `ShutdownService` 不会重复执行。

### 依赖声明

服务可声明在注册时验证的依赖。若依赖未注册，抛出带描述信息的 `GameException`：

```csharp
public class AudioService : ServiceBase, IAudioService
{
    // 声明 IResourceService 必须先于此服务注册
    protected internal override Type[] Dependencies => new[] { typeof(IResourceService) };

    public override void OnInit()
    {
        var resource = Require<IResourceService>();
        // ...
    }
}
```

### 跨服务依赖

```csharp
public class BattleService : ServiceBase
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

内置服务实现类型在 `AppSettings.Initiation()`（`RuntimeInitializeLoadType.AfterAssembliesLoaded` 阶段）由配置注册到 `GameServices`，可在 Inspector 中替换为自定义实现（如替换 `ITimerService` 的实现类）。`AppSettings.Initiation()` 中的注册顺序决定初始化顺序——确保依赖先于被依赖注册。

### 框架事件（GameAppMessageEvent）

`GameApp` 在引擎回调中触发框架事件（命名空间 `Moirai.Atropos.Events`）：

```csharp
// 获取/失去焦点、退出时由 GameApp 自动触发：
// EMessageEventType.ApplicationFocus / NotApplicationFocus / ApplicationQuit
GameAppMessageEvent.Trigger(EMessageEventType.ApplicationQuit);

// 通过 EventManager 订阅（池化事件，零 GC 分发）
EventManager.RegisterCallback<GameAppMessageEvent>(OnMessageEvent);
```

### 编辑器工具

菜单 `Tools/Moirai/Service System` 打开服务系统窗口，可查看已注册服务的接口、实现、作用域、优先级与生命周期接口实现情况（数据来自 `GameServices.GetDiagnosticInfo()`）。

### 异步初始化 [ASYNC INIT]

实现 `IAsyncInitService` 接口的服务可在所有服务注册完成后进行异步初始化（如预加载资源、读取配置）：

```csharp
public class MyResourceService : ServiceBase, IMyResourceService, IAsyncInitService
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
// await GameServices.InitializeAsync();
// await ProcedureSettings.StartProcedure();
```

### 服务事件 [SERVICE EVENTS]

```csharp
// 监听服务注册/注销
GameServices.ServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

GameServices.ServiceUnregistered += (service) =>
{
    Debug.Log($"Service unregistered: {service.GetType().Name}");
};
```

### MonoBehaviour 服务 [MONO SERVICE]

继承 `ServiceMono<TScope>` 可将 MonoBehaviour 注册为服务。覆写 `RegisterAs` 指定合约接口：

```csharp
public class MyMonoService : ServiceMono<AppScope>, IMyService
{
    protected override Type RegisterAs => typeof(IMyService);

    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope 自动 DontDestroyOnLoad；SceneScope/GameplayScope 随场景卸载自然销毁
}
```

### 服务拦截器 [SERVICE INTERCEPTORS (AOP)]

实现 `IServiceInterceptor` 在生命周期关键点插入横切逻辑（日志、性能监控、缓存等）：

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100; // 高优先先执行

    public void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds)
    {
        // 每次 Tick 前性能采样
    }

    public void OnServiceShutdown(IService service)
    {
        // Shutdown 前清理
    }
}

// 注册拦截器
GameServices.AddInterceptor(new ProfilingInterceptor());
```

六个拦截点，默认空实现——只需覆写关注的切面：

| 方法 | 时机 |
|------|------|
| `OnServiceRegistering` | `OnInit()` 前——可抛异常拒绝注册 |
| `OnServiceRegistered` | `OnInit()` 后、状态切换为 `Initialized` 后 |
| `OnServiceUnregistering` | `Shutdown()` 前 |
| `OnServiceUnregistered` | 从注册表移除后 |
| `OnServiceTick` | 每次 `Tick()` 调用前（仅 Update 路径） |
| `OnServiceShutdown` | `Shutdown()` 调用前 |

多个拦截器按 `Priority` 降序执行。`GameServices.Shutdown()` 时清空全部拦截器。

## 注意事项

- `GameServices` 仅允许主线程调用；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Post`/`Send` 切回主线程。
- `GetService<T>()` 与 `UnregisterService<T>()` 必须传入接口类型，传入具体类会抛出 `GameException`。
- `GetService<T>()` 未注册时直接抛出 `GameException`——无反射回退。
- `RegisterService<T>` 快速失败校验：服务必须实现所注册的接口；同一作用域内重复注册同一接口仅告警并返回已有实例。
- 编辑器下退出 Play 模式时 `GameApp` 会自动调用 `GameServices.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回主 README](../../README.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
