# Odin 4.0.x 专属契约与实证坑（本项目）

> 逐条含「症状 → 根因 → 正确写法」。对照官方 patch notes：https://odininspector.com/patch-notes
> 排查 Odin 窗口渲染问题总入口：**最小探针窗口 + 二分 override** 法（文末）。

## 目录

1. OdinMenuEditorWindow 三条硬契约
2. OdinEditor 嵌入 ScrollView 高度失效
3. 4.0.x managed reference 绘制接管（DrawerPriority + UnityPropertyPath 回退）
4. OdinEntityId（int InstanceID 迁移）
5. 域重载早期 EditorStyles NRE
6. PropertyTree / InspectorProperty 常用正确姿势
7. Visual Designer（4.0 新增）
8. 探针窗口排查法

---

## 1. OdinMenuEditorWindow 三条硬契约

### 1.1 override OnImGUI 必须调 base.OnImGUI()

**症状**：整窗空白，Console 零报错。
**根因**：`OnImGUI` 是绘制链虚入口，override 后不调 base 即无任何绘制。
**写法**：顶部工具栏窗口 = 先画自定义内容，再调 base：

```csharp
protected override void OnImGUI()
{
    // 顶部工具栏（自定义内容）
    using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
    {
        // 注意：条件按钮的选中条目仅在 Layout 遍求值——帧内改状态会触发
        // "Getting control X's position in a group with only Y" 控件数异常。
        // 先在 Layout 遍把选中条目缓存成局部值，再画按钮。
    }
    base.OnImGUI(); // 必须
}
```

### 1.2 僵尸态兜底：基类不建树、懒建路径不补建

**症状**：`isInitialized == true` 但 `menuTree == null`、窗口全空、无报错（域重载后偶发）。
**根因**：基类 `OnEnable` 不构建菜单树，懒建路径遇到 Odin 样式未就绪也不补建。
**写法**：子类自行 `ForceMenuTreeRebuild()` 兜底 + `EditorStyles.label != null` 守卫 + `OnImGUI` 空树时经 `delayCall` 防抖重建：

```csharp
private bool _rebuildQueued;

protected override void OnEnable()
{
    base.OnEnable();
    if (EditorStyles.label != null) ForceMenuTreeRebuild();
}

private void QueueTreeRebuild()
{
    if (_rebuildQueued) return;
    _rebuildQueued = true;
    EditorApplication.delayCall += () =>
    {
        _rebuildQueued = false;
        if (_entries == null || _entries.Count == 0) ForceMenuTreeRebuild();
        Repaint();
    };
}
```

### 1.3 OdinMenuItem API（Odin 4 变更）

- `OdinMenuItem.ObjectInstance` 已弃用（CS0618）→ 用 **`OdinMenuItem.Value`**。
- `TrySelectMenuItemWithObject` 经 `trySelectObject` 延迟到下一次 OnGUI 消费——**窗口无焦点不生效**。
- 编程式选中（不依赖焦点）：`menuTree.Selection.Clear(); item.Select(false);`
- `MenuWidth` 不用属性 override（基类内部读字段），经 setter 赋值：`MenuWidth = 260f;`
- 侧边栏加粗：`tree.DefaultMenuStyle.Clone()` 换 `DefaultLabelStyle/SelectedLabelStyle` 的 `FontStyle.Bold` 回填。
- 状态图标：`item.Icon = SdfIconType.CheckCircleFill / XCircleFill` + `SdfIconColor` 上色。

## 2. OdinEditor 嵌入 ScrollView 高度失效

**症状**：`EditorGUILayout.BeginScrollView` 内嵌 `odinEditor.OnInspectorGUI()`，Layout 事件报告内容高度 ≈1px，底部内容裁剪、不可点击。
**根因**：`OdinEditor.OnInspectorGUI()`（Odin PropertyTree 自管布局）不通过 GUILayout 分配 rect，`GUILayoutUtility.GetLastRect()` 拿不到真实高度。
**写法**（`FrameworkSettingsWindow._cachedContentHeight` 模式）：Repaint 事件测量并缓存 yMax，Layout 事件用 `GUILayout.Space` 占位；切换条目时重置缓存为 0。首帧预热（height=0）不可见属正常。

```csharp
_editorScroll = EditorGUILayout.BeginScrollView(_editorScroll);
editor.OnInspectorGUI();
if (Event.current.type == EventType.Repaint)
    _cachedContentHeight = GUILayoutUtility.GetLastRect().yMax;
else
    GUILayout.Space(_cachedContentHeight);
EditorGUILayout.EndScrollView();
```

