---
name: moirai-framework-installer-conventions
description: Moirai Framework Installer（com.moirai.framework.installer）的规范知识层。写或改本包代码、跑验证与发版收尾时按需加载对应细则块；主文档 AGENTS.md 只留硬约束摘要与指针。
---

# Moirai Framework Installer 规范知识层

细则在本层只存一份，权威位置就是下表「块」列出的文件。`AGENTS.md` 与命令正文里同主题只保留摘要与指向本层的指针；同一段话在两边各写一遍即视为缺陷。

## 规范细则（按主题）

| 什么时候读 | 块 |
|---|---|
| 写/改生产代码前的命名、结构、领域模板与热路径 | `references/coding.md` |
| 测试装配与替代判据 | `references/testing.md` |
| 决定"这一轮算不算过"、编译门与新鲜度 | `references/verification.md` |
| 提交、文档、版本号与发版预检 | `references/release.md` |

## 判据口径（按执行体）

执行体在别处（宿主编辑器的 pipeline 通道、编译产物、仓库内的 asmdef/asmref 与 `.meta`）。本层只述口径、射程边界与修法优先级；判据形状要变时改执行体，本层跟着改述。

| 什么时候读 | 块 |
|---|---|
| 核对命令/文档里的符号是否是本包真名 | `references/gates/symbol-truth.md` |
| 跑 `unity recompile` 并取回执与新鲜度证据 | `references/gates/bridge-protocol.md` |
| 动手前确认路径能不能碰、搬了会不会静默断 | `references/gates/forbidden-paths.md` |
| 新增或搬动源文件时的 `.meta` 成对范围 | `references/gates/metas-pairing.md` |

## 与兄弟包的分工

安装器脚本归本包；`com.moirai.framework`、OpenUPM 注册表、URP、HybridCLR 属外部包/服务；宿主 `Packages/manifest.json` 与 `ProjectSettings/` 属宿主工程。

## 诚实边界

- 本包无测试装配
- 本包没有 CI 工作流，不跑测试、不产生覆盖率。
- 本包没有 CHANGELOG 文件；版本以 `package.json` 为准。
- `README.md` 描述环境检查、核心包安装、模板选择与安装状态；没有 Documentation~。
