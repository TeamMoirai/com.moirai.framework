# Core 服务系统（@Service）

> 框架的服务化基座：以依赖注入容器管理所有子服务的构造、生命周期、轮询与作用域，并由 `GameApp`（MonoBehaviour）驱动。

`@Service` 是整个框架的服务基础设施。所有功能服务（资源、UI、音频、计时器等）均为继承 `ServiceBase` 的普通 C# 类，通过构造函数声明依赖，由 `ServiceContainer` 在构建期拓扑排序、构造注入并按依赖序初始化；非服务代码通过 `GameApp` 缓存属性（如 `GameApp.Audio`、`GameApp.Resource`、`GameApp.UI`）访问服务，或通过 `GameApp.Services`（`IServiceProvider`）进行非标准查找。服务支持 App/Scene/Gameplay 三级作用域，跨作用域按 Gameplay → Scene → App 链式查找，场景卸载时自动清理场景与玩法级服务。

## 核心特性

- 构造函数注入：纯 C# 服务通过构造函数参数声明依赖，编译期即可验证，容器在构建期自动解析
- 作用域容器：`ServiceContainer` 负责服务的构造、注入、初始化与逆序销毁；App ← Scene ← Gameplay 父链实现跨作用域遮蔽查找
- 三级作用域（`EServiceScopeKind.App` / `Scene` / `Gameplay`），跨作用域按 Gameplay > Scene > App 遮蔽查找
- 拓扑排序：从构造函数参数自动推断依赖，被依赖服务先创建、先初始化；循环依赖构建期抛出异常
- 生命周期接口按需实现：`IServiceTickable`、`IServiceFixedTickable`、`IServiceLateTickable`、`IServiceGizmoDrawable`
- `Priority` 优先级控制轮询顺序（高优先先轮询、后关闭）
- 异步初始化：实现 `IAsyncInitService` 的服务在 `ServiceContainer.BuildAsync()` 中按拓扑序异步初始化
- 服务事件：`ServiceRegistered`/`ServiceUnregistered` 事件支持热替换通知
- 迭代安全：轮询期间的注册/注销延迟到本轮结束后统一应用
- 主线程亲和守卫：编辑器与开发构建下断言调用线程，发布版零开销
- 生命周期状态机：每个服务跟踪 `EServiceState`（Created → Initialized → ShuttingDown → Disposed），关闭幂等
- Tick 异常隔离：单个服务在 `Tick` 中抛异常不会中断同帧其他服务

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `IService` | 服务核心契约：`Priority`、`Scope`、`State`、`OnInit()`、`Shutdown()` |
| `ServiceBase` | 纯 C# 服务抽象基类，依赖通过构造函数参数声明，由容器注入 |
| `IServiceProvider` | 服务访问统一入口：`GetRequiredService<T>()` / `GetService<T>()` / `TryGetService<T>()` |
| `ServiceCollection` | 服务注册集合（组合根中创建），`Register<TInterface, TImpl>(scope)` 流式注册 |
| `ServiceContainer` | 作用域容器：`BuildAsync()` 执行拓扑排序 → 构造注入 → OnInit → OnInitAsync |
| `GameServices` | 静态门面：容器管理（`BuildContainer`/`ShutdownContainer`）、轮询驱动、拦截器 |
| `EServiceScopeKind` | 服务作用域枚举：`App`（全局）、`Scene`（场景卸载时重置）、`Gameplay`（单局玩法） |
| `EServiceState` | 服务生命周期状态：`Created`、`Initialized`、`ShuttingDown`、`Disposed` |
| `IServiceTickable` / `IServiceFixedTickable` / `IServiceLateTickable` | 轮询接口，方法签名 `Tick(float elapseSeconds, float realElapseSeconds)` 等 |
| `IServiceGizmoDrawable` | 编辑器 Gizmos 绘制接口 `OnDrawGizmos()` |
| `IAsyncInitService` | 异步初始化接口，实现 `OnInitAsync()` 的服务在 `BuildAsync()` 中统一驱动 |
| `ServiceMono<TScope>` | MonoBehaviour 服务基类，由容器通过 `AddComponent` 创建并调用 `Inject(IServiceProvider)` 注入依赖 |
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

// 3. 在组合根注册（AppSettings.BuildServiceCollection 或场景/玩法初始化代码）
var collection = new ServiceCollection();
collection.Register<IMyService, MyService>(EServiceScopeKind.Gameplay);
GameServices.BuildContainer(EServiceScopeKind.Gameplay, collection, parent: GameServices.SceneContainer);

// 4. 构建容器（创建实例 → 构造注入 → OnInit → OnInitAsync）
await GameServices.GameplayContainer.BuildAsync();

// 5. 关闭容器——服务按逆拓扑序（依赖方先）关闭
GameServices.ShutdownContainer(EServiceScopeKind.Gameplay);
```

## 进阶用法

### 生命周期与作用域

- `ServiceContainer.BuildAsync()` 按拓扑序执行：创建实例 → 注册到作用域 → `OnInit()` → `OnInitAsync()`；被依赖服务先于依赖方初始化。
- `GameServices.Shutdown()` 按 Gameplay → Scene → App 逆序关闭全部容器；`GameServices.ShutdownContainer(scope)` 只关闭指定作用域。
- `GameApp` 监听 `SceneManager.sceneUnloaded`，场景卸载时自动关闭 `Scene` 与 `Gameplay` 作用域的容器。
- 同一接口可在不同作用域注册不同实现，`IServiceProvider` 查找顺序为 Gameplay > Scene > App（父链遮蔽），可用于战斗内临时替换全局实现。

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

    // IResourceService 必须已注册（本容器或父容器），且先于本服务创建
    public AudioService(IResourceService resource) => _resource = resource;

    public override void OnInit() { /* 依赖已就位，直接使用 _resource */ }
}
```

