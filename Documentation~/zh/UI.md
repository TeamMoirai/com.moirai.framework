# UI 服务

> 基于 UGUI 的栈式窗口管理框架，提供窗口生命周期、层级深度排序、模态遮挡、Widget 子控件与多分辨率适配能力。

UI 服务（`Moirai.Atropos.UI`）将界面抽象为纯 C# 类的 `UIWindow` / `UIWidget`，由 `UIService` 统一管理窗口栈、层级深度与可见性。窗口面板通过资源服务（YooAsset）或 `Resources` 加载实例化，窗口类本身不挂 MonoBehaviour。通过 `GameApp.Services.GetRequiredService<IUIService>()` 静态访问器即可完成打开、关闭、隐藏、查询等全部操作。

## 核心特性

- 窗口栈式管理：按 `UILayer` 层级插入排序，同层窗口深度自动递增（`LAYER_DEEP = 2000`、`WINDOW_DEEP = 100`）
- 五级层级：`Bottom` / `UI` / `Popup` / `Tips` / `System`，其中 `UI`、`Popup`、`System` 为模态层级
- 完整生命周期：`OnCreate` → `OnRefresh` → `OnUpdate` → `OnClose` → `OnDestroy`，可重写打开/关闭动画
- 模态遮挡：模态窗口压栈后自动禁用下层窗口交互（`Interactable`），`IsBlockedByModal` 可查询遮挡
- 全屏窗口优化：全屏窗口之下的窗口自动隐藏，减少渲染与更新开销
- 窗口缓存：`cacheInstance` 关闭时不销毁，再次打开直接复用实例
- Widget 子控件：窗口内嵌控件复用同一套生命周期，支持按节点 / 资源路径 / prefab 创建
- 多分辨率适配：安全区域（刘海屏）适配、`UIAdapter` 布局适配器（横向 / 纵向 / 环形 / 安全区）
- 编辑器代码生成：`GameObject/ScriptGenerator` 菜单自动生成 UI 绑定代码

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.UI.IUIService` | UI 服务接口，`GameApp.Services.GetRequiredService<IUIService>()` 返回此类型 |
| `Moirai.Atropos.UI.UIService` | UI 服务实现，窗口栈管理、深度排序、可见性控制；静态属性 `UIRoot`、`Resource` |
| `Moirai.Atropos.UI.UIBase` | UI 基类，定义生命周期虚方法与 Widget 创建 API |
| `Moirai.Atropos.UI.UIWindow` | 窗口抽象基类，继承 `UIBase`，含 Canvas 深度、可见性、交互性、开关动画 |
| `Moirai.Atropos.UI.UIWidget` | 窗口内嵌控件基类，继承 `UIBase` |
| `Moirai.Atropos.UI.WindowAttribute` | 窗口特性，声明层级、资源地址、全屏、缓存等配置 |
| `Moirai.Atropos.UI.UILayer` | UI 层级枚举：`Bottom=0`、`UI=1`、`Popup=2`、`Tips=3`、`System=4` |
| `Moirai.Atropos.UI.UIServiceEvent` | 窗口打开/关闭事件（`Shown` / `Closed`），经 `EventManager` 派发 |
| `Moirai.Atropos.UI.UIServiceHelper` | 交互辅助：`IsInteractionBlockedByModal`、`IsUIObjectInteractable` |
| `Moirai.Atropos.UI.IUIResourceLoader` | UI 资源加载器接口，默认实现 `UIResourceLoader` 走资源服务 |
| `Moirai.Atropos.UI.UIBindComponent` | Window/Widget 组件绑定 MonoBehaviour 基类 |
| `Moirai.Atropos.UI.ErrorLogger` | 运行时异常捕获器，异常时弹出 `LogUI` 窗口 |
| `Moirai.Atropos.UI.Adapter.AdapterBase` | 布局适配器抽象基类（`Moirai.Atropos.UI.Adapter` 命名空间） |

## 快速上手

定义一个窗口（窗口类必须有无参构造，即 `new()` 约束）：

```csharp
using Moirai.Atropos.UI;

// 层级 Popup、非全屏、关闭后缓存实例
[Window(UILayer.Popup, location: "MainWindow", fullScreen: false, cacheInstance: true)]
public class MainWindow : UIWindow
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
GameApp.Services.GetRequiredService<IUIService>().ShowUI<MainWindow>();

// 异步打开，可携带自定义参数（窗口内以 UserData / Params 读取）
GameApp.Services.GetRequiredService<IUIService>().ShowUIAsync<MainWindow>(userData: 1001);

// 异步打开并等待加载完成（超时 60 秒）
UIWindow window = await GameApp.Services.GetRequiredService<IUIService>().ShowUIAsyncAwait<MainWindow>();

// 关闭 / 隐藏（HideTimeToClose 秒后自动关闭）
GameApp.Services.GetRequiredService<IUIService>().CloseUI<MainWindow>();
GameApp.Services.GetRequiredService<IUIService>().HideUI<MainWindow>();

