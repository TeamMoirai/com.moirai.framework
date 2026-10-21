---
name: moirai-plugin-conventions
description: Moirai Framework Plugins（com.moirai.framework.plugin）的规范知识层。写或改本包代码、跑验证与发版收尾时按需加载对应细则块；主文档 CLAUDE.md 只留硬约束摘要与指针。
---

# Moirai Framework Plugins 规范知识层

细则在本层只存一份，权威位置就是下表「块」列出的文件。`CLAUDE.md` 与命令正文里同主题只保留摘要与指向本层的指针；同一段话在两边各写一遍即视为缺陷。

## 规范细则（按主题）

| 什么时候读 | 块 |
|---|---|
| 写/改生产代码前的命名、结构、领域模板与热路径 | `references/coding.md` |
| 测试装配与替代判据 | `references/testing.md` |
| 决定"这一轮算不算过"、编译门与新鲜度 | `references/verification.md` |
| 提交、文档、版本号与发版预检 | `references/release.md` |

## 判据口径（按执行体）

执行体在别处（宿主编辑器的文件桥、编译产物、仓库内的 asmdef/asmref 与 `.meta`）。本层只述口径、射程边界与修法优先级；判据形状要变时改执行体，本层跟着改述。

| 什么时候读 | 块 |
|---|---|
| 核对命令/文档里的符号是否是本包真名 | `references/gates/symbol-truth.md` |
| 投 `refresh` / `recompile` 并取回执与新鲜度证据 | `references/gates/bridge-protocol.md` |
| 动手前确认路径能不能碰、搬了会不会静默断 | `references/gates/forbidden-paths.md` |
| 新增或搬动源文件时的 `.meta` 成对范围 | `references/gates/metas-pairing.md` |

## 与兄弟包的分工

插件扩展框架类型（`Moirai.Atropos.*`），但插件程序集是 `Moirai.Plugin.*`；框架类型归框架包，插件类型归各自 asmdef，第三方 SDK 归外部包。

## 诚实边界

- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无
- 本包没有 CI 工作流（没有 `.github/`），不跑测试、不产生覆盖率。
- 本包没有 CHANGELOG 文件；版本以根 `package.json` 与各插件 manifest 为准。
- `README.md`/`README_EN.md` 记录目录形状、插件清单与清单字段；没有 Documentation~。
