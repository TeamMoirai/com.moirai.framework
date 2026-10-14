# Odin 抽屉（Custom Drawers）作业手册

文中 `Demos/...` 一律指**宿主工程**的 `Assets/Plugins/Sirenix/Demos/...`（本仓不含这些示例）。

## 类型与入口

基类链：`OdinDrawer` → `OdinDrawer<TValue>` → `OdinValueDrawer<T>` / `OdinAttributeDrawer<TAttr,TValue>`；
分组用 `OdinGroupDrawer<TGroup>`；菜单项用 `OdinMenuDrawer`。

`OdinDrawer` 上确认存在的方法：`Initialize` `CanDrawProperty` `CanDrawTypeFilter` `DrawProperty`
`DrawPropertyLayout` `CallNextDrawer`，扩展 `GetPersistentValue<T>`（存抽屉实例态，别用静态字段——
抽屉按属性实例化）。

常用成员（`InspectorProperty`）：`Name` `NiceName` `Label` `Children` `FindChild` `Parent` `Tree`
`Attributes` `GetAttribute<T>` `GetComponent<T>` `LastDrawnValueRect` `MarkSerializationRootDirty`
`RecordForUndo` `BaseValueEntry` `UnityPropertyPath` `Path` `PushDraw/PopDraw` `Update`。

值入口（`PropertyValueEntry` / `PropertyValueEntry<T>`）：`ValueCount` `Values` `WeakValues`
`SmartValue` `WeakSmartValue` `ValueState` `TypeOfValue` `IsEditable` `SerializationBackend`
`Property`，方法 `ApplyChanges()` `Update()` `UpdateValues()` `Initialize()`。
注意类名是 **`PropertyValueEntry`**，`ValueEntry` 只是抽屉上的属性名。

## 改值唯一正路：走 ValueEntry，不要裸写 SerializedProperty

**结论**：抽屉里 `someProp.stringValue = x; someProp.serializedObject.ApplyModifiedProperties();`
这种写法能改到数据，但**不会进 Odin 的变更链**，于是字段上的 `[OnValueChanged]`、`[ShowIf]` 依赖的
状态刷新、以及宿主的重绘/脏标记链路都收不到通知——静默不报错。

实测证据（同宿主、同一次操作、两个对照）：挂在 Odin 自绘枚举字段上的 `[OnValueChanged]` 回调触发，
挂在由抽屉裸写 `m_GUID` 的引用字段上的回调 **0 次触发**；改成 `ValueEntry` 赋值 + `ApplyChanges()` 后
才成立。官方成例即此写法：

```csharp
// Demos/Custom Drawers/Scripts/GenericMenuExample.cs:63-64
this.ValueEntry.SmartValue = color;
this.ValueEntry.ApplyChanges();   // 注释明说这是抽屉改值的正规路径
```
同形：`MinesweeperExample.cs:149`、`PriorityExamples.cs:41-43`、`CustomDrawerExample.cs:36,54`。

多目标要按 `ValueCount`/`Values[i]` 逐个写；引用类型**原地改字段不会被识别为变更**，
要么赋新实例进 `Values[i]`，要么改完再赋值一次。撤销用 `Property.RecordForUndo(...)` 或
`Property.MarkSerializationRootDirty()`，不要自己 `EditorUtility.SetDirty` 凑。

## 官方立场：布局写在属性上，抽屉只画"属性表达不了的呈现"

- 官方唯一的"薄抽屉"成例只做一件事——剥掉包装类多出来的一层标签：
  `Demos/Sample - RPG Editor/Scripts/Misc/StatList.cs:95-116`，
  `internal class StatListValueDrawer : OdinValueDrawer<StatList> { protected override void DrawPropertyLayout(GUIContent label) { this.Property.Children[0].Draw(label); } }`
- 抽屉换呈现而非换布局：`Editor/ItemDrawer.cs:10-14`（把 ObjectField 换成"图标+名字"预览）
- 何时该用抽屉而不是属性：同一份定制要复用到很多类型时（`Editor/ItemSlotCellDrawer.cs:11-16` 的注释即此取舍）
- 手写布局的代价样本：`CustomDrawerExample.cs:34-55` 为把两个 float 排一行，要 `GetControlRect` +
  判 `label != null` + 手改并复原 `EditorGUIUtility.labelWidth`——**能用排版属性就别写这段**
