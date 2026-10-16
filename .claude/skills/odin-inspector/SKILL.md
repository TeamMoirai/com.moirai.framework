---
name: odin-inspector
description: Odin Inspector 4 (Sirenix) expert for the Moirai project — Inspector customization, editor tools, and attribute/drawer authoring conventions. Use when writing or reviewing any C# that uses Sirenix.OdinInspector / Sirenix.OdinInspector.Editor / OdinEditor / OdinEditorWindow / OdinMenuEditorWindow, authoring custom Odin attributes or drawers (OdinExtends, ValueDropdown, ProviderDropdown), fixing Odin Inspector rendering issues (blank window, layout broken, CS0618), or answering Odin 4.0.x API/migration questions.
---

## Triggers
- Writing or reviewing C# that uses Sirenix.OdinInspector / OdinEditor / OdinEditorWindow / OdinMenuEditorWindow
- Authoring custom Odin attributes or drawers (OdinExtends, ValueDropdown, ProviderDropdown)
- Fixing Odin rendering problems (blank window, broken layout, CS0618)
- 写/审用 Odin 特性的 C#、自定 Odin 特性与 Drawer、修 Odin 渲染问题（窗口空白、布局错位、CS0618）

# Odin Inspector（本项目 4.0.2.4）

## 环境事实

- **版本**：Odin Inspector & Validator **4.0.2.4**（Odin 4，原生支持 Unity 6000.3）。安装于 `Assets/Plugins/Sirenix/`，禁止改动其内容。
- **程序集**：`Sirenix.OdinInspector.Attributes`（特性）、`Sirenix.OdinInspector.Editor`、`Sirenix.Utilities(.Editor)`、`Sirenix.Serialization(.Config)`、`Sirenix.OdinValidator.Editor`、`Sirenix.Reflection.Editor`。
- **引用方式**：所有 Sirenix DLL 为预编译插件且 **Auto Reference 已开启**（`isExplicitlyReferenced: 0`）——**任何程序集直接 `using Sirenix.*` 即可，禁止改 asmdef**。
- **link.xml**：`Assets/Plugins/Sirenix/Assemblies/link.xml.backup.txt` 是被停用的备份（后缀 .txt 不生效）。若 Odin 序列化数据在 AOT/裁剪构建中丢失，检查是否有生效的 link.xml 保它。

## 全局 Inspector 接管

`Moirai.Atropos.Editor` 的 `MonoBehaviourEditor : OdinEditor`（`[CustomEditor(typeof(MonoBehaviour), true)]`）让 **所有 MonoBehaviour 自动使用 Odin 渲染**。因此：

- 数据类上加 Odin 特性即生效，无需为每个类写 Editor。
- 自定义 Editor 继承 `OdinEditor`（不是 `Editor`），保持 Odin 特性渲染。
- 需要持续重绘的类加项目自定义 `ConstantRepaintAttribute`（支持 runtimeOnly），不要 override `RequiresConstantRepaint`。

## 项目约定

### 1. 数据模型直接贴特性

典型栈（见 `SaveServiceSettings.cs`）：`[Tooltip] + [ProviderDropdown] + [SerializeReference]` 用于 Handler/后端多态字段；数值用 `[MinValue]/[MaxValue]`；完整惯用清单见 [references/attributes.md](references/attributes.md)。

### 2. 自定义 Odin 特性（OdinExtends 模式，强制）

| 层 | 位置 | 约定 |
|----|------|------|
| 特性类 | `Runtime/Core/Attributes/OdinExtends/` | 命名空间写 **`Sirenix.OdinInspector`**；继承 Odin 原生特性（如 `ConditionDropdownAttribute : ValueDropdownAttribute`）；**必须加 `[Conditional("UNITY_EDITOR")]`**——玩家构建自动剥离特性使用，运行时程序集零依赖编辑器 |
| Drawer | `Editor/Core/Attributes/Drawers/OdinExtends/` | 命名空间写 **`Sirenix.OdinInspector.Editor.Drawers`**；继承 `OdinAttributeDrawer<T>`；用 `[DrawerPriority(...)]` 控制接管优先级 |

```csharp
// MyDropdownAttribute.cs（放在上表「特性类」那一栏的目录里）
namespace Sirenix.OdinInspector
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = true)]
    [Conditional("UNITY_EDITOR")]
    public class MyDropdownAttribute : ValueDropdownAttribute
    {
        public string ShowIf; // 自定义扩展字段
        public MyDropdownAttribute(string valuesGetter) : base(valuesGetter) { }
    }
}
```

```csharp
// MyDropdownAttributeDrawer.cs（放在上表「Drawer」那一栏的目录里）
namespace Sirenix.OdinInspector.Editor.Drawers
{
    [DrawerPriority(0.0, 0.0, 2002.0)]
    public class MyDropdownAttributeDrawer : OdinAttributeDrawer<MyDropdownAttribute>
    {
        protected override void Initialize()
        {
            // 字符串表达式求值：ValueResolver.Get<T>(Property, Attribute.X, default)
            // 列表判定：Property.ChildResolver is ICollectionResolver
            // 持久化状态：this.GetPersistentValue<bool>("Key", default)
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            // 出错显示：SirenixEditorGUI.ErrorMessageBox(resolver.ErrorMessage)
            // 条件隐藏：不满足时 CallNextDrawer(label) 透传
            // 下拉弹层：OdinSelector<object>.DrawSelectorDropdown / GenericSelector<object> + ShowInPopup
        }
    }
}
```