// 查询
bool exist = GameApp.Services.GetRequiredService<IUIService>().HasWindow<MainWindow>();
UIWindow top = GameApp.Services.GetRequiredService<IUIService>().GetTopWindow();
```

## 进阶用法

### 窗口层级与深度

窗口栈按 `WindowLayer` 插入排序，`OnSortWindowDepth` 以 `layer * LAYER_DEEP` 为起点、同层每个窗口递增 `WINDOW_DEEP` 写入 Canvas `sortingOrder`。模态层级（`UI`/`Popup`/`System`）窗口入栈时，会自动把紧邻下层窗口置为不可交互：

```csharp
// 关闭除 System 层外的所有窗口
GameApp.Services.GetRequiredService<IUIService>().CloseAllWithOut(UILayer.System);

// 判断某 UI 对象是否被模态窗口遮挡
bool blocked = GameApp.Services.GetRequiredService<IUIService>().IsBlockedByModal(gameObject);
```

### Widget 子控件

Widget 复用窗口的生命周期方法，由所属窗口驱动更新。在窗口/Widget 内通过 `UIBase` 提供的工厂方法创建：

```csharp
// 从窗口内已有节点路径创建
HeroItemWidget item = CreateWidget<HeroItemWidget>("m_list/m_heroItem");

// 按资源定位地址同步/异步实例化创建
HeroItemWidget item2 = CreateWidgetByPath<HeroItemWidget>(parentTrans, "HeroItem");
HeroItemWidget item3 = await CreateWidgetByPathAsync<HeroItemWidget>(parentTrans, "HeroItem");

// 按 prefab 副本创建（列表项常用）
HeroItemWidget item4 = CreateWidgetByPrefab<HeroItemWidget>(prefab, parentTrans);

// 批量调整列表图标数量（含异步分帧版本 AsyncAdjustIconNum）
AdjustIconNum<HeroItemWidget>(_items, count, parentTrans, prefab);
```

### 开关动画与交互锁

窗口默认内置 0.5 秒打开 / 0.25 秒关闭的等待，可重写替换为动画播放；动画期间窗口自动锁定交互，模态窗口还会联动输入服务（`GameApp.Services.GetRequiredService<IInputService>().PreventInteractionUI`）：

```csharp
protected override async UniTask OpenAnimation()
{
    await panel.DOFade(1f, 0.3f);  // 播放自定义动画
}
```

### 安全区域与 UIAdapter

- 窗口内：`SetUIFit(RectTransform, liuHaiFit, topSpacing, bottomFit, bottomSpacing)` 对指定节点做刘海屏上下适配，`SetUINotFit` 排除个别节点。
- 全局：静态方法 `UIService.ApplyScreenSafeRect(Rect)` 直接调整 UIRoot；`UIService.SimulateIPhoneXNotchScreen()` 在编辑器模拟异形屏。
- 布局适配器（`Moirai.Atropos.UI.Adapter`）：`SafeAreaAdapter`（安全区）、`HorizontalAdapter` / `VerticalAdapter`（横/纵向自适应排列，支持 `Gap`）、`AngleAdapter`（环形排列，支持 `Distance`、`BiasAngle`、`Clockwise`），均挂载 MonoBehaviour 并可每帧重算。

### 运行时错误窗口

当调试器配置（`DebuggerComp.ActiveWindowType`）判定不启用错误日志时，服务会注册 `ErrorLogger` 捕获 `LogType.Exception`，自动弹出内置 `LogUI` 窗口（`[Window(UILayer.System, fromResources:true)]`，预制体位于服务 `Resources/LogUI.prefab`）逐条查看异常堆栈。

### 编辑器绑定代码生成

选中 UI 预制体根节点，使用菜单：

- `GameObject/ScriptGenerator/生成绑定代码`：生成 `partial class XXX : UIWindow` 脚本及 `XXXBinder : UIBindComponent` 绑定组件
- `GameObject/ScriptGenerator/复制绑定属性`：复制成员变量代码到剪贴板

## 注意事项

- 场景中必须存在名为 `UIRoot` 的物体且其下含 `Canvas`，否则初始化报 Fatal；UIRoot 会自动 `DontDestroyOnLoad`
- `ShowUI` 同步加载依赖资源服务的同步加载能力，WebGL 下自动退化为异步；建议优先使用 `ShowUIAsync`
- `HideUI` 仅当窗口 `HideTimeToClose > 0` 时生效，否则等同直接 `CloseUI`
- `GetUIAsyncAwait<T>()` / `GetUIAsync<T>` 只等待"已打开"窗口的加载完成，窗口不存在时返回 null / 不回调
- 窗口更新（`OnUpdate`）仅对可见窗口触发；全屏窗口会遮挡其下窗口的可见性

---
[« 返回主 README](../../README.md) · [Input](Input.md) · [Scene](Scene.md)
