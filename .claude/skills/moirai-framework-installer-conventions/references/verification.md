# 验证细则（本包）

## 验证面

安装器只有一个编辑器脚本，没有 asmdef；编译发生在宿主工程的预定义编辑器程序集里，不是独立包 DLL。

## pipeline 连接与判活

连接体是宿主工程的 `com.unity.pipeline` 包（Unity 6 装 registry 版；**Unity 2022 通过 MoiraiFrameworkPackage 安装兼容版 pipeline**，框架包管理器 `Tools/Moirai/Framework Package Manager`），命令行侧是 `unity` CLI：

```bash
unity status --project-path 'P' --format json
unity command editor status --project-path 'P' --result-only --format json
```

| 读什么 | 用法 |
|---|---|
| `status` | 为 `ready` 才是连上了；`STATUS_NO_INSTANCES` = 没有可连的编辑器 |
| `compiling` / `domainReloadInProgress` | 任一为 true 即忙碌，不投命令 |
| `codeLoadCount` | 改动前后各取一次，推进即域重载真发生过 |
| `lastHeartbeat` | 陈旧几秒即主线程没在推进（导入中、被原生模态框挡住） |

工程有编译错误时编辑器以 Safe Mode 启动、pipeline 不加载，先修错误并重启 Unity，不要另开工程。

## 编译门

1. 先判活：`unity command editor status` 的 `compiling`、`domainReloadInProgress` 皆 false；`unity command test status` 为 `no_tests`。
2. 触发编译：`unity recompile --project-path 'P' --timeout 420 --format json`（含 asset refresh，增删 `.cs` 后也走这一条）；退出码 `0` 编过 / `6` 编译报错 / `7` 连不到编辑器。结论以 `unity command script recompile_status` 的 `{status, failed, errors[], compilationFailed}` 为准，`exit=6` 单独不构成结论。
3. 判绿要同时满足：`script recompile_status` 的 `failed:false` 且 `errors:[]`；宿主 `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll`（本包无 asmdef）的 mtime 越过本次改动时刻；`unity status` 的 `codeLoadCount` 有推进。
4. 不要再叠发 `script recompile`：`unity recompile` 已含刷新与编译。
5. 安装器的行为依赖宿主工程的 Package Manager 与 ProjectSettings；编译门只能证明脚本编过，不能证明安装流程正确。

## 测试门

本包无测试装配：不投 `unity command test run`、不填 `assemblies`、不读测试产物；报告里固定写“测试段：不适用”。

## 新鲜度三段

| 段 | 本包能不能取到 | 写法 |
|---|---|---|
| 源树 mtime → 归属产物 mtime | 能 | 比源文件与宿主 `Library/ScriptAssemblies/Assembly-CSharp-Editor.dll`（本包无 asmdef） |
| `codeLoadCount` 是否推进 | 能 | 改动前后各读一次 `unity status` |
| 本轮编译是否覆盖本包脚本 | 能 | 比宿主 `Assembly-CSharp-Editor.dll` 的 mtime；不拿框架或其他包的读数冒充 |

## 不许做的验证捷径

- 不用 `-batchmode` 另开同一工程（会与活动编辑器争锁）。
- 不把"工具调用返回成功"当编译/测试通过。
- 不在编译失败时用"上次的绿"当本轮结论。
- 不向 pipeline 请求不存在的测试程序集。
- 不把人工点验写成回归。
