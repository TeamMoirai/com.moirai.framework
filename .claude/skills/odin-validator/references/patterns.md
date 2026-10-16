# Odin Validator —— 官方代码骨架

以下骨架**逐字**来自本机 4.0.2.4 的 `Sirenix.OdinValidator.Editor.ValidatorScriptTemplates` 字段（即 `Assets/Odin Validator/Create Validator/*` 与 `Create Rule/*` 生成的代码）。`{name}`（类名）与 `{target}`（目标类型/特性类型）由菜单动作替换；**`TargetValueType` 是留给手改的字面占位符，不会被自动替换**。

要点先记牢：

- 所有校验器都包在 `#if UNITY_EDITOR` 里（`Sirenix.OdinValidator.Editor` 仅 Editor 平台）。
- 重写的方法是 **`protected override void Validate(ValidationResult result)`**；唯一例外是 `GlobalValidator`，它重写 `public override IEnumerable RunValidation(ValidationResult result)`。
- 取目标值的属性：`ValueValidator<T>` → `this.Value`；`RootObjectValidator<T>` → `this.Object`（`Value` 也可用）；`AttributeValidator<TAttr,TValue>` → `this.Value` + `this.Attribute`。

## 目录

- [1. RootObjectValidator（标准）](#1-rootobjectvalidator标准)
- [2. ValueValidator（类 / 结构体，标准）](#2-valuevalidator类--结构体标准)
- [3. SceneValidator（标准）](#3-scenevalidator标准)
- [4. AttributeValidator（标准）](#4-attributevalidator标准)
- [5. GlobalValidator（标准）](#5-globalvalidator标准)
- [6. Rule 变体](#6-rule-变体)
- [7. ResultItem 链式动作](#7-resultitem-链式动作)
- [8. ISelfValidator（自校验，非模板来源）](#8-isselfvalidator自校验非模板来源)

---

## 1. RootObjectValidator（标准）

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor.Validation;
using UnityEngine;
using UnityEditor;

[assembly: RegisterValidator(typeof(MyRootObjectValidator))]

public class MyRootObjectValidator : RootObjectValidator<SomeTargetType>
{
    protected override void Validate(ValidationResult result)
    {
        // var obj = this.Object;
        // if (obj has something wrong with it)
        // {
        //     result.AddError("Something is wrong");
        // }
    }
}
#endif
```

`RootObjectValidator<T>` 额外提供 `CanValidateRootProperty(InspectorProperty)` 与 `CanValidateObject(TValue)` 用于收窄适用面。

---

## 2. ValueValidator（类 / 结构体，标准）

**目标为类**（泛型包装，覆盖所有派生类型）：

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor.Validation;
using UnityEngine;
using UnityEditor;

[assembly: RegisterValidator(typeof(MyValueValidator<>))]

public class MyValueValidator<T> : ValueValidator<T>
    where T : SomeBaseType
{
    protected override void Validate(ValidationResult result)
    {
        //var val = this.Value;

        //if (val has something wrong with it)
        //{
        //    result.AddError("Something is wrong");
        //}
    }
}
#endif
```

**目标为结构体**（非泛型，直接绑定具体值类型）：

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor.Validation;
using UnityEngine;
using UnityEditor;

[assembly: RegisterValidator(typeof(MyStructValidator))]

public class MyStructValidator : ValueValidator<SomeStructType>
{
    protected override void Validate(ValidationResult result)
    {
        //var val = this.Value;

        //if (val has something wrong with it)
        //{
        //    result.AddError("Something is wrong");
        //}
    }
}
#endif
```

---

## 3. SceneValidator（标准）

模板注释即官方对场景查询助手的权威说明：

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor.Validation;
using UnityEngine;
using UnityEditor;

[assembly: RegisterValidator(typeof(MySceneValidator))]

public class MySceneValidator : SceneValidator
{
    protected override void Validate(ValidationResult result)
    {
        // Scene Validators have many useful methods for querying the scene:

        // var theScene = this.ValidatedScene;
        // var camera = this.FindComponentInSceneOfType<Camera>();
        // var cameras = this.FindAllComponentsInSceneOfType<Camera>();
        // var allGameObjects = this.GetAllGameObjectsInScene();
        // var rootGameObjects = this.GetSceneRoots();
        // var gameObjectAtPath = this.GetGameObjectAtPath("Managers/Spawner");
        // 
        // if (scene has something wrong)
        // {
        //     result.AddError("Something is wrong")
        //         .SetSelectionObject(objectInScene); // Optional
        // }
    }
}
#endif
```

`SceneValidator` 的完整助手面（反射确认，比模板注释更多）：`ValidatedScene`（`SceneReference`）、`GetSceneRoots()`、`GetSceneRoot(string)`、`FindComponentInSceneOfType<T>(bool includeInactive)`、`FindAllComponentsInSceneOfType<T>(bool includeInactive)`、`GetAllGameObjectsInScene()`、`GetGameObjectAtPath(string, char)` / `GetGameObjectAtPath(IList<string>)`、`GetComponentAtPath<T>(...)`、`LoadSceneIfNotLoaded(bool askToSave)`、`CanValidateScene(SceneReference)`、`Initialize(SceneReference)`。

---

## 4. AttributeValidator（标准）

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor.Validation;
using UnityEngine;
using UnityEditor;

[assembly: RegisterValidator(typeof(MyAttributeValidator<>))]

public class MyAttributeValidator<T> : AttributeValidator<MyAttribute, T>
    where T : TargetValueType
{
    protected override void Validate(ValidationResult result)
    {
        //var attr = this.Attribute;
        //var val = this.Value;

        //if (val has something wrong with it)
        //{
        //    result.AddError("Something is wrong");
        //}
    }
}

// Alternative version that does not target a specific type of value (above must be deleted or commented out for the below to work)

//[assembly: RegisterValidator(typeof(MyAttributeValidator))]

//public class MyAttributeValidator : AttributeValidator<MyAttribute>
//{
//    protected override void Validate(ValidationResult result)
//    {
//        //var attr = this.Attribute;
//
//        //if (if something is wrong)
//        //{
//        //    result.AddError("Something is wrong");
//        //}
//    }
//}
#endif
```

- `AttributeValidator<TAttribute,TValue>` = 针对特定值类型；`AttributeValidator<TAttribute>` = 不限值类型（**两者不可同时注册同一类名**，模板明确要求二选一）。
- 本项目自定义校验特性走 OdinExtends 模式（特性在 `Runtime/Core/Attributes/OdinExtends/`、带 `[Conditional("UNITY_EDITOR")]`；该校验器在编辑器程序集），见 odin-inspector 技能。

---

## 5. GlobalValidator（标准）

注意**没有** `Create Rule/Global Validator` 菜单项——全局校验器模板只有标准版。

```csharp
#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor.Validation;
using UnityEngine;
using UnityEditor;
using Sirenix.OdinValidator.Editor;
using System.Collections;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using System.Linq;

[assembly: RegisterValidationRule(typeof(MyGlobalValidator), Name = "My Global Validator", Description = "Some description text.")]

public class MyGlobalValidator : GlobalValidator
{
    public BuildTarget[] ValidBuildTargets = new BuildTarget[] { BuildTarget.Android, BuildTarget.iOS };

    public override IEnumerable RunValidation(ValidationResult result)
    {
        // Example:

        if (ValidBuildTargets.Contains(EditorUserBuildSettings.activeBuildTarget) == false)
        {
            ref var error = ref result.AddError($"Your current build target ({EditorUserBuildSettings.activeBuildTarget}) is not supported by this project.");
            foreach (var item in ValidBuildTargets)
            {
                var buildTarget = item;
                error.WithButton($"Switch to {buildTarget}", () => { EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(buildTarget), buildTarget); });
            }
        }

        // Similar to coroutins, you can yield return null for long running validators similar to coroutins.
        // Each yield will return the control flow back to the editor,
        // and validation will continue next frame.
        return null;
    }
}
#endif
```

两个可直接借用的模式：`ref var error = ref result.AddError(...)` 拿 `ref` 后累积多个按钮；`yield return null` 分帧（长扫描不要阻塞编辑器）。

---

## 6. Rule 变体

`Rule_*` 与对应 `Standard_*` 的差异**只有三处**，其余逐字相同：

| # | 差异 |
|---|---|
| 1 | 注册特性换成 `[assembly: RegisterValidationRule(typeof(X), Name = "X", Description = "Some description text.")]` |
| 2 | 类内新增注释 `// Introduce serialized fields here to make your validator configurable from the validator window under rules.` |
| 3 | 类内新增一个示例配置字段 `public int SerializedConfig;`（**公有可序列化字段**，Odin 会画进窗口 Rules 面板） |

例：`Rule_SceneValidatorFileTemplate` 相对标准版的增量即

```csharp
[assembly: RegisterValidationRule(typeof(MySceneRule))]

public class MySceneRule : SceneValidator
{
    // Introduce serialized fields here to make your validator
    // configurable from the validator window under rules.
    public string SpawnerManagerPath = "Managers/Spawner";

    protected override void Validate(ValidationResult result) { /* 用 SpawnerManagerPath 代替硬编码路径 */ }
}
```

```csharp
[assembly: RegisterValidationRule(typeof(MyValueRule))]

public class MyValueRule : ValueValidator<SomeStructType>
{
    public int SerializedConfig; // 同上
    protected override void Validate(ValidationResult result) { /* ... */ }
}
```

`Rule_RootObjectValidatorFileTemplate` 与 `Rule_AttributeValidatorFileTemplate` 同此增量（后者的第二泛型参位置写的是字面量 `TargetValueType`，需手改）。

---

## 7. ResultItem 链式动作

`AddError` / `AddWarning` / `Add(...)` 返回 `ref ResultItem`，扩展方法（`ResultItemExtensions`）可直接链上去：

```csharp
// 选中定位（场景校验器常用）
result.AddError("There is no camera in the scene").SetSelectionObject(cameraGo);

// 一键修复
result.AddError("Reference is missing")
      .WithFix("Assign the default material", () => target.material = defaultMat, offerInInspector: true);

// 一键动作（多条按钮：先 ref 再循环通常见 GlobalValidator 骨架）
ref var item = ref result.AddError("...");
item.WithButton("Fix now", () => DoFix());

// 富文本 + 元数据 + 右键菜单 + 场景 GUI 绘制
result.AddWarning("**可疑**配置").EnableRichText();
result.AddError("...").WithContextClick("打开配置", () => OpenConfig());
result.AddError("...").WithSceneGUI(() => Handles.DrawWireCube(pos, Vector3.one));
result.AddError("...").WithMetaData("Key", someValue, null);

// 等价写法：Fix.Create / ResultItem.Warning
result.Add(ResultItem.Warning("...").WithFix(Fix.Create("标题", (Action)DoFix, true)));
```

- `Fix.Create(...)` 有 `(Action)` / `(Action<T>)` 两种；`offerInInspector` 决定该修复是否也在 Inspector 里提供入口。无参修复可用 `NoFixArgs`。
- `ValidationResultType`（校验器侧）= `IgnoreResult=0 / Valid=1 / Error=2 / Warning=3`。

---

## 8. ISelfValidator（自校验，非模板来源）

`ValidatorScriptTemplates` 里**没有**自校验模板；下面是按接口签名（`Sirenix.OdinInspector.Attributes` 程序集，运行时可用）重建的形态。好处是契约类型在运行时程序集里，**数据类可以在 Runtime/热更程序集里实现它**，校验只在编辑器跑。

```csharp
using Sirenix.OdinInspector;

public class MyData : ISelfValidator
{
    public int Count;

    public void Validate(SelfValidationResult result)
    {
        if (Count < 0) result.AddError("Count 不能为负");
        if (Count == 0) result.AddWarning("Count 为 0，确认是否有意为之");
    }
}
```

- `SelfValidationResult`：`AddError(string)` / `AddWarning(string)` / `Add(ValidatorSeverity, string)` / `Add(ResultItem)`。
- `SelfValidationResult.ResultType` = `Error=0 / Warning=1 / Valid=2`（**与校验器侧的 `ValidationResultType` 数值不同**）。
- 链式扩展来自 `SelfValidationResultItemExtensions`：`WithFix`（接受 `SelfFix` 或 `Action`）、`WithContextClick`、`WithSceneGUI`、`WithButton`、`WithMetaData`、`EnableRichText`、`SetSelectionObject`。
- Odin 侧由非抽象的 `SelfValidator<T> : ValueValidator<T>` 桥接到校验管线——你只实现接口，不继承基类。
