# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 测试与门禁

- 发版自动化 `build-release.yaml`：把 CHANGELOG 的 `[Unreleased]` 段切成 Release notes 并 Publish，随后自动开一个清空该段的 PR。

#### 存档

- 出厂占位密钥有了门禁：`StaticSaveKeyProvider.UsesPlaceholderCredentials` 判生效口令/盐是否为 `CHANGE_ME_*` 或空，Inspector 据此标红，构建期由 `SaveSettingsBuildValidator` 用同一判据再报一次——默认只告警，设 `MOIRAI_SAVE_SETTINGS_STRICT=1` 转为拦停。运行期不拦（已有存档可能正是占位密钥写的）。

### Fixed

#### UI

- 错误日志的启用判据方向反了：原写法在「不启用错误日志」时才注册 `ErrorLogger`，于是发布包（默认 `OnlyOpenWhenDevelopment` 且非开发构建）每次异常弹出 `LogUI`，编辑器与开发包反而静默。现按同一判据单向成立，双语 `UI.md` 里把反写成约定的那条说明一并改掉。

#### 调试器

- 内置窗口改随激活注册：`OnInit` 不再无条件构造 27 个窗口，未激活的构建（`AlwaysClose`，或非开发构建下的 `OnlyOpenWhenDevelopment`）注册表为空；运行期从关切到开时补齐一次，重复开启不重注册。
