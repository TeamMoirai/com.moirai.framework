---
name: make:new-plugin
description: 新增一个插件：目录 + `<id>.plugin.json` 清单 + asmdef + 最小适配面
when_to_use: 要把一个外部后端/适配器纳入包管理器的插件目录时
argument-hint: <插件 id> [显示名]
arguments: [plugin_id, display_name]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Edit, Write
---

## 判据

1. 新目录名与 manifest `id` 一致，小写 kebab-case；目录放包根。
2. 新建 `<id>/<id>.plugin.json`，字段与 `zlogger/zlogger.plugin.json` 同形：`id`、`name`、`function`、`description`、`author`、`version`、`minUnityVersion`、`minFrameworkVersion`、`tags`、`dependencies`、`upmDependencies`。
3. 新建 `Moirai.Plugin.<名>.asmdef`，引用框架程序集与外部 SDK；可选依赖用 `versionDefines` + `#if`。
4. 插件只实现框架契约，不复制框架服务；具体契约基类先到 `../com.moirai.framework` grep 声明。
5. 外部 SDK 不 vendored；安装前置写进 manifest 的 `description`。
6. 需要测试时按 `/make:new-plugin-test` 建 `Tests/` 独立装配；新增 `.cs` 的 `.meta` 由 Unity 生成并同笔提交。

## 落地位置

```
<id>/
├── <id>.plugin.json
├── Moirai.Plugin.<名>.asmdef
├── <适配代码>.cs
└── Tests/                 # 可选，独立测试 asmdef
```

## 要点

- 先读 `zlogger/`（最小日志后端）与 `messagepack/`（带测试的后端）作为形状样本。
- `minFrameworkVersion` 是安装地板，不能低于框架当前版本；`upmDependencies` 只列 UPM 依赖。
- 插件不写宿主工程设置；不新增根 asmdef。
- 收尾跑 `/check:manifest` 与 `/gate:verify`；没有测试装配时把验证写成“编译门 + 人工点验”。
