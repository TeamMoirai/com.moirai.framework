---
name: odin-validator
description: Odin Validator 4 (Sirenix) expert for the Moirai project — project/scene/asset validation, custom validator authoring (ValueValidator / RootObjectValidator / SceneValidator / AttributeValidator / GlobalValidator / ISelfValidator), validation rules and profiles, result items and one-click fixes, and automation hooks (build / play-mode / project-startup gates). Use when scanning, explaining or fixing project validation issues; writing or reviewing any C# that references Sirenix.OdinInspector.Editor.Validation or Sirenix.OdinValidator.Editor; registering validators or rules; configuring the Validator window, profiles, RuleConfig / AutomationConfig / GlobalValidationConfig; or wiring validation into a build/CI gate.
---

## Triggers
- Scanning, explaining or fixing project, scene or asset validation issues
- Authoring validators or rules (ValueValidator, RootObjectValidator, SceneValidator, AttributeValidator, GlobalValidator, ISelfValidator)
- Configuring the Validator window, profiles, RuleConfig / AutomationConfig
- Wiring validation into a build or CI gate
- 扫查/解释/修复项目与场景资产校验问题、自定校验器与规则、配置 Validator 窗口与 Profile、把校验接进构建或 CI 门禁

# Odin Validator（本项目 4.0.2.4）

配套技能：[odin-inspector](../odin-inspector/SKILL.md)（Inspector 特性、Drawer、编辑器窗口）。本技能只管 **Validator** 一侧。

## 环境事实

- **版本**：Odin Inspector & Validator **4.0.2.4**，安装于 `Assets/Plugins/Sirenix/`，禁止改动其内容。
- **程序集与平台**（决定代码能放哪儿，最关键的一条）：

  | 程序集 | 内容 | 平台 |
  |---|---|---|
  | `Sirenix.OdinValidator.Editor` | Validator 窗口、会话、运行器、Profile、RuleConfig、AutomationConfig、脚本模板 | **仅 Editor** |
  | `Sirenix.OdinInspector.Editor` | 校验器基类与结果模型：`Validator` / `ValueValidator<T>` / `RootObjectValidator<T>` / `SceneValidator` / `AttributeValidator<,>` / `GlobalValidator` / `ValidationResult` / `ResultItem` / `Fix` | 仅 Editor |
  | `Sirenix.OdinInspector.Attributes` | 运行时可用的自校验契约 `ISelfValidator` / `SelfValidationResult` / `SelfFix` / `ValidatorSeverity`，以及全部校验特性 | **运行时可用** |

- **引用方式**：全部 Sirenix DLL 的 `isExplicitlyReferenced: 0`（Auto Reference 开启）——编辑器程序集**直接 `using Sirenix.*` 即可，禁止改 asmdef**。
- **硬约束**：`Sirenix.OdinValidator.Editor.dll` 的 PluginImporter 只有 `Editor: Editor` 为 enabled，其余全平台 `Exclude: 1`。**任何引用 `Sirenix.OdinInspector.Editor.Validation.*` 或 `Sirenix.OdinValidator.Editor.*` 的类型只能存在于编辑器程序集**——热更程序集（GameLib / GameLogic / GameProto）与运行时程序集引用它会打断玩家构建。
- **项目内配置资产**（`Assets/Plugins/Sirenix/Odin Validator/Editor/`）：

  | 资产 | 内容 | 当前状态 |
  |---|---|---|
  | `Profiles/Main Profile.asset` | 主校验 Profile 的 Include 清单 | 默认=**全项目**（见下） |
  | `Config/AutomationConfig.asset` | 三个触发点 | 三个总开关全 `None(0)`，**未启用** |
  | `Config/GlobalValidationConfig.asset` | 24 个 `ProjectSettingBool` | 全 0（`HasShownValidationConfig: 1` = 首启弹窗已关） |
  | `Config/RuleConfig.asset` | 规则启用/配置数据 | `projectRules: []`，无自定义规则 |

  即：**Validator 已安装但整套自动化与规则尚未配置**；默认 Profile 的 4 条 Include = 打开的 Scene（含依赖）+ Build Settings 中的 Scene（含依赖）+ `Assets` 下全部 Scene + `Assets` 下全部资产。