- 官方劝退手写反射：`ReflectionExample.cs:16-20`（"recommended to use ValueResolver<T> and ActionResolver"），
  配套 `ValueAndActionResolversExample.cs:82-121` 给 `ValueResolver.Get<T>` / `ActionResolver.Get` 用法

## 实测陷阱（都付过代价）

1. **`child.Draw(GUIContent.none)` 不一定抑制标签**。字符串抽屉会，枚举抽屉不会——于是"自绘标签列 +
   子属性无标签绘制"的排版在枚举行变成两个标签并排。判据：同一屏里哪一行多出了 nicify 的英文名。
2. **`InspectorProperty.Label` 可能是 nicify 的字段名而不是 `[LabelText]` 的显示名**（实测同一宿主里
   `m_PackageType` 的 `.Label` 给 "Package Type"，而抽屉自绘那张标签给 "打包方式"）。要显示名就从
   特性取，或干脆把标签交给子属性自己画。
3. **`GUILayoutUtility.GetLastRect()` 是 group 局部坐标，`Event.current.mousePosition` 是窗口坐标**，
   两者直接 `Contains` 得到的布尔是废读数。要量矩形用 `InspectorProperty.LastDrawnValueRect`。
4. **`GUI.tooltip` 是跨窗口、跨事件残留的全局静态**，逐事件读它只能证明"曾经有人登记过"，
   不能证明"本帧这个控件登记的"；IMGUI 真正的显示门控另有其物。归因请用开关式 A/B（把疑似来源摘掉看现象变不变）。
5. **`[Tooltip]` 经"抽屉自绘的标签"进入 IMGUI**：`GUILayout.Label(child.Label, ...)` 里那个
   `child.Label` 就带着字段特性的文案，与 Odin 的属性绘制无关。
6. **`EditorStyles` 在域重载的 `OnEnable` 里可能未就绪**（本包踩过：窗口因此空白），别拿它当绘制前提。
7. `GetLastRect` 只在 `Repaint`/`Layout` 事件合法，别的事件里调用会抛并污染控制台。

## 存在性与版本核对

任何"这个 API 在这台机器上有没有"的问题，跑 `scripts/odin_api_probe.py <Name> [<Name> ...]`
（自带正例反例，输出 PRESENT/ABSENT + 命中的 dll 与 XML 条目）。
不要引用官方文档站或别的项目版本的成员名——本包实测过：文档页里的
`DictionaryDrawerSettings`、`ValueDropdownList` 在装机版根本不存在。

## 在本工程怎么验证（项目特定，换工程要重核）

桥是本仓设施：`Tests/EditorMode/EditorStateBridge.cs`（状态桥 `Temp/MoiraiEditorState.json`）。UPM 包自己不能编译，靠宿主编辑器的文件桥（编辑器开着即可，不要 batchmode）：

```
# 1) 编译：写临时文件再 mv（重命名即原子消费）
printf '{"id":"x1","action":"refresh"}' > Temp/MoiraiEditorCommand.tmp && mv Temp/MoiraiEditorCommand.tmp Temp/MoiraiEditorCommand.json
# 2) 轮询 Temp/MoiraiEditorState.json：isCompiling 归 false、domainSeq 递增、consoleErrors 计数
# 3) 新鲜度自证：Library/ScriptAssemblies/<目标>.dll 的 mtime >= 源 mtime，且新符号在该 dll 命中 >0
# 4) 跑测试：Temp/MoiraiTestRequest.json {id,mode:EditMode,output,assemblies:[...],tests:[],timeoutSeconds}
#    只认 <output>.done 内容 == 本次 id，再看报告首行 failed N
```

`output` 路径必须用正斜杠；命令通道是共享的，回执里的 id 不是你的也≠你的动作没执行（判生效看产物）。
**IMGUI 交互（拖拽、悬停、tooltip 是否真画出来）测不了**，那类结论只能标成"待用户目测"。
