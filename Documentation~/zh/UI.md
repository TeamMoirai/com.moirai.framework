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
- 开窗结果契约：`ShowUIAwaitResult<T>` / `GetUIAwaitResult<T>` 交回 `UIOpenResult`，按 `EUIOpenStatus` 四档（`Opened` / `Failed` / `Missing` / `Timeout`）分明成败——装载失败的窗口当场回滚出栈，等待不再以 null 与超时混言成败
- 全屏窗口优化：全屏窗口之下的窗口自动隐藏，减少渲染与更新开销
- 窗口缓存：`cacheInstance` 关闭时不销毁，再次打开直接复用实例
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
| `Moirai.Atropos.UI.UGUIWindow` | uGUI 轨窗口基类（`Handler/UGUI/`）：把三份意图落到 GameObject / Canvas / GraphicRaycaster 面板上，**uGUI 业务窗口一律继承此类** |
| `Moirai.Atropos.UI.UITKWindow` | UI Toolkit 轨窗口基类（`Handler/UITK/`）：`UIDocument` 壳与内容根装配、窗口级 `PanelSettings` 覆盖（开窗族比 uGUI 腿多出的那枚形参），UI Toolkit 业务窗口继承此类 |
| `Moirai.Atropos.UI.UIWidget` | 窗口内嵌控件基类，继承 `UIBase` |
| `Moirai.Atropos.UI.WindowAttribute` | 窗口特性（必标），声明层级、资源地址、全屏、缓存等配置；由源生成器 `UIWindowCodegen` 编译期解析并登记进 `UIWindowRegistry` |
| `Moirai.Atropos.UI.EUILayer` | UI 层级枚举：`Bottom=0`、`UI=1`、`Popup=2`、`Tips=3`、`System=4` |
| `Moirai.Atropos.UI.EUIModal` | 模态档枚举：`Inherit=0`（按层级结算）、`Modal=1`、`NonModal=2`；`[Window(modal:)]` 的形参与 `WindowAttribute.Modal` 的存储档 |
| `Moirai.Atropos.UI.UIWindowRegistry` | 窗口注册表（`public static`）：类型句柄 → 描述符 + 编译期工厂，由各程序集生成的模块初始化器登记，之后只读；`TryGet` 为 `internal` |
| `Moirai.Atropos.UI.UIWindowDescriptor` | 窗口元数据描述符（`readonly struct`）：注册期一次解析好的 `[Window]` 全量取值，开窗时零反射直取 |
| `Moirai.Atropos.UI.IUITransition` | 开/关过渡契约：`Play(open, ct)` 播放并等走完、`Snap(open)` 把面板当场拨到终态；经 `UIWindow.Transition` 覆写交回，缺位即瞬时 |
| `Moirai.Atropos.UI.UIOpenResult` / `EUIOpenStatus` | 开窗/取窗终态（`readonly struct` + `byte` 枚举四档）：`Window` 与 `Status` 成对交回，`Success` 与隐式布尔只答「就绪」一档 |
| `Moirai.Atropos.UI.UIServiceEvent` | 窗口打开/关闭事件（`Shown` / `Closed`），经 `EventManager` 派发 |
| `Moirai.Atropos.UI.UIServiceHelper` | 交互辅助：`IsInteractionBlockedByModal`、`IsUIObjectInteractable` |
| `Moirai.Atropos.UI.UIBindComponent` | Window/Widget 组件绑定 MonoBehaviour 基类 |
| `Moirai.Atropos.UI.ErrorLogger` | 运行时异常捕获器，异常时弹出 `LogUI` 窗口 |
| `Moirai.Atropos.UI.Adapter.AdapterBase` | 布局适配器抽象基类（`Moirai.Atropos.UI.Adapter` 命名空间） |

## 快速上手

定义一个窗口（窗口类必须有无参构造，即 `new()` 约束）：

```csharp
using Moirai.Atropos.UI;

// 层级 Popup、非全屏、关闭后缓存实例
[Window(EUILayer.Popup, location: "MainWindow", fullScreen: false, cacheInstance: true)]
public class MainWindow : UGUIWindow
{
    protected override void ScriptGenerator() { }   // 生成的绑定代码在此重写

    protected override void OnCreate() { /* 首次创建，绑定事件 */ }

    protected override void OnRefresh() { /* 打开或上层窗口关闭时刷新，通过 UserData/Params 取参 */ }

    protected override void OnUpdate() { /* 每帧更新（仅可见窗口） */ }

    protected override void OnClose() { /* 关闭清理 */ }

    protected override void OnDestroy() { /* 实例销毁 */ }
}
```

