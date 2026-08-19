# Procedure 流程模块

> 基于 FSM 的游戏流程管理：把启动、热更、预加载等阶段建模为一个个可切换的流程状态。

Procedure 模块（`ProcedureModule`）构建在 [FSM](FSM.md) 有限状态机之上：内部通过 `IFSMModule.CreateFSM` 创建一台持有者为 `IProcedureModule` 的状态机，每个游戏阶段（启动、检查更新、下载资源、加载程序集、预加载等）都是一个 `ProcedureBase` 状态。可用流程与入口流程由 `ProcedureSettings` 配置，`GameModule.Awake` 时自动反射实例化并启动，无需手写引导代码。通过 `GameModule.Procedure`（`IProcedureModule`）访问。

## 核心特性

- 基于 FSM：流程即状态，复用 `FSMState<T>` 的完整生命周期与 `ChangeState` 切换机制
- 配置化启动：`ProcedureSettings` 记录可用流程类型与入口流程，`GameModule.Awake` 自动调用 `ProcedureSettings.StartProcedure()` 完成实例化与启动
- `[ProcedureLauncher]` 标记：只有标记该 Attribute 的 `ProcedureBase` 子类才会被 `ProcedureSettings` 扫描收录（编辑器 Reset 时自动扫描，默认以名称含 `ProcedureLaunch` 的流程作为入口）
- 双套切换入口：流程内部可用基类 `ChangeState<T>(procedureOwner)`，外部（如热更层）可用 `GameModule.Procedure.ChangeState<T>()`
- 支持运行时重建：`RestartProcedure` 销毁旧状态机后按新流程列表重建并以第一个流程启动

## 核心类型

命名空间：`Moirai.Atropos.Procedure`

| 类/接口 | 说明 |
|---------|------|
| `IProcedureModule` | 流程管理器接口：`Initialize` / `StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` / `RestartProcedure` 及 `CurrentProcedure`、`CurrentProcedureTime`；经 `GameModule.Procedure` 访问 |
| `ProcedureModule` | 模块实现（`Module, IProcedureModule`，`Priority = -2`），持有内部 `IFSM<IProcedureModule>` 状态机 |
| `ProcedureBase` | 流程基类，继承 `FSMState<IProcedureModule>`，提供 `OnInit / OnEnter / OnUpdate / OnExit / OnDestroy` 生命周期 |
| `ProcedureSettings` | 框架设置（面板名「流程设置」）：序列化可用流程类型名列表与入口流程类型名，静态 `StartProcedure()` 负责反射建流 |
| `ProcedureLauncherAttribute` | 类标记 Attribute，标记可被流程系统收录的 `ProcedureBase` 子类 |
| `ProcedureEvents` / `IProcedureEvent` | 流程相关事件标记接口（`public interface IProcedureEvent { }`），供业务扩展流程事件 |

依赖的 FSM 类型（命名空间 `Moirai.Atropos.FSM`）：`FSMState<T>`（状态基类与 `ChangeState` 切换）、`IFSM<T>` / `IFSMModule`（状态机与状态机管理器接口，后者经 `GameModule.FSM` 访问）。

## 快速上手

定义一个流程并标记收录（来自 `Templates~/@Requirements/Scripts/GameBase/Procedure` 的真实示例）：

```csharp
using Moirai.Atropos;
using Moirai.Atropos.FSM;
using Moirai.Atropos.Procedure;

// 流程基类：标记 [ProcedureLauncher] 才会出现在 ProcedureSettings 的可用列表
[ProcedureLauncher]
public abstract class ProcedurePremainBase : ProcedureBase
{
    public abstract bool UseNativeDialog { get; }

    protected readonly IResourceModule _resourceModule = ModuleSystem.GetModule<IResourceModule>();
}

// 具体流程
public class ProcedureLaunch : ProcedurePremainBase
{
    public override bool UseNativeDialog => true;

    protected override void OnEnter(IFSM<IProcedureModule> procedureOwner)
    {
        base.OnEnter(procedureOwner);
        // 启动阶段初始化（模板中此处初始化热更 UI：LauncherMgr.Initialize()）
    }

    protected override void OnUpdate(IFSM<IProcedureModule> procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

        // 流程内部切换到下一阶段（FSMState<T> 提供的 protected 方法）
        ChangeState<ProcedureInitPackage>(procedureOwner);
    }
}
```

