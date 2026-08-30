# Input 服务

> 抽象输入层：以统一的轮询 API 屏蔽 Unity 新旧输入系统与移动端 UI 触控的差异，并内置按键提示（Prompts）系统。

输入服务（`Moirai.Atropos.Input`）通过 `InputServiceHandler` 抽象三种输入后端，业务代码只需面向 `InputService.Xxx()` 静态外观的动作名（Action）API 编程，切换后端无需改动调用方。服务还监听 UI 模态事件与应用焦点事件，自动屏蔽/恢复输入，并基于 Input System 提供跨设备的按键图标提示组件。

## 核心特性

- 三种输入后端可配置：新版 Input System、旧版 Input Manager、移动端 UI 触控组件
- 统一动作轮询 API：`GetButtonDown` / `GetButtonUp` / `GetButtonPressed` / `GetBool` / `GetFloat` / `GetVector2`，支持动作分组（`actionGroup`）
- 鼠标专用查询：按键三态、位置、滚轮（新旧系统滚轮值已归一化对齐）
- 输入状态开关：`Enabled`（全局）、`LockPlayerController`（锁角色控制）、`PreventInteractionUI`（锁 UI 交互），切换时自动重置残留输入状态
- UI 模态联动：监听 `UIServiceEvent`，存在模态窗口时自动锁定玩家控制
- 应用焦点联动：失焦自动禁用输入，聚焦自动恢复
- 按键提示系统（Prompts）：按键图标随当前活动输入设备自动切换，支持图文混排

## 核心类型

| 类/接口 | 说明 |
|---------|------|
| `Moirai.Atropos.Input.InputService` | 输入服务静态外观（`[HandlerHost]`），全部轮询 API 为静态方法，经 `Handler` 属性转发（fail-fast：未就绪时按需初始化，工厂缺失时抛异常，不静默降级） |
| `Moirai.Atropos.Input.InputServiceHandler` | 输入处理器抽象基类（普通运行时类，不参与序列化），定义全部输入查询方法。由 `InputServiceConfig` 经工厂创建 |
| `Moirai.Atropos.Input.UnityInputSystemHandler` | 基于 Unity Input System 的处理器（宏 `ENABLE_INPUT_SYSTEM`） |
| `Moirai.Atropos.Input.UnityInputManagerHandler` | 基于旧版 Input Manager 的处理器（宏 `ENABLE_LEGACY_INPUT_MANAGER`） |
| `Moirai.Atropos.Input.UIMobileInputHandler` | 移动端处理器，读取场景中 `InputButton` / `InputAxes` 组件状态 |
| `Moirai.Atropos.Input.InputServiceSettings` | 框架设置（"输入设置"），通过 `[SerializeReference]` 配置输入处理器并懒加载初始化 |
| `Moirai.Atropos.Input.InputActionsConfiguration` | 输入动作配置资产，按分组登记 bool/float/Vector2 动作名，用于生成代码 |
| `Moirai.Atropos.Input.EMouseButton` | 鼠标键枚举：`Left = 0`、`Right = 1`、`Middle = 2` |
| `Moirai.Atropos.Input.BoolAction` / `FloatAction` / `Vector2Action` | 可序列化动作值结构体，含按下/抬起状态与方向判断 |
| `Moirai.Atropos.Input.InputButton` | 移动端 UI 按钮（UGUI 事件实现 `IUIBoolAction`），菜单 `Tools/Input/UI/Input Button` |
| `Moirai.Atropos.Input.InputAxes` | 移动端虚拟摇杆（`IUIVector2Action`），支持死区/反转/回弹，菜单 `Tools/Input/UI/Input Axes` |
| `Moirai.Atropos.Input.PreventInputOnEnable` | 辅助组件：启用时锁定输入、禁用时恢复 |
| `Moirai.Atropos.Input.Prompts.InputDevicePromptSystem` | 提示系统核心（静态类），维护动作绑定与设备字形映射 |
| `Moirai.Atropos.Input.Prompts.PromptActionIcon` / `PromptActionText` / `PromptDeviceIcon` | 按键/图文混排/设备图标显示组件 |
| `Moirai.Atropos.Input.Prompts.GlyphMap` / `GlyphCollection` | 设备字形映射资产 / 字形集合资产 |

## 快速上手

在框架设置（Project Settings 的"输入设置"）中选择输入处理器后，即可直接轮询动作：

```csharp
// Input System 后端：分组名/动作名 对应 Action Map/Action
if (InputService.GetButtonDown("Jump", "Player"))
{
    // 本帧按下跳跃键
}

float moveX = InputService.GetFloat("Move", "Player");
Vector2 move = InputService.GetVector2("Move", "Player");

// actionGroup 传空时 actionName 视为完整路径（"Player/Jump"）
bool submit = InputService.GetButtonPressed("UI/Submit");

// 鼠标
if (InputService.GetMouseButtonDown(EMouseButton.Right)) { }
Vector2 pos = InputService.GetMousePosition();
Vector2 scroll = InputService.GetScrollDelta();
```

锁定/恢复输入：