实现类取值/写值：`Property.ValueEntry.WeakSmartValue`（单值）、`WeakValues[i]`（多对象）；列表写入走 `ICollectionResolver.QueueAdd/QueueClear`；深拷贝用 `SerializationUtility.CreateCopy(obj)`。

### 3. ProviderDropdown + SerializeReference（HandlerHost 核心，勿另造机制）

所有可拔插 Handler/后端字段 = `[SerializeReference] + [ProviderDropdown] + m_ 前缀私有字段`（`AudioServiceHandler`、`SaveServiceHandler`、`SaveStorageBackend` 等）。

- `ProviderDropdownAttributeDrawer`（Editor/Attributes/Drawers/ProviderDropdownAttributeDrawer.cs）：**Odin 唯一实现**（Odin 为框架必备组件，无 Unity 原生 PropertyDrawer 路径）。两条驱动：SerializedProperty 主路径（SerializedObject 场景）+ ValueEntry 回退（4.0.x UnityPropertyPath 解析失败时）；子属性优先交 Odin PropertyTree，Odin 未解析出子属性时回退 Unity 序列化绘制。纯 UITK 宿主窗口内嵌 Odin 绘制时：`OdinEditorWindow` + `PropertyTree.Create(serializedObject)` 经 `IMGUIContainer` 托管 `tree.Draw()`（`BuildPipelineWindow.DrawConfigTree` 模式；`OdinEditor.CreateInspectorGUI()` 返回 null，不能直接嵌入 UITK），勿为其复活 Unity drawer。
- 候选类型来自 `TypeCache.GetTypesDerivedFrom`，按 `TypeMenuCache` 按基类全局缓存（`beforeAssemblyReload` 失效）。
- **Odin 侧接管必须 `[DrawerPriority(1, 0, 0)]`**——低于它会被 4.0.x 默认 managed reference drawer 或 `DrawWithUnity(10000)` 抢走，导致实现类字段上的 Odin 特性失效。
- 改名任何被 `[SerializeReference]` 引用的具体类后，必须同步迁移设置资产 YAML 的 `references:` 块类名，否则反序列化静默为 null。

### 4. 编辑器窗口选型

| 场景 | 选型 |
|------|------|
| 小工具/计算器/批量操作 | `OdinEditorWindow`：字段直接贴 `[TabGroup] [LabelText] [Button] [ReadOnly] [PropertyOrder]`（范例 `ComprehensiveProbabilityWindow`） |
| Inspector 中心的自定义编辑器 | 继承 `OdinEditor`（范例 `InputActionsConfigurationEditor`、gameplay `CustomEditorV2`） |
| 大型工作台/调试器 | 框架惯例是 **纯 EditorWindow + 原生 UITK**（`SaveBrowserWindow`、`SaveServiceDebuggerWindow` 先例）——不要为了 Odin 而 Odin |
| UITK 窗口内嵌另一对象的 Odin Inspector | `OdinEditorWindow` + `PropertyTree.Create(serializedObject)` 嵌入 `IMGUIContainer`（`BuildPipelineWindow` 先例）；**编辑器程序集类型 Odin 默认编辑器不接管（回退 GenericInspector），必须显式建树** |
| 树形导航多面板 | `OdinMenuEditorWindow`——**Odin 4 有三条硬契约，写之前必读 [references/odin4-pitfalls.md](references/odin4-pitfalls.md)** |

## Odin 4.0.x 速查契约（详见 references/odin4-pitfalls.md）

1. `OdinMenuItem.ObjectInstance` 已弃用（CS0618）→ 用 **`.Value`**；编程式选中用 `OdinMenuItem.Select(false)`。
2. `OdinMenuEditorWindow` override `OnImGUI` **必须调 `base.OnImGUI()`**，否则整窗空白且无报错。
3. `OdinEditor.OnInspectorGUI()` 不参与 GUILayout Layout——嵌入 ScrollView 需缓存高度占位（`FrameworkSettingsWindow._cachedContentHeight` 模式）。
4. 4.0.x managed reference 的 `Property.UnityPropertyPath` 可能解析失败 → `GetUnityPropertyForPath` 返回 null/抛异常，须走 `ValueEntry` 回退路径。
5. 新 Unity 的 EntityId 后端：接受 int InstanceID 的 Odin API 改用 **`OdinEntityId`** 重载（`Sirenix.Reflection.Editor`），旧 int 重载在部分版本抛 `NotSupportedException`。
6. 域重载早期 `EditorStyles.label` 可能为 null——Odin 样式/树构建加守卫，勿在 `OnEnable` 初始化样式。

## 验证

- **编译**：`unity_editor.refresh` → 读 Console error；或 shell 轮询 `Library/ScriptAssemblies/*.dll` 时间戳。
- **状态（首选）**：`exec_editor_script` 打开窗口后反射读 `menuTree`、条目数、选中项 `.Value` 等，返回结构化事实。
- **视觉**：`unity_screenshot` **不覆盖 EditorWindow**（仅 GameView/SceneView）。Odin 窗口视觉门禁用项目记忆中的 PowerShell P/Invoke 工作流（EnumWindows + SetForegroundWindow + CopyFromScreen 存 PNG 后 read_file 查看）；排查渲染问题用「最小探针窗口 + 二分 override」法。

## 官方资源

- Attributes 目录（权威特性清单）：https://odininspector.com/attributes
- 教程：https://odininspector.com/tutorials
- 文档：https://odininspector.com/documentation
- Patch notes（Odin 4 变更逐条）：https://odininspector.com/patch-notes
