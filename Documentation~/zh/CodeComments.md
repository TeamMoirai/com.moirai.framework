# XML 文档注释规范

> `summary` 一句话说清「是什么」；调用方必须知道的不变量进 `remarks`（一条一行）；用法进 `example`；变更史与过程叙事不进代码；**换行是排版手段，不是违规**。

适用于包内全部 C# 文件（`Runtime/`、`Editor/`、`Tests/`、`SourceGenerators/Source~/`、`Templates~`）。口径参考 Microsoft .NET API 文档惯例（`summary` 陈义、`remarks` 补充、`example` 用法、覆写用 `inheritdoc`）与 Google C# Style Guide 的「注释描述是什么，不描述怎么做、为何这么做」。

## 规则

### R1 `summary` = 一句话

- **类型与方法用三行式**：`/// <summary>`、内容、`/// </summary>` 各占一行；内容 1~2 行，长句按语义断行（`<br />`）。
- **其余（属性 / 字段 / 枚举成员 / 事件等）一律单行内联**：`/// <summary>一句话</summary>`，保持紧凑。
- 只答「是什么 / 做什么」，用第三人称动词短语；设计动因、历史背景、评审过程一律外移（R2/R3）。
- summary 是单段，不套 `<para>`；行内引用用 `<c>`、`<see cref="…"/>`、`<paramref name="…"/>`。

### R2 `remarks` = 调用方必须知道的不变量（≤6 条，可省）

放：线程/时序约束、性能承诺（0-GC、帧预算）、生命周期配对（谁 Acquire 谁 Release）、降级与失败语义、幂等性、协议文件与关键调用顺序。

不放：设计动因、评审结论、历史沿革、事故复盘。**一条一行**：一条不变量占一行，长条目可续行（续行以 `<br />` 结尾、末行不加）；确有两个以上主题时用 `<para>` 分组。**调用方可感知的信息必须保留**——精简是压缩，不是删除。

### R3 叙述禁入（任何标签内）

❌ 日期（`2026-09-28`）；提交号 / PR 号 / 分支名；`CHANGELOG` 引用；`实测`、`实证`、`复现`、`事故`、`教训`、`评审`、`先红后绿`、`红态`、`绿态`、`回归锁` 一类过程叙事；`原为 X，现改为 Y` 变更史。

这些内容的归宿是 `CHANGELOG.md`、提交信息与评审底稿。代码里只留能读懂契约的话。

### R4 `example` 允许且鼓励（≤15 行）

✅ 短用法示例进 `<example>` + `<code lang="csharp">`，与单测呼应；❌ 把示例写成完整业务流程。

### R5 结构化标签

`<param>` 一行、不复述类型；`<returns>` 一行；会抛的地方写 `<exception cref="…">`；覆写优先 `<inheritdoc/>`，只补差异。

### R6 语言

中文叙述（与现状一致）；类型名、成员名、路径、协议字段保持原文；不中英混排堆叠。

### R7 排版：换行是手段，不是违规

- **单行 ≤120 字符**（中文按 1 字符计）。超限就按语义断行，别把三条约束挤成一条 200+ 字符的长条行。
- 同段换行用 `<br />`（行末加，最后一行不加）；分段用 `<para>` 包住整段——只在确有 2 个以上主题时才分段，别把一两句话拆成碎段。
- `<code>` / `<example>` 内是预格式文本，**不受行宽限制**，也不要动里面的换行与缩进。
- 断行不得切开行内标签（`<see cref="…"/>`、`<c>…</c>`、`<paramref name="…"/>`）。

## ✅ / ❌ 对照

### 表面与换行（同一份内容，两种排布）

❌ 反例：约束挤成一条长条行（读源码时要么横向滚动，要么靠编辑器折行）：

```csharp
/// <remarks>
/// 生成 s_Handler 字段（private，partial 同类可访问）、IsValid、Handler（get/set）与 RequireHandler（不触发懒加载，未就绪抛 GameException，写路径 fail-fast 入口）；工厂契约三档：两者皆声明时懒加载先调 GetHandlerFromSettings、返回 null 回退 CreateDefaultHandler，仅声明后者则直接调用，仅声明前者（MIRAI102）必须返回非空，null 即抛 InvalidOperationException；两者都未声明（MIRAI101）时访问 Handler 抛异常，显式 setter 赋值始终可用。
/// </remarks>
```

✅ 正例：一条一行，同段用 `<br />`，示例另挂 `<example>`：

