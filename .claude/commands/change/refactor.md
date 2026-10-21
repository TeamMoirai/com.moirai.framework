---
name: change:refactor
description: 结构性改造：不改行为、不动序列化线格式、同程序集调用点一次扫完
when_to_use: 要拆分类、收拢重复逻辑、调整目录命名空间或清理存量时
argument-hint: <重构目标> [范围]
arguments: [target, scope]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Bash(git log:*), Edit, Write
---

## 判据

- 行为不变：公共 API、Inspector 语义、序列化键与时序都不变。
- 序列化字段名、类名、命名空间不动；确需改名先走 `/change:migrate`。
- 插件之间不同程序集；改框架契约要同笔检查所有引用该契约的插件。
- 搬文件一律 `git mv` 保 `.meta` GUID；不删除重建、不手写新 GUID。
- 各插件内的第三方 SDK 源码与未安装宏分支不在重构射程。
- 收尾过编译门；有测试装配时测试应保持原绿。

## 流程

1. 先量范围：grep 出目标类型/成员的所有声明与调用点，按目录/程序集分组。
2. `git status --short` 分辨并行会话改动；共享文件避免整文件重写。
3. 设计最小结构：优先移动/提取，不引入只转发一层的包装。
4. 分步执行，每步可编译；搬文件用 `git mv`，同步 asmref 覆盖关系。
5. 跑编译门与相关测试；机械更新路径/类型名，不改断言。
6. 若路径变化，更新 `.claude/` 指针与 `CLAUDE.md` 摘要。

## 输出

- 改动面：文件/类型清单。
- 不变面：序列化字段、公共签名、Inspector 语义。
- 验证：编译门读数、测试计数或"无测试装配"、未跑项。
