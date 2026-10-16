---
name: moirai-conventions
description: Moirai Framework convention layer — the single authoritative home for this package's coding, testing, verification and release rules. Load the matching block before changing code, wiring inspector data, running verification or closing out a release; the main doc keeps only hard constraints and pointers.
---

## Triggers
- Writing or changing code in this package (naming, structure, hot-path and engine rules)
- Writing tests, choosing test layers, or maintaining guards and coverage baselines
- Deciding whether to test, in what order, and what counts as green
- Committing, updating docs and CHANGELOG, or closing out a release
- 写/改本包代码、建用例与选用例分层、决定要不要测与怎么算判绿、提交与文档 CHANGELOG 版本收尾

# Moirai 规范知识层

细则在本层只存一份，权威位置就是下表「块」列出的文件。`CLAUDE.md` 里同主题只保留「哪几档违反要打回」的摘要与指向本层的指针；
同一段话在两边各写一遍即视为缺陷——改细则只改本层，改主文档只改摘要与指针。命令正文的 `## 判据` 段同样只指路径，不复述第二套。

| 什么时候读 | 块 | 有无自动判据 |
|---|---|---|
| 写/改本包生产代码前的命名、结构、热路径与引擎约束 | `.claude/skills/moirai-conventions/references/coding.md` | 无：靠评审与编译器，命名面部分落在 `.claude/skills/moirai-conventions/scripts/check_config_surface.py` 的 C 段 |
| 写用例、选分层、建夹具、放可见性、维护守卫与覆盖率基线 | `.claude/skills/moirai-conventions/references/testing.md` | 无：由 `Tests/EditorMode/**` 的用例与守卫承担 |
| 决定"要不要写测试、按什么顺序写、怎么算判绿" | `.claude/skills/moirai-conventions/references/verification.md` | 无：对错由桥落盘的报告当场见分晓 |
| 提交、文档与 CHANGELOG、版本号与 PR 收尾 | `.claude/skills/moirai-conventions/references/release.md` | 无：靠工作流与人工自查承担 |
| 核对命令正文里的框架符号是否真存在 | `.claude/skills/moirai-conventions/references/gates/symbol-truth.md` | 有：`.claude/skills/moirai-conventions/scripts/check_config_surface.py` 的 C 段 |
| 投单取回执的可执行片段 | `.claude/skills/moirai-conventions/references/gates/bridge-protocol.md` | 无：对错由桥落盘的报告当场见分晓 |
| 动手前确认路径能不能碰 | `.claude/skills/moirai-conventions/references/gates/forbidden-paths.md` | 无：靠权限 deny 与命令正文承担 |
| 新增/搬动源文件的 `.meta` 成对 | `.claude/skills/moirai-conventions/references/gates/metas-pairing.md` | 无：靠该块自带的 shell 片段自查 |

上表八块分两类：四块**规范细则**（`coding`、`testing`、`verification`、`release`）是各自主题的唯一权威落点，`CLAUDE.md` 只留摘要与指针；四块**判据口径**（`gates/*`）述执行体的射程与修法优先级。八块里只有符号真名那块有量具替你报错：`.claude/skills/moirai-conventions/scripts/check_config_surface.py` 是本地脚本，
一趟跑完配置层的全部判据段（段数与分段标题以量具自己的输出为准，本层不抄），不进测试装配也不进 CI 门禁——跑不跑由动手的人负责。
**上表的块与量具的段不是一一对应**：有的段没有独立细则块可指，也有块的规矩一部分落在权限 deny 与人工自查上、并不只由某一段管——
别把「上表没列这一段」读成「那段不存在」，也别把「这段绿了」读成「这个主题有人替你守着」（各段管到哪一层，写在该段自己的射程边界里）。
其余三块「本层写了」不等于「有人会替你报错」：
`forbidden-paths` 靠权限 deny 与命令正文承担，`metas-pairing` 靠该块自带的 shell 片段自查，`bridge-protocol` 的形状对不对由桥落盘的报告当场见分晓。
判据形状要变时改执行体，本层跟着改述；只改本层就是话与牙没对上号。

**指针形态（全层统一）**：跨文件指针一律写成反引号内、以包根为基座的全路径，例
`.claude/skills/moirai-conventions/references/gates/symbol-truth.md`。以仓内已知根段起头的反引号路径都按包根受检，
受检根段的清单就是量具的 `POINTER_ROOTS` 常数本身——本层不抄第二份：抄进去的根段会被判据当成一条指针去核 tracked
（还没落位的根段当场红），漏抄一项又会让读者以为那种写法不受检。
skill 根相对形态（`references/gates/…` 一类）不用——同一条串在 `SKILL.md` 与 gates 文件里解析出的基座不同，一条指针只能对一边。
运行期产物（外层工程 `Temp` 目录下的请求与报告）和绝对路径不写成受检形态：干净的检出里它们本就不存在，受检就是假红。
