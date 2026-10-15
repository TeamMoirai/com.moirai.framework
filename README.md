# Moirai Framework Plugins

这是 `com.moirai.framework` 的 `plugin` 分支，与 `master`、`installer` 无共同历史，只作插件目录（catalog）分发通道，由框架内的 **包管理器** 窗口消费。

[English](README_EN.md)

## 目录形状

```
com.moirai.framework.plugin/
├── package.json              # 给 Unity 读的那一份，由包管理器维护
├── README.md / README_EN.md
└── <id>/                     # 一个插件一个顶层目录，扁平不嵌套
    ├── <id>.plugin.json      # 该插件的清单，只给包管理器解析
    ├── *.asmdef              # 必需，见下
    └── ...
```

顶层目录一律扁平，按 `<id>` 命名；**不**再套功能层级。功能归类写在清单的 `function` 字段里，由窗口在菜单树上分组。

## 插件作者必须遵守

1. **每个插件自带 `.asmdef`。** Unity 要求包内脚本关联程序集定义，缺失的脚本会静默不参与编译——不会报错，只会"功能不存在"。包管理器只校验并拒绝安装，不代为生成。
2. **程序集名全局唯一。** 建议 `Moirai.Plugin.<Id>`。同名程序集在别的装配里平时完全隐形，只有同时引用双方的装配（通常是测试集）才报 CS0433。
3. **插件目录内不要放 `package.json`。** Unity 只读分支根那一份；插件自带的那份是清单的干扰源。清单文件名固定为 `<id>.plugin.json`。
4. **`.meta` 与源文件一起提交。** GUID 由 `.meta` 携带，缺了会在别人机器上重新生成，序列化引用随即断链。
5. **`minUnityVersion` 与 `minFrameworkVersion` 会被真比较**，任一不满足即拒绝安装；**`versionOperator` 会被真解析**，只认 `>=`、`>`、`=`、`<=`、`<`。
6. **要 NuGet 包就把安装前置写进 `description`。** `upmDependencies` 只覆盖 UPM 包（写进工程 `manifest.json`）；`ZLogger.dll` 这类来自 NuGet 的依赖，得让使用者先用 NuGetForUnity 装好——这一步写进 `description`，窗口详情面的说明框会原样展示。

## 清单字段

```json
{
    "id": "blackboard",
    "name": "Blackboard",
    "function": "Data",
    "description": "……",
    "author": "……",
    "version": "0.1.0",
    "minUnityVersion": "2022.3.0",
    "minFrameworkVersion": "1.1.0",
    "tags": [ "MOIRAI_BLACKBOARD" ],
    "dependencies": [ { "id": "system-extensions", "version": "1.0.0", "versionOperator": ">=" } ],
    "upmDependencies": [ { "id": "com.cysharp.unitask", "version": "2.5.11" } ]
}
```

| 字段 | 含义 | 约束 |
|---|---|---|
| `id` | 插件标识，同时是目录名 | `[a-z0-9._-]+`，全分支唯一 |
| `name` | 显示名 | — |
| `function` | 窗口菜单树的分组名 | 仅作分类，不参与路径 |
| `description` | 说明，**安装前置也写这里** | 窗口详情面原样展示（折行） |
| `author` | 作者 | — |
| `version` | 插件版本 | 语义化三段，`x.y.z` |
| `minUnityVersion` | 最低 Unity 版本 | 与当前编辑器比较，不满足拒绝安装 |
| `minFrameworkVersion` | 最低框架版本 | 与当前 `com.moirai.framework` 包版本比较，不满足拒绝安装（与 `minUnityVersion` 同款硬性要求） |
| `tags` | 脚本宏 | 安装时添加、卸载时移除，成对 |
| `dependencies` | 指向同分支其它插件 `id` | 传递解析，按拓扑序安装；缺失即中止 |
| `upmDependencies` | 外部 UPM 包 | 写进**工程的** `Packages/manifest.json`，不写进分支根 `package.json` |

## 安装与卸载的落盘约定

- 安装 = 只把勾选的 `<id>/` 从分支取到本地工作树，未勾选的插件内容不落盘、不下载。
- 已装状态与 UPM 依赖归属记在 `ProjectSettings/MoiraiPackageManager.json`。
- 用户手工加进 `manifest.json` 的同名依赖条目，包管理器不覆盖也不删除；共享依赖按引用计数回收，仅在最后一个使用者卸载且条目属自己写入时才移除。
- 卸载某个仍被在场插件依赖的包，会在弹窗里先问。
