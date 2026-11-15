# 创建新 UI

在 Moirai Framework 的 UI 服务中新建一个窗口（uGUI 轨派生 `UGUIWindow`，对象模型基类是 `UIWindow`）或控件（`UIWidget`）。

## 参数
- $UI_NAME: UI 名称（如 `MainMenu`、`SettingsPanel`、`BattleHUD`）

## 落地位置

UI 脚本住在游戏侧程序集，不在框架包内：

```
<游戏程序集>/UI/
├── Windows/<UI_NAME>.cs          # 派生 UGUIWindow（uGUI 轨窗口基类）
└── Widgets/<Name>Widget.cs       # 派生 UIWidget（按需）
Assets/.../UI/Prefabs/<UI_NAME>.prefab
```

## 窗口骨架

```csharp
using Moirai.Atropos.UI;

[Window(EUILayer.UI, "UI/<UI_NAME>")]             // 层级 + 资源地址；fromResources: true 时走 Resources
public class <UI_NAME> : UGUIWindow
{
    protected override void OnCreate() { }               // 实例化后一次
    protected override void BindMemberProperty() { }     // 接收绑定生成器写回的字段
    protected override void RegisterEvent() { }
    protected override void UnregisterEvent() { }
    protected override void OnRefresh() { }              // 每次打开时
    protected override void OnUpdate() { }               // 全屏窗口的逐帧逻辑
    protected override void OnClose() { }
}
```

生命周期方法名以 `Runtime/Services/UI/Objects/UIBase.cs` 与 `Objects/UIWindow.cs` 为准——框架里没有 `UIForm` / `UIFormLogic` 这一族，也没有 `[UIForm]` 特性。

## Widget 控件（可复用组件）

**Window 是容器**（整窗预制体，栈序/显隐/缓存生命周期归 `UIService`）；**Widget 是可复用组件**（同一 widget 类在一个窗口内可挂多实例，随宿主窗口泵驱动 `OnUpdate`，随窗回收）。两者走同一条 Binder 生成链路，差别只在挂点层级与生命周期归属。

### 节点语法与命名

- **Widget 根节点**：`{WidgetClassName}@{InstanceTag}`（如 `TotemEnergyWidget@0`）。生成类名取 **@ 前段**——多实例共享一个类与一份 `<Class>.g.cs`；实例段只用于 window 侧字段区分（`m_{Class}{Instance}`，如 `m_TotemEnergyWidget0`）。单实例 widget 可省 `@` 段，直接用类名（同窗口「根名==类名==文件名」三同规则）。
- **内部节点**：与窗口同款 `{ComDef1#ComDef2}@{Suffix}` / `*Com@Group*N`（数组）语法，收集进 **widget 自己的 binder**；window 收集器在 widget 根停递归（`EBindType.Widget`）——window 的 binder 只拿到 widget-typed 字段（类型 = widget binder 类），**永不触及 widget 内部字段**，widget 与 window 的字段名空间互不冲突。
- 手写 `{WidgetClassName}.cs`（`sealed partial class ... : UIWidget`）先于生成存在；生成器补 `_binder`、访问器与事件声明（`ScriptGenerator()` 覆写经 `gameObject.GetComponent<{Class}Binder>()` 拉起，挂点即创建根）。

### 生成流程

1. 进宿主 prefab 的 Prefab Stage，选中 widget 根节点 → 右键 `GameObject/ScriptGenerator/生成绑定代码`（或直调 `UIScriptGeneratorHelper.GenerateUIBindScript(节点, config)`）。
2. recompile：`[DidReloadScripts] BindUIScript` 按**层级路径**（子节点相对 prefab 根的 child-index 链）回绑该实例的 binder 字段。
3. 多实例：对每个实例节点重跑第 1 步——g.cs 内容不变时 writer 短路直调 `BindUIScript()`，就地绑当前实例。
4. window 侧最后重生成一次：widget 根被收集为 widget-typed 字段，窗口经 `_totemEnergyWidget0.gameObject` 等访问器拿挂点创建控件（`new XxxWidget(...).Create(this, 挂点)`）；更新泵与回收由 `UIWidget` 子件清单自动接办。

### 口径

- 按名/按路径查找（`transform.Find` / `FindChildComponent` 一族）**已从框架删除弃用**：一切节点引用走 binder 生成面（见 `coding.md` 的 Unity 引擎条款）。
- widget 内部 `Btn@` 的事件声明生成在 `<Class>.g.cs` 的 partial 里，由手写 partial 实现；数组（`*` 语法）不生成事件，控件在 `OnCreate` 自挂并按索引闭包分发。
- EditorMode 测试：搭镜像节点树 → `AddComponent<{Class}Binder>()` + `SerializedObject` 赋字段 → widget 测试挂点（`CreateBase + ScriptGenerator() + OnCreate`）直连，不经窗口创建链。

## 打开与关闭

```csharp
UIService.ShowUIAsync<<UI_NAME>>(userData: args);
var window = await UIService.ShowUIAsyncAwait<<UI_NAME>>();

UIService.HideUI<<UI_NAME>>();     // 隐藏但保留实例
UIService.CloseUI<<UI_NAME>>();    // 关闭，是否留缓存实例由 WindowAttribute 决定
```

## 要点

- `[Window]` 决定层级（`Bottom=0` / `UI=1` / `Popup=2` / `Tips=3` / `System=4`）、是否全屏、自动隐藏与缓存时长；查询走 `UIService.HasWindow<T>` / `GetWindow<T>` / `GetTopWindow()`。
- 组件引用不手写 `transform.Find`（按名/按路径查找 API 已从框架删除弃用）：窗口与 Widget 一律挂 `UIBindComponent` 走生成器产出字段（窗口：根节点右键 `GameObject/ScriptGenerator/生成绑定代码`；Widget：见上方 Widget 节的子节点流程；发射器实现见 `Editor/Services/UI/UGUIGenerator/Support/UICodeEmitter.cs`）。
- 模态与交互压制读 `UIService.CurrentModal` / `IsBlockedByModal(GameObject)`，不要另立一套"锁屏"标志。
- 动到 UI 公开面时同步 `Documentation~/UI.md` 与 `CHANGELOG.md`，口径见 `AGENTS.md` 的「提交时的文档与 CHANGELOG」。

请告诉我要创建什么界面，我按上面的形状生成窗口类与预制体侧的绑定清单。