打开与关闭窗口：

```csharp
// 同步打开（WebGL 平台自动转为异步）
UIService.ShowUI<MainWindow>();

// 异步打开，可携带自定义参数（窗口内以 UserData / Params 读取）
UIService.ShowUIAsync<MainWindow>(userData: new object[] { 1001 });

// 异步打开并等待加载完成（超时 60 秒）
UIWindow window = await UIService.ShowUIAsyncAwait<MainWindow>();

// 异步打开并等终态：就绪 / 失败 / 缺失 / 超时分明（失败窗已回滚出栈，不得复用）
UIOpenResult result = await UIService.ShowUIAwaitResult<MainWindow>();
if (result.Status == EUIOpenStatus.Opened) { /* result.Window 可用 */ }

// 关闭 / 隐藏（HideTimeToClose 秒后自动关闭）
UIService.CloseUI<MainWindow>();
UIService.HideUI<MainWindow>();

// 查询
bool exist = UIService.HasWindow<MainWindow>();
UIWindow top = UIService.GetTopWindow();
```

## 进阶用法

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

### 安全区域与 UIAdapter

- 窗口内：`SetUIFit(RectTransform, liuHaiFit, topSpacing, bottomFit, bottomSpacing)` 对指定节点做刘海屏上下适配，`SetUINotFit` 排除个别节点。
- 全局：静态方法 `UIService.ApplyScreenSafeRect(Rect)` 直接调整 UIRoot；`UIService.SimulateIPhoneXNotchScreen()` 在编辑器模拟异形屏。
- 布局适配器（`Moirai.Atropos.UI.Adapter`）：`SafeAreaAdapter`（安全区）、`HorizontalAdapter` / `VerticalAdapter`（横/纵向自适应排列，支持 `Gap`）、`AngleAdapter`（环形排列，支持 `Distance`、`BiasAngle`、`Clockwise`），均挂载 MonoBehaviour 并可每帧重算。

### 运行时错误窗口

当调试器配置（`DebuggerService.ActiveWindowType`）判定**启用**错误日志时，服务才注册 `ErrorLogger` 捕获 `LogType.Exception`，自动弹出内置 `LogUI` 窗口（`[Window(EUILayer.System, fromResources:true)]`，预制体位于服务 `Resources/LogUI.prefab`）逐条查看异常堆栈。启用判据：`AlwaysOpen` 恒启用；`OnlyOpenWhenDevelopment` 随开发构建；`OnlyOpenInEditor` 随编辑器；`AlwaysClose` 与非开发构建下的 `OnlyOpenWhenDevelopment`（即发布包默认形态）都不启用，异常不弹窗。

### 编辑器绑定代码生成

选中 UI 预制体根节点，使用菜单：

- `GameObject/ScriptGenerator/生成绑定代码`：生成 `partial class XXX : UGUIWindow` 窗口脚本及 `XXXBinder : UIBindComponent` 绑定组件
- `GameObject/ScriptGenerator/复制绑定属性`：复制成员变量代码到剪贴板

## 注意事项

- uGUI 轨的 UI 根由场景物体上的 `UIRootBinding` 组件登记（其下需含 `Canvas`）：`SingletonMono` 先到先得，后到者整物体销毁；取用走 `TryGetInstance()`，只回读、不自动创建。后端在首个 Update tick 取用，缺绑定报一条 Error、缺 Canvas 报一条 Fatal，之后都每帧续等（后加入的场景、运行期实例化的根、事后补上的 Canvas 都补得上）。登记到位后 UI 根自动 `DontDestroyOnLoad`（仅播放态）。**已不再按物体名字查找**——改名不报编译错、多场景/热更下同名还可能命中错的根。UI Toolkit 轨的文档壳也挂在这枚根下，关停时它先于根销毁被收走。
- `ShowUI` 同步加载依赖资源服务的同步加载能力，WebGL 下自动退化为异步；建议优先使用 `ShowUIAsync`
- `HideUI` 仅当窗口 `HideTimeToClose > 0` 时生效，否则等同直接 `CloseUI`
- `GetUIAsyncAwait<T>()` / `GetUIAsync<T>` 只等待"已打开"窗口的加载完成，窗口不存在时返回 null / 不回调
- 窗口更新（`OnUpdate`）仅对可见窗口触发；全屏窗口会遮挡其下窗口的可见性

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Input](Input.md) · [Scene](Scene.md) · [Audio](Audio.md)