- **入口**：Unity 主菜单 **`Tools/Odin/Validator`** 打开窗口。右键上下文菜单（来自程序集字符串）：`Assets/Odin Validator/Validate this`、`Assets/Odin Validator/Create Validator/*`、`Assets/Odin Validator/Create Rule/*`、`Assets/Odin Validator/Filter/*`；Hierarchy 上 `GameObject/Odin/Validate this` 与 `Validate this and children`。

## 写校验器之前：先选种类

| 需求 | 基类 | 作用域 | 关键成员 |
|---|---|---|---|
| 校验某种**值**（任何字段/属性） | `ValueValidator<T>` | 任意类型的值 | `Value`、`ValueEntry` |
| 校验一个**根对象**（SO/MonoBehaviour 资产） | `RootObjectValidator<T>` | 资产根对象 | `Object`、`CanValidateObject(T)`、`CanValidateRootProperty` |
| 由**自定义特性**驱动 | `AttributeValidator<TAttr>` / `AttributeValidator<TAttr,TValue>` | 带该特性的成员 | `Attribute`、`Value` |
| 校验**整个场景** | `SceneValidator` | Scene | `ValidatedScene` + 一组场景查询助手 |
| **全局**检查（与对象无关） | `GlobalValidator` | 全项目 | `public abstract IEnumerable RunValidation(ValidationResult)` |
| 对象**自报**校验（可在运行时程序集编译） | `ISelfValidator` + `SelfValidationResult` | 该对象自身 | `void Validate(SelfValidationResult result)` |

**裸 `Validator` 不要直接继承**：`Validator` 是抽象底座，需要自己接管 `RunValidation(ref ValidationResult)`；上面 5 个基类已提供正确装配。

**`GlobalValidator` 返回 `IEnumerable`**：`yield return null` 会把控制权交回编辑器、下一帧继续，用于长耗时扫描。它的 `RunValidation` 是 `public abstract IEnumerable`，与其余基类的 `protected override void Validate(ValidationResult)` **签名不同**。

## 注册：Validator 还是 Rule

两种都是**程序集级特性**（写在文件顶部、类外）：

```csharp
// 普通校验器：始终生效
[assembly: RegisterValidator(typeof(MyValidator))]

// 规则：出现在窗口的 Rules 列表，可按项目/本机启用、禁用并配置字段
[assembly: RegisterValidationRule(typeof(MyRule), Name = "MyRule", Description = "说明文本")]
```

- `RegisterValidatorAttribute` 字段：`ValidatorType`、`SpecialInstantiator`、`Priority`；`RegisterValidationRuleAttribute` 追加 `Name`、`Description`、`EnabledByDefault`。
- **选 Rule 的判据**：这条检查是否"有主张/可配置"（阈值、路径白名单等）。Rule 的配置字段就是校验器上的**公有可序列化字段**，Odin 会自动把它们画进窗口的 Rules 面板；配置数据按来源分层（`ConfigSourceType`：`Default=1` / `Project=2` 团队共享写进 `RuleConfig.asset` / `Local=4` 仅本机 / `All=7`）。
- 菜单里 `Create Rule/*` 生成的模板已带该特性与示例配置字段。

## 结果与修复 API

`Validate()` 内往 `ValidationResult` 里写条目，方法返回 `ref ResultItem`，因此可以链式追加动作：

```csharp
if (bad) result.AddError("Something is wrong");
result.AddWarning("...");
result.Add(ValidatorSeverity.Warning, "也许该检查一下");
result.Add(ResultItem.Warning("..."));
```

- `ValidationResultType`：`IgnoreResult=0` / `Valid=1` / `Error=2` / `Warning=3`；`ValidatorSeverity`：`Error=0` / `Warning=1` / `Ignore=2`。
- `ResultItem` 字段：`Message`、`ResultType`、`MetaData[]`、`Fix`、`OnContextClick`、`OnSceneGUI`、`SelectionObject`、`RichText`。
- 链式扩展（`ResultItemExtensions`）：`WithFix(...)`、`WithContextClick(...)`、`WithSceneGUI(...)`、`SetSelectionObject(obj)`、`EnableRichText()`、`WithMetaData(...)`、`WithButton(name, onClick)`。
- `Fix.Create(title, action, offerInInspector)` / `item.WithFix("标题", () => {...}, offerInInspector: true)` = **一键修复**；`Fix.OfferInInspector` 决定是否在 Inspector 里也给修复入口。

