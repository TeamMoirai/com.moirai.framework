# Package Manager

> Treats a configurable git branch as a package source for browsing and installing framework plugin packages: unchecked packages are neither downloaded nor materialized, and external UPM dependencies are written into the project `manifest.json` under an ownership ledger.

Menu entry: `Tools/Moirai/Package Manager`. The window is an Odin menu tree — leaves are grouped by the manifest's `function`, and the detail pane is the selected entry itself. The toolbar offers `Refresh` (scan the branch), `Update All (n)` (one pass for everything updatable) and `Settings` (jump to the inline source settings).

## Source and landing directory

| Item | Value |
|---|---|
| Remote and branch | edited in the window's `分支包源` node, default branch `plugin` |
| Landing directory | `Packages/com.moirai.framework.plugin/` |
| State ledger | `ProjectSettings/MoiraiPackageManager.json` |
| git executable | `git` by default, driven out of process |

The landing directory *is* the branch worktree. The tool clones with `--filter=blob:none --no-checkout --single-branch` and then applies cone sparse-checkout, so **folders of unchecked plugins never appear on disk**, while `git ls-tree` still lists the whole tree — browsing reads the tree and file contents are fetched on demand.

## Plugin manifest

Each plugin lives in `<id>/`, and its manifest file is always `<id>.plugin.json`. Only this tool reads it; Unity reads the branch root's `package.json`, which the tool validates read-only and never writes.

| Field | Meaning |
|---|---|
| `id` | lowercase slug, charset `[a-z0-9._-]`, not starting with a dot; must equal both the folder name and the manifest file stem |
| `name` | display name |
| `function` | menu group |
| `description` / `author` | blurb and author |
| `version` | three-part decimal version |
| `minUnityVersion` | minimum engine version |
| `tags` | scripting define symbols added on install and removed on uninstall |
| `dependencies` | inter-plugin requirements: `id` + `version` + `versionOperator` (`>=` `>` `=` `<=` `<`) |
| `upmDependencies` | external UPM packages: `id` + `version` |

## Install, uninstall, update

`Install` follows this sequence: clone → fetch → attach branch → list tree → build catalog → plan → abort the whole batch on any conflict → apply the cone selection → validate each `.asmdef` (it must exist, and its assembly name must not collide with any `.asmdef` in the project) → write external dependencies into the project `manifest.json` → add scripting define symbols → **persist the ledger first** → a single `AssetDatabase.Refresh()`.

Planning is a pure function. Any of these conflicts aborts the batch: missing dependency, dependency cycle, unsatisfied version requirement, engine floor not met. Multiple packages must be installed in one pass — a per-package pass would trigger a domain reload after each one, and nothing would be left running for the next.

`Uninstall` relies entirely on the ledger rather than inferring state from the worktree: external dependencies are reclaimed by owner count (a line is deleted only when its owners are empty *and* the tool wrote it; an entry the user had already written stays verbatim even with no owners left), define symbols are removed only when no other installed plugin claims them, and the cone selection shrinks to the remaining installed ids. When installed plugins still depend on the target, the window asks first, with three real branches: cascade, abort, uninstall-only.

`Update available` compares two tree hashes for `<id>/<id>.plugin.json`: one at the `commit` recorded at install time, one at `refs/remotes/origin/<branch>`.

## Known limits

- Cone mode always materializes loose files at the branch root (such as the root `package.json`); that is its defined behaviour.
- After an uninstall or an empty selection, the plugin folder may remain as an empty directory: git removes files but not directories, and Unity clears the `.meta` alongside.
- A local `file://` source (handy for offline self-test) does not negotiate filtering — git reports `filtering not recognized by server` and fetches every object. "Fewer bytes" only holds against a real remote; offline the verifiable judgments are "unchecked folders are not materialized" and "the filter is recorded in `remote.origin.partialclonefilter`".
- The first action surface is single install, single uninstall and `Update All`; there is no multi-select batch queue.

## Verification

Everything decidable about install/uninstall lives in L1 (EditMode): the version value type and operators, manifest validation, line-level edits of the project `manifest.json`, the state ledger and ownership counting, dependency closure and topology, tree-path-to-catalog discovery, and the git layer (clone / attach / sparse materialization) against a temporary `file://` fixture.

```bash
# reproduces the current reading: 83 test names / 128 cells passed / 0 failed
bash "$LOCALAPPDATA/pm-names.sh" Tests/EditorMode/Editor/Tools/PackageManager/*Tests.cs > names.txt
bash "$LOCALAPPDATA/pm-gate.sh" pm refresh
bash "$LOCALAPPDATA/pm-test.sh" pm names.txt
```
