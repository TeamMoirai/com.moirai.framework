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

## Current plugin inventory

Everything except `zlogger` was extracted out of `com.moirai.framework`: type names and namespaces are unchanged,
and the framework keeps only its default/built-in backends. Install a plugin, then re-pick the backend in the matching settings Provider dropdown.

| Group | Plugin id | Assembly | Macro | UPM dependency |
|---|---|---|---|---|
| String | `newtonsoft-json` | `Moirai.Plugin.NewtonsoftJson` | `NEWTONSOFT_JSON_INSTALLED` (auto) | com.unity.nuget.newtonsoft-json 3.2.2 |
| String | `zstring` | `Moirai.Plugin.ZString` | `ZSTRING_INSTALLED` (auto) | com.cysharp.zstring 2.6.0 |
| Save | `memorypack` | `Moirai.Plugin.MemoryPack` | `hand-authored (see description)` | — |
| Save | `messagepack` | `Moirai.Plugin.MessagePack` | `hand-authored (see description)` | — |
| Save | `protobuf` | `Moirai.Plugin.Protobuf` | `hand-authored (see description)` | — |
| Save | `unity-cloud-save` | `Moirai.Plugin.UnityCloudSave` | `UNITY_CLOUD_SAVE_INSTALLED` (auto) | — |
| Object | `photon-fusion` | `Moirai.Plugin.PhotonFusion` | `hand-authored (see description)` | — |
| Logging | `serilog` | `Moirai.Plugin.Serilog` | `hand-authored (see description)` | — |
| Logging | `unity-logging` | `Moirai.Plugin.UnityLogging` | `UNITY_LOGGING_INSTALLED` (auto) | com.unity.logging 1.3.10 |
| Logging | `zlogger` | `Moirai.Plugin.ZLogger` | `ZLOGGER_INSTALLED` (auto) | com.cysharp.zlogger 2.5.10 |
| Tween | `litmotion` | `Moirai.Plugin.LitMotion` | `LITMOTION_INSTALLED` (auto) | com.annulusgames.lit-motion 1.0.0、com.cysharp.unitask 2.5.11 |
| Tween | `primetween` | `Moirai.Plugin.PrimeTween` | `PRIMETWEEN_INSTALLED` (auto) | com.kyrylokuzyk.primetween 1.4.11 |
| Resource | `addressables` | `Moirai.Plugin.Addressables` | `ADDRESSABLES_INSTALLED` (auto) | com.unity.addressables 2.10.3 |
| Audio | `fmod` | `Moirai.Plugin.Fmod` | `hand-authored (see description)` | — |
| Audio | `wwise` | `Moirai.Plugin.Wwise` | `hand-authored (see description)` | — |

The rows marked hand-authored take their dependency from NuGetForUnity or a vendor SDK (outside UPM);
each manifest's `description` spells out the prerequisite steps. With the macro undefined the backend is preprocessed away and never appears in the dropdown.
The two audio plugins reference only the framework assembly: after installing the FMOD / Wwise integration you must add its assemblies
(e.g. `FMODUnity`, `Ak.*`) to `references` — their GUIDs are not resolvable from this catalog, so nothing was guessed.

## Rules for plugin authors

1. **Ship your own `.asmdef`.** Unity requires scripts inside a package to be associated with an assembly definition; scripts without one silently stop compiling — no error, just "the feature doesn't exist". The Package Manager only validates and refuses to install; it never generates one for you.
2. **Keep assembly names globally unique.** `Moirai.Plugin.<Id>` is the convention. Duplicate assembly names are invisible until an assembly references both (usually the test suite), which then fails with CS0433.
3. **Do not put a `package.json` inside a plugin folder.** Unity reads only the branch-root one. The plugin manifest filename is fixed: `<id>.plugin.json`.
4. **Commit `.meta` files next to their assets.** GUIDs come from `.meta`; without them Unity regenerates GUIDs on another machine and serialized references break.
5. **`minUnityVersion` and `minFrameworkVersion` are actually enforced** (either unmet floor refuses the install), and **`versionOperator` is actually parsed** — `>=`, `>`, `=`, `<=`, `<`.
6. **Need a NuGet package? Write the prerequisite into `description`.** `upmDependencies` only covers UPM packages (written into the project's `manifest.json`); dependencies that come from NuGet — `ZLogger.dll`, for example — require the user to install NuGetForUnity and that package first. Put that step in `description`; the detail pane's description box shows it verbatim.

## Manifest fields

| Field | Meaning | Constraint |
|---|---|---|
| `id` | Plugin identifier, also the folder name | `[a-z0-9._-]+`, unique across the branch |
| `name` | Display name | — |
| `function` | Group label in the window's menu tree | Classification only, never part of a path |
| `description` | Blurb — **put install prerequisites here too** | Shown verbatim (word-wrapped) in the detail pane |
| `author` | Author | — |
| `version` | Plugin version | Three-segment semantic version |
| `minUnityVersion` | Minimum Unity version | Compared against the running editor |
| `minFrameworkVersion` | Minimum framework version | Compared against the installed `com.moirai.framework` package version; a hard requirement exactly like `minUnityVersion` |
| `tags` | Scripting define symbols | Added on install, removed on uninstall |
| `dependencies` | Other plugin `id`s in this branch | Transitively resolved, installed in topological order; a missing one aborts the batch |
| `upmDependencies` | External UPM packages | Written to the **project's** `Packages/manifest.json`, never to the branch-root `package.json` |

## Install and uninstall on disk

- Install fetches only the selected `<id>/` folders into the working tree; unselected plugins are neither checked out nor downloaded.
- Installed state and UPM dependency ownership are recorded in `ProjectSettings/MoiraiPackageManager.json`.
- Entries the user added to `manifest.json` by hand are never overwritten or removed. Shared dependencies are reference counted and reclaimed only when the last user uninstalls and the entry was written by the manager.
- Uninstalling a plugin that other installed plugins depend on asks first.
