# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Changed

#### Audio

- 新增 `AudioBackendCapabilities`（由后端自报的 `SupportsDelayedStart` / `SupportsPlaybackTrim` / `SupportsSoloControl`，缺省为不支持）与构建期校验器 `AudioBackendCapabilityBuildValidator`：播放选项资产用到当前音频后端不消费的能力时逐条报出资产与字段，设 `MOIRAI_AUDIO_SETTINGS_STRICT=1` 改为挡包；运行期热路径不新增任何判定
- ⚠ `AudioPlayOptions` 的 `SoloSingleTrack` / `SoloAllTracks` 合并为 `EAudioSoloMode SoloMode`（`None` / `SingleTrack` / `AllTracks`），`AudioPlayOptionsSO` 与 `BackgroundMusic` 上的同名序列化字段一并合一；旧资产键不再读取，原 `SoloAllTracks` 为真的配置需重设为 `AllTracks`

#### Core

- `ProviderHandlerHost` 的处理器改为初始化成功后才发布：并发读不再取到半初始化实例，`OnInit` 抛错不再把污染实例永久留在缓存里（下次访问重试），同宿主初始化期递归取用改为明确报错而非栈溢出
- 后端切换赋值时，同一实例重复赋值幂等；新实例初始化失败则保留并继续服务旧实例，成功替换才关停旧实例一次

#### Input

- `InputServiceHandler.SetUIModal` 由 `internal abstract` 改为对派生方开放，非友元程序集的自定义输入后端现可按 `protected` 覆写实现（此前无法完成该契约）
