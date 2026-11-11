---
name: check:review
description: 按 Mandatory/Prefer/Reference 三档评审本包代码，只读，每条带文件:行号与判据出处
when_to_use: 需要评审一轮改动或存量模块时
argument-hint: [文件或目录，默认本轮改动]
arguments: [target]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Bash(git show:*)
---

## 判据

- 三档定义与细则只在 `.claude/skills/moirai-plugin-conventions/references/coding.md` 存一份；本命令不复述第二套。
- 每条结论必须给 `文件:行号` 与判据出处；给不出的写"未取证"。
- 插件命名跟随所扩展的框架模块；不要按某一插件的写法给全部插件定规。
- 各插件内的第三方 SDK 源码与未安装宏分支不在重构射程。
- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无；不得把"测试通过/CI 绿/PlayMode 全绿"写进评审。
- 只读：不改源码、不改测试、不跑 git 写命令。

## 流程

1. 读 `git status --short` 与 `git diff`，确定评审面；别人未提交的改动标注归属。
2. 按被审对象取细则：coding / testing / verification / release。
3. 对每条疑点先亲读声明与调用点，再定性。
4. 出报告：三档分节 + 「未取证项」 + 「本轮不声称的事」。

## 输出

```markdown
## 评审报告：<对象>

### Mandatory（违反要打回）
- [ ] <问题> —— `<文件:行号>`，判据：`<细则块#段>`

### Prefer（性能敏感区必须）
- [ ] ...

### Reference（存量，改动时顺手收）
- [ ] ...

### 未取证项
- ...

### 本轮不声称的事
- ...
```
