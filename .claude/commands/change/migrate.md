---
name: change:migrate
description: 公共 API、命名空间与序列化字段迁移：清单式改动 + 资产口径 + 文档同步
when_to_use: 要改公开签名、类型名、命名空间、序列化字段或程序集引用时
argument-hint: <迁移对象> [目标形态]
arguments: [subject, target]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Edit, Write
---

## 判据

- 先出符号级清单：每个被改的声明、每个调用点、每个受影响的资产/场景/预制、每个文档引用。
- 插件之间不同程序集；改框架契约要同笔检查所有引用该契约的插件。
- 文件移动用 `git mv` 保 `.meta` GUID；类型名/命名空间会进入序列化线，改名必须给资产迁移口径。
- `[FormerlySerializedAs]` 只作用于 Unity 序列化器；自定义数据通道要单独写迁移。
- 不留兼容层：破坏性变更直接切新形态，在 Release 正文/对应文档给迁移口径。
- 收尾过编译门；插件测试按装配存在性：只有 6 个插件有测试装配，其余无。

## 清单模板

```markdown
### 迁移：<旧> → <新>

| 面 | 清单 | 状态 |
|---|---|---|
| 声明 | <类型/成员/字段>（文件:行号） | 已改 |
| 同程序集调用点 | <文件:行号> … | 已改 |
| 资产/场景/预制 | <路径>（引用形态） | 已迁移/未取证 |
| 文档 | README / .claude | 已同步 |
| asmdef/asmref | 引用、versionDefines、asmref 目标 | 已核对 |
```

## 禁止

- 不把迁移和无关重构混在一笔；不保留两套公共 API。
- 不在没有资产清单的情况下声称"资产已迁移"。
