# UI 服务

> 多后端栈式窗口管理框架（uGUI / UI Toolkit / 自定义后端），提供窗口生命周期、层级深度排序、模态遮挡、Widget 子控件与多分辨率适配能力。

UI 服务（`Moirai.Atropos.UI`）将界面抽象为纯 C# 类：`UIWindow` 是后端无关的窗口对象模型（持有显隐 / 深度 / 交互三份面板意图与生命周期），各渲染后端（「轨」）提供自己的窗口基类——uGUI 轨的业务窗口继承 `UGUIWindow`、UI Toolkit 轨继承 `UITKWindow`；`UIWidget` 为窗口内嵌控件。多支后端可在同一会话并存，共享同一条窗口栈：栈序、层级深度、可见性与模态遮挡不分轨。`UIService` 作为静态外观对外暴露全部 API。窗口面板通过资源服务（YooAsset）或 `Resources` 加载实例化，窗口类本身不挂 MonoBehaviour。通过 `UIService.Xxx()` 静态方法即可完成打开、关闭、隐藏、查询等全部操作。

## 架构（多后端轨道）

UI 服务按「轨道」组织渲染后端：每支后端的三件套自洽，主入口不认识任何具体后端。

- **`UIService`**：静态外观（`[AutoRegisterService]` + `[ServiceDependency(typeof(DebuggerService), typeof(ResourceService), typeof(TimerService), typeof(InputService))]`），承载通用 API——Type 形开窗分派、关闭/隐藏/查询、模态交互租约、安全区广播与生命周期；全部公共成员为静态方法/属性
- **后端轨道**（每支一组 partial + 驱动者 + 窗口基类）：
  - `Handler/UGUI/`：`UIService.UGUI.cs`（槽位、认领门、开窗腿与轨道自登记）+ `UGUIHandler`（UI 根绑定、摄像机、错误日志、CanvasScaler 安全区落点）+ `UGUIWindow`（uGUI 面板意图落地）
  - `Handler/UITK/`：`UIService.UITK.cs` + `UITKHandler` + `UITKWindow`（`UIDocument` 壳 + 窗口级 `PanelSettings`）；本轨文档壳挂在 uGUI 轨那枚 UI 根下
- **`UIWindowLedger`**：共享窗口栈、停放表与交互租约的持有者——各轨窗口并进同一条栈，关·隐·查询不分轨
- **`UITrack`**：轨道自述（认窗判据、有效性探针、Type 形开窗实现、关停档位）。各轨在自己的 partial 里静态自登记进门面目录，主文件只枚举目录做认轨分派、`IsValid` 聚合与按档位升序的关停收口——**加一轨 = 加一枚 partial 自登记，主文件零改动**
- **`UIServiceSettings`**：框架设置（菜单「UI设置」），`[SerializeReference]` 启用清单 `EnabledHandlers` 列哪几支就启用哪几支（可同时多支）；清单为空初始化当场报错，没有「没配就自动造一支」的退路
- 服务标记 `[AutoRegisterService]`，由组合根经生成的内置服务清单自动注册（App 作用域，`[ServiceDependency]` 拓扑序保证初始化先后），也可手动 `GameServices.RegisterService(EServiceScopeKind.App, new UIService())`

关停次序由各轨自报的档位表述：持有别轨面板挂靠的宿主根那一轨（uGUI）取 `UITrack.SHUTDOWN_ORDER_HOST` 最后收，其余取 `SHUTDOWN_ORDER_DEFAULT` 先收。

## 核心特性

