# Moirai Framework Plugins

This is the `plugin` branch of `com.moirai.framework`. It shares no history with `master` or `installer` and serves only as a plugin catalog consumed by the **Package Manager** window inside the framework.

[中文](README.md)

## Layout

```
com.moirai.framework.plugin/
├── package.json              # the one manifest Unity reads, maintained by the Package Manager
├── README.md / README_EN.md
└── <id>/                     # one top-level folder per plugin, flat
    ├── <id>.plugin.json      # plugin manifest, parsed by the Package Manager only
    ├── *.asmdef              # required, see below
    └── ...
```

Top-level folders are named by `<id>` and stay flat — there is no function layer. Grouping by function lives in the manifest's `function` field and drives the window's menu tree.

## Rules for plugin authors

1. **Ship your own `.asmdef`.** Unity requires scripts inside a package to be associated with an assembly definition; scripts without one silently stop compiling — no error, just "the feature doesn't exist". The Package Manager only validates and refuses to install; it never generates one for you.
2. **Keep assembly names globally unique.** `Moirai.Plugin.<Id>` is the convention. Duplicate assembly names are invisible until an assembly references both (usually the test suite), which then fails with CS0433.
3. **Do not put a `package.json` inside a plugin folder.** Unity reads only the branch-root one. The plugin manifest filename is fixed: `<id>.plugin.json`.
4. **Commit `.meta` files next to their assets.** GUIDs come from `.meta`; without them Unity regenerates GUIDs on another machine and serialized references break.
5. **`minUnityVersion` is actually enforced** (install is refused when unmet), and **`versionOperator` is actually parsed** — `>=`, `>`, `=`, `<=`, `<`.

## Manifest fields

| Field | Meaning | Constraint |
|---|---|---|
| `id` | Plugin identifier, also the folder name | `[a-z0-9._-]+`, unique across the branch |
| `name` | Display name | — |
| `function` | Group label in the window's menu tree | Classification only, never part of a path |
| `version` | Plugin version | Three-segment semantic version |
| `minUnityVersion` | Minimum Unity version | Compared against the running editor |
| `tags` | Scripting define symbols | Added on install, removed on uninstall |
| `dependencies` | Other plugin `id`s in this branch | Transitively resolved, installed in topological order; a missing one aborts the batch |
| `upmDependencies` | External UPM packages | Written to the **project's** `Packages/manifest.json`, never to the branch-root `package.json` |

## Install and uninstall on disk

- Install fetches only the selected `<id>/` folders into the working tree; unselected plugins are neither checked out nor downloaded.
- Installed state and UPM dependency ownership are recorded in `ProjectSettings/MoiraiPackageManager.json`.
- Entries the user added to `manifest.json` by hand are never overwritten or removed. Shared dependencies are reference counted and reclaimed only when the last user uninstalls and the entry was written by the manager.
- Uninstalling a plugin that other installed plugins depend on asks first.
