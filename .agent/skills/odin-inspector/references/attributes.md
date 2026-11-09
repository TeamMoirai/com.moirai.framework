# Odin 特性目录（项目惯用清单）

> 权威全集：https://odininspector.com/attributes （每特性含示例与参数说明）
> 本文件只列本项目实际使用 / 推荐使用的特性。标记 ⭐ 的有项目自定义扩展。

## 布局分组

| 特性 | 用途 | 项目示例 |
|------|------|----------|
| `FoldoutGroup("组名")` | 折叠组 | `FrameworkSettingsWindow` 搜索示例 |
| `TabGroup("组名", "页名")` | 页签组 | `ComprehensiveProbabilityWindow` |
| `BoxGroup("组名")` | 盒子组 | 各 Settings |
| `TitleGroup("标题", "副标题")` | 带标题组 | 配置面板 |
| `HorizontalGroup` / `VerticalGroup` | 行/列布局 | 值行内并排控件 |
| `ButtonGroup("组名")` | 按钮横排 | 多操作按钮 |
| ⭐ `InspectorGroup` | 项目自定义分组 | `Runtime/Core/Attributes/OdinExtends/InspectorGroupAttribute.cs` |

## 显示与标签

| 特性 | 用途 | 备注 |
|------|------|------|
| `LabelText("...")` | 标签文本 | 支持 `$表达式` / `@表达式`；被 `FrameworkSettingsWindow` 字段搜索识别 |
| `Tooltip("...")` | 悬停提示 | 与 Unity 原生同参，Settings 字段全用 |
| `InfoBox("...", InfoMessageType.Warning)` | 字段上方信息框 | |
| ⭐ `InfoBoxBelow` | 项目自定义：信息框置于字段下方 | `Runtime/Core/Attributes/OdinExtends/` |
| ⭐ `HelpInfo("标题", "内容")` | 项目自定义帮助块 | |
| `HideLabel` | 隐藏标签 | 列表项/预览字段 |
| `InlineProperty` | 内联展开子字段 | 窄宽度结构体 |
| `PreviewField(70)` | 资源预览图 | 贴图/Sprite 字段 |
| `PropertyOrder(n)` | 排序 | 结果字段沉底用大值（如 100） |
| `PropertySpace(n)` | 额外间距 | |
| `GUIColor("...")` | 着色 | 状态提示 |
| `DisplayAsString` / `DetailedInfoBox` | 只读展示 | |

## 按钮

| 特性 | 用途 | 备注 |
|------|------|------|
| `Button("文本")` | 方法变按钮 | 可加参数 `Button(ButtonSizes.Large)`；OdinEditorWindow 里最常用 |
| `ButtonGroup` | 按钮组 | |
| `InlineButton` | 字段行内按钮 | |
| ⭐ `InlineEnableButton` | 项目自定义：行内启停按钮 | Drawer 处理列表元素场景 |

## 校验

| 特性 | 用途 | 备注 |
|------|------|------|
| `Required("提示")` | 非空校验 | 配 Odin Validator 扫描 |
| `ValidateInput("方法名")` | 自定义校验 | |
| `MinValue(n)` / `MaxValue(n)` | 数值钳制 | `[MinValue(16)]`（SaveServiceSettings 截图尺寸） |
| `AssetsOnly` / `SceneObjectsOnly` / `ChildGameObjectsOnly` | 限定拖入对象 | |

## 下拉与选择

| 特性 | 用途 | 备注 |
|------|------|------|
| ⭐⭐ `ProviderDropdown` | **项目核心**：`[SerializeReference]` 多态实现类下拉（TypeCache 自动发现候选） | 见 SKILL.md §3；三宿主共用一套逻辑 |
| `ValueDropdown("方法名")` | 值下拉（`IEnumerable`/`ValueDropdownItem`） | ⭐ `ConditionDropdown`（项目自定义）在其上扩展 `ShowIf` |
| `ValueDropdownItem("文本", value)` | 下拉项显示值/真实值 | 实现 `IValueDropdownItem` 可自定义文本 |
| `AssetList(Path = "/..", AutoPopulate = .., LayerNames = .., AssetNamePrefix = ..)` | 资源列表过滤 | |
| `TypeFilter` | Object 字段类型过滤 | |

## 条件显隐

| 特性 | 用途 | 备注 |
|------|------|------|
| `ShowIf` / `HideIf` / `DisableIf` / `EnableIf` | 条件显隐/禁用 | 值为成员名或 `@表达式` |
| `ReadOnly` | 只读 | 输出结果字段 |

## 集合

| 特性 | 用途 | 备注 |
|------|------|------|
| `TableList(AlwaysExpanded = true)` | 表格化列表 | 需要列头时用 |
| `ListDrawerSettings(ShowFoldout = .., DraggableItems = ..)` | 列表行为 | |
| `DictionaryDrawerSettings(KeyLabel = .., ValueLabel = ..)` | 字典展示 | |

## 特性参数字符串求值

所有接受 string 的特性参数（LabelText/ShowIf/InfoBox 等）支持三种模式：

```csharp
[LabelText("普通文本")]              // 字面量
[LabelText("$memberName")]          // $ = 引用成员（字段/属性/方法返回值）
[LabelText("@obj?.DisplayName")]    // @ = 表达式求值
```

自定义 Drawer 侧对应 `ValueResolver.Get<T>(Property, 表达式, default)`，检查 `resolver.ErrorMessage` / `resolver.HasError` 后再取值。
