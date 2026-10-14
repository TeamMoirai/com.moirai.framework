---
name: moirai-conventions
description: Moirai Framework 的规范知识层。写/改本包代码、做检视器接线、跑验证与发版收尾时按需加载对应细则块；主文档只留硬约束与指针。
---

# Moirai 规范知识层

细则在本层只存一份，权威位置就是下表「块」列出的文件。`CLAUDE.md` 里同主题只保留「哪几档违反要打回」的摘要与指向本层的指针；
同一段话在两边各写一遍即视为缺陷——改细则只改本层，改主文档只改摘要与指针。命令正文的 `## 判据` 段同样只指路径，不复述第二套。

| 什么时候读 | 块 | 有无自动判据 |
|---|---|---|
| 核对命令正文里的框架符号是否真存在 | `.claude/skills/moirai-conventions/references/gates/symbol-truth.md` | 有：`.claude/skills/moirai-conventions/scripts/check_config_surface.py` 的 C 段 |
| 投单取回执的可执行片段 | `.claude/skills/moirai-conventions/references/gates/bridge-protocol.md` | 无：对错由桥落盘的报告当场见分晓 |
| 动手前确认路径能不能碰 | `.claude/skills/moirai-conventions/references/gates/forbidden-paths.md` | 无：靠权限 deny 与命令正文承担 |
| 新增/搬动源文件的 `.meta` 成对 | `.claude/skills/moirai-conventions/references/gates/metas-pairing.md` | 无：靠该块自带的 shell 片段自查 |

四块分两档，只有符号真名那块有量具替你报错：`.claude/skills/moirai-conventions/scripts/check_config_surface.py` 是本地脚本，
一趟跑完配置层的全部判据段（段数与分段标题以量具自己的输出为准，本层不抄），不进测试装配也不进 CI 门禁——跑不跑由动手的人负责。其余三块「本层写了」不等于「有人会替你报错」：
`forbidden-paths` 靠权限 deny 与命令正文承担，`metas-pairing` 靠该块自带的 shell 片段自查，`bridge-protocol` 的形状对不对由桥落盘的报告当场见分晓。
判据形状要变时改执行体，本层跟着改述；只改本层就是话与牙没对上号。

**指针形态（全层统一）**：跨文件指针一律写成反引号内、以包根为基座的全路径，例
`.claude/skills/moirai-conventions/references/gates/symbol-truth.md`。以仓内已知根段起头的反引号路径都按包根受检，
受检根段的清单就是量具的 `POINTER_ROOTS` 常数本身——本层不抄第二份：抄进去的根段会被判据当成一条指针去核 tracked
（还没落位的根段当场红），漏抄一项又会让读者以为那种写法不受检。
skill 根相对形态（`references/gates/…` 一类）不用——同一条串在 `SKILL.md` 与 gates 文件里解析出的基座不同，一条指针只能对一边。
运行期产物（外层工程 `Temp` 目录下的请求与报告）和绝对路径不写成受检形态：干净的检出里它们本就不存在，受检就是假红。