在流程外部查询与切换：

```csharp
// 当前流程与停留时间
ProcedureBase current = GameModule.Procedure.CurrentProcedure;
float seconds = GameModule.Procedure.CurrentProcedureTime;

// 查询/获取流程实例
bool has = GameModule.Procedure.HasProcedure<ProcedureSplash>();
ProcedureBase proc = GameModule.Procedure.GetProcedure<ProcedureSplash>();

// 外部强制切换（例如热更代码中的跳转逻辑）
GameModule.Procedure.ChangeState<ProcedurePreload>();
```

## 配置与扩展

### 与 FSM 的关系

`ProcedureModule.Initialize(IFSMModule fsmModule, params ProcedureBase[] procedures)` 内部调用 `fsmModule.CreateFSM(this, procedures)` 创建唯一一台流程状态机；`StartProcedure` / `HasProcedure` / `ChangeState` / `GetProcedure` 分别转调状态机的 `Start` / `HasState` / `ChangeState` / `GetState`。流程生命周期即状态生命周期：

| 流程回调 | 签名 | 说明 |
|----------|------|------|
| `OnInit` | `(IFSM<IProcedureModule>)` | 状态机创建后调用一次 |
| `OnEnter` | `(IFSM<IProcedureModule>)` | 进入流程时调用 |
| `OnUpdate` | `(IFSM<IProcedureModule>, float elapseSeconds, float realElapseSeconds)` | 每帧轮询（逻辑/真实流逝时间） |
| `OnExit` | `(IFSM<IProcedureModule>, bool isShutdown)` | 离开流程时调用（含状态机销毁标记） |
| `OnDestroy` | `(IFSM<IProcedureModule>)` | 状态销毁时调用 |

### 启动链参考

`Templates~/@Requirements/Scripts/GameBase/Procedure` 提供了完整的启动流程模板，典型链路为：

```
ProcedureLaunch -> ProcedureSplash -> ProcedureInitPackage -> ProcedureInitResources
-> ProcedureCreateDownloader -> ProcedureDownloadFile -> ProcedureDownloadOver
-> ProcedureClearCache -> ProcedureLoadAssembly -> ProcedurePreload -> ProcedurePrepare4Entrance
```

其中 `ProcedureInitResources` 演示了与 Resource 模块的配合：调用 `_resourceModule.RequestPackageVersionAsync()` 获取远端清单版本、`UpdatePackageManifestAsync(packageVersion)` 更新清单，再按播放模式（`EPlayMode.HostPlayMode` / `WebPlayMode`、是否 `UpdatableWhilePlaying`）决定走下载流程还是直接预加载。

### 重启流程

```csharp
// 销毁当前状态机，用新流程列表重建，并以列表第一个流程启动（返回是否成功）
bool ok = GameModule.Procedure.RestartProcedure(
    new ProcedureLaunch(),
    new ProcedureInitPackage(),
    new ProcedurePreload());
```

## 注意事项

- 使用流程前必须先 `Initialize`，否则 `StartProcedure` / `ChangeState` 等会抛出 `GameException("You must initialize procedure first.")`；常规项目由 `ProcedureSettings.StartProcedure()` 在 `GameModule.Awake` 自动完成。
- 入口流程在编辑器侧由 Reset 逻辑选取「名称包含 `ProcedureLaunch` 的第一个类型」，重命名入口流程类时需在 `ProcedureSettings` 面板 Reset 刷新。
- 流程类需要无参构造（`ProcedureSettings` 通过 `Activator.CreateInstance` 反射实例化），不要在流程类中做构造器注入。
- 流程实例由状态机持有并长期存活，不要在其中缓存短生命周期对象；需要每帧逻辑写在 `OnUpdate`，耗时异步操作建议在 `OnEnter` 启动、在 `OnUpdate` 轮询完成标记（参考模板 `_initResourcesComplete` 的写法）。
- `ProcedureBase.OnUpdate` 含两个时间参数（`elapseSeconds` / `realElapseSeconds`），重写时注意保持签名一致。

---
[« 返回主 README](../../README.md) · [FSM](FSM.md) · [Resource](Resource.md)