- 多后端共存：uGUI 与 UI Toolkit 两支内建轨可在同一会话并存，共享同一条窗口栈；新增后端只需添加自己那一轨的 partial 文件，主入口零改动
- 窗口栈式管理：按 `EUILayer` 层级插入排序，同层窗口深度自动递增（`LAYER_DEEP = 2000`、`WINDOW_DEEP = 100`）
- 五级层级：`Bottom` / `UI` / `Popup` / `Tips` / `System`，其中 `UI`、`Popup`、`System` 为模态层级
- 模态档三态：`[Window(modal:)]` 取 `EUIModal`——`Inherit`（缺省，按层级结算）/ `Modal`（非模态层级强制模态）/ `NonModal`（模态层级强制非模态）；压栈压下层交互位、过渡占全局压制位、`CurrentModal` 查询三处判据都读窗口初始化时结算好的那一枚
- 完整生命周期：`OnCreate` → `OnRefresh` → `OnUpdate` → `OnClose` → `OnDestroy`；开/关过渡经 `IUITransition`（覆写 `UIWindow.Transition` 交回实现，缺位即瞬时——默认开窗无延迟、无输入锁，关闭即时停放）
- 窗口注册：窗口类必标 `[Window]`，由源生成器 `UIWindowCodegen` 在编译期登记进 `UIWindowRegistry`（描述符 + 编译期工厂），未标注的窗口类不可开
- 模态遮挡：模态窗口压栈后自动禁用下层窗口交互（`Interactable`），`IsBlockedByModal` 可查询遮挡
- 开窗结果契约：`ShowUIAwaitResult<T>` / `GetUIAwaitResult<T>` 交回 `UIOpenResult`，按 `EUIOpenStatus` 五档（`Opened` / `Failed` / `Missing` / `Timeout` / `Cancelled`）分明成败——装载失败的窗口当场回滚出栈，等待不再以 null 与超时混言成败
- 载荷双通道：带载荷窗口基类 `UGUIWindow<TArg>` / `UITKWindow<TArg>` 各持一枚强类型 `Payload`——编译期已知窗口类的**静态腿**按 `in TArg` 泛型直塞（struct 不装箱，一步都不经擦除），运行期才拿到 `Type` 的**动态腿**把载荷擦进 `UIPayload` 过手（引用型只存引用，值类型装箱一次）
- 在飞合并 last-wins：同一只窗装载在途时再开不重开发装载、不压第二只，载荷覆盖为最后一枚，`OnRefresh` 只见终载荷
- 全腿收 `CancellationToken`：`default` 零开销；撤销只在装载在途那一段有意义——void 腿静默回滚、等待腿原样上抛 `OperationCanceledException`、结果腿落 `Cancelled` 档，与 `Timeout` 分档可辨
- 最小导航：`NavigationDepth` 与 `TryCloseTopWindow()` 按**开启序**回答「最近开的是谁」（栈按层级排序答不出这一问），关顶走既有 `CanClose` 政策
- 生命周期钩子隔离：钩子抛出「隔离并继续」，全手工 try/catch 不包 lambda（每帧路径零分配），政策表见〈生命周期钩子隔离〉
- 全屏窗口优化：全屏窗口之下的窗口自动隐藏，减少渲染与更新开销
- 窗口缓存：`cacheTimeToDestroy` 一枚管三档——`0` = 不缓存（关闭即销毁，缺省即此）、正数 = 停放并在这么多秒后由账本移出停放表并销毁、负数 = 停放永久；停放窗再次打开直接复用实例，重新取用即取消计时
- Widget 子控件：窗口内嵌控件复用同一套生命周期，支持按节点 / 资源路径 / prefab 创建
- 多分辨率适配：安全区域（刘海屏）适配、`UIAdapter` 布局适配器（横向 / 纵向 / 环形 / 安全区）
- 编辑器代码生成：`GameObject/ScriptGenerator` 菜单自动生成 UI 绑定代码

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.UI.UIService` | UI 服务静态外观，通用 API 全在此：Type 形打开、关闭/隐藏/查询、模态租约、安全区广播；静态属性 `IsValid`（任一轨驱动者就位即真）、`CurrentModal`，轨专有查询 `UIRoot` / `UICamera`（只由 uGUI 轨回答，未启用答 `null`） |
| `Moirai.Atropos.UI.UIServiceHandler` | 后端驱动者抽象基类（继承 `FrameworkHandler`）：`UGUIHandler` / `UITKHandler` 的公共契约，含本轨认窗判据 `IsWindowOnOwnTrack` 与自述注册 `Internal_Register` |
| `Moirai.Atropos.UI.UGUIHandler` | uGUI 轨驱动者（`Handler/UGUI/`）：UI 根绑定、UI 摄像机、错误日志、CanvasScaler 安全区换算落点 |
| `Moirai.Atropos.UI.UITKHandler` | UI Toolkit 轨驱动者（`Handler/UITK/`）：本轨窗关停收口；面板本体在 `UITKWindow` 上 |
| `Moirai.Atropos.UI.UITrack` | 轨道自述：认窗判据、有效性探针、Type 形开窗实现、关停档位；各轨 partial 静态自登记进门面目录 |
| `Moirai.Atropos.UI.UIServiceSettings` | 框架设置：`EnabledHandlers` 启用清单（`[SerializeReference]`）决定初始化哪几支后端，可同时多支 |
| `Moirai.Atropos.UI.UIRootBinding` | UI 根绑定组件：挂在充当 UI 根的场景物体上，`SingletonMono` 先到先得登记，供 uGUI 轨 `UGUIHandler` 经 `TryGetInstance()` 取用（不自动创建；取代按名字查找） |
| `Moirai.Atropos.UI.UIBase` | UI 基类，定义生命周期虚方法与 Widget 创建 API |
| `Moirai.Atropos.UI.UIWindow` | 窗口对象模型基类（继承 `UIBase`）：可见性 / 深度 / 交互三份面板意图、生命周期与开/关过渡（`Transition`）；面板装载由轨基类实现，直接继承它开不出面板 |
| `Moirai.Atropos.UI.UGUIWindow` | uGUI 轨窗口基类（`Handler/UGUI/`）：把三份意图落到 GameObject / Canvas / GraphicRaycaster 面板上，**uGUI 业务窗口一律继承此类**（带载荷的那一类继承 `UGUIWindow<TArg>`） |
| `Moirai.Atropos.UI.UITKWindow` | UI Toolkit 轨窗口基类（`Handler/UITK/`）：`UIDocument` 壳与内容根装配、窗口级 `PanelSettings` 覆盖（开窗族比 uGUI 腿多出的那枚形参），UI Toolkit 业务窗口继承此类（带载荷的那一类继承 `UITKWindow<TArg>`） |
| `Moirai.Atropos.UI.UGUIWindow<TArg>` / `UITKWindow<TArg>` | 两轨带载荷窗口基类：每次开窗最多一枚强类型 DTO，`Payload` 读点即那一枚；静态腿经 `IUIPayloadSlot<TArg>` 泛型直塞，动态腿经 `UIPayload` 擦除后从同一枚槽取回 |
| `Moirai.Atropos.UI.UIPayload` | 动态腿唯一擦除载体（`readonly struct`）：`Empty` 与 `null` 引用同判，`From` / `To<T>` / `TryGet<T>`——失败面一律 `GameException` 且消息带期望类型名 |
| `Moirai.Atropos.UI.IUIPayloadSlot<TArg>` | 载荷槽的内部泛型桥（`internal`）：账本的泛型直塞通道经它按 `TArg` 把载荷落进窗口，不经过 `UIPayload` 擦除；两轨泛型基类实现它 |
| `UIService.onWindowShown` / `onWindowClosed` | 窗口开合回执（`public static event Action<UIWindow>`）：入栈/出栈各发一次，停放与销毁都发；订阅者自持生命周期，门面关停与归零门整批摘订阅 |
| `Moirai.Atropos.UI.UIWidget` | 窗口内嵌控件基类，继承 `UIBase` |
| `Moirai.Atropos.UI.WindowAttribute` | 窗口特性（必标），声明层级、资源地址、全屏、缓存等配置；由源生成器 `UIWindowCodegen` 编译期解析并登记进 `UIWindowRegistry` |
| `Moirai.Atropos.UI.EUILayer` | UI 层级枚举：`Bottom=0`、`UI=1`、`Popup=2`、`Tips=3`、`System=4` |
| `Moirai.Atropos.UI.EUIModal` | 模态档枚举：`Inherit=0`（按层级结算）、`Modal=1`、`NonModal=2`；`[Window(modal:)]` 的形参与 `WindowAttribute.Modal` 的存储档 |
| `Moirai.Atropos.UI.UIWindowRegistry` | 窗口注册表（`public static`）：类型句柄 → 描述符 + 编译期工厂，由各程序集生成的模块初始化器登记，之后只读；`TryGet` 为 `internal` |
| `Moirai.Atropos.UI.UIWindowDescriptor` | 窗口元数据描述符（`readonly struct`）：注册期一次解析好的 `[Window]` 全量取值，开窗时零反射直取 |
| `Moirai.Atropos.UI.IUITransition` | 开/关过渡契约：`Play(open, ct)` 播放并等走完、`Snap(open)` 把面板当场拨到终态；经 `UIWindow.Transition` 覆写交回，缺位即瞬时 |
| `Moirai.Atropos.UI.UIOpenResult` / `EUIOpenStatus` | 开窗/取窗终态（`readonly struct` + `byte` 枚举五档：`Opened` / `Failed` / `Missing` / `Timeout` / `Cancelled`）：`Window` 与 `Status` 成对交回，`Success` 与隐式布尔只答「就绪」一档 |
| `Moirai.Atropos.UI.UIServiceHelper` | 交互辅助：`IsInteractionBlockedByModal`、`IsUIObjectInteractable` |
| `Moirai.Atropos.UI.UIBindComponent` | Window/Widget 组件绑定 MonoBehaviour 基类 |
| `Moirai.Atropos.UI.ErrorLogger` | 运行时异常捕获器，异常时弹出 `LogUI` 窗口 |
| `Moirai.Atropos.UI.Adapter.AdapterBase` | 布局适配器抽象基类（`Moirai.Atropos.UI.Adapter` 命名空间） |

## 快速上手

定义一个窗口（窗口类必须有无参构造，即 `new()` 约束）：

```csharp
using Moirai.Atropos.UI;

