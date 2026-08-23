# Core 服务系统（@Service）

> 框架的服务化基座：以统一服务世界（`ServiceWorld`）管理所有子服务的构造、生命周期、轮询与作用域，并由 `GameApp`（MonoBehaviour）驱动。

`@Service` 是整个框架的服务基础设施。所有功能服务（资源、UI、音频、计时器等）均为继承 `ServiceBase` 的普通 C# 类，通过构造函数声明依赖，由 `ServiceWorld` 在构建期拓扑排序、构造注入并按依赖序初始化；非服务代码通过 `GameApp` 缓存属性（如 `GameApp.Audio`、`GameApp.Resource`、`GameApp.UI`）访问服务，或通过 `GameApp.Services`（`IServiceProvider`）进行非标准查找。服务支持 App/Scene/Gameplay 三级作用域，跨作用域通过 `ContractBindings` 值类型 struct 实现 O(1) 查找（Gameplay > Scene > App 优先级），场景卸载时自动清理场景与玩法级服务。

## 核心特性

- **统一服务世界**：`ServiceWorld` 持有 3-slot 固定数组（App/Scene/Gameplay），通过 `ContractBindings` 值类型 struct 实现 O(1) 跨作用域查找，无需父链遍历
- **构造函数注入**：纯 C# 服务通过构造函数参数声明依赖，编译期即可验证，容器在构建期自动解析
- **拓扑排序**：Kahn 算法从构造函数参数自动推断依赖，被依赖服务先创建、先初始化；循环依赖构建期抛出异常
- **三级作用域**（`EServiceScopeKind.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 优先级查找
- **生命周期接口按需实现**：`IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable`、`IServiceGizmoDrawable`
- **`Priority` 优先级**控制轮询顺序（高优先先轮询、后关闭）
- **异步初始化**：实现 `IAsyncInitService` 的服务在 `BuildAsync()` 中按拓扑序异步初始化
- **服务事件**：`onServiceRegistered`/`onServiceUnregistered` 事件支持热替换通知
- **迭代安全**：轮询期间的注册操作延迟到本轮结束后统一应用；轮询中请求的作用域销毁也延迟执行
- **Tick 异常隔离**：单个服务在 `Tick` 中抛异常不会中断同帧其他服务
- **主线程亲和守卫**：编辑器与开发构建下断言调用线程，发布版零开销
- **生命周期状态机**：每个服务跟踪 `EServiceState`（Created → Initialized → ShuttingDown → Disposed），关闭幂等
- **MonoBehaviour Tick 约束**：MonoBehaviour 服务不可实现 `IServiceTickable` 等 Tick 接口，应使用 Unity 自身的 Update/FixedUpdate/LateUpdate
- **服务基类内置查找**：`ServiceBase` / `ServiceMonoBase` 提供 `Require<T>()`、`TryGet<T>()`、`RequireApp<T>()`、`RequireScene<T>()`、`RequireGameplay<T>()` 等 protected 方法，无需注入 `IServiceProvider` 即可运行时查找依赖

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `IService` | 服务核心契约：`Priority`、`Scope`、`OnInit()`、`Shutdown()` |
| `ServiceBase` | 纯 C# 服务抽象基类，依赖通过构造函数参数声明，由容器注入；内置 `Require<T>()` / `TryGet<T>()` / `RequireApp<T>()` 等运行时查找方法 |
| `ServiceMonoBase` | MonoBehaviour 服务基类，由容器通过 `AddComponent` 创建并调用 `Inject(IServiceProvider)` 注入依赖；同样内置运行时查找方法 |
| `IServiceProvider` | 服务访问统一入口：`GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>()` / `GetRequiredServiceInScope<T>(scope)` / `TryGetServiceInScope<T>(scope)` |
| `ServiceWorld` | 统一服务世界：`BuildAsync(scope, collection)` 执行拓扑排序 → 构造注入 → OnInit → OnInitAsync；实现 `IServiceProvider`；持有 `ContractBindings` 值类型 struct 实现 O(1) 跨作用域查找 |
| `ServiceScope` | 单作用域注册表、轮询列表与迭代安全机制；注册/注销时同步 `ServiceWorld` 的 `ContractBindings` |
| `ServiceCollection` | 服务注册集合（组合根中创建），`Register<TInterface, TImpl>(scope)` 流式注册 |
| `GameServices` | 静态门面：作用域管理（`BuildAsync`/`ShutdownContainer`/`HasApp`/`HasScene`/`HasGameplay`）、轮询驱动、拦截器 |
| `EServiceScopeKind` | 服务作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `EServiceState` | 服务生命周期状态：`Created`、`Initialized`、`ShuttingDown`、`Disposed`（`ServiceBase.State` / `ServiceMonoBase.State` 属性） |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | 轮询接口，方法签名 `Tick(float elapseSeconds, float realElapseSeconds)` 等（MonoBehaviour 服务不可实现） |
| `IServiceGizmoDrawable` | 编辑器 Gizmos 绘制接口 `OnDrawGizmos()` |
| `IAsyncInitService` | 异步初始化接口，实现 `OnInitAsync()` 的服务在 `BuildAsync()` 中统一驱动 |
| `ServiceMono<TScope>` | 泛型 MonoBehaviour 服务基类，通过 `TScope` 编译期确定作用域 |
| `GameApp` | MonoBehaviour 入口（`[DefaultExecutionOrder(-1000)]`），仅驱动生命周期与轮询，服务访问通过 `GameApp` 缓存属性（如 `GameApp.Audio`、`GameApp.UI`） |
| `GameAppMessageEvent` / `EMessageEventType` | 命名空间 `Moirai.Atropos.Events`，框架级池化事件（对焦/失焦/退出、SDK 回调） |

## 快速上手

```csharp
// 1. 非服务代码通过 GameApp 缓存属性获取服务
ITimerService timer = GameApp.Timer;
IResourceService resource = GameApp.Resource; // 未注册返回 null

