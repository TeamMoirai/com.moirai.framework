# Odin 属性清单（按装机版本核过）

## 枚举的正确姿势

**从 dll 枚举，不要从 XML 枚举。** `Assemblies/*.xml` 只收"写了注释的成员"，所以：

- XML 里查不到 ≠ API 不存在（实测：`TypeFilterAttribute`、`UnitAttribute`、`RequiredListLengthAttribute`、
  `PolymorphicDrawerSettingsAttribute`、`TypeDrawerSettingsAttribute` 在 `Sirenix.OdinInspector.Attributes.xml`
  里都没有 `T:` 条目，但在 dll 的 #Strings 堆里都在）
- 方法（`M:`）与属性（`P:`）是两套条目，只 grep `M:` 会漏掉所有属性成员（我因此误判过一次）
- 官方文档站描述的是**最新版**，不是本机这版；存在性一律以 `scripts/odin_api_probe.py` 的读数为准

一条命令核多个名字（正例反例都要放，验证探针自己活着）：

```
python scripts/odin_api_probe.py ValueDropdownAttribute DictionaryDrawerSettingsAttribute ApplyChanges
```

本机实测：`DictionaryDrawerSettingsAttribute`、`ValueDropdownListAttribute` **确实不存在**；
XML 里有注释的属性共 **114 个**（这是下界，不是全集）。

## 分组清单（名字即 `Sirenix.OdinInspector.<Name>`）

### 分组与排版
`BoxGroup` `FoldoutGroup` `TitleGroup` `TabGroup` `HorizontalGroup` `VerticalGroup` `ToggleGroup`
`ShowIfGroup` `HideIfGroup` `ButtonGroup` `ResponsiveButtonGroup` `PropertyGroup`(基类)
`Title` `TypeInfoBox` `Image` `PropertySpace` `Indent` `GUIColor` `Wrap` `DisplayAsString`

标签三件套：`LabelText`（改显示名，支持 `"$Member"` 取成员值）、`LabelWidth`、`HideLabel`。
`PropertyTooltip` 与 Unity 的 `[Tooltip]` 是两条不同通路（前者走 Odin 自己的 tooltip 层）。

**多字段一行的对齐**：官方成例是给组路径常量 + 显式宽度，不让自动标签宽互相污染
（`Demos/Sample - RPG Editor/Scripts/Items/Item.cs:7-24`：`protected const string LEFT_VERTICAL_GROUP = "Split/Left";`
+ `[HorizontalGroup(LEFT_VERTICAL_GROUP + "/Split", 55, LabelWidth = 67)]`）；
需要关掉自动对齐时用 `DisableAutomaticLabelWidth = true`（`ConsumableItem.cs:10-17`）。

### 条件显隐/可用性
`ShowIf` `HideIf` `EnableIf` `DisableIf`（各自都有 `ShowIfGroup`/`HideIfGroup` 的整组版）
按宿主形态的还有：`ShowIn` `HideIn` `EnableIn` `DisableIn` `RequiredIn` `DisallowModificationsIn`
`*InEditorMode` `*InPlayMode` `*InPrefabs` `*InPrefabAssets` `*InPrefabInstances` `*InNonPrefabs` `*InInlineEditors`。

字符串参数三种形态：成员名（`nameof(x)`）、`@` 表达式（`"@m_List.Count > 0"`）、枚举两参比较
（`[EnableIf("SomeEnumField", SomeEnum.Member)]`）。本仓已用成例：
`Runtime/Core/Graphics/GraphicsSettings.Resolution.cs:78`、`Runtime/Services/Audio/Models/AudioPlayOptions.cs:88,92,97`
（`[ShowIf(nameof(m_FadeInOnPlay))]` 一个条件挂多字段）；枚举两参形态本仓未用，要用先按上面的读数口径核一遍。

### 校验与提示
`ValidateInput`（方法名或 `@` 表达式，可用 `$value`；旧的重载带 `rejectInvalidInput` 已废弃——
"Rejecting invalid input is no longer supported"）、`InfoBox` / `DetailedInfoBox`（`message` 支持 `$成员` 动态串，
带 `visibleIf` 条件）、`Required`、`MinValue` `MaxValue` `Range` `MinMaxSlider` `PropertyRange`、
`AssetsOnly` `SceneObjectsOnly` `ChildGameObjectsOnly` `FilePath` `FolderPath`、
`DontValidate` `SuppressInvalidAttributeError`。
全局可扫的红字走"自定义 attribute + `AttributeValidator` + 程序集级 `RegisterValidator`"
（`Demos/Custom Drawers/Scripts/ValidationExample.cs:2,22-45`）。
**校验器侧的完整面（基类取舍、Rule vs Validator、`ref` 报告、窗口/配置生效值）在 sibling 技能
`odin-validator`（同级 `.claude/skills/odin-validator/`）**——本清单只管属性名，别在这里长校验知识。

### 集合与表格
`ListDrawerSettings`（含 `IsReadOnly`、`ShowPaged`、`NumberOfItemsPerPage` 等）、`TableList` `TableMatrix`
`TableColumnWidth` `HideInTables`、`AssetList`（`Path`/`Tags`/`AutoPopulate`/`CustomFilterMethod`；
**没有** `AssetsList` 这个拼法，也没有 `DictionaryDrawerSettings`）、`ValueCollectionChanged` 系：`OnCollectionChanged`。

### 选择器与呈现
`ValueDropdown`（指向成员名/方法，返回 `IEnumerable` 或 `ValueDropdownItem` 序列；
`IsUniqueList`/`ExcludeExistingValuesInList`/`AppendNextDrawer`/`DropdownTitle` 常用；
本仓成例 `Runtime/Services/Localization/LocalizationServiceSettings.cs:13`、
`Editor/Tools/ReleaseTools/BuildConfig.cs:25`）、`AssetSelector`、`CustomValueDrawer`、
`TypeFilter`、`PreviewField`、`InlineEditor`、`InlineProperty`、`Searchable`、`Optional`、
`HideReferenceObjectPicker` `HideDuplicateReferenceBox` `DoNotDrawAsReference`、
`DrawWithUnity`（退回 Unity 原生绘制）、`DrawWithVisualElements`。

### 函数与回调
`Button` `ButtonGroup` `InlineButton`、`OnInspectorGUI` `OnInspectorInit` `OnInspectorDispose`
`OnStateUpdate` `OnValueChanged`、`CustomContextMenu` `DisableContextMenu`、`DelayedProperty`、`EnableGUI`。

**`OnValueChanged` 的触发条件是 Odin 自己看见值变了**——抽屉里裸写 `SerializedProperty` 再
`ApplyModifiedProperties()` 不会触发它，见 `drawers.md` 的"改值唯一正路"。

### 枚举与开关
`EnumToggleButtons` `EnumPaging` `Toggle` `ToggleLeft` `ProgressBar` `ColorPalette`。

### 元/调试
`ShowDrawerChain` `ShowPropertyResolver`（调试抽屉链，排查"谁在画这一行"最直接）、
`ShowOdinSerializedPropertiesInInspector`、`IncludeMyAttributes` `DontApplyToListElements`、
`HideMonoScript` `HideNetworkBehaviourFields`、`PropertyOrder`、`ShowInInspector`。
