---
name: check:manifest
description: 只读校验 15 份插件 manifest：id/目录一致、字段齐全、版本地板与依赖形态
when_to_use: 新增/修改插件清单、发版或批量整理插件目录后
argument-hint: [插件 id，默认全部]
arguments: [plugin_id]
allowed-tools: Read, Grep, Glob, Bash(python:*), Bash(git status:*), Bash(git diff:*)
---

## 判据

- 15 个顶层目录各有一份 `<id>/<id>.plugin.json`；`id` 与目录名一致。
- 必填字段齐全：`id`、`name`、`function`、`description`、`author`、`version`、`minUnityVersion`、`minFrameworkVersion`、`tags`、`dependencies`、`upmDependencies`。
- `minFrameworkVersion` 不低于框架当前版本；`minUnityVersion` 与插件真实要求一致。
- 插件 asmdef 存在且命名为 `Moirai.Plugin.<名>`；测试 asmdef 只在 `*/Tests/`。
- 只读：不改 manifest、不改 asmdef。

## 可执行片段

```bash
python - <<'PY'
import json, glob
from pathlib import Path
ROOT = Path.cwd()
required = ["id","name","function","description","author","version","minUnityVersion","minFrameworkVersion","tags","dependencies","upmDependencies"]
for f in sorted(ROOT.glob("*/*.plugin.json")):
    d = json.loads(f.read_text(encoding="utf-8"))
    miss = [k for k in required if k not in d]
    same = f.parent.name == d.get("id")
    print(f.name, "missing=", miss, "id==dir:", same)
PY
```

## 输出

```markdown
## 插件清单判据

| 插件 | id/目录 | 缺字段 | minFrameworkVersion | 测试装配 |
|---|---|---|---|---|

未取证：
```