// 2. 定义自定义服务——依赖通过构造函数声明
public interface IMyService { void DoSomething(); }

public class MyService : ServiceBase, IMyService, IServiceTickable
{
    private readonly ITimerService _timer; // 构造注入，容器自动解析

    public MyService(ITimerService timer) => _timer = timer;

    public override int Priority => 10;              // 高优先级先轮询
    public override EServiceScopeKind Scope => EServiceScopeKind.Gameplay;

    public override void OnInit() { }
    public override void Shutdown() { }
    public void DoSomething() { }
    public void Tick(float elapseSeconds, float realElapseSeconds) { }
}

// 3. 在组合根注册并构建
var collection = new ServiceCollection();
collection.Register<IMyService, MyService>(EServiceScopeKind.Gameplay);
await GameServices.BuildAsync(EServiceScopeKind.Gameplay, collection);

// 4. 关闭——服务按逆拓扑序（依赖方先）关闭
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## 进阶用法

### 生命周期与作用域

- `GameServices.BuildAsync(scope, collection)` 按拓扑序执行：创建实例 → 注册到作用域 → `OnInit()` → `OnInitAsync()`；被依赖服务先于依赖方初始化。
- `GameServices.Shutdown()` 按 Gameplay → Scene → App 逆序关闭全部作用域；`GameServices.ShutdownContainer(scope)` 只关闭指定作用域。
- `GameApp` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域。
- 同一接口可在不同作用域注册不同实现，`IServiceProvider` 查找顺序为 Gameplay > Scene > App（`ContractBindings.TryGetBest()`），可用于战斗内临时替换全局实现。
- 重复构建同一作用域抛出 `GameException`；需先 `ShutdownContainer` 再重新构建。

### 生命周期状态机

每个服务通过 `ServiceBase.State`（`EServiceState`）跟踪生命周期状态：

| 状态 | 说明 |
|------|------|
| `Created` | 实例已创建并注册，尚未初始化 |
| `Initialized` | `OnInit()` 已调用，服务运行中 |
| `ShuttingDown` | 正在调用 `Shutdown()` |
| `Disposed` | 服务已完全关闭并移除 |

关闭幂等：对已销毁的服务再次调用关闭不会重复执行。

### 依赖注入

纯 C# 服务通过构造函数声明依赖，容器在构建期解析并注入。依赖未注册时构建失败并抛出带描述信息的 `GameException`：

```csharp
public class AudioService : ServiceBase, IAudioService
{
    private readonly IResourceService _resource;

    // IResourceService 必须已注册（本作用域或跨作用域），且先于本服务创建
    public AudioService(IResourceService resource) => _resource = resource;

    public override void OnInit() { /* 依赖已就位，直接使用 _resource */ }
}
```

需要运行时延迟解析（如可选依赖）时，有两种方式：

```csharp
// 方式一：注入 IServiceProvider 本身（适用于外部程序集或不继承 ServiceBase 的类）
public class BattleService : ServiceBase
{
    private readonly IServiceProvider _provider;

    public BattleService(IServiceProvider provider) => _provider = provider;

    public override void OnInit()
    {
        var debugger = _provider.GetService<IDebuggerService>(); // 可选依赖，未注册返回 null
    }
}

// 方式二：使用 ServiceBase 内置的 Require<T>() / TryGet<T>()（无需注入 IServiceProvider）
public class BattleService : ServiceBase
{
    public override void OnInit()
    {
        if (TryGet(out IDebuggerService debugger)) // 可选依赖
        {
            debugger.Enable();
        }
    }
}
```

### 服务基类内置查找方法

`ServiceBase` 和 `ServiceMonoBase` 提供以下 `protected` 方法，无需注入 `IServiceProvider` 即可运行时查找依赖：

| 方法 | 说明 |
|------|------|
| `Require<T>()` | 跨作用域查找服务，未找到抛 `GameException`（Gameplay > Scene > App 优先级） |
| `TryGet<T>(out T)` | 跨作用域尝试查找服务，返回 bool |
| `RequireApp<T>()` | 仅在 App 作用域中查找，未找到抛 `GameException` |
| `RequireScene<T>()` | 仅在 Scene 作用域中查找，未找到抛 `GameException` |
| `RequireGameplay<T>()` | 仅在 Gameplay 作用域中查找，未找到抛 `GameException` |