## 3. 4.0.x managed reference 绘制接管

为 `[SerializeReference]` 字段写自定义 Odin Drawer（如 `ProviderDropdownOdinDrawer`）时：

1. **`[DrawerPriority(1, 0, 0)]`（super=1）**——低于它时 4.0.x 默认 managed reference drawer 与 `DrawWithUnity(10000)` 抢占绘制，实现类字段上的 Odin 特性（`[ValueDropdown]` `[LabelText]`…）全部失效。
2. **`Property.UnityPropertyPath` 在 4.0.x 可能解析失败**（null 或抛异常）→ 取 Unity `SerializedProperty` 必须 try/catch + null 检查，失败时退化为 `Property.ValueEntry` 驱动路径（读 `WeakSmartValue`、写 `WeakSmartValue`），**不要**回退到 Odin 默认 managed-reference 绘制（其子内容渲染易失效）。
3. 类型切换后调 `Property.Update(true)` 强制重解析值与子属性。
4. 子属性可见性取 `child.State.Visible`（Odin 已处理 `[HideInInspector]` 等），与 Unity 侧 `NextVisible` 取并集。
5. 子内容交 Odin 绘制：`GUIHelper.PushIndentLevel(1)` / `PopIndentLevel()` 包裹，逐个 `child.Draw()`。
6. foldout 状态持久化：`this.GetPersistentValue<bool>("Key", default)`（Odin 提供，跨域重载稳定）。
7. 字符串表达式求值一律 `ValueResolver.Get<T>(Property, 表达式, 默认值)`，先查 `HasError/ErrorMessage`。

## 4. OdinEntityId（int InstanceID 迁移）

Odin 4.0.x 为适配 Unity EntityId 后端（Unity 6000.3.23f1 属此类版本线）：

- 暴露/接受实例 ID 的 Validator/Editor API 出现 **`OdinEntityId`** 重载（4.0.2.1 起类型在 `Sirenix.Reflection.Editor`，此前在 `Sirenix.Utilities.Editor`）。
- 旧 `int` InstanceID 重载标记 obsolete；在 EntityId 后端的 Unity 版本上**不可桥接的重载直接抛 `NotSupportedException`**。
- 新代码涉及对象 ID 一律用 `OdinEntityId` 重载；迁移老代码时逐个检查编译警告。

## 5. 域重载早期 EditorStyles NRE

**症状**：域重载后 Odin 窗口/Drawer 首帧抛 NRE，堆栈在 `SirenixGUIStyles.get_Label` 一类内部样式访问。
**根因**：`EditorStyles` 尚未初始化（属性返回 null），Odin 内部样式克隆时解引用。
**写法**：任何构建树/克隆样式/初始化 GUIStyle 的代码加 `EditorStyles.label != null` 守卫；不满足就跳过本帧等下一次 OnGUI 重试（参照 `FrameworkSettingsWindow.Styles.Init()` 模式：初始化失败保持 `IsReady=false`，`Repaint()` 后重试）。**禁止在 `OnEnable` 直接初始化 Odin/GUI 样式。**

## 6. PropertyTree / InspectorProperty 常用正确姿势

```csharp
// 取值（多对象编辑）
var values = Property.ValueEntry.WeakValues;      // IPropertyValueEntry
var single = Property.ValueEntry.WeakSmartValue;  // 单值便捷

// 列表判定与写入
bool isList = Property.ChildResolver is ICollectionResolver;
var resolver = (ICollectionResolver)Property.ChildResolver;
resolver.QueueClear();
resolver.QueueAdd(valuesArrayPerParent);          // 每父对象一个槽位数组

// 深拷贝写入
Property.ValueEntry.WeakValues[i] = SerializationUtility.CreateCopy(obj);

// 子属性遍历（Odin 侧可见性）
var children = Property.Children;
for (int i = 0; i < children.Count; i++)
    if (children[i].State.Visible) children[i].Draw(); // 或 Draw(label)

// 值变化后重解析
Property.Update(true);

// 下拉弹层
var selector = new GenericSelector<object>(title, false,
    items.Select(x => new GenericSelectorItem<object>(x.Text, x.Value)));
selector.EnableSingleClickToSelect();             // 单击选中（默认双击）
selector.SetSelection(current);
selector.SelectionTree.EnumerateTree().AddThumbnailIcons(true);
selector.ShowInPopup(rect, new Vector2(w, h));
// 或行内绘制：OdinSelector<object>.DrawSelectorDropdown(label, btnLabel, rect => selector)
```