// 层级 Popup、非全屏、关闭后停放（负数 = 永久停放；填正数即到期销毁）
[Window(EUILayer.Popup, fullScreen: false, cacheTimeToDestroy: -1f)]
public class MainWindow : UGUIWindow
{
    protected override void ScriptGenerator() { }   // 生成的绑定代码在此重写

    protected override void OnCreate() { /* 首次创建，绑定事件 */ }

    protected override void OnRefresh() { /* 打开或上层窗口关闭时刷新；带载荷的窗口在这一枚读 Payload */ }

    protected override void OnUpdate() { /* 每帧更新（仅可见窗口） */ }

    protected override void OnClose() { /* 关闭清理 */ }

    protected override void OnDestroy() { /* 实例销毁 */ }
}
```

打开与关闭窗口：

```csharp
// 同步打开（WebGL 平台自动转为异步）
UIService.ShowUI<MainWindow>();

// 异步打开（无载荷腿）
UIService.ShowUIAsync<MainWindow>();

// 带载荷的开窗换两枚类型实参那一族，载荷排第一枚（窗口内以 Payload 读取）
UIService.ShowUIAsync<DetailWindow, int>(1001);

// 运行期才知道窗口类的动态腿：载荷擦进 UIPayload，第三枚给窗口标识、由门面按档换算
UIService.ShowUIAsync(type, windowName, windowId,
    fromResources: false, payload: UIPayload.From(dto));

