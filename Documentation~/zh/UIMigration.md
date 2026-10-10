# UI 载荷迁移

> 硬切一笔的迁移口径：`params object[]` 载荷形态从全部公开腿退役，改强类型 `Payload` 槽（静态腿）与 `UIPayload` 擦除载体（动态腿）；`UIWindowEvent` 与 `UIServiceEvent` 两个事件一并退役。本页是外部业务工程（第五消费方）的一页改完清单：声明 DTO 并换基类 → 写点两种去向 → 行为变更四条。

契约全貌与签名表见 [UI 服务](UI.md)。无载荷调用点（占绝对多数）不用动：`ShowUI<T>` / `ShowUIAsync<T>` / `ShowUIAsyncAwait<T>` / `ShowUIAwaitResult<T>` 只是尾参多了 `ct`，原有实参形状照常绑得上。

## 一、声明 DTO 并换基类

带载荷的窗口先声明一个 DTO（`struct` 优先——静态腿 `in TArg` 泛型直塞不装箱），再把基类换成带槽的那个：

| 旧 | 新 |
|---|---|
| `class MyWindow : UGUIWindow` | `class MyWindow : UGUIWindow<MyWindowPayload>` |
| `class MyWindow : UITKWindow` | `class MyWindow : UITKWindow<MyWindowPayload>` |
| 读点 `UserData?.ToString()` / `(string)UserData` | `Payload`（已是强类型，不再转） |
| 读点 `_params[i]` / `Params[i]`（含 `Params.Length` 判空那一档） | `Payload.字段`（一次开窗最多一个 DTO，没塞的字段按 DTO 自己的默认值） |

```csharp
// 旧写法（已退役）：位置实参，读点靠序号
[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow
{
    protected override void OnRefresh()
    {
        var initial = Params.Length > 0 ? (string)Params[0] : string.Empty;
        _input.text = initial;
    }
}

// 新写法：一个 DTO，名字读
public struct RenameWindowPayload
{
    public string InitialText;
    public int MaxLength;
}

[Window(EUILayer.Popup)]
public class RenameWindow : UGUIWindow<RenameWindowPayload>
{
    protected override void OnRefresh()
    {
        _input.text = Payload.InitialText;
        _input.maxLength = Payload.MaxLength;
    }
}
```

> 工具链注：本工程工具链（C# 9 / netstandard2.1，无 `IsExternalInit` polyfill）下 `readonly struct` 配公共可写字段不编译（初始化点 CS8340；字段改 `readonly` 再配对象初始化器是 CS0191，`{ get; init; }` 是 CS0518），DTO 用普通 `struct` + 公共字段 + 对象初始化器。

- 一个 DTO 装全部字段：旧写法把三个值排成三个位置，新写法把错位可能整个消掉——载荷与 `panelSettings` 谁吃谁的第一个，由签名表定死
- `Payload` 每次开窗覆盖、关闭不清：读它之前不需要判空（拿到的就是这一次的赋值）；无载荷腿再开同一个窗也不清残留，残留到下一次覆盖为止
- 不带载荷的窗口不必换基类：直继 `UGUIWindow` / `UITKWindow` 照旧可开，只是被塞非空载荷时当场抬 `GameException`（文案带窗口类名）——漏换基类的调用点由这一句指出来，而不是静默吞掉

## 二、写点两种去向

读点改完改写点，按「编译期是否已知窗口类」分两种去向。

### 静态腿：编译期已知窗口类（绝大多数写点）

标识在前、载荷随后，两个类型实参给出窗口类与 DTO 类：

```csharp
var dto = new RenameWindowPayload { InitialText = current, MaxLength = 16 };

UIService.ShowUIAsync<RenameWindow, RenameWindowPayload>("rename", in dto);                                     // 异步
UIService.ShowUI<RenameWindow, RenameWindowPayload>("rename", in dto);                                          // 同步档
RenameWindow w = await UIService.ShowUIAsyncAwait<RenameWindow, RenameWindowPayload>("rename", dto);            // 等待腿（async 禁 in）
UIOpenResult r = await UIService.ShowUIAwaitResult<RenameWindow, RenameWindowPayload>("rename", in dto);        // 结果腿
```

落地签名与位置序：`(string windowId, in TArg payload, CancellationToken ct = default)`；UI Toolkit 腿在载荷之后、`ct` 之前多一个 `PanelSettings panelSettings = null`。开窗标识必填，取法不由入口给。

手写 `Show` 助手时把标识直接交给腿（旧写法先自己算地址，如今门面按档换算）：

```csharp
public static void ShowRenameWindow(RenameWindowPayload dto)
{
    const string WindowId = "RenameWindow";
    UIService.ShowUIAsync<RenameWindow, RenameWindowPayload>(
        in dto,
        WindowId);
}
```

- 那个窗口标识不是面板地址：`fromResources` 为真时按 `UIServiceSettings` 的 Resources 父目录拼地址，为假时按标识查配置表；两档的换算只在开窗造新实例那一格发生
- 取法只由 `[Window(fromResources:)]` 决定（调用方不再带这一档）；配置表服务未就绪时取到 `null`、查无此 id 取到空串，两者都落进装载失败回滚，不在门面代答

