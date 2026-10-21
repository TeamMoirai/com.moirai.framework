---
name: gate:release
description: 发版前本地预检：版本归属、文档同步、meta 成对、编译门与未取证项
when_to_use: 准备在 GitHub 手动 dispatch 发版前做最后一遍只读核对
argument-hint: [目标 tag]
arguments: [tag]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Bash(git show:*), Bash(python:*)
---

## 判据

- 根 `package.json` 版本与各插件 manifest 版本各自独立；本目录没有发版工作流，版本变更以实际发布动作为准，日常提交不批量改版本。
- 本包没有 CHANGELOG 文件；版本以根 `package.json` 与各插件 manifest 为准。
- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无；本包没有 CI 工作流（没有 `.github/`），不跑测试、不产生覆盖率。。
- 发版前必须有编译门证据；未取证项单列。
- 本命令不改版本、不建 tag、不 dispatch。

## 预检清单

| # | 项 | 判据 | 结果 |
|---|---|---|---|
| 1 | 工作树归属 | `git status --short`；别人的改动不混入 | |
| 2 | 版本未手改 | 工作树 `package.json.version` 与 HEAD 一致 | |
| 3 | 文档 | 用户可见变化已同步；目录/TOC 未被手改 | |
| 4 | meta 成对 | `.cs`/`.asmdef`/`.asmref` 两侧差集为空 | |
| 5 | 编译门 | `consoleErrors == 0`，产物 mtime 越过改动时刻 | |
| 6 | 测试 | 插件测试按装配存在性：只有 6 个插件有测试装配，其余无 | |
| 7 | 虚假声明 | 无 CI 测试/覆盖率/CHANGELOG/PlayMode 回归声称 | |
| 8 | 发布正文 | 需要迁移口径的变更已写进 Release 正文草稿 | |

## 可执行片段

```bash
python - <<'PY'
import json, subprocess
from pathlib import Path
ROOT = Path.cwd()
head = json.loads(subprocess.run(["git","show","HEAD:package.json"], capture_output=True, text=True, check=True).stdout)
cur = json.loads((ROOT/"package.json").read_text(encoding="utf-8-sig"))
print("[version] HEAD", head.get("version"), "worktree", cur.get("version"))
print("[docs]", [p.name for p in ROOT.glob("README*")])
print("[changelog]", [p.name for p in ROOT.glob("CHANGELOG*")] + [p.name for p in ROOT.glob("changelog*")])
print("[workflows]", sorted(p.name for p in (ROOT/".github/workflows").glob("*.yaml")) if (ROOT/".github").exists() else [])
PY
```

## 输出

```markdown
## 发版预检

- 目标 tag：
- 包版本（HEAD / 工作树）：
- 编译门：
- 测试：插件测试按装配存在性：只有 6 个插件有测试装配，其余无
- 文档同步：
- 未取证：
- 阻断项：任一条 fail 即"不建议 dispatch"
- 本轮不声称的事：
```