```csharp
InputService.LockPlayerController = true;    // 模态弹窗期间锁角色移动
InputService.PreventInteractionUI = true;    // 过场动画期间禁用 UI 交互
InputService.Enabled = false;                // 全局禁用（重置所有输入状态）
```

## 进阶用法

### 输入后端差异

| 处理器 | 动作解析方式 | 备注 |
|--------|-------------|------|
| `UnityInputSystemHandler` | `$"{actionGroup}/{actionName}"` 查找 `InputSystem.actions` 中的 `InputAction` | 需在 Project Settings → Input System Package 配置 Action Asset；滚轮值除以 120 与旧系统对齐 |
| `UnityInputManagerHandler` | `actionName` 即 Input Manager 轴名；Vector2 按 `"{name} X"` / `"{name} Y"` 组合两轴 | 使用 `GetAxisRaw`，不存在的轴会输出警告 |
| `UIMobileInputHandler` | 按 `ActionName` 查找场景中的 `InputButton`（bool）与 `InputAxes`（Vector2） | 鼠标相关接口恒为默认值 |

### 移动端 UI 输入组件

`UIMobileInputHandler` 依赖场景中的 UI 组件产生输入，二者的动作名需与角色动作一致：

- `InputButton`：实现 `IPointerDownHandler` / `IPointerUpHandler`，按住时 `BoolValue == true`
- `InputAxes`：虚拟摇杆，支持圆形（Radial）/按轴（PerAxis）两种死区模式、`m_BoundsRadius` 摇杆半径、`m_ReturnLerpSpeed` 回弹速度、水平/垂直反转

### InputActionsConfiguration 与代码生成

通过 `Create Asset → Moirai Framework/Input/InputActions Config` 创建配置资产，登记按键组（`m_ActionsGroup`）与各类型动作名数组，配合编辑器生成强类型访问代码（与 Input System 的 Generate Class 功能类似）。

序列化动作值结构体可直接嵌入组件，逐帧更新状态：

```csharp
private BoolAction _jump = new BoolAction();

void Update()
{
    _jump.Value = InputService.GetBool("Jump", "Player");
    _jump.Update(Time.deltaTime);

    if (_jump.IsDown) { }             // 本帧按下（等价 Started）
    if (_jump.IsPressed) { }          // 持续按住
    if (_jump.IsUp) { }               // 本帧抬起（等价 Canceled）
}
```

`Vector2Action` 额外提供 `Detected`、`Right`、`Left`、`Up`、`Down` 方向判断。

### 按键提示系统（Prompts）

基于 Input System，按键图标随最近一次产生输入的设备自动切换（`InputDevicePromptSystem.OnActiveDeviceChanged`）：

```csharp
// 图文混排：PromptActionText（TextMeshProUGUI），标签格式 {action:动作路径}
// 示例文本：Press {action:UI/Submit}
m_TextField.text = InputDevicePromptSystem.InsertPromptSprites(m_OriginalText, isComposite: false);

// 单独按键图标：PromptActionIcon（Image），Action 填完整路径如 "Player/Move"
Sprite sprite = InputDevicePromptSystem.GetActionPathBindingSprite("Player/Move", false);

// 设备图标（如手柄类型图标）
Sprite device = InputDevicePromptSystem.GetDeviceSprite(spriteName);
```

配置资产：

- `GlyphMap`（`Moirai Framework/Input/Glyph Map`）：单个设备的动作绑定路径到图标映射
- `GlyphCollection`（`Moirai Framework/Input/Glyph Collection`）：同一主题多设备字形集合，含未连接/未绑定/无效动作时的兜底图标
- `InputSystemDevicePromptSettings`（框架设置）：登记 InputActionAsset、字形集合、默认设备优先级、平台覆盖与富文本标签

通过 Package Manager 导入示例 `Samples~/InputSystem Action Prompts`（含 Xelu Prompts 图标集、示例场景与字体）可快速上手。

### PreventInputOnEnable

挂载该组件的对象启用时按勾选项锁定 `LockPlayerController` / `PreventInteractionUI`，禁用时恢复原值，适合过场、教程等节点式控制。

## 注意事项

- 后端配置在框架设置"输入设置"中通过 `[SerializeReference]` 选择（`InputServiceConfig` 子类，如 `UnityInputSystemConfig`），运行时经 `InputServiceSettings.InputServiceConfig.CreateHandler()` 懒创建；切换处理器需重启生效
- `UnityInputSystemHandler` / `UnityInputManagerHandler` 分别受 `ENABLE_INPUT_SYSTEM` / `ENABLE_LEGACY_INPUT_MANAGER` 宏控制编译
- 存在 UI 模态窗口时 `LockPlayerController` 恒为 true（由 `UIServiceEvent` 驱动），属预期行为
- `UIMobileInputHandler` 的 `GetButtonDown` / `GetButtonUp` 尚未实现（抛出 `NotImplementedException`），仅使用 bool 持续态查询
- 输入查询应每帧轮询调用，服务本身不做事件推送

---
[« 返回主 README](../../README.md) · [UI](UI.md) · [Scene](Scene.md)