// 全腿收 CancellationToken（default 零开销）；撤销只在装载在途那一段有意义
UIService.ShowUIAsync<MainWindow>(windowName: "Main", ct: cts.Token);

// 异步打开并等待加载完成（超时 60 秒；被调用方令牌撤销时原样上抛 OperationCanceledException）
UIWindow window = await UIService.ShowUIAsyncAwait<MainWindow>();

// 异步打开并等终态：就绪 / 失败 / 缺失 / 超时 / 取消分明（失败窗已回滚出栈，不得复用）
UIOpenResult result = await UIService.ShowUIAwaitResult<MainWindow>();
if (result.Status == EUIOpenStatus.Opened) { /* result.Window 可用 */ }

// 关闭 / 隐藏（HideTimeToClose 秒后自动关闭）
UIService.CloseUI<MainWindow>();
UIService.HideUI<MainWindow>();

// 查询
bool exist = UIService.HasWindow<MainWindow>();
UIWindow top = UIService.GetTopWindow();

// 导航：关上最近开的那只（开启序，不是层级序）
int depth = UIService.NavigationDepth;
bool closed = UIService.TryCloseTopWindow();
```

## 开窗腿签名

一轨 8 支 = 无载荷 4 支 + 带载荷 4 支，两支后端同名重载靠窗口基类约束分辨而不是形参个数；UI Toolkit 腿每支在 `ct` 前多收一枚 `PanelSettings panelSettings = null`（窗口级面板配置，`null` 时该窗回共享那一份）。`Type` 形入口另有动态 3 支，全局另有导航 2 成员与取窗 3 支。

### 每轨 8 支（uGUI 轨签名；UITK 轨同形多一枚 `panelSettings`）

| 腿 | 签名 | 交回 |
|---|---|---|
| 异步·无载荷 | `ShowUIAsync<T>(string windowName = null, string windowId = null, bool fromResources = false, CancellationToken ct = default)`，`T : UGUIWindow, new()` | `void` |
| 同步·无载荷 | `ShowUI<T>(…同形…)`，`T : UGUIWindow, new()` | `void` |
| 等待·无载荷 | `ShowUIAsyncAwait<T>(…同形…)`，`T : UGUIWindow, new()` | `UniTask<UIWindow>` |
| 结果·无载荷 | `ShowUIAwaitResult<T>(…同形…)`，`T : UGUIWindow, new()` | `UniTask<UIOpenResult>` |
| 异步·带载荷 | `ShowUIAsync<TWindow, TArg>(in TArg payload, string windowName = null, string windowId = null, bool fromResources = false, CancellationToken ct = default)`，`TWindow : UGUIWindow<TArg>, new()` | `void` |
| 同步·带载荷 | `ShowUI<TWindow, TArg>(in TArg payload, …同形…)` | `void` |
| 等待·带载荷 | `ShowUIAsyncAwait<TWindow, TArg>(TArg payload, …同形…)` | `UniTask<TWindow>` |
| 结果·带载荷 | `ShowUIAwaitResult<TWindow, TArg>(in TArg payload, …同形…)` | `UniTask<UIOpenResult>` |

- 等待·带载荷那一支的 `payload` 用普通形参而非 `in`：`async` 方法禁 `in` 形参（CS1988），账本那两枚 async 泛型通道同样收普通 `TArg`；其余三支仍是 `in TArg`（泛型直塞，struct 不装箱）
- UITK 带载荷腿的 `panelSettings` 排在 `fromResources` 之后、`ct` 之前，载荷永远排第一枚——错位守卫有格钉着
- 每支腿先过认领门：本轨没启用（槽位空着）当场抬错，不静默落空也不替本轨造一枚驱动者

### 动态 3 支（`Type` 形入口，载荷走 `UIPayload`）

| 腿 | 签名 | 交回 |
|---|---|---|
| 异步 | `ShowUIAsync(Type type, string windowName = null, string windowId = null, bool fromResources = false, UIPayload payload = default, CancellationToken ct = default)` | `void` |
| 同步 | `ShowUI(Type type, …同形…)` | `void` |
| 等待 | `ShowUIAsyncAwait(Type type, …同形…)` | `UniTask<UIWindow>` |

认轨判据在各轨自述的窗口基类上：认不出轨当场抬错，认出来却没人认领驱动那一档也当场抬错——不把窗口推进栈再等装载静默失败。这一张形参表不带窗口级 `panelSettings`（那是 UITK 泛型腿多出的那一枚），Type 形入口的该位恒为 `null`。

### 导航 2 成员与取窗 3 支

| 成员 | 签名 | 语义 |
|---|---|---|
| 导航深度 | `NavigationDepth`（`int` 属性） | 开启序历史的长度（栈按层级排序答不出「最近开的是谁」） |
| 关顶 | `TryCloseTopWindow()` | 关上最近开的那只，走既有 `CanClose` 政策；无历史 / 拒关 / 过渡中回假，历史不出栈 |
| 取窗·等待 | `GetUIAsyncAwait<T>()` | 栈上没有这一名或那只是别的类型时交回 `null` |
| 取窗·回调 | `GetUIAsync<T>(Action<T> callback)` | 找不到时只发一条 Warning，回调不被调用 |
| 取窗·结果 | `GetUIAwaitResult<T>()` | 交回 `UIOpenResult`，`Missing` 档表栈上没有那一只 |

取窗这三支问的是那条共享栈、只等已开窗的装载终态，因此不接调用方令牌（等待内部只受 60 秒上界约束）。

## 进阶用法

### 载荷通道：静态腿与动态腿

每只窗口最多一枚强类型载荷（DTO），落在两轨泛型基类的那一枚 `Payload` 槽上；写入按**覆盖**语义——每次开窗覆盖、关闭不清，残留到下一次开窗被覆盖为止。

```csharp
public struct RenameWindowPayload
{
    public string InitialText;
    public int MaxLength;
}

