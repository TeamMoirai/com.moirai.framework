# 创建新 UI

在 Moirai Framework 的 UI 服务中新建一个窗口（`UIWindow`）或控件（`UIWidget`）。

## 参数
- $UI_NAME: UI 名称（如 `MainMenu`、`SettingsPanel`、`BattleHUD`）

## 落地位置

UI 脚本住在游戏侧程序集，不在框架包内：

```
<游戏程序集>/UI/
├── Windows/<UI_NAME>.cs          # 派生 UIWindow
└── Widgets/<Name>Widget.cs       # 派生 UIWidget（按需）
Assets/.../UI/Prefabs/<UI_NAME>.prefab
```

## 窗口骨架

```csharp
using Moirai.Atropos.UI;

[Window(UILayer.UI, "UI/<UI_NAME>")]             // 层级 + 资源地址；fromResources: true 时走 Resources
public class <UI_NAME> : UIWindow
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

生命周期方法名以 `Runtime/Services/UI/UIBase.cs` 与 `UIWindow.cs` 为准——框架里没有 `UIForm` / `UIFormLogic` 这一族，也没有 `[UIForm]` 特性。

## 打开与关闭

```csharp
UIService.ShowUIAsync<<UI_NAME>>(userData: args);
var window = await UIService.ShowUIAsyncAwait<<UI_NAME>>();

UIService.HideUI<<UI_NAME>>();     // 隐藏但保留实例
UIService.CloseUI<<UI_NAME>>();    // 关闭，是否留缓存实例由 WindowAttribute 决定
```

## 要点

- `[Window]` 决定层级（`Bottom=0` / `UI=1` / `Popup=2` / `Tips=3` / `System=4`）、是否全屏、自动隐藏与缓存时长；查询走 `UIService.HasWindow<T>` / `GetWindow<T>` / `GetTopWindow()`。
- 组件引用不手写 `transform.Find`：在层级里挂 `UIBindComponent`，用右键菜单 `GameObject/ScriptGenerator/生成绑定代码` 生成字段，值在 `BindMemberProperty()` 里落地（发射器实现见 `Editor/Services/UI/Helper/UICodeEmitter.cs`）。
- 模态与交互压制读 `UIService.CurrentModal` / `IsBlockedByModal(GameObject)`，不要另立一套"锁屏"标志。
- 动到 UI 公开面时同步 `Documentation~/zh|en/UI.md` 与 `CHANGELOG.md`，口径见 `CLAUDE.md` 的「提交时的文档与 CHANGELOG」。

请告诉我要创建什么界面，我按上面的形状生成窗口类与预制体侧的绑定清单。
