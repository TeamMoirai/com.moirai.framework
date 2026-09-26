# Testing

> Layer assignment, case conventions, run channels, gate thresholds and release exit criteria for framework tests. This file is the **single source of truth** for testing constraints; the "Test Specification" and "AI Test Workflow" sections in `CLAUDE.md` are agent-facing summaries — this file wins on conflict.

## Purpose

Framework tests are not homework — they are **regression locks**. Every defect that got fixed, every external contract, every performance promise should have a case pinning it down. The test is: *if I delete this case, can the defect come back silently?* If yes, it earns its place. If no, delete it.

Three principles:

1. **Assert behavior, not implementation.** Assert "after calling X, Y holds", not "X internally called Z". Implementation-bound cases go red in bulk during refactors while catching no real defects.
2. **A failure must be attributable.** One red case must point directly at one clear cause. A case that needs manual reproduction to interpret is as good as absent.
3. **Determinism beats coverage.** A flaky case destroys more signal value than the coverage it contributes.

## Test layers

Four layers. **Pick the layer from this table before writing a case** — picking the wrong layer is the number-one source of poor cases.

| Layer | Assembly | Location | Covers | Criterion |
|---|---|---|---|---|
| **L1 Unit / contract** | `Moirai.Atropos.Tests.EditorMode` | `Tests/EditorMode/` | Pure logic, data structures, state machines, contract shape, degraded paths | Decidable without the Unity runtime (PlayerLoop, real frames, scenes, real IO) |
| **L2 Integration** | `Moirai.Atropos.Tests.PlayMode` | `Tests/PlayMode/` | Cross-component collaboration, real frame driving, scene/host lifecycle, real IO round-trips | The conclusion depends on *actually running* — only observable in play mode |
| **L3 Player acceptance** | `Moirai.Atropos.Tests.Player` | `Tests/Player/` | Zero-GC hot paths, managed allocation metering, IL2CPP behavior differences | **Not measurable in the editor** (the meter does not advance); only decidable in a player build |
| **L4 Benchmark** | `[Explicit]`, in its host assembly | Module directory or `Tests/Benchmarks/` | Throughput/latency/allocation baselines | Excluded from the regular suite; run manually or via the benchmark CI channel |

### Layer selection criteria

