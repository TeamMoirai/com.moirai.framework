---
name: make:new-plugin-test
description: 为插件新增 `*/Tests/` 测试装配与用例
when_to_use: 插件有可测的适配契约、序列化往返或后端能力探测时
argument-hint: <插件目录> [测试类名]
arguments: [plugin_dir, test_class]
allowed-tools: Read, Grep, Glob, Bash(git status:*), Bash(git diff:*), Edit, Write
---

## 判据

1. 测试放 `<插件目录>/Tests/`，新建独立 `Moirai.Plugin.<名>.Tests.asmdef`；引用插件运行期 asmdef、TestRunner 与 nunit。
2. 现有 6 个测试装配可作样本：`addressables/Tests/`、`fmod/Tests/`、`memorypack/Tests/`、`messagepack/Tests/`、`protobuf/Tests/`、`wwise/Tests/`。
3. 用例只验插件契约、序列化往返、后端能力探测与降级；不验外部 SDK 内部行为。
4. 测试装配不得被插件运行期 asmdef 引用；测试代码不进入运行期程序集。
5. 新增 `.cs` 的 `.meta` 由 Unity 生成并同笔提交；收尾用真实装配名投测试。

## 落地位置

```
<插件目录>/Tests/
├── Moirai.Plugin.<名>.Tests.asmdef
└── <测试类>.cs
```

## 要点

- 测试方法名用 `场景_条件_期望`；失败信息写期望与实际。
- 需要 internal 访问时走插件 asmdef 的 `InternalsVisibleTo`，不要反射。
- 外部 SDK 未安装时，测试应能明确跳过或给出可读的阻塞原因，不伪装成通过。
