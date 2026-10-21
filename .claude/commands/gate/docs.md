---
name: gate:docs
description: 只读核对 README/文档、包版本、程序集事实与 .claude 指针是否和树一致
when_to_use: 改完公共 API、程序集引用、目录或 AI 约定文档后
argument-hint: [无参数]
arguments: []
allowed-tools: Read, Grep, Glob, Bash(python:*), Bash(git status:*), Bash(git diff:*), Bash(git log:*)
---

## 判据

- 文档只登记现行事实：不写迁移史、不写"已移除/旧版"叙事（迁移口径进 Release 正文）。
- 本包文档是 `README.md`/`README_EN.md` 与 15 份插件 manifest；它与代码不一致时报告漂移，不按文档反推代码。
- 插件测试按装配存在性：只有 6 个插件有测试装配，其余无；本包没有 CI 工作流（没有 `.github/`），不跑测试、不产生覆盖率。；本包没有 CHANGELOG 文件；版本以根 `package.json` 与各插件 manifest 为准。。
- 只读：不改 README、不改版本号。

## 检查项

| 项 | 判据 |
|---|---|
| 文档存在性 | README 的插件清单与盘上 15 个 manifest 一一对应 |
| 包版本 | `package.json.version` 与 HEAD 一致 |
| 程序集事实 | 文档里的 asmdef/程序集名与盘上一致 |
| 指针 | `.claude` 与 `CLAUDE.md` 里的包根相对路径逐条存在 |
| 虚假声明 | 不得出现"CI 已跑测试""覆盖率达标""CHANGELOG 已同步""PlayMode 回归" |

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
## 文档判据

| 项 | 结果 | 证据 |
|---|---|---|
| 文档存在性 | pass/fail | 缺失文件 |
| 包版本 | pass/fail/未取证 | HEAD vs 工作树 |
| 程序集事实 | pass/fail | 不一致项 |
| 指针 | pass/fail | 缺失清单 |
| 虚假声明 | pass/fail | 文件:行号 |

漂移项（不自动改）：
未取证：
```