- Decidable in EditMode → **must** be L1. Do not push pure logic into PlayMode "for realism" (slower, harder to attribute, flakier).
- Needs `Awake`/`OnDestroy`/`DontDestroyOnLoad`/coroutines/real `Update` → L2.
- The conclusion depends on **managed allocation metering** → L3 (the editor's Mono returns a constant 0 from `GC.GetAllocatedBytesForCurrentThread()`; see "Zero-GC acceptance").
- You merely "want to know how fast it is right now" → L4, and it must be `[Explicit]`.

### Current distribution (2026-09-24)

| Layer | Files | Cases |
|---|---|---|
| L1 | 131 | ~1802 |
| L2 | 15 | ~70 |
| L3 | 5 | ~17 |

L2/L3 are notably thin: L2 currently covers only Audio and Kernel (BootChain). Resource / Save / UI / Scene / Input / Procedure have no integration coverage yet. See "Coverage targets" for the fill-in plan.

## Directory layout

```
Tests/
├── EditorMode/                  # L1
│   ├── Moirai.Atropos.Tests.EditorMode.asmdef
│   ├── TestRequestRunner.cs     # test bridge (infrastructure, not a case)
│   ├── EditorStateBridge.cs     # editor state bridge (infrastructure, not a case)
│   ├── Core/                    # mirrors Runtime/Core/<module>
│   │   ├── MemoryPool/
│   │   ├── Singleton/
│   │   ├── Events/
│   │   └── GameApp/
│   ├── DataStructure/           # mirrors Runtime/Core/DataStructure (pure data-structure cases)
│   ├── Service/                 # mirrors Runtime/Services/<module>
│   │   ├── Audio/  Save/  Resource/  UI/  ...
│   │   └── Kernel/
│   └── Utility/                 # mirrors Runtime/Core/Utilities
├── PlayMode/                    # L2
│   └── Service/<module>/
└── Player/                      # L3
    ├── PlayerTestBootstrap.cs   # disables GameApp.AutoBoot (infrastructure)
    └── Service/<module>/
```

Rules:

- **Test directories mirror the code under test.** A case for `Runtime/Services/Save/Container/X.cs` lives in `Tests/EditorMode/Service/Save/`. Finer sub-directory mirroring inside a module is optional.
- Infrastructure (bridges, bootstrap, shared support) that belongs to no module goes at the test root or beside its module.
- One type under test may have several case files (split by behavior cluster), but **one file holds exactly one public test class** (consistent with "one top-level type per file"); fixture bases and test-only types are exceptions — see "Fixtures and isolation".

## Naming conventions

| Element | Convention | Example |
|---|---|---|
| Test file / class | `<Subject>Tests` | `AudioClipCacheTests`, `SaveMigrationBusTests` |
| Fixture base | `XxxFixture` | `MemoryPoolFixture` |
| Shared support | `XxxTestSupport` / `XxxTestHost` | `AudioCacheTestSupport`, `AudioServiceTestHost` |
| Benchmark | `XxxBenchmark`, and it must be `[Explicit]` | `GenericObjectPoolBenchmark` |
| Case method | three-part `Scenario_Condition_Expectation` | `RetainRelease_CycleAllocatesZeroBytes`, `PauseGame_NestedSources_OnlyLastResumeRestoresSpeed` |
| Namespace | **short name** aligned with the module under test, no `Moirai` root | `Service.Audio`, `Core.MemoryPool`, `Utility` |

> Short namespaces are **deliberate** (the `CheckNamespace` rule was downgraded to SUGGESTION precisely for this). The test assembly itself declares global namespaces such as `Resource`/`ObjectPool`/`Utility` that collide with framework sub-namespaces; also `UnityEngine`'s `Audio`/`UI`/`Input` classes shadow framework sub-namespaces. **Referencing framework sub-namespace types in a test file requires a `using` alias** (`using Res = Moirai.Atropos.Resource;`); bare qualified names are forbidden (`Resource.Xxx` resolves to the global namespace or a `UnityEngine` type and fails with CS0246/CS0426).

### Three prohibitions on test-only types

1. **Do not create custom subclasses of `[Serializable]` framework base classes** — `LogHandler`, `JsonHandler`, `TweenHandler`, the various `XxxServiceHandler`, etc. are all used via `[SerializeReference]` fields, and Unity scans **all assemblies** for derived types to populate Inspector dropdowns. A fake implementation in tests pollutes the dropdowns of production assets. To capture logs, use a built-in implementation plus an event callback (`LogUtility.OnMessageLogged`).
2. **Test-only types are always `internal`**, and live only inside the test assembly.
3. **Logging inside `Test` / `Editor` / non-runtime scripts always uses `Debug.LogXX`**, never `LogUtility` (`LogUtility` is runtime infrastructure with category filtering and a Handler pipeline; tests do not need it, and it makes "does this log count as a test failure" uncontrollable).

## Fixtures and isolation

Pools, registries, singletons and static settings are all **process-global state**. One case that forgets to return an object makes the next case read an inflated count — presenting as "green alone, red in the suite". The fixture base's job is to pin that pollution **onto the offending case's own red**.

### Fixture base contract

```csharp
public abstract class XxxFixture
{
    [SetUp]
    public void SetUpFixture()
    {
        // 1. Snapshot every global knob that will be touched
        // 2. Reset the subject to a known state (clear contents, grant capacity, zero stats)
        // 3. Assert the initial state is clean (name the previous case in the message for attribution)
    }

    [TearDown]
    public void TearDownFixture()
    {
        // 1. Assert no residue (unreturned leases, unsubscribed events, undisposed handles)
        // 2. Clean up the subject
        // 3. Restore every global knob in finally (even if the above threw)
        // 4. Aggregate collected exceptions into an AggregateException — never swallow
    }
}
```

Key points:

- **`TearDown` must restore global knobs, and the restore must sit in `finally`.** Otherwise one failing case leaks pollution into every subsequent case.
- **Cleanup exceptions in `TearDown` are collected and aggregated**, so the first exception does not skip the remaining restores.
- **Assert the initial state is clean** (e.g. `Assert.AreEqual(0, Info<T>().UsingCount, ...)`). Cleaning without asserting turns the previous case's leak into an inexplicable failure in this one.

### EditMode vs PlayMode lifecycle differences (high-frequency trap)

| Behavior | EditMode | PlayMode |
|---|---|---|
| `Awake` / `OnDestroy` on non-`[ExecuteInEditMode]` components | **Not executed** (even via `AddComponent` on an active object) | Executed |
| `DontDestroyOnLoad` | **Throws `InvalidOperationException`** | Works |
| `Time.frameCount` | **Does not advance** | Advances |
| Coroutines | Need external frame driving | Driven by the engine |
| `UniTask.SwitchToMainThread` | Available (UniTask pumps PlayerLoop via `EditorApplication.update`) | Available |

Three hard constraints follow:

1. A pipeline that registers in `Awake` at runtime must provide an **explicit registration fallback** (an idempotent `EnsureActivated` pattern); never assume `Awake` ran in an EditMode case.
2. Any `DontDestroyOnLoad` call site must be guarded by `Application.isPlaying`, otherwise every EditMode case for that module goes red.
3. Logic needing "across frames" in EditMode must **carry its own frame cursor** (`Tick(++Frame)`) rather than relying on `Time.frameCount`.

### `[UnityTest]` and threads

- Something that must happen **on the main thread across frames** (managed allocation metering, Unity API timing, `ProfilerRecorder`) → **must use `[UnityTest]` + `IEnumerator`** with `yield return null` per frame. The continuation of an `async Task` runs on a **thread-pool thread** (Unity captures no `SynchronizationContext`); touching Unity APIs there is a main-thread violation and every conclusion drawn is void.
- After a facade method `await`s a Handler task that internally goes to the thread pool, and before touching Unity APIs, it must `if (!MainThreadDispatcher.IsMainThread) await UniTask.SwitchToMainThread(ct);`. When reviewing "facade awaits Handler then does work" code, **thread affinity is a mandatory check**.

### Reflection into fields is forbidden

`Runtime/AssemblyInfo.cs` already grants `InternalsVisibleTo` to `Moirai.Atropos.Editor` and the three test assemblies, therefore:

- **Widen the member you need from `private` to `internal`** instead of reflecting.
- Reflection turns field names into test dependencies: a rename produces no compile error, only a runtime `GetField` returning null followed by an NRE; `internal` is enforced by the compiler.
- Changing a serialized field to `internal` does not affect Unity serialization (`[SerializeField]` does not require `private`); prefixes still follow the `m_`/`s_`/`_` private-family convention.
- Do not open fields merely because a narrow seam exists: switch handlers via the generated `Internal_PeekHandler()` / `Internal_UseHandler(next)`; `s_Handler` stays `private`.

**Reflection allowlist** (only these three legitimate uses, and each must state its reason in the file header):

1. **Contract shape guards** — walking API shape and asserting member annotations (`ResourceSeamShapeGuardTests`, `ResourceMethodSetContractTests`, `YooAssetHandlerSmokeTests.RuntimeArrayFields_AreNonSerialized`). These **can only** reflect; do not delete them as violations.
2. **Invoking Unity lifecycle callbacks** — `Awake` / `OnEnable` / `OnInit`.
3. **Generated-field probes** — reading fields emitted by code generators (`MemoryPoolFixture.StaticField`).

> The allowlist is not a blanket pardon: a new reflection site must clearly fall into one of the three categories, otherwise widen to `internal`.

## Determinism discipline

**One flaky case is worse than ten unwritten cases** — it teaches people to ignore red.

- **No real wall-clock waits** (`Thread.Sleep`, `await Task.Delay` for timing assertions, `DateTime.Now` as a criterion). Time must be **injected**: timer cases carry their own frame cursor and `Advance(delta)`.
- **Randomness must be seeded.** `RandomSource` / `RandomUtility` cases fix the seed; never rely on the default.
- **No shared mutable statics across cases.** Shared read-only data uses `static readonly`.
- **No dependence on case execution order.** Green in the suite and green alone must both hold.
- **`Assert.ThrowsAsync<T>` fails on async lambdas** because `TaskCanceledException` does not match exactly — use `task.GetAwaiter().GetResult()` to obtain the original exception type.
- **Assert exceptions along the `InnerException` / `AggregateException` chain**, not with a hard `Assert.Throws<T>` match (the generic `new T()` actually goes through `Activator.CreateInstance<T>()`, and the original exception gets wrapped).
- **Assert value-type results via their fields**: when `TryLoadBlock` returns `SaveResult<T>`, write `Assert.AreEqual(SaveError.None, result.Error)`; comparing directly reports a fully-qualified type name instead of the enum value.

### Flaky case triage

1. **Re-run in isolation**: run the fixture / case alone. Green → cross-fixture interference or order dependence; fix per the above. Still red → a real defect or an environment dependency.
2. **Confirm attribution**: `git diff` to rule out parallel edits; confirm whether you introduced it.
3. **Never hide it behind `Assert.Ignore`.** Ignore is reserved for **environmental** reasons such as a failed capability probe (see "Zero-GC acceptance"), and the comment must state which capability was probed and under what conditions it recovers.
4. If it cannot be fixed and is confirmed pre-existing, record it (issue or a known-issues CHANGELOG entry) — **do not leave it red in the suite**.

## Log assertion policy

The framework's `LogUtility` is **pluggable**: the Handler active in the test domain follows `m_LogHandler` in `GameAppSettings.asset`. That directly determines how you assert.

| Handler | Visible to Unity Test Framework | Consequence |
|---|---|---|
| `DefaultLogHandler` | Yes | Must `LogAssert.Expect` |
| `ZLoggerHandler` | Yes (still goes through `Debug`) | Must `LogAssert.Expect` |
| `UnityLoggingHandler` | **No** (writes straight to `ConsoleWindow.AddMessage`) | Declaring `Expect` instead reports "Expected log did not appear" |

Therefore:

- **Assert content through the internal event** `LogUtility.OnMessageLogged` — it is Handler-independent and the only stable assertion channel.
- **`LogAssert.Expect` only serves to silence unhandled logs; always use the regex `".*"`** and never couple the regex to a Handler's rendering prefix (`[ERR]`/`[FAT]` three-character prefixes differ from the `[ERROR]`/`FATAL` in docs and cause bulk false reds).
- **Always silence unhandled logs through `UtfLogExpect.Error()` / `UtfLogExpect.Warning()`** (`Tests/EditorMode/Support/UtfLogExpect.cs`): the handler-visibility judgement lives there, so a case carries no `#if` and never names a handler type. Do **not** hand-roll `LogAssert.Expect` plus a handler check inside a case — that spreads "`UnityLoggingHandler` does not exist at all without com.unity.logging" into one `#if` per site.
  - The test assembly therefore keeps its `com.unity.logging` → `UNITY_LOGGING_INSTALLED` `versionDefines` entry: `UtfLogExpect` is the only place in the repository that needs that macro — do not rely on it anywhere else.

## Zero-GC acceptance (L3)

**Editor Mono has no usable managed allocation meter**: `GC.GetAllocatedBytesForCurrentThread()` returns a constant 0 (measured: a 64 MB allocation on the main thread, genuinely written to, still yields delta 0); `ProfilerRecorder(ProfilerCategory.Memory, "GC.Alloc")` likewise does not respond to known allocations. **Never treat "cannot measure allocation" as "no allocation".**

The correct shape for a zero-GC case is therefore:

```csharp
// Calibrate: confirm the bench can catch an allocation
// (MeasureManaged self-Ignores when the counter is unusable; if we get here it must catch it)
AllocationCapture.CalibrateKnownAllocation();

// Steady-state measurement: one warm-up iteration is discarded inside (JIT/pool growth land there),
// then `iterations` are counted
long bytes = AllocationCapture.MeasureManaged("cached-play-stop", 200,
    () => PlayCached(),
    b => Assert.AreEqual(0, b, "the hot path must not allocate managed memory"));
```

- When the counter is unusable, `MeasureManaged` calls **`Assert.Ignore`** (the `AllocationCapture` bench caches its own capability probe). **Never write "before/after delta" metering.**
- To genuinely verify zero-GC on this machine you **must** use an L3 player build.

### How player-side cases run (hard constraints)

`Moirai.Atropos.Tests.Player` has `defineConstraints: ["UNITY_INCLUDE_TESTS", "!UNITY_EDITOR"]` — it **does not compile in the editor at all**. Hence:

1. **The test bridge cannot find it.** `TestRequestRunner` lives in the editor, and the `assemblies` filter cannot match an uncompiled assembly.
2. **It can only be started from the Test Runner window's PlayMode tab → `Run all in Player`** (narrow the scope with the search box), and the player-side report is authoritative.
3. **Do not hand-build a test player with `BuildPipeline.BuildPlayer`.** The in-player test entry point is not the `-runTests` argument but a **bootstrap scene injected at build time** (`CreateBootstrapSceneTask` generates `Assets/InitTestScene<guid>.unity` hosting `PlaymodeTestsController`); a hand-built player lacks that scene and `-runTests` does nothing. The player also **does not write a result XML itself** — results travel back over PlayerConnection via `RemoteTestResultSender` and are written to disk by the editor.
4. **`Tests/Player/PlayerTestBootstrap.cs` is a required prerequisite**: the player auto-starts the framework by default (`GameApp.AutoBoot` defaults to true), and a test player runs an empty scene, so the UI backend never sees a `UIRootBinding` registration, the boot chain halts at `UGUIHandler`'s "UI root not yet bound" error, and the test run never gets a turn. The bootstrap sets `AutoBoot` to false in `AfterAssembliesLoaded`.

### IL2CPP player verification criteria

- **`_Data/Managed/` does not exist.** Using `File.Exists(.../Managed/X.dll)` to decide "did the assembly make it into the build" yields a **false negative**.
- Correct criteria: whether the assembly name appears in `_Data/ScriptingAssemblies.json`; or ISO-8859-1 decode `il2cpp_data/Metadata/global-metadata.dat` and search for the type name (UTF-8 identifiers — a hit means it was compiled in).
- Cost reference (StandaloneWindows64): cold build ≈ 22 minutes (3.0 GB Development build); incremental build after changing a single assembly ≈ 3.5 minutes. So "change, then verify again" is not expensive.

## Benchmark policy (L4)

- Always `[Explicit]`, **never run with the regular suite**. Benchmark numbers depend on machine load; mixing them into the regression suite only creates noise.
- Named `<Subject>Benchmark`, in the module directory.
- **Performance conclusions must come from a before/after A/B measurement with the same tool on the same data** (numbers from different tools are not comparable).
- Editor Mono benchmarks carry ±2× noise; **compare only within the same round**, never assert absolute values across rounds.
- Non-NUnit manual benchmarks (e.g. `TimerServiceBenchmark`, a MonoBehaviour driven from a menu) must be **explicitly marked as non-automated** so nobody mistakes them for part of the suite.
- The CI benchmark channel is documented in `Packages/GitHubActions~/README.BENCHMARK.md`.

## Maintaining contract guards

A "contract guard" is a case that pins the **current API shape** into baseline constants (e.g. `ResourceSeamShapeGuardTests` records the abstract member count, the `internal abstract` count and the `[Obsolete]` count as constants). Its value is making "a member quietly disappeared" and "a reference silently stopped resolving" visible in the diff.

**But it must be maintained, or it degrades into permanently red** — at which point it neither guards against regressions nor stops masking real defects. The 2026-09-24 baseline contains two such examples:

| Case | Symptom | Cause |
|---|---|---|
| `ResourceSeamShapeGuardTests.Seam_AbstractMemberCount_MatchesRecordedBaseline` | Expected 66, actual 67 | One abstract member was added; the baseline was not updated |
| `ResourceMethodSetContractTests.InitializePackageAsync_Signature` | Expected `UniTask<bool>`, actual `UniTask<ResourcePackageInitResult>` | The package-management API was intentionally renamed and retyped; the guard was not updated |

Maintenance discipline:

1. **When the API changes intentionally, update the baseline constant in the same commit** — do not defer it to "next time".
2. **Record the provenance of each number in the baseline comment** (`2026-09-24 baseline: 19 abstract properties + 47 abstract methods; …`) so the next reader can see where the numbers came from.
3. **State in `CHANGELOG.md` which members were removed.** The baseline constant is "the shape now"; the CHANGELOG is "why it became this".
4. When a guard goes red you **must** decide: intentional change (update the baseline) or accidental convergence (fix the code). **Editing the constant just to make it green is not allowed.**

## Coverage and gates

Tooling: the `com.unity.testtools.codecoverage` package (1.3.0). **The run recipes (editor window / batchmode CLI), filter scope and gate script live in [`Tests/Coverage/README.md`](../../Tests/Coverage/README.md)**; this section only covers the judgement rules.

On CI the same rules run from `.github/workflows/coverage.yaml`: one instrumented EditMode pass, then `Tests/Coverage/coverage-gate.ps1`. If the script cannot find the report, does not recognise the XML shape, or finds no class rows, it exits non-zero — **"cannot measure" is not "meets the bar"**. Test and PlayMode regression live in `.github/workflows/tests.yaml`; `.meta` integrity in `.github/workflows/metas.yaml` (all three fire on `pull_request` only).

> This repository is a UPM package with no `ProjectSettings/`, so Unity tests must run inside a **host project**; the workflows therefore check out the host repository (`HOST_REPOSITORY`) before running. Adjust that value for your setup and provide `HOST_REPO_TOKEN`.

### Configuration

- **assemblyFilters**: `+Moirai.Atropos`; `-Moirai.Atropos.Editor`, `-Moirai.Atropos.Tests.*`, `-*.Generated`, `-Moirai.Atropos.SourceGenerators.*`.
- Enable `GenerateAdditionalMetrics` (branch/complexity).
- Reports land in `Tests/Coverage/` (`baseline-<date>.md` records the baseline; `latest/` holds the current run and is not version-controlled).

### Tiered thresholds (set 2026-09-24)

| Tier | Scope | Line | Branch |
|---|---|---|---|
| **Core services** | Resource / Save / Audio / UI / Kernel | ≥ 80% | ≥ 70% |
| **Other services** | ConfigTable / Debugger / Input / Localization / ObjectPool / Procedure / Scene / Timer | ≥ 70% | — |
| **Editor tooling and generated code** | `Moirai.Atropos.Editor`, SourceGenerators | ≥ 50% | — |

Notes:

- Thresholds are **floors**, not goals. Core services should sit well above 80%.
- **Coverage is a tool for finding holes, not a quality metric.** 100% line coverage with nothing but `Assert.IsNotNull` is worth zero. Review cases for assertion strength, not percentages.
- New code must not **lower** the coverage of its module (CI compares against the baseline).
- The gap table (current baseline → target) is maintained alongside the baseline report.

## Release exit criteria

A release must be **green on all five gates** — none optional:

| Gate | Criterion |
|---|---|
| 1. Compile | 0 errors (and no new Roslyn Analyzer warnings) |
| 2. L1 regression | `Moirai.Atropos.Tests.EditorMode` full suite, 0 failures |
| 3. L2 regression | `Moirai.Atropos.Tests.PlayMode` full suite, 0 failures |
| 4. L3 acceptance | `Run all in Player` report, 0 failures (including the zero-GC group) |
| 5. Coverage | Every module at or above its tiered threshold, and not below the previous baseline |

**The baseline must be green.** The moment the full suite has red, "all green" stops being a signal, and the red must be fixed before development continues. If a pre-existing break is confirmed not to be introduced by the current work, record it with a fix plan — but it cannot stay red in the suite long-term.

## Run channels

### Channel 1: test bridge (preferred, EditMode / PlayMode)

`Tests/EditorMode/TestRequestRunner.cs` is a debug bridge inside the test assembly. It polls `Client/Temp/MoriaiTestRequest.json` over a one-way file protocol; the caller only polls:

```json
{"id":"<unique>","mode":"EditMode","output":"<absolute path>/report.txt","timeoutSeconds":180,
 "assemblies":["Moirai.Atropos.Tests.EditorMode"],"tests":["<namespace.class.method>"]}
```

Artifacts:

| File | Content |
|---|---|
| `report.txt` | `run <id> \| passed N \| failed N \| skipped N \| duration`, followed by per-case failure details |
| `report.txt.progress` | Full names of cases currently running (stall detection; deleted at completion) |
| `report.txt.done` | Contains the `id` from the request |

Discipline:

- **Always carry a unique `id` and only trust the matching `.done`**, otherwise you will read the previous round's report as this round's conclusion.
- **A request with both `assemblies` and `tests` empty is rejected outright** — an empty filter makes Test Runner re-run "whatever was last selected in the window", which looks like success but tests the wrong set.
- The prerequisite is that the assembly has compiled at least once and the editor has had an `update`. While compiling, importing, changing play mode, or while any run is active (including one started manually from the window) it accepts no new request — **the request file stays and is consumed automatically once idle**.
- `timeoutSeconds` is a wall-clock limit (compile, import and domain-reload waiting all count); on timeout the run is closed out as ABORTED and the report carries the `collected passed/failed/skipped` so far. **Cells already finished in an ABORTED report are not wasted** and can be used for attribution.
- **Cancelling an in-flight run**: write the request `id` to `Client/Temp/MoriaiTestRequest.cancel.json`. After a UTF cancellation `RunFinished` is never delivered; the driver closes out on acceptance.

### Channel 2: running `TestRunnerApi` directly (fallback when the bridge is unavailable)

Inside an editor script, use `ScriptableObject.CreateInstance<TestRunnerApi>()` plus an `ICallbacks` host. Key points:

- Make the callback host a `ScriptableObject`, and after completion call `UnregisterCallbacks(host)` + `DestroyImmediate(host)` to avoid global callback residue.
- `Filter` must set `testMode` explicitly, otherwise it defaults to the PlayMode flow, triggers a domain reload, aborts the calling script and returns empty results.
- Count results by **recursing manually** over `ITestResultAdaptor.Children` (the root node has no `TestCount`), and note that the failure state string is not guaranteed to be the official `"Failed"` — classify with a broad match on `Fail`/`Error`/`Cancel`/`Inconclusive`.

### Channel 3: editor state bridge (liveness and refresh)

`Tests/EditorMode/EditorStateBridge.cs` rewrites editor state to `Client/Temp/MoriaiEditorState.json` roughly every second. Uses:

- **Liveness**: a `now - unix` gap of several seconds means the main thread is not running `update` (importing, domain reloading, blocked by a native modal).
- **New domain**: an incrementing `domainSeq` proves a domain reload really happened.
- **Freshness**: compare each source tree against the `assemblies[].unix` it belongs to (`Runtime/**` → `Moirai.Atropos`, `Tests/EditorMode/**` → `.Tests.EditorMode`). **Also compare `assemblies[]` against `domainDllUnix`: inequality means "the dll was replaced by a background compile but this domain has not reloaded"**, in which case you are still running old code.
- **Before submitting a test request**: `testRunActive` must be 0 and `isCompiling`/`isUpdating`/`isChangingPlayMode` all false. This editor is shared — another session may occupy it at any moment.

### Channel 4: Test Runner window (the only channel for L3)

`Run all in Player`, see "How player-side cases run".

## Trap quick reference

| Symptom | Root cause | Action |
|---|---|---|
| Green alone, red in the suite | Cross-fixture global state pollution | Fixture base asserts a clean start + TearDown restores |
| EditMode case throws `DontDestroyOnLoad` | Not allowed in EditMode | Guard with `Application.isPlaying` |
| `Awake` did not run in an EditMode case | EditMode does not run lifecycle | Provide an idempotent `EnsureActivated` fallback |
| `Time.frameCount` does not advance | No frames in EditMode | Carry your own frame cursor |
| `LogAssert` reports "Expected log did not appear" | Active Handler is `UnityLoggingHandler` (invisible) | Declare via `UtfLogExpect` (the judgement is centralised — do not add your own) |
| Many cases suddenly fail with CS0246/CS0426 | Bare qualified names in tests colliding with the global namespace or `UnityEngine` types | Switch to `using` aliases |
| Zero-GC assertions "pass" while actually allocating | The editor meter is a constant 0 | Capability probe + `Assert.Ignore`; real verification is L3 |
| `Assert.ThrowsAsync<T>` type mismatch | Exact `TaskCanceledException` type | `task.GetAwaiter().GetResult()` |
| Cases NRE after a field rename | Cases read fields by reflection | Widen to `internal`, drop reflection |
| Cases go red while you changed nothing | A parallel session changed the API | `git diff` to rule out parallel edits before attributing |
| An unfamiliar Handler appears in an Inspector dropdown | A test created a subclass of a `[SerializeReference]` base | Delete that type; use a built-in implementation plus an event callback |

---

[« Back to Documentation Index](Index.md) · [Core](Core.md) · [Debugger](Debugger.md)