- 枚举树：`selector.SelectionTree.EnumerateTree(x => x.Toggled = true)` 展开；`SelectionTree.Config.DrawSearchToolbar = true` 加搜索条；`SortMenuItemsByName()` 排序。
- 混合 UI（`SirenixEditorGUI.Foldout` / `BeginFadeGroup` / `ErrorMessageBox`）优先于手写 IMGUI。

## 7. Visual Designer（4.0 新增）

- 右键 `OdinEditorWindow` 页签可定制窗口本身；右键 `OdinMenuEditorWindow` 页签定制当前选中条目。
- 它会把**代码添加的特性打标签**；OVDF 文件存类型 meta GUID（脚本改名/移动仍可解析）。
- 代码开窗：静态类 `OdinVisualDesigner`。
- 不要在 Drawer/窗口代码里与 Visual Designer 的内部状态对抗；它不能定制 Odin 无法创建 Editor 的类型。

## 8. 探针窗口排查法

Odin 窗口渲染异常且无报错时，在同一文件里定义最小探针 `OdinMenuEditorWindow`/`OdinEditorWindow` 子类：

1. 探针 A：只 override 生命周期（复现基类行为）。
2. 探针 B：A + override `OnImGUI` 调 base（验证绘制链）。
3. 探针 C：B + 逐段加自己的逻辑。
4. A/B/C 对照二分定位到具体 override。

## 9. Odin 4.0.2.4 绘制行为实证（BuildConfig Odin 化会话）

1. **FoldoutGroup 默认折叠**：`FoldoutGroup("名", order)` 的组默认 `Expanded=false`——需默认展开必须显式 `FoldoutGroup("名", true, order)`。
2. **LabelText 是 Drawer 处理**（`LabelTextAttributeDrawer : OdinAttributeDrawer`）：PropertyTree 枚举期 `property.Label` 仍是默认驼峰拆分名，LabelText 在绘制期才生效——验证标签只能靠视觉门禁/实际绘制，勿用枚举期 `Label` 断言。
3. **默认成员排序：同 PropertyOrder 下序列化字段先于 `[ShowInInspector]` 属性**（与声明顺序无关）。字段间夹属性时需显式 PropertyOrder 锚定（BuildConfig.PackageVersion 实证：属性 order 1 + 后续字段 order 2；order -1 会把该行提到全组最前）。
4. **自定义 Editor 改 Odin 后嵌入方同步改**：`OdinEditor.CreateInspectorGUI()` 返回 null（Odin 仅 IMGUI 绘制）——原 UITK `CreateInspectorGUI()` 嵌入点必须改为 `IMGUIContainer(editor.OnInspectorGUI)`，否则宿主窗口空白且无报错。
5. **编辑器程序集类型 Odin 默认编辑器不接管**：SO 定义在 Editor 程序集（如 `BuildConfig`）且无 `[CustomEditor]` 时，`Editor.CreateEditor` 回退 `GenericInspector`（Unity 默认绘制），全部 Odin 特性静默失效且无报错。修法二选一：(a) 显式 `[CustomEditor] XxxEditor : OdinEditor`；(b) 宿主窗口改 `OdinEditorWindow` + `PropertyTree.Create(new SerializedObject(target))` 嵌入 `IMGUIContainer(tree.Draw)`（`BuildPipelineWindow` 先例——PropertyTree 绑定 SerializedObject 时撤销/脏标记/写回穿透由树自带，`tree.ApplyChanges()` 即落值；生命周期 `OnEnable/OnDisable` 须 override+base，树与 SerializedObject 随窗口切换/关闭 Dispose）。
6. **OdinEditorWindow 会向 rootVisualElement 注入自身属性树的 IMGUIContainer，且参与布局**：窗口自身无 Odin 成员可绘制时该容器不可见但仍吃掉实测高度（BuildPipelineWindow 实证 720 窗高被占 368px，SplitView 被挤到 352px 即「一半空白」）；注入时机晚于 OnEnable（首个 OnGUI 前后），`root.Clear()` 后 Odin 会重建。修法：OnEnable 中立即隐藏一次 + `rootVisualElement.RegisterCallback<AttachToPanelEvent>` 兜底重建（`BuildPipelineWindow.HideOdinInjectedContainer` 模式，只处理根的直接子级）；同时窗口主内容（SplitView 等）须显式 `flexGrow=1` 才能撑满剩余高度。另外注意：**同步脚本内读 worldBound 是布局前过期值**（`w.position` 改尺寸 + Repaint 后须 `yield return null` 等布局帧再测）。