运行时可用的自校验走平行的一套：`SelfValidationResult`（`AddError`/`AddWarning`/`Add(ValidatorSeverity, msg)`）+ `SelfValidationResultItemExtensions`（`WithFix`/`WithButton`/…）+ `SelfFix`；其 `ResultType` 是 `Error=0 / Warning=1 / Valid=2`（**与 `ValidationResultType` 的数值不同，勿混用**）。

## 代码骨架

六种模板的权威形态（Sirenix 自带 `ValidatorScriptTemplates`，即 `Assets/Odin Validator/Create */*` 生成的代码）已逐字收录在 [references/patterns.md](references/patterns.md)，写之前先照抄对应骨架，别自创结构。

## 项目约定

1. **一律 `#if UNITY_EDITOR` 包裹**（Sirenix 官方模板就是这么做的），且文件放在编辑器程序集：
   - 框架级 → `Packages/com.moirai.framework/Editor/**`（程序集 `Moirai.Atropos.Editor`）
   - 游戏内容级 → `Assets/Scripts/GameBase/Editor/**`（Unity 自动编译进 `Assembly-CSharp-Editor`）
   - 仓库 `Assets/` 下目前**没有**游戏侧 Editor asmdef，需要独立程序集时再新建（`includePlatforms: ["Editor"]`）。
2. **不要**把校验器放进 `Runtime/`、`GameLib`、`GameLogic`、`GameProto` —— 仅 Editor 程序集可引用。
3. 要暴露给数据的**自定义校验特性**仍走 OdinExtends 模式（特性入 `Runtime/Core/Attributes/OdinExtends/` + `[Conditional("UNITY_EDITOR")]`，`AttributeValidator` 入 `Editor/Core/Attributes/Drawers/OdinExtends/`），详见 odin-inspector 技能。
4. 在框架内新增文件时，仍遵守框架 AAA C# 规范（命名前缀、双语 `#region 中文 [ENGLISH]`、`<summary>` 一句话）与"注释精简"要求。

## 自动化与构建门禁

`AutomationConfig`（`GlobalConfig<AutomationConfig>`）每个触发点一组字段：`Xxx`（总开关 bool）、`XxxIfWarnings` / `XxxIfErrors`（动作）、`XxxAlwaysCompleteValidationFully`、`XxxFlashScreen`、`XxxSetup`（`RunKind`：`GlobalValidation=0` / `Custom=1` + `ProfileAsset`）。

| 触发点 | 动作枚举（位标志） |
|---|---|
| `OnPlayMode` | `None=0` / `OpenValidator=1` / `StopPlayMode=2` / `LogToConsole=4` |
| `OnBuild` | `None=0` / `OpenValidator=1` / `StopBuild=2` / `LogToConsole=4` |
| `OnProjectStartup` | `None=0` / `OpenValidator=1` / `LogToConsole=2` |

**已验证的关键细节**：虽然三个总开关当前都是 `0`，但资产里已存有出错策略 `OnPlayModeIfErrors: 3`（OpenValidator|StopPlayMode）、`OnBuildIfErrors: 3`（OpenValidator|StopBuild）、`OnProjectStartupIfErrors: 1`（OpenValidator）。因此**只把 `OnBuild` 打开就立刻得到"构建期出错即中止构建"的门禁**，无需再配动作。

- 构建钩子实现类是 `BuildEventHookTrigger : IPreprocessBuildWithReport`（`OnPreprocessBuild(BuildReport)`），`AutomationConfig.TriggerOnBuild()` 可被程序化调用；系统是 batchmode 感知的（内部 `IsHeadlessOrBatchMode`）。**该程序集里没有独立的校验 CLI 可执行文件**——CI 门禁通过构建钩子（中止构建）达成，别去找不存在的命令行工具。
- 手动调用可用于批处理：

```csharp
Sirenix.OdinValidator.Editor.AutomationConfig.TriggerOnBuild(); // 走已配置的 OnBuild 策略
```

## 程序化驱动

窗口之外的自动化入口（都在 `Sirenix.OdinValidator.Editor`）：

