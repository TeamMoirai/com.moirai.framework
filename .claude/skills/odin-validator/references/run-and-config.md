# 跑起来：属性拾取、窗口配置、流水线（读数取自本机宿主工程）

## 校验跑不跑由三层门决定，先量再谈行为

`python .claude/skills/odin-validator/scripts/validator_env.py --project <宿主工程根>` 一次读全这四段
（版本与 define 读的是**宿主工程**，不在本包内）。

1. **define**：宿主工程 `ProjectSettings/ProjectSettings.asset` 里 Standalone 有（2026-10-13 读数） `ODIN_VALIDATOR;ODIN_VALIDATOR_3_1;ODIN_VALIDATOR_3_2;ODIN_VALIDATOR_3_3`。
   装机 validator 是 4.0.2.4，但**没有** `ODIN_VALIDATOR_4_0` 这一档 ⇒ `#if` 只写 `ODIN_VALIDATOR`，
   版本档要用脚本核实再用，否则整块被预处理静默裁掉（零警告的"照旧不生效"）。
2. **程序集**：基类与注册属性在 `Sirenix.OdinInspector.Editor.dll`，窗口/会话/Runner 在
   `Sirenix.OdinValidator.Editor.dll`（Editor-only 预编译 dll，meta 读数 `isExplicitlyReferenced: 0`）。
   本仓 `Editor/Moirai.Atropos.Editor.asmdef` 读数 `overrideReferences=false`、`autoReferenced=true` ⇒ **不用改 asmdef**，
   包内 Editor 代码直接 `using` 这套 API。
   编译门证据来自 sibling 包 `com.moirai.gameplay` 的 Editor asmdef（同形态读数，探针编绿后已删）；**framework 仓本身这一轮没跑过编译门**，第一次在本仓落校验器时要按末节协议补一次。
3. **配置生效值**在宿主的 .asset 里，不在 C# 字段初值。本机 `Assets/Plugins/Sirenix/Odin Validator/Editor/` 下：

| 资产 | 本机读数 | 含义 |
|---|---|---|
| `Config/AutomationConfig.asset` | `OnPlayMode=0 OnBuild=0 OnProjectStartup=0` | 进 Play / 构建 / 启动**都不会**自动跑校验 |
| `Config/GlobalValidationConfig.asset` | 开关为真的 0 个、`changedFromDefault` 0 个 | DeepValidation、ContinuouslyValidateVisibleIssues、ValidateMainProfileOnLoad 等全默认关 |
| `Config/RuleConfig.asset` | `projectRules` 条目 = 0 | 还没有任何 rule 被序列化过；新注册的 rule 要跑过一轮才落表 |
| `Profiles/Main Profile.asset` | include 条目 4 条（`Enabled/Type` 数字） | 扫描范围；Type 编号没对到枚举前**别解释它** |

dll 里还留着 `OdinValidationConfig.asset` 与 `Old_OdinValidationConfig_Type` 的字面量，但盘上没有那份文件
⇒ 别去翻旧配置；生效配置就是上面这几份。

## 属性即规则：项目扫描会拾起哪些属性

内置 attribute validator（`Sirenix.OdinInspector.Editor.Validation`）：
`ValidateInputAttributeValidator`、`InfoBoxValidator`、`DetailedInfoBoxValidator`、`RequiredValidator`、
`RequiredInValidator`、`MinValueValidator`、`MaxValueValidator`、`RangeValidator`、`PropertyRangeValidator`、
`MinMaxSliderValidator`、`MinMaxValueCompositeValidator`、`AssetsOnlyValidator`、`SceneObjectsOnlyValidator`、
`ChildGameObjectsOnlyValidator`、`FilePathValidator`、`FolderPathValidator`、`RequireComponentValidator`、
`RequiredListLengthValidator`、`CrossSceneReferenceValidator`、`DisallowModificationsInAttributeValidator`、
`TypeRegistryIllegalInstanceValidator`。
⇒ 能用属性表达的约束就别写 validator；写了也要先确认属性面没有现成的。

项目级内置 validator（`Sirenix.OdinValidator.Editor.Validators`）：
`MissingScript`、`DuplicateComponents`、`BrokenPrefabConnection`、`ShaderCompilerErrors`、
`UICanvasChildElement`、`HugeTransformPositions`、`SceneNotInBuildSettings`、`InvalidLayer`、
`ListNullElement`、`NoEmptyStrings`、`Material`、`MeshRenderer`、`ReferenceRequiredByDefault`、
`SerializeReference`、`UnityEvent`、`UnityObjectScriptMatch`、`MustBeAPrefab`、`ObsoleteAttribute`。

## 流水线里怎么跑（成员级读数，未在本机跑通，只证明 API 形态）

- `Sirenix.OdinValidator.Editor.OdinValidationRunner`：public sealed、`IDisposable`；
  `Policy`(OdinValidationPolicy)、`Config`(ValidationRunnerConfig)；入口返回
  `IEnumerable<PersistentValidationResultBatch>` 且是迭代器形态：
  `ValidateAllAssetsAtPath(string)`、`ValidateScene(SceneReference)`、`ValidateSceneContent`、
  `ValidateSceneValidators`、`ValidateObject(object[, object | ObjectAddress])`、
  `ValidateUnityObjectRecursively(UnityEngine.Object)` ⇒ 要在编辑器循环里逐帧推。
- `ValidationSession`（`MainValidationSession`、`ValidateEverythingEnumerator()`）、报告导出 `.ToJson()` / `.ToHtml()`。
- `IValidatorLocator`：`GetValidators(InspectorProperty)`、`GetSceneValidators(SceneReference)`、
  `PotentiallyHasValidatorsFor`、`CustomValidatorFilter` —— 想知道"这个属性挂了哪些校验器"用它，别反射。
- `SceneExtensions.ToSceneReference(Scene)` 把 Unity 的 Scene 换成 validator 侧的句柄。
- 修复：`Fix.Create(...)`（4 重载，末参 `bool offerInInspector`）、字段 `Title`/`Action`/`ArgType`/`OfferInInspector`；
  `ResultItem.Fix` 是直接字段。窗口侧还有 `SelectNextIssueOnFix`、`RevalidationDuringFixes`、批量修（bulk fix）。
- headless：`EnsureCorrectOdinVersion` 在 `GraphicsDeviceType.Null` 或 `inBatchMode` 时**直接 return**
  ⇒ 别指望首次 batch 会把配置/版本对齐补上；CI 里要跑校验就得自己驱动 Runner/Session。

## 本机验证协议（交付判据）

1. 编译门走 sibling 的 `../odin-inspector/references/drawers.md` 末节（桥即本仓 `Tests/EditorMode/EditorStateBridge.cs`）（桥 refresh → `domainSeq` 递增 → `consoleErrors` 归 0）。
   **增删过 .cs 一律先发 `refresh`**，`recompile` 会拿旧文件清单假报错。
2. 取错误清单要整文件搜再 `sort -u`：
   `grep -aoE "<文件名>\([0-9]+,[0-9]+\): error CS[0-9]+: .*" "$LOCALAPPDATA/Unity/Editor/Editor.log" | sed 's/<[^>]*>//g' | sort -u`
   —— 我本轮就是先 `head -12` 截尾，把"还剩几个错、是什么"读错过一次。
3. 窗口里"这条错误有没有出现 / 按钮点不点得动 / 批量修对不对"是交互面，只能用户目测；
   不要拿编译读数或资产读数冒充它。
4. 改了 rule 之后重跑 `validator_env.py`，用 `projectRules entries=` 自证有没有真的落进表。
