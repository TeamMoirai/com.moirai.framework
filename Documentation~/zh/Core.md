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
- **异步关闭**：实现 `IAsyncShutdownService` 的服务在 `ShutdownContainerAsync()` / `ShutdownAsync()` 中按逆拓扑序先异步关闭
- **AOT 安全的延迟解析**：`IServiceResolver<T>` 作为 `Func<T>` 的 AOT 优选替代，零 `MakeGenericMethod` 路径
- **运行时服务注册**：`GameServices.RegisterService<T>()` / `UnregisterService<T>()` 在已构建的作用域中动态增删单个服务
- **自注册 Mono 服务**：`SelfRegisteringMono<TScope>` 在 Awake 中自动注册、OnDestroy 中自动注销
- **作用域优先级常量**：`ServiceScopeOrder` 显式定义 App/Scene/Gameplay 排序优先级
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
| `IAsyncShutdownService` | 异步关闭接口，实现 `OnShutdownAsync()` 的服务在 `ShutdownContainerAsync()` 中按逆拓扑序异步关闭 |
| `IServiceResolver<T>` | AOT 安全的延迟服务解析器，替代 `Func<T>` 注入的 `MakeGenericMethod` 路径 |
| `ServiceMono<TScope>` | 泛型 MonoBehaviour 服务基类，通过 `TScope` 编译期确定作用域 |
| `SelfRegisteringMono<TScope>` | 自注册 MonoBehaviour 服务基类，Awake 自动注册、OnDestroy 自动注销 |
| `ServiceScopeOrder` | 作用域优先级常量（App=-10000, Scene=-5000, Gameplay=0） |
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

### AOT 安全的延迟解析 [AOT-SAFE LAZY RESOLUTION]

`Func<T>` 注入使用 `MakeGenericMethod`，IL2CPP 全量泛型共享下可用但存在风险。`IServiceResolver<T>` 提供零反射替代路径：

```csharp
public class BattleService : ServiceBase, IBattleService
{
    private readonly IServiceResolver<IStatsService> _statsResolver;

    // IServiceResolver<T> 构造参数：容器直接 new ServiceResolver<T>(this)——零反射
    public BattleService(IServiceResolver<IStatsService> statsResolver)
    {
        _statsResolver = statsResolver;
    }

    public override void OnInit()
    {
        // 延迟解析：拓扑建边保证目标此时已就绪
        var stats = _statsResolver.Resolve();
    }
}

// 注册与 Func<T> 完全一致
collection.Register<IBattleService, BattleService>(EServiceScopeKind.Gameplay);
collection.Register<IStatsService, StatsService>(EServiceScopeKind.Gameplay);
```

> `Func<T>` 注入保留向后兼容。新代码推荐使用 `IServiceResolver<T>`。

### 运行时服务注册 [RUNTIME SERVICE REGISTRATION]

在已构建的作用域中动态增删单个服务（Mod 系统、DLC 热加载等场景）：

```csharp
// 运行时注册——立即驱动 OnInit
var buffService = new BuffService();
GameServices.RegisterService(EServiceScopeKind.Gameplay, buffService as IBuffService);

// 运行时注销——立即驱动 Shutdown
GameServices.UnregisterService<IBuffService>(EServiceScopeKind.Gameplay);
```

> 迭代中（Tick）调用抛出 `GameException`——须在非 Tick 上下文调用。

### 自注册 MonoBehaviour 服务 [SELF-REGISTERING MONO SERVICE]

`SelfRegisteringMono<TScope>` 在 `Awake` 中自动注册到对应作用域，`OnDestroy` 中自动注销。适用于快速原型、Inspector 驱动配置：

```csharp
public class MyInspectorService : SelfRegisteringMono<AppScope>, IMyService
{
    [SerializeField] private int m_ConfigValue;

    public override void OnInit() { /* Awake 注册后自动调用 */ }
    public override void Shutdown() { /* OnDestroy 注销前自动调用 */ }
}

// 直接在 Inspector 中挂载即可——无需 ServiceCollection 注册
```