需要运行时延迟解析（如可选依赖）时，注入 `IServiceProvider` 本身：

```csharp
public class BattleService : ServiceBase
{
    private readonly IServiceProvider _provider;

    public BattleService(IServiceProvider provider) => _provider = provider;

    public override void OnInit()
    {
        // 可选依赖——未注册返回 null，不抛异常
        var debugger = _provider.GetService<IDebuggerService>();
    }
}
```

### 多契约注册 [MULTI-CONTRACT REGISTRATION]

单个实例可注册在多个接口下，通过 Fluent API `.As<TExtraContract>()` 声明额外契约。所有契约共享同一实例，拓扑排序也通过额外契约类型识别依赖：

```csharp
// AudioService 同时实现 IAudioService 和 IAudioLoader
collection.Register<IAudioService, AudioService>(EServiceScopeKind.App)
    .As<IAudioLoader>(); // 同一实例可通过两个接口解析

// 任意依赖 IAudioLoader 的服务也能被正确拓扑排序
public class AssetLoader : ServiceBase
{
    public AssetLoader(IAudioLoader audioLoader) { ... } // 解析到同一 AudioService 实例
}
```

### 组合根与内置服务注册

内置服务在 `AppSettings`（组合根）中通过 `ServiceCollection` 声明注册，实现类型可在 Inspector 中替换（如替换 `ITimerService` 的实现类）。`GameApp.Awake` 中调用 `AppContainer.BuildAsync()` 完成实际构建——依赖顺序由拓扑排序保证，与注册顺序无关。

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

菜单 `Window/Moirai/Service System` 打开服务系统窗口，可查看已注册服务的接口、实现、作用域、优先级与生命周期接口实现情况（数据来自 `GameServices.GetDiagnosticInfo()`），以及各作用域容器的活跃状态。

### 异步初始化 [ASYNC INIT]

实现 `IAsyncInitService` 接口的服务在所有服务同步初始化完成后按拓扑序异步初始化（如预加载资源、读取配置）：

```csharp
public class MyResourceService : ServiceBase, IMyResourceService, IAsyncInitService
{
    public override void OnInit()
    {
        // 同步快速设置（BuildAsync 阶段调用）
    }

    public async UniTask OnInitAsync()
    {
        // 异步加载（全部 OnInit 完成后按拓扑序调用）
        await LoadCatalogAsync();
    }
}

// GameApp.Awake 中自动调用：
// await GameServices.AppContainer.BuildAsync();
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

继承 `ServiceMono<TScope>` 并覆写 `Inject` 声明依赖。容器通过 `AddComponent` 创建实例并调用 `Inject(IServiceProvider)`：

```csharp
public class MyMonoService : ServiceMono<AppScope>, IMyService
{
    private IResourceService _resource;

    protected internal override void Inject(IServiceProvider provider)
    {
        // MonoBehaviour 无法使用构造函数——通过 Inject 获取依赖
        _resource = provider.GetRequiredService<IResourceService>();
    }

    public override void OnInit() { }
    public override void Shutdown() { }
    // AppScope 容器创建时自动 DontDestroyOnLoad；SceneScope/GameplayScope 随场景卸载自然销毁
}

// 注册（依赖需通过 DependsOn 显式声明以参与拓扑排序）
collection.RegisterMono<IMyService, MyMonoService>(EServiceScopeKind.App)
    .DependsOn<IResourceService>();
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

五个拦截点，默认空实现——只需覆写关注的切面：

| 方法 | 时机 |
|------|------|
| `OnServiceRegistering` | `OnInit()` 前——可抛异常拒绝注册 |
| `OnServiceRegistered` | `OnInit()` 后、状态切换为 `Initialized` 后 |
| `OnServiceUnregistered` | `Shutdown()` 已调用并从注册表移除后 |
| `OnServiceTick` | 每次 `Tick()` 调用前（仅 Update 路径） |
| `OnServiceShutdown` | `Shutdown()` 调用前 |

多个拦截器按 `Priority` 降序执行。`GameServices.Shutdown()` 时清空全部拦截器。

## 注意事项

- `GameServices` 与 `IServiceProvider` 仅允许主线程调用；后台线程/异步回调请通过 `MainThreadDispatcher` 的 `Post`/`Send` 切回主线程。
- 服务类应优先使用构造函数注入而非 `GameApp` 缓存属性——后者面向非服务代码（MonoBehaviour、UI 脚本等）。
- `GetRequiredService<T>()` 未注册时抛出 `GameException`；`GetService<T>()` 返回 null；`TryGetService<T>()` 返回 bool。
- 重复调用 `ServiceContainer.BuildAsync()` 抛出 `GameException`；同一接口在同一作用域重复注册仅告警并保留首个实例。
- 循环依赖在 `BuildAsync()` 拓扑排序阶段即被检测并抛出异常。
- 编辑器下退出 Play 模式时 `GameApp` 会自动调用 `GameServices.Shutdown()`，兼容跳过域重载的 Enter Play Mode Options 设置。

---
[« 返回主 README](../../README.md) · [Timer](Timer.md) · [UpdateDriver](UpdateDriver.md)