### 多契约注册 [MULTI-CONTRACT REGISTRATION]

单个实例可注册在多个接口下，通过 Fluent API `.As<TExtraContract>()` 声明额外契约：

```csharp
collection.Register<IAudioService, AudioService>(EServiceScopeKind.App)
    .As<IAudioLoader>(); // 同一实例可通过两个接口解析
```

### 组合根与内置服务注册

内置服务在 `AppSettings`（组合根）中通过 `ServiceCollection` 声明注册，实现类型可在 Inspector 中替换。`GameAppSettings` 调用 `await GameServices.BuildAsync(EServiceScopeKind.App, collection)` 完成实际构建——依赖顺序由拓扑排序保证，与注册顺序无关。

### 异步初始化 [ASYNC INIT]

```csharp
public class MyResourceService : ServiceBase, IMyResourceService, IAsyncInitService
{
    public override void OnInit() { /* 同步快速设置 */ }

    public async UniTask OnInitAsync()
    {
        await LoadCatalogAsync(); // 全部 OnInit 完成后按拓扑序调用
    }
}

// GameAppSettings 中自动调用：
// await GameServices.BuildAsync(EServiceScopeKind.App, collection);
// await ProcedureSettings.StartProcedure();
```

### 服务事件 [SERVICE EVENTS]

```csharp
GameServices.onServiceRegistered += (service, interfaceType, scope) =>
{
    Debug.Log($"Service registered: {interfaceType.Name} in {scope} scope");
};

GameServices.onServiceUnregistered += (service) =>
{
    Debug.Log($"Service unregistered: {service.GetType().Name}");
};
```

### MonoBehaviour 服务 [MONO SERVICE]

继承 `ServiceMono<TScope>` 并覆写 `Inject` 声明依赖。容器通过 `AddComponent` 创建实例并调用 `Inject(IServiceProvider)`：

```csharp
public class MyMonoService : ServiceMono<AppScope>, IMyService
{
    private IResourceService _resource;

    protected internal override void Inject(IServiceProvider provider)
    {
        _resource = provider.GetRequiredService<IResourceService>();
    }

    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope 自动 DontDestroyOnLoad；SceneScope/GameplayScope 随场景卸载自然销毁
}

// 注册（依赖需通过 DependsOn 显式声明以参与拓扑排序）
collection.RegisterMono<IMyService, MyMonoService>(EServiceScopeKind.App)
    .DependsOn<IResourceService>();
```

> **注意**：MonoBehaviour 服务不可实现 `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable`。注册时如果检测到 MonoBehaviour 服务实现了这些接口，会抛出 `GameException`。请使用 Unity 自身的 `Update()` / `FixedUpdate()` / `LateUpdate()`。

### 服务拦截器 [SERVICE INTERCEPTORS (AOP)]

```csharp
public class ProfilingInterceptor : IServiceInterceptor
{
    public int Priority => 100;

    public void OnServiceTick(IService service, float elapseSeconds, float realElapseSeconds)
    {
        // 每次 Tick 前性能采样
    }

    public void OnServiceShutdown(IService service)
    {
        // Shutdown 前清理
    }
}

GameServices.AddInterceptor(new ProfilingInterceptor());
```

五个拦截点，默认空实现：

| 方法 | 时机 |
|------|------|
| `OnServiceRegistering` | `OnInit()` 前——可抛异常拒绝注册 |
| `OnServiceRegistered` | `OnInit()` 后、状态切换为 `Initialized` 后 |
| `OnServiceUnregistered` | `Shutdown()` 已调用并从注册表移除后 |
| `OnServiceTick` | 每次 `Tick()` 调用前（仅 Update 路径） |
| `OnServiceShutdown` | `Shutdown()` 调用前 |

多个拦截器按 `Priority` 降序执行。`GameServices.Shutdown()` 时清空全部拦截器。

### 运行时调试

在运行时调试器（DebuggerComp）的 Service System 窗口中可查看已注册服务的接口、实现、作用域、优先级与 Tick 接口实现情况（数据来自 `GameServices.GetDiagnosticInfo()`），以及各作用域的活跃状态（`HasApp` / `HasScene` / `HasGameplay`）。

## 注意事项

- `GameServices` 与 `IServiceProvider` 仅允许主线程调用；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Post`/`Send` 切回主线程。
- 服务类应优先使用构造函数注入而非 `GameApp` 缓存属性——后者面向非服务代码（MonoBehaviour、UI 脚本等）。
- `GetRequiredService<T>()` 未注册时抛出 `GameException`；`GetService<T>()` 返回 null；`TryGetService<T>()` 返回 bool。
- 重复构建同一作用域抛出 `GameException`；需先 `ShutdownContainer` 再重新构建。
- 同一接口在同一作用域重复注册仅告警并保留首个实例。
- 循环依赖在 `BuildAsync()` 拓扑排序阶段即被检测并抛出异常。
- MonoBehaviour 服务不可实现 `IServiceTickable` 等 Tick 接口——使用 Unity 自身的 Update 生命周期。
- 编辑器下退出 Play 模式时 `GameApp` 会自动调用 `GameServices.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回主 README](../../README.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