### 动态腿：运行期才知道 `Type`（类型替换缝、注册表驱动的开窗）

`TArg` 运行期未知，载荷擦进 `UIPayload` 这个唯一载体：

```csharp
// 旧写法在末位排一个位置实参数组（已退役）；新写法把载荷收进 UIPayload 这个载体
UIService.ShowUIAsync(type, windowId, UIPayload.From(dto), ct);
UIService.ShowUI(type, windowId, UIPayload.From(dto), ct);        // 同步档同形
UIWindow win = await UIService.ShowUIAsyncAwait(type, windowId, UIPayload.From(dto), ct);
```

- 动态腿共三支（异步 / 同步 / 等待），`UIPayload payload` 恒排在 `ct` 之前；结果腿只有泛型形，Type 形入口不带结果档
- 引用型载荷只存引用：0 分配、到达后 `Payload` 与送出的那个引用同一；值类型经 `object` 装箱一次——热路径上的基元与 struct 尽量改走静态腿
- 空载荷与 `null` 同判：`UIPayload.Empty`、`default(UIPayload)`、`UIPayload.From(null)` 三者等价，无载荷腿传的就是这一档
- 取回按窗口类的 `TArg`：`To<T>()` 类型不符、或空载荷作用于值类型时抬 `GameException`（消息带期望类型名），不想抬错用 `TryGet<T>(out T)`
- 带 `mgr.SomeWindowType` 那一类支路（运行期换窗口类）保持动态腿、直达支路换静态腿：同一个 DTO 形状两通道共用，不必为动态腿单开一个载体

## 三、行为变更四条

1. **`UIWindowEvent` 整体退役**：这个事件类连同 `Show` / `Close` / `Hide` / `CloseAll` 的全部形态一并删除，开合窗不再有「发事件 + 订阅者转手」那一段中转，调用点直调门面腿即时执行——连不带载荷的 `Show` 也要换。旧→新对照：`Show<T>(id)` → `UIService.ShowUIAsync<T>(id)`（要同步交回换 `ShowUI<T>`）、`Show(type, id)` → `UIService.ShowUIAsync(type, id)`、`Close<T>(id)` → `UIService.CloseUI<T>(id)`、`Close(type, id)` → `UIService.CloseUI(type, id)`、`Hide<T>(id)` → `UIService.HideUI<T>(id)`、`Hide(type, id)` → `UIService.HideUI(type, id)`、`CloseAll()` → `UIService.CloseAll()`。带载荷的写点按第二节换。
2. **开合窗回执改走门面静态事件**：`UIServiceEvent`（连同标记接口 `IUIEvent`）删除，不再经 `EventManager` 派发；订阅点换成 `UIService.onWindowShown` / `UIService.onWindowClosed`（`public static event Action<UIWindow>`，形参即那个窗口）。入栈/出栈各恰一次、停放与销毁都发；退订由订阅者自己配对，门面关停与归零门会整批摘掉。
3. **在飞合并 last-wins**：同一个窗装载在途时再开（任意腿、任意通道）不重开发装载、不压第二个实例，载荷覆盖为最后一个；`OnRefresh` 只在面板就绪那一次跑，见的是终载荷。旧写法「两次 Show 各刷一次」的假设不再成立——需要在途里换内容，请等结果腿交回或先关再开。
4. **全腿收 `CancellationToken`**：每支腿尾参 `CancellationToken ct = default`，不传零开销；令牌只在装载在途那段被消费（已就绪的复用与停放重取同步交回、不消费 `ct`；复用一个仍在装载的窗时，令牌照样登记，撤销会掐断那一次在途装载——与在飞合并同段语义）。撤销的落点按腿分档：void 腿静默回滚出栈（不报 Error），等待腿原样上抛 `OperationCanceledException`，结果腿落 `EUIOpenStatus.Cancelled`（新增档，与 `Timeout` 可分辨）。`Failed` 的窗口已回滚作废、不得复用；`Cancelled` 只说明本次等待以取消落定，装载是否续跑取决于其余等待者（无人在等则回滚）——两者都不得当就绪窗复用。

## 收口自检

- 全域不再出现 UI 载荷的 `userData` / `UserData` / `Params` / `_params` / `params object[]` 写法
- 带载荷窗口基类一律带 `<TArg>`；同一个 DTO 形状两通道共用
- 全域不再出现 `UIWindowEvent` 与 `UIServiceEvent`；寻址经 `ResolveWindowLocation` / `ResolveFromResources` 这一对
- 绑不上的三档都有明确指认：漏换基类 → 泛型约束绑不上（编译期）或被塞非空载荷时 `GameException`（运行期，文案带窗口类名）；槽型不符 → `GameException`（带期望与实际类型名）；把 `in` 实参写给等待腿 → 那一支本就是普通形参，按签名传即可

---
[« 返回文档索引](Index.md) · [UI 服务](UI.md) · [主 README](../../README.md)
