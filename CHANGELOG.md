# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Changed

#### 工具链

- ⚠ 分析器诊断编号统一为 MOIRAI 前缀（原 MIRAI1xx–7xx 全部改名），`#pragma warning disable` 与快照注释需同步替换
#### 编辑器

- 包管理器扫分支改为一次探缺、一次取包、一条进程读齐清单：浏览只取分支根的 `package.json` 与各插件的 `<id>.plugin.json`，刚建好的检出不再重复 fetch

### Fixed

#### 框架设置

- 设置资产只补缺、不再覆盖：资产库导入途中 `FindAssets` 漏掉已落盘文件时改读盘上那份（仍不可用则跳过创建、每路径只告警一次），此前会 `CreateAsset` 顶掉用户配置、重生成 meta GUID，并经 `projectChanged` 同步递归把编辑器钉死在导入阶段
- `LoadSettingSO` 加重入保护：`CreateAsset` 期间被 `projectChanged` 回调重入时直接返回 `null`，由上层按「暂不可用」降级，不再叠加创建

#### 本地化

- `InvalidateEditorPreview` 重入时只丢预览缓存、不再回写设置；`LocalizationServiceSettings.EditorLanguage` 与 `LocalizationServiceHandler` 在设置暂取不到时分别降级为「未选」与 `null`，不再 NRE
