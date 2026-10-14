---
name: odin-inspector
description: 在 Unity 工程里安全地用 Odin Inspector 属性与自定义抽屉做/改检视面板。查属性是否存在、写 OdinValueDrawer/OdinAttributeDrawer、把字段布局改成属性驱动、加 OnValueChanged/ShowIf/ValidateInput/InfoBox/ValueDropdown、排查"改了不生效/回调不触发/标签重复/tooltip 乱弹/字段不滚动"。凡涉及 Sirenix 程序集 API 是否存在、抽屉写回、检视器布局对齐都先用它。
---

# Odin Inspector 作业手册

## Overview

Odin 的官方文档站描述的是**最新版**，而工程里装的是**某个具体版本**；同时 Odin 的变更链有自己的入口，
按 Unity 习惯写（裸 `SerializedProperty` + `ApplyModifiedProperties`）会静默不生效。本技能给出：
先核存在性、再选属性还是抽屉、以及一批已付过代价的陷阱与验证协议。

## 铁律

1. **先核 API 存在性再写代码。** 跑 `scripts/odin_api_probe.py <Name> ...`（正例反例一起放，证明探针活着）。
   文档站有、装机版没有的属性确实存在（实测 `DictionaryDrawerSettings`、`ValueDropdownList` 不存在）。
2. **布局优先用属性，抽屉只画属性表达不了的呈现。** 官方成例见 `references/drawers.md`；
   手写 `GetControlRect` + 改 `labelWidth` 那一类代码是最后手段。
3. **抽屉改值必须走 `ValueEntry` 赋值 + `ApplyChanges()`。** 裸写 `SerializedProperty` 不进 Odin 变更链，
   `[OnValueChanged]` 等一律静默不触发。
4. **交互面（拖拽/悬停/是否真画出来）不能凭读数下结论。** 只能标"待用户目测"，或用开关式 A/B 归因。

## 工作流

1. 判性质：排版/显隐/校验/下拉 → 属性（`references/attributes.md` 按分组查名字，别猜拼法）；
   换某个类型的呈现、或要复用到多类型 → 抽屉（`references/drawers.md`）。
2. 拿本工程的**同类成例**当模板，而不是文档站的示例：
   - 属性驱动布局：`Assets/Plugins/Sirenix/Demos/Sample - RPG Editor/Scripts/Items/Item.cs`（组路径常量 + `HorizontalGroup(..., LabelWidth = n)`）
   - 薄抽屉剥标签：`.../Misc/StatList.cs` 的 `StatListValueDrawer`
   - 抽屉改值：`Assets/Plugins/Sirenix/Demos/Custom Drawers/Scripts/GenericMenuExample.cs:63-64`
   - 动态候选下拉：本仓 `Runtime/Services/Localization/LocalizationServiceSettings.cs:13`
   - 自定义属性 + 成对抽屉：本仓 `Runtime/Core/Attributes/OdinExtends/InfoBoxBelowAttribute.cs`
     配 `Editor/Core/Attributes/Drawers/OdinExtends/InfoBoxBelowAttributeDrawer.cs`（属性声明在 `Sirenix.OdinInspector` 命名空间下）
3. 写之前把"承重假设"列出来单独验。本技能里每条陷阱最初都是一个没验的假设。
4. 验证：编译门 + 用例 + 交互面目测（协议在 `references/drawers.md` 末节）。

## 常见症状 → 成因

| 症状 | 先看这条 |
|---|---|
| 抽屉里改了值，`[OnValueChanged]`/刷新没反应 | 铁律 3 |
| 一行里出现两个标签（一个是英文 nicify 名） | `drawers.md` 陷阱 1、2 |
| 鼠标停在任意位置都弹同一句 tooltip | 陷阱 4、5（`GUI.tooltip` 是全局粘性静态，别拿它做归因） |
| 悬停/点击判定与矩形对不上 | 陷阱 3（`GetLastRect` 与 `mousePosition` 不同坐标空间） |
| 控件不滚动、被裁掉、高度虚高 | 属性化排版（IMGUI 控件不自带滚动），回 `attributes.md` 换排版属性 |
| 文档里的属性编译报不存在 | 铁律 1 |

## Resources

- `references/attributes.md` — 装机版属性分组清单、字符串参数三形态、排版对齐成例
- `references/drawers.md` — 抽屉类型与成员表、`ValueEntry` 写回正路、7 条实测陷阱、本工程编译/测试验证协议
- `scripts/odin_api_probe.py` — 存在性探针：`python scripts/odin_api_probe.py [--dir <Assemblies>] Name1 Name2`