[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow<RenameWindowPayload>   // 带载荷必须继承 UGUIWindow<TArg> / UITKWindow<TArg>
{
    protected override void OnRefresh()
    {
        _input.text = Payload.InitialText;                    // 每次开窗覆盖后的那一枚载荷
        _input.maxLength = Payload.MaxLength;
    }
}

// 静态腿：编译期已知窗口类 → 泛型直塞，struct 不装箱，一步都不经 UIPayload 擦除
UIService.ShowUIAsync<RenameWindow, RenameWindowPayload>(in dto);

// 动态腿：运行期才有 Type → 载荷擦进 UIPayload（引用型只存引用，值类型装箱一次）
UIService.ShowUIAsync(type, windowName, windowId,
    fromResources: false, payload: UIPayload.From(dto));
```

> 工具链注：本工程工具链（C# 9 / netstandard2.1，无 `IsExternalInit` polyfill）下 `readonly struct` 配公共可写字段不编译（初始化点 CS8340；字段改 `readonly` 再配对象初始化器是 CS0191，`{ get; init; }` 是 CS0518），DTO 用普通 `struct` + 公共字段 + 对象初始化器。

- 载荷写入排在**准备回执与压栈之前**这一条管的是**停放重取与新开**两条支路（两条通道同一口径），所以 `OnRefresh` 读到的永远是这一次的载荷；复用支路的 Pop→Push 排在验槽之前（那只窗本就完整在栈，验槽不过抬错，回执与挪序已发生）
- 不带槽的窗口（直继 `UGUIWindow` / `UITKWindow`）被塞非空载荷当场抬错——fail-fast，不静默吞；空载荷作用于无槽窗是合法档（无载荷腿一路走这一档）
- 按名命中的窗槽型不符（`SetPayloadChecked` 认 `IUIPayloadSlot<TArg>`）、或门面按名取回的实例不是 `TWindow`，都抬 `GameException` 且消息带期望/实际类型名；「抬错排在卸停放与压栈之前」说的是**停放重取与新开**这两条支路，因此既不压半只窗、也不消费停放态（那只实例仍从停放表取得回）；复用支路的 Pop→Push 排在验槽之前（见上一条），抬错时回执与挪序都已发生
- `UIPayload` 的失败面：`To<T>` 在类型不符、或空载荷作用于值类型时抬 `GameException`（消息带期望类型名）；`TryGet<T>` 回假不抬错；`From(null)` 归约为 `Empty`
- 分配档位：无载荷往返（停放重取稳态）、静态腿 struct/class 载荷、动态腿 class 载荷都承诺增量 0；只有动态腿的基元/值类型载荷允许装箱一次。量具是 `GC.GetAllocatedBytesForCurrentThread`（本线程口径；L3 `[Explicit]` 基准格 `UIOpenAllocBenchmarkTests`，实测字节导出 `Temp/ui-open-alloc-benchmark.txt`——编辑器 Mono 下这一口径恒 0，真判据以玩家侧报告为准）

### 在飞合并与取消语义

同一只窗装载在途时再开（不论哪一支腿、不论静态还是动态通道）**合并在飞**：不重开发装载、不压第二只、同一枚实例；载荷 last-wins（覆盖为最后一枚），`OnRefresh` 只在面板就绪那一次跑，见的是终载荷。

- 取消令牌只在装载在途那一段被消费：已就绪的复用与停放重取同步交回、不消费 `ct`（语义诚实，不假装可取消）；复用一只仍在装载的窗时，令牌照样登记，撤销会掐断那一次在途装载（与在飞合并同段语义）
- 三档落点各不相同：void 腿的令牌撤销 → 等待者归零即掐断在途装载并回滚出栈（静默撤销，不报 Error）；等待腿 → 原样上抛 `OperationCanceledException`；结果腿 → `EUIOpenStatus.Cancelled`
- 与超时分成两档：等待上界 60 秒，超时只发一条 Warning 后仍交回那只窗口（结果腿落 `Timeout`，窗口可能还在装载）；`Cancelled` 只说明本次等待以取消落定，装载是否续跑取决于其余等待者（无人在等则回滚），两档都不得当就绪窗用
- 等待者配平：一名等待者登记一次、离场摘一次；超时落定的等待者不摘计数，只有每一枚已登记且可撤销的等待者都离场才掐断装载

### 最小导航

窗口栈按层级插入排序，答不出「最近开的是谁」；账本另记一份开启序（`Push` 追加、摘栈移除，复用与停放重取经 Pop→Push 会把那只窗挪成最新），导航那一问由它答。

```csharp
// 返回键：关上最近开的那只，走既有 CanClose 政策（拒关回假、历史不出栈）
if (!UIService.TryCloseTopWindow()) UIService.CloseAll();

int depth = UIService.NavigationDepth;   // 开启序历史的长度
```

不建路由表、不建导航事件、不建守卫链——`NavigationDepth` 与 `TryCloseTopWindow()` 是这一层露出的全部。

### 多后端与第三轨扩展

各后端 = 一条「轨道」，由三件套构成：`UIService.<轨>.cs` partial（槽位、认领门、开窗腿与 `UITrack` 自登记）、`<轨>Handler`（驱动者）、`<轨>Window`（窗口基类）。主入口 `UIService.cs` 只做通用逻辑——Type 形认轨分派、共享栈编排、`IsValid` 聚合、按档位升序的关停收口——不登记任何具体后端。

加一支新后端（如 FairyGUI）的完整清单：

1. `Handler/FairyGUI/FairyGUIWindow.cs`：窗口基类，继承 `UIWindow` 并覆写七枚面板钩子（`LoadPanel` / `LoadPanelAsync` / `ApplyVisible` / `ApplyDepth` / `ApplyInteractable` / `ParkPanel` / `DestroyPanel`）
2. `Handler/FairyGUI/FairyGUIHandler.cs`：驱动者，继承 `UIServiceHandler`；自述本轨认窗判据（`IsWindowOnOwnTrack`）与注册门（`Internal_Register` → 本轨 partial 的认领门）
3. `Handler/FairyGUI/UIService.FairyGUI.cs`：本轨 partial——驱动者槽 + 认领门（compare-exchange 占位、挂关停回调）、三条广播订阅（帧职责 / 安全区 / 刘海屏）、开窗腿（`ShowUI<T>` 族泛型腿收在 `FairyGUIWindow` 约束上）、`UITrack` 静态自登记（轨道名、窗口基类、关停档位）
4. `UIServiceSettings` 的启用清单加上该轨驱动者一项（Inspector 托管引用列表）

关停档位按面板挂靠关系选：面板挂在本轨自有根下取 `UITrack.SHUTDOWN_ORDER_DEFAULT`；挂靠别轨的宿主根（如 UI Toolkit 壳挂 uGUI 根）取 `UITrack.SHUTDOWN_ORDER_HOST`，最后收。此后认轨分派、`IsValid`、关停收口都按目录枚举自动覆盖新轨，主文件零改动。

### 窗口层级与深度

窗口栈按 `WindowLayer` 插入排序，`OnSortWindowDepth` 以 `layer * LAYER_DEEP` 为起点、同层每个窗口递增 `WINDOW_DEEP` 写入 Canvas `sortingOrder`。模态层级（`UI`/`Popup`/`System`）窗口入栈时，会自动把紧邻下层窗口置为不可交互：

```csharp
// 关闭除 System 层外的所有窗口
UIService.CloseAllWithOut(EUILayer.System);

// 判断某 UI 对象是否被模态窗口遮挡
bool blocked = UIService.IsBlockedByModal(gameObject);
```

### Widget 子控件

Widget 复用窗口的生命周期方法，由所属窗口驱动更新。在窗口/Widget 内通过 `UIWidget` 的工厂方法创建：

```csharp
// 在窗口内已有节点上创建
var item = new HeroItemWidget();
item.Create(this, widgetRootGo);

// 按资源定位地址实例化创建
var item2 = new HeroItemWidget();
item2.CreateByPath("HeroItem", this, parentTrans);

// 按 prefab 副本创建（列表项常用）
var item3 = new HeroItemWidget();
item3.CreateByPrefab(this, goPrefab, parentTrans);

// 销毁交还
item.Destroy();
```

### 开关过渡与交互锁

窗口默认**无内置开/关过渡**——开与关都当场结算，无延迟、无输入锁、无交互压制窗口。提供过渡：覆写 `UIWindow.Transition` 交回一枚 `IUITransition` 实现（`Play(open, ct)` 为动画过程，`Snap(open)` 供跳过路径就近拨终态）；过渡播放期间窗口锁交互，模态窗口联动输入服务（`InputService.PreventInteractionUI`）。交还只发生在**当轮**转移：全局压制位按归属仲裁（`UIInteractionLease`）仅由最后持有者清除，被重开/销毁接管的旧过渡续体不再解锁也不再隐藏，过渡实现无需自行判断是否已被接管：

```csharp
private CanvasGroup _canvasGroup;   // OnCreate 里 GetComponent 缓存一次
private IUITransition _transition;

// 缓存复用：过渡属性每轮开关各取用一次，写成新建会把分配带回开窗与关闭路径
protected internal override IUITransition Transition => _transition ??= new FadeTransition(this);

private sealed class FadeTransition : IUITransition
{
    private readonly MainWindow _window;

    public FadeTransition(MainWindow window) => _window = window;

    public async UniTask Play(bool open, CancellationToken ct)
        => await _window._canvasGroup.DOFade(open ? 1f : 0f, 0.3f).WithCancellation(ct);

    // 跳过等待的路径（被接管、关停、即时收口）由框架调用，当场把面板拨到那一档终态
    public void Snap(bool open) => _window._canvasGroup.alpha = open ? 1f : 0f;
}
```

### 窗口自关（等待与门）

窗口自己关自己（`Close()`）一律先等可交互——开窗动画结束、或压住它的上层模态解除——再过 `CanClose` 门；门为假时窗口留在栈上并收到 `OnCloseFail`：

```csharp
[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow
{
    protected override bool CanClose => _input.text.Length > 0;   // 过不了门就落 OnCloseFail

    protected override void OnCloseFail() { /* 提示非法输入，窗口留在栈上 */ }
}
```

- 策略住在 `UIWindow`（后端无关对象模型）上：uGUI / UI Toolkit 两轨窗口同形覆写，各轨不必复制中间基类
- 等待是被动观察：窗口在等待期被重开/销毁接管时本轮静默终止，锁的交还由接管方收口；已销毁的窗不会空转轮询
- `ForceClose()` 跳过等待与门当场结算，是覆写者按需立即关窗的旁路
- 只有窗口自关走这一道：外部经 `UIService.CloseUI` / `CloseAll` 的关闭直接进共享栈，不受等待与门的影响

### 生命周期钩子隔离

政策是「隔离并继续」：钩子抛出只记一条日志、不牵连同批的别的窗，也不让本窗流程停在半截。全手工 try/catch，不用 lambda 包钩子（每帧路径零分配）。

| 钩子位 | 抛出时 |
|---|---|
| 创建链（`Inject` / `ScriptGenerator` / `BindMemberProperty` / `RegisterEvent` / `OnCreate`） | 与装载失败同一条收口：`RollbackFailedLoad` → 窗口摘出栈并进显式失败态 |
| `OnRefresh` | Error 日志，窗口照常入栈 |
| 账本每帧 `Tick` 的每窗 `Internal_Update` | Error 日志，继续下一窗（遍历期间栈被改写即收尾，防半程索引读到错位窗口） |
| `UpdateCore` 的每枚控件 | Error 日志，本帧其余控件照常结算 |
| `CanClose` | Error 日志，按拒关计（fail-closed）——`TryCloseTopWindow` 与窗口自关吃同一道判据 |
| `OnClose` / `OnDestroy` / `UnregisterEvent` / `Apply*` 面板钩 | Error 日志，关闭与销毁流程照常走完（不半截） |
| 开/关过渡 `Play` / `Snap` | `Play` 抛 → 退 `Snap` 落终态；`Snap` 也抛 → 日志照停、流程照走 |

### 安全区域与 UIAdapter

- 窗口内：`SetUIFit(RectTransform, liuHaiFit, topSpacing, bottomFit, bottomSpacing)` 对指定节点做刘海屏上下适配，`SetUINotFit` 排除个别节点。
- 全局：静态方法 `UIService.ApplyScreenSafeRect(Rect)` 直接调整 UIRoot；`UIService.SimulateIPhoneXNotchScreen()` 在编辑器模拟异形屏。
- 布局适配器（`Moirai.Atropos.UI.Adapter`）：`SafeAreaAdapter`（安全区）、`HorizontalAdapter` / `VerticalAdapter`（横/纵向自适应排列，支持 `Gap`）、`AngleAdapter`（环形排列，支持 `Distance`、`BiasAngle`、`Clockwise`），均挂载 MonoBehaviour 并可每帧重算。

### 运行时错误窗口

当调试器配置（`DebuggerService.ActiveWindowType`）判定**启用**错误日志时，服务才注册 `ErrorLogger` 捕获 `LogType.Exception`，自动弹出内置 `LogUI` 窗口（`[Window(EUILayer.System, fromResources:true)]`，预制体位于服务 `Resources/LogUI.prefab`；`LogUI : UGUIWindow<string>`，异常文本经带载荷腿落进它的 `Payload`）逐条查看异常堆栈。启用判据：`AlwaysOpen` 恒启用；`OnlyOpenWhenDevelopment` 随开发构建；`OnlyOpenInEditor` 随编辑器；`AlwaysClose` 与非开发构建下的 `OnlyOpenWhenDevelopment`（即发布包默认形态）都不启用，异常不弹窗。

### 编辑器绑定代码生成

选中 UI 预制体根节点，使用菜单：

- `GameObject/ScriptGenerator/生成绑定代码`：生成 `partial class XXX : UGUIWindow` 窗口脚本及 `XXXBinder : UIBindComponent` 绑定组件
- `GameObject/ScriptGenerator/复制绑定属性`：复制成员变量代码到剪贴板

## 注意事项

- uGUI 轨的 UI 根由场景物体上的 `UIRootBinding` 组件登记（其下需含 `Canvas`）：`SingletonMono` 先到先得，后到者整物体销毁；取用走 `TryGetInstance()`，只回读、不自动创建。后端在首个 Update tick 取用，缺绑定报一条 Error、缺 Canvas 报一条 Fatal，之后都每帧续等（后加入的场景、运行期实例化的根、事后补上的 Canvas 都补得上）。登记到位后 UI 根自动 `DontDestroyOnLoad`（仅播放态）。**查找不按物体名字**——改名不影响，多场景/热更下同名也不会错挂根。UI Toolkit 轨的文档壳也挂在这枚根下，关停时它先于根销毁被收走。
- `ShowUI` 同步加载依赖资源服务的同步加载能力，WebGL 下自动退化为异步；建议优先使用 `ShowUIAsync`
- `HideUI` 仅当窗口 `HideTimeToClose > 0` 时生效，否则等同直接 `CloseUI`
- `GetUIAsyncAwait<T>()` / `GetUIAsync<T>` 只等待"已打开"窗口的加载完成，窗口不存在时返回 null / 不回调
- 窗口更新（`OnUpdate`）仅对可见窗口触发；全屏窗口会遮挡其下窗口的可见性
- 带载荷窗口必须继承 `UGUIWindow<TArg>` / `UITKWindow<TArg>`：直继无槽基类却被塞非空载荷当场抬错。载荷每次开窗覆盖、关闭不清（残留到下一次覆盖为止），无「读一次即清」的语义
- 基元与 struct 的主路是静态腿 `in TArg`（泛型直塞、零装箱）；`UIPayload` 只服务运行期才知 `Type` 的动态腿，值类型经它装箱一次——热路径别把 struct 塞进动态腿
- 全腿的 `CancellationToken` 传 `default` 零开销，且只在装载在途那一段被消费：已就绪的复用与停放重取不消费 `ct`；复用一只仍在装载的窗时，令牌照样登记，撤销会掐断那一次在途装载（与在飞合并同段语义）
- 窗口开合回执走门面静态广播：`UIService.onWindowShown += OnWindowShownEvent` / `onWindowClosed += OnWindowClosedEvent`（形参 `UIWindow`），入栈/出栈各恰一次、停放与销毁都发；订阅者自己配对退订，门面关停与归零门会整批摘掉
- 停放档一枚三态：`[Window(cacheTimeToDestroy: …)]`，`0` = 不缓存（关闭即销毁，缺省即此）、正数 = 停放并在这么多秒后转销毁、负数 = 停放永久；到期由账本移出停放表并终态销毁，重新取用即取消计时
- 寻址归门面：开窗腿的第三枚是**窗口标识**——`fromResources` 为真时把它拼到 `UIServiceSettings` 的 Resources 父目录下，为假时按它查 `ConfigTableService.GetUIWindowLocation`；标识必填（没带即当场 `GameException`，地址没有第二条来路）；`[Window]` 不再声明地址。原 `UIManager` 与它的两枚公共静态定位口已退役，换算判据只此一份，且只在账本造新实例那一格发生（复用栈上窗与停放重取不查表）

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [UI 迁移](UIMigration.md) · [Input](Input.md) · [Scene](Scene.md) · [Audio](Audio.md)