```csharp
// 一次性校验某个对象/资产/场景（可 using，务必 Dispose：有泄漏检测）
using (var runner = new OdinValidationRunner(OdinValidationPolicy.Default, ValidationRunnerConfig.Default))
{
    foreach (var r in runner.ValidateObject(someObject)) { /* ... */ }
    foreach (var r in runner.ValidateAllAssetsAtPath("Assets/Prefabs")) { /* ... */ }
    foreach (var r in runner.ValidateScene(SceneReference.FromPath("Assets/Scenes/main.unity"))) { /* ... */ }
}

// 会话式（带队列、进度、结果收集、后台校验）
var session = new ValidationSession("My Session", /*SessionConfig*/ config);
session.PopulateQueue(clearCurrentQueue: true, populateUnloadedScenes: true,
                      queueScenes: true, queueAssets: true, queueGlobalValidators: true);
session.StartSession(startWatching: false, startBackgroundValidation: false);
// 枚举器驱动（在主线程逐帧推进）：ValidateEverythingEnumerator(openClosedScenes, showProgressBar)
// 计数：CurrentErrorCount / CurrentWarningCount / CurrentValidCount
// 结果：GetCurrentResults(applyFilters)
session.StopSession(true, true);
session.Dispose();

// 报告导出（静态）：JSON / HTML，适合作为 CI 产物
string json = ValidationReport.ToJson(session, prettyPrint: true);
string html = ValidationReport.ToHtml(session);
```

- `ValidationSession.ActiveValidationSessions`（静态 List）可枚举活动会话；`ValidationSession.MainValidationSession` 是窗口用的主会话。
- `ValidationSession.Runner` 是公开字段，可取用其 `OdinValidationRunner`。
- 泄漏检测：`OdinValidationRunner.EnableLeakDetection`（静态 `EditorPrefBool`）与 `GlobalValidationConfig.EnableLeakDetection`。
- Profile 侧：`MainValidationProfile.Instance.OpenValidatorWindow()`、`ValidationProfile.FindAll()`、`ClaimSessionHandle()`（配 `ValidationSessionAssetHandle`）、`ValidationItem.FromAssetPath/FromScenePath/FromSelection/...` 用于组装 Include/Exclude 清单。

## 验证

- 校验器**只有在编辑器程序集里才会被发现**（`DefaultValidatorLocator` 扫描程序集特性）。改完先确认文件所在程序集未被平台过滤出 Editor。
- 让校验器真正跑起来的最快路径：菜单 `Tools/Odin/Validator` → 窗口里对目标跑一遍；或右键资产/对象用 `Assets/Odin Validator/Validate this`；或在代码里按上节 `OdinValidationRunner.ValidateObject(...)` 反射/直调取回条目，断言"改动前无条目、改动后有条目"，这是最可靠的 A/B。
- 规则类校验器若"看不到"，先查窗口 Rules 面板里的启用状态与 `RuleConfig` 来源层（`Default/Project/Local`），别直接怀疑注册特性。
- Unity 侧编译/桥接可用性按项目通用流程判断（见 CODELY.md / odin-inspector 技能的验证节）。

## 已知边界（诚实声明）

- 官方站点（odininspector.com 的 validator / tutorials / documentation / patch-notes）是 SPA，本会话 `web_fetch` 只能取到营销页骨架，**未能取得文档正文**；本技能的事实来源是**本机安装的 4.0.2.4 程序集反射 + 项目内配置资产 + Sirenix 自带脚本模板**，因此 API 层面可信度高，而"设计意图/推荐用法"的叙述性内容以官方站为准。
- 未在运行中的编辑器里实测（本会话 Unity 桥接未运行），**菜单路径与上下文菜单项来自程序集字符串**，未经点击验证。
- 未收录的细节：`MigrationWizard` / `OldValidator`（旧 Odin Project Validator 迁移）与 `ProjectWatcher` / `BackgroundTaskRunner` / `PersistentValidationResult`（内部机制）只有签名级信息，需要时再行反射确认。

## 官方资源

- Validator 主页：https://odininspector.com/odin-validator
- 教程（含 Validator / Getting Started）：https://odininspector.com/tutorials
- 文档：https://odininspector.com/documentation
- Patch notes（Odin 4 逐条变更）：https://odininspector.com/patch-notes
- 程序集内自报的入门链接：`Assets/Plugins/Sirenix/Odin Validator/Readme.txt` →
  https://odininspector.com/tutorials/odin-project-validator/getting-started-with-odin-project-validator

## 参考文件

- [references/patterns.md](references/patterns.md) —— 六种校验器的官方代码骨架（含 Rule 变体）与 ResultItem 链式用法，写代码前必读。
- [references/api.md](references/api.md) —— 反射枚举出的完整 API 事实表（类型 + 成员签名 + 枚举值），查"有没有这个成员"时用它。