> 与 `ServiceMono<TScope>`（容器通过 `AddComponent` 创建）互斥——选择其一即可。

### 异步关闭 [ASYNC SHUTDOWN]

实现 `IAsyncShutdownService` 的服务在异步关闭管线中按逆拓扑序先执行 `OnShutdownAsync()`，再执行同步 `Shutdown()`：

```csharp
public class ResourceService : ServiceBase, IResourceService, IAsyncShutdownService
{
    public async UniTask OnShutdownAsync()
    {
        await UnloadAllAssetsAsync(); // 异步卸载资源
    }

    public override void Shutdown() { /* 同步清理 */ }
}

// 异步关闭单个作用域
await GameServices.ShutdownContainerAsync(EServiceScopeKind.Gameplay);

// 异步关闭全部作用域
await GameServices.ShutdownAsync();
```

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

---

## 处理器宿主（HandlerHost）

> 通过 `[HandlerHost]` 特性 + 源生成器，为静态工具类自动生成线程安全的 Handler 属性与懒加载机制。

框架的 7 个工具门面（`LogUtility`、`SettingUtility`、`VersionUtility`、`JsonUtility`、`ObjectUtility`、`StringUtility`、`TweenUtility`）均采用统一的处理器宿主模式：

- **`[HandlerHost(typeof(XxxHandler))]`** 标记 `static partial class`，源生成器自动生成 `Handler` 属性（`volatile` + `Interlocked` 线程安全 get/set）
- **`FrameworkHandler`** 是所有处理器抽象基类的统一基类，提供 `Internal_Init()` / `Internal_Shutdown()` 幂等生命周期和 `OnInit()` / `OnShutdown()` 虚方法回调
- 用户在 partial 类中提供 `private static XxxHandler CreateDefaultHandler()` 工厂方法，首次访问 `Handler` 时自动调用
- 未提供 `CreateDefaultHandler` 时，编译器报 **MIRAI001** 警告（IDE 提供快速修复生成空方法）；`Handler.get` 在未显式设置时抛出 `InvalidOperationException`（运行时托底）
- `Handler` 赋值 `null` 抛出 `ArgumentNullException`（fail-fast）
- `s_Handler` 字段为 `private`，partial 同类可直接访问

### 使用方式

```csharp
[HandlerHost(typeof(LogHandler))]
public static partial class LogUtility
{
    private static LogHandler CreateDefaultHandler()
    {
#if ZLOGGER_INSTALLED
        return new ZLoggerHandler();
#else
        return new DefaultLogHandler();
#endif
    }

    // ... facade 方法通过 Handler 调用
    public static void Info(string msg) => Handler.Log(/* ... */);
}
```

### 源生成器输出

源生成器为每个标记 `[HandlerHost]` 的类生成 `{ClassName}.g.cs`，包含：

| 成员 | 说明 |
|------|------|
| `s_Handler` | `private static volatile` 处理器字段 |
| `s_DefaultFactory` | `private static Func<T>` = `CreateDefaultHandler`（方法存在时生成） |
| `Handler` | `public static` 属性：get 懒加载（Interlocked），set 替换并关闭旧处理器 |
| `Handler.set` | 初始化新处理器 → `Interlocked.Exchange` → 旧处理器 `Internal_Shutdown()` |

### 处理器继承体系

```
FrameworkHandler (abstract)
├── OnInit() / OnShutdown()  — 虚方法回调
├── Internal_Init() / Internal_Shutdown()  — 幂等生命周期入口
├── LogHandler : FrameworkHandler
├── SettingHandler : FrameworkHandler
├── VersionHandler : FrameworkHandler
├── JsonHandler : FrameworkHandler
├── ObjectHandler : FrameworkHandler
├── StringHandler : FrameworkHandler
├── TweenHandler : FrameworkHandler  (重写 Internal_Init 注册 TweenManager)
└── InputHandler : FrameworkHandler
```
