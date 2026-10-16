---
name: unity-yaml-editing
description: Last-resort guidance for safely hand-editing Unity YAML
---

## Triggers
- Hand-editing YAML when no live Editor can reach the object
- Fixing m_Script GUIDs or broken fileID references
- Fixing .meta files or merge conflicts
- 无法经活动编辑器触达时手编 YAML、修复 m_Script GUID 或断裂 fileID 引用、修复 .meta 文件或合并冲突

# YAML Editing - Safe Hand-Edit Rules

Advisory module. This is operational guidance for directly editing Unity serialized YAML text when the live Editor and its tooling cannot do the job. Hand-editing YAML is the **last resort**, not a shortcut.

> **Mode**: Documentation only — it gates no other skill; load it freely whenever the subject matches.

## When To Load

**Prefer a live Editor first.** For normal object/asset work, drive the running Editor — it keeps serialization valid for you:

- A running Editor over MCP tools → `.claude/skills/unity-mcp/SKILL.md`
- A running Editor over the CLI (`unity status` / `unity command`) → `.claude/skills/unity-cli/SKILL.md`
- A project script run inside the Editor (`run_script` / `eval`) → the same skill

**Only hand-edit YAML when none of them can reach**, i.e. one of:

1. **Compile failure** — a script fails to compile, so the Editor cannot deserialize the component. No Editor API can touch it; the only level left is the YAML text.
2. **.meta files** — no Editor API or command owns `.meta`; GUID and importer fields live only in text.
3. **Hidden ProjectSettings fields** — many `ProjectSettings/*.asset` fields have no public API or tool surface.
4. **Merge conflicts** — Editor tooling runs sequentially and cannot perform a 3-way merge of `.unity` / `.prefab`.

If none of these apply, stop and use the live Editor. Do **not** hand-edit YAML for convenience.

## Core Concepts

- **Object block / anchor**: every serialized object is a block headed by `--- !u!<classID> &<fileID>`. `<classID>` is the Unity type id (e.g. `1` = GameObject, `4` = Transform, `114` = MonoBehaviour). `<fileID>` is the object's anchor, unique **within this file**.
- **GUID vs fileID**:
  - **GUID** = asset-level identity, a 32-char hex string, stored in the asset's `.meta`. Identifies the whole asset (a script, prefab, texture, ...).
  - **fileID** = sub-object identity inside a file (a specific component, the specific MonoBehaviour script class, ...).
- **Serialized reference**: a cross-object/asset reference is `{fileID: N, guid: H, type: T}`. `guid` is omitted for same-file references (`{fileID: N}`).
- **`m_Script`**: a MonoBehaviour points at its script via `m_Script: {fileID: 11500000, guid: <scriptGUID>, type: 3}`. `fileID: 11500000` is the conventional MonoScript fileID; `guid` is the `.cs` file's GUID from its `.meta`; `type: 3` means the reference targets an asset on disk. A wrong `guid`/`fileID` here is exactly what produces a "missing script / missing component".

## Coverage Scenarios

### 1. Reference / fileID repair

**When**: a script was renamed or moved, so a prefab/scene `m_Script` `guid`/`fileID` no longer resolves; or a compile failure left a component as Missing and you must locate, delete, or re-point it at the YAML level (the Editor cannot deserialize a broken component, so its own APIs are unavailable).

**How**: read the script's current GUID from its `.cs.meta`. In the prefab/scene, find the offending `m_Script: {fileID: 11500000, guid: ..., type: 3}` and set `guid` to the correct value (keep `fileID: 11500000`, `type: 3`). To delete a Missing component, remove its whole `--- !u!114 &<fileID>` block **and** the matching `{component: {fileID: <fileID>}}` entry under the owner GameObject's `m_Component` list — leaving one without the other corrupts the file.

**Danger**: deleting the object block but not its `m_Component` reference (or vice versa) breaks the GameObject; changing a `fileID` that other objects reference orphans those references.

### 2. .meta / GUID safety

**When**: a `.meta` GUID looks like a pseudo-random GUID and risks third-party collision, or you must edit an importer field that no Editor API exposes.

**How**: treat a GUID as suspect when it shows any of five shapes — consecutive hex runs, interleaved increment, repeated characters, the literal `abcdef`, or a length other than 32. Regenerate a suspect GUID as uuid4, never by hand and never by typing another pattern: `python -c "import uuid; print(uuid.uuid4().hex)"`.

**Danger**: changing a GUID without updating every reference. **Before changing any GUID, grep the whole project** for that GUID (`.asset` / `.prefab` / `.unity` / `.meta`) and update every reference point in the same change; grep the same way to audit GUID health before and after.

### 3. ProjectSettings patch

**When**: you need a `ProjectSettings/*.asset` field (YAML) that no Editor API exposes — e.g. a specific physics layer / collision matrix entry, or a hidden `QualitySettings` value.

**How**: git-back up (or confirm a clean tree) first, locate the exact field by key, change only its value, then let Unity reload and re-serialize to validate. Confirm the change round-trips (Unity rewriting the file the same way) rather than reverting or reformatting it.

**Danger**: editing collision-matrix or enum-backed fields by guessing the encoding can silently break settings; always verify Unity accepts the value on reload.

### 4. Merge conflict assistance

**When**: a `.unity` / `.prefab` has Git conflict markers and you must resolve them.

**How**: Unity YAML is line-mergeable because each object is an independent `--- !u!<classID> &<fileID>` block. Resolve per-block: for a block changed on only one side, take that side; for the same block changed on both sides, do a true 3-way merge of the conflicting fields. After resolving, ensure every `fileID`/`guid` referenced still has a defining block.

**Danger**: blindly accepting one side (`--theirs`/`--ours`) drops objects added on the other side or leaves dangling references. Never resolve by deleting whole conflict regions without checking the `fileID` references inside them.

## Hard Rules

- **Back up first**: confirm a clean git tree (or commit/stash) before any YAML edit, so you can diff and revert.
- **Don't touch structure unless repairing references**: change values only; do not renumber or reorder `anchor`/`fileID` unless the explicit task is reference repair.
- **Re-import to validate**: after editing, trigger a Unity reimport/reload and confirm no new errors and the file round-trips.
- **GUIDs are uuid4, never hand-written**: `python -c "import uuid; print(uuid.uuid4().hex)"`.
- **Dry-run before bulk edits**: grep the impact set first; know exactly how many references a change touches before making it.
- **Change a GUID → change every reference**: a GUID edit is not done until all referencing files are updated in the same change.

## Routing

- If the live Editor can do it, drive the Editor — `.claude/skills/unity-mcp/SKILL.md`, `.claude/skills/unity-cli/SKILL.md` — and do not hand-edit.
- GUID health audit / pseudo-GUID detection / uuid4 repair → the `.meta / GUID safety` scenario above.
- After editing skill docs or for doc/code consistency → run `.claude/skills/moirai-conventions/scripts/check_config_surface.py`（指针形态与符号真名判据）.