```csharp
/// <summary>
/// 标记静态类为处理器宿主，由源生成器生成 <c>Handler</c> 属性与线程安全懒加载。
/// </summary>
/// <remarks>
/// 生成 <c>s_Handler</c> 字段（private，partial 同类可访问）、<c>IsValid</c>、<c>Handler</c>（get/set）与 <c>RequireHandler</c>（未就绪抛 <see cref="GameException"/>，写路径 fail-fast 入口）。<br />
/// 工厂契约三档：两者皆声明时先调 <c>GetHandlerFromSettings</c>、返回 null 回退 <c>CreateDefaultHandler</c>；仅声明后者直接调用。<br />
/// 仅声明 <c>GetHandlerFromSettings</c>（MIRAI102）必须返回非空，null 即抛 <see cref="InvalidOperationException"/>；两者都未声明（MIRAI101）时访问 <c>Handler</c> 抛异常。
/// </remarks>
/// <example>
/// <code>
/// [HandlerHost(typeof(LogHandler))]
/// public static partial class LogUtility
/// {
///     private static LogHandler CreateDefaultHandler() => new DefaultLogHandler();
/// }
/// </code>
/// </example>
```

### 类级：把设计小作文压回契约

❌ 反例（类级 10 行 summary，背景 / 取向 / 语义三段铺陈）：

```csharp
/// <summary>
/// 池维护调度器：按帧节拍驱动各池的过期回收与容量回收。
/// <para>背景：…（为什么要有它，怎么演变来的）</para>
/// <para>取向：…（当初为什么这样实现）</para>
/// <para>语义：…（长段落解释）</para>
/// </summary>
```

✅ 正例：

```csharp
/// <summary>
/// 池维护调度器：按帧节拍驱动各池的过期回收与容量回收。
/// </summary>
/// <remarks>
/// 维护在 <c>PlayerLoop</c> 的更新阶段顺序执行，单帧回收批量有上限。
/// </remarks>
```

### 类级：协议契约进 `remarks`

❌ 反例（`Tests/EditorMode/TestRequestRunner.cs` 32 行 summary：背景 / 跨域重载 / 协议四文件 / 调用方义务 / 接单门 / 孤儿单 / 归属说明）。

✅ 正例：

```csharp
/// <summary>
/// 请求式测试驱动：轮询工程 <c>Temp/</c> 下的请求文件跑一轮 Test Runner，进度与结果回写指定路径。
/// </summary>
/// <remarks>
/// 住测试程序集（<c>UNITY_INCLUDE_TESTS</c> 门控），不进玩家包。<br />
/// 协议：请求 <c>Temp/MoiraiTestRequest.json</c>（读到即删）、取消 <c>…cancel.json</c>、进度 <c>{output}.progress</c>、结果 <c>{output}</c> 与配对标记 <c>.done</c>。<br />
/// 调用方必须自带唯一 id 并只认配对的 <c>.done</c>；<c>assemblies</c> 与 <c>tests</c> 全空直接拒绝。<br />
/// 编译、导入、切 PlayMode 或已有 run 在跑时不接单；域重载后按作业 guid 判活，判不了即按 ABORTED 收口。
/// </remarks>
```

### 成员级：约束一条一行

❌ 反例：

```csharp
/// <summary>设置心跳超时。<para>这个值是用来判断玩家侧断连的，历史上因为过短误杀过，所以下限是 10 秒；目前默认取 600 秒。</para></summary>
```

✅ 正例：

```csharp
/// <summary>设置玩家侧心跳超时（秒）。</summary>
/// <remarks>下限 10 秒；低于下限按 10 秒取值。</remarks>
```

### 用法示例（`example`）

```csharp
/// <summary>按分类播放音频并返回代理句柄。</summary>
/// <example>
/// <code lang="csharp">
/// var agent = AudioService.Play(EAudioTrack.Bgm, "bgm_main", options);
/// agent.SetVolume(0.6f);
/// </code>
/// </example>
```

## 自查清单

- `summary` 是否一句话（≤3 行）？
- 调用方必须知道的不变量是否都进了 `remarks`（线程 / 性能 / 配对 / 降级），且**一条一行**？
- 有没有 120 字符以上的长条行？有就按语义断行（`<br />` 同段、`<para>` 分段），别切开行内标签。
- 是否残留日期、提交号、`CHANGELOG` 引用或「实测/教训/评审」叙事？
- 是否有能一眼看懂的用法示例需要补 `example`？
- `param` / `returns` / `exception` 是否各一行、不复述类型？
