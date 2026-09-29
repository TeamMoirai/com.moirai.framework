# XML Comment Style

> `summary` states what the thing is in one sentence; invariants callers must know go to `remarks` (one clause per line); usage goes to `example`; history and process narrative stay out of code; **line breaks are formatting, not a violation**.

Applies to every C# file in this package (`Runtime/`, `Editor/`, `Tests/`, `SourceGenerators/Source~/`, `Templates~`). The bar follows Microsoft .NET API documentation practice (`summary` states, `remarks` supplements, `example` shows usage, `inheritdoc` for overrides) and the Google C# Style Guide rule that comments describe *what*, not *how* or *why*.

## Rules

### R1 `summary` = one sentence

- **Types and methods use the three-line form**: `/// <summary>`, content, `/// </summary>` on their own lines; content is 1–2 lines, long sentences break at semantic boundaries (`<br />`).
- **Everything else (properties / fields / enum members / events, …) stays inline**: `/// <summary>one sentence</summary>`, kept compact.
- Answer only "what it is / what it does", as a third-person verb phrase. Rationale, background, and review history move out (R2/R3).
- A summary is a single paragraph, never wrapped in `<para>`. Inline references use `<c>`, `<see cref="…"/>`, `<paramref name="…"/>`.

### R2 `remarks` = invariants callers must know (≤6 clauses, optional)

Keep: threading/timing constraints, performance promises (0-GC, frame budget), lifecycle pairing (who Acquires, who Releases), degradation and failure semantics, idempotency, protocol files and required call order.

Drop: design rationale, review conclusions, history, incident post-mortems. **One clause per line**: a long clause may continue on the next line (continuation lines end with `<br />`, the last one does not); group distinct themes with `<para>`. **Caller-visible information must survive** — tightening compresses, it does not delete.

### R3 Banned in any tag

❌ Dates (`2026-09-28`); commit hashes / PR numbers / branch names; `CHANGELOG` references; process narrative such as "measured", "reproduced", "incident", "lesson", "review", "red-then-green", "regression lock"; "used to be X, now Y" change history.

Those belong in `CHANGELOG.md`, commit messages, and review notes. Code keeps only what a reader needs to understand the contract.

### R4 `example` allowed and encouraged (≤15 lines)

✅ Short usage samples in `<example>` + `<code lang="csharp">`, echoing the unit tests. ❌ Examples written as a full business flow.

### R5 Structured tags

`<param>` one line, never restating the type; `<returns>` one line; `<exception cref="…">` where something throws; overrides prefer `<inheritdoc/>` and add only the delta.

### R6 Language

Narrative in Chinese, consistent with the existing codebase; type names, member names, paths, and protocol fields stay verbatim. No stacked bilingual repetition.

### R7 Layout: line breaks are a tool, not a violation

- **Keep lines ≤120 characters** (a CJK character counts as one). Past that, break at semantic boundaries instead of leaving one 200+ character line carrying three constraints.
- Same paragraph: `<br />` at the end of each line (not the last). Distinct themes: wrap each in `<para>` — only when there really are two or more themes; never shred one or two sentences into fragments.
- `<code>` / `<example>` bodies are preformatted: **no line-width limit**, and don't touch their line breaks or indentation.
- Never break inside an inline tag (`<see cref="…"/>`, `<c>…</c>`, `<paramref name="…"/>`).

## ✅ / ❌ Examples

### Layout (same content, two arrangements)

❌ Bad: constraints crammed into one long line (horizontal scrolling or editor soft-wrap to read):

```csharp
/// <remarks>
/// 生成 s_Handler 字段（private，partial 同类可访问）、IsValid、Handler（get/set）与 RequireHandler（不触发懒加载，未就绪抛 GameException，写路径 fail-fast 入口）；工厂契约三档：两者皆声明时懒加载先调 GetHandlerFromSettings、返回 null 回退 CreateDefaultHandler，仅声明后者则直接调用，仅声明前者（MIRAI102）必须返回非空，null 即抛 InvalidOperationException；两者都未声明（MIRAI101）时访问 Handler 抛异常，显式 setter 赋值始终可用。
/// </remarks>
```

✅ Good: one clause per line, `<br />` inside the paragraph, the sample moved to `<example>`:

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

### Type level: compress the design essay back to a contract

❌ Bad (class-level 10-line `summary` with background / approach / semantics paragraphs):

```csharp
/// <summary>
/// Pool maintenance scheduler: drives expiry and capacity reclamation per frame tick.
/// <para>Background: … (why it exists, how it evolved)</para>
/// <para>Approach: … (why it was implemented this way)</para>
/// <para>Semantics: … (long explanatory paragraph)</para>
/// </summary>
```

✅ Good:

```csharp
/// <summary>
/// Pool maintenance scheduler: drives expiry and capacity reclamation per frame tick.
/// </summary>
/// <remarks>
/// Maintenance runs in order during the PlayerLoop update phase; per-frame reclamation is batched and capped.
/// </remarks>
```

### Type level: protocol contracts belong in `remarks`

❌ Bad (`Tests/EditorMode/TestRequestRunner.cs` with a 32-line `summary`: background / domain reload / four protocol files / caller duties / intake gate / orphan handling).

✅ Good:

```csharp
/// <summary>
/// Request-driven test runner: polls a request file under the project <c>Temp/</c> folder, runs one Test Runner pass, and writes results to the requested path.
/// </summary>
/// <remarks>
/// Lives in the test assembly (<c>UNITY_INCLUDE_TESTS</c> gated) and never ships in player builds.<br />
/// Protocol files: request <c>Temp/MoiraiTestRequest.json</c> (deleted on read), cancel <c>…cancel.json</c>, progress <c>{output}.progress</c>, result <c>{output}</c> plus the pairing marker <c>.done</c>.<br />
/// Callers must supply a unique id and trust only the matching <c>.done</c>; a request with both <c>assemblies</c> and <c>tests</c> empty is rejected outright.<br />
/// No intake while compiling, importing, switching PlayMode, or while any run is active; after a domain reload liveness is judged by job guid and an unprovable job is closed as ABORTED.
/// </remarks>
```

### Member level: one line per constraint

❌ Bad:

```csharp
/// <summary>Sets the heartbeat timeout.<para>This value detects player-side disconnects; it was set too low historically which killed valid runs, so the floor is 10 seconds; the default is 600 seconds today.</para></summary>
```

✅ Good:

```csharp
/// <summary>Sets the player-side heartbeat timeout in seconds.</summary>
/// <remarks>Floor is 10 seconds; values below it are clamped to 10.</remarks>
```

### Usage sample (`example`)

```csharp
/// <summary>Plays audio by category and returns an agent handle.</summary>
/// <example>
/// <code lang="csharp">
/// var agent = AudioService.Play(EAudioTrack.Bgm, "bgm_main", options);
/// agent.SetVolume(0.6f);
/// </code>
/// </example>
```

## Checklist

- Is `summary` one sentence (≤3 lines)?
- Did every caller-visible invariant survive into `remarks` (threading / performance / pairing / degradation), one clause per line?
- Any line over 120 characters? Break it at semantic boundaries (`<br />` within a paragraph, `<para>` between themes) without splitting inline tags.
- Any leftover date, commit hash, `CHANGELOG` reference, or process narrative?
- Is a short usage sample worth adding via `example`?
- Are `param` / `returns` / `exception` one line each, without restating types?
