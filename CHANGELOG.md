# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### UI

- Widget 绑定纳入 UI 生成器产线：widget 根节点语法 `{WidgetClassName}@{InstanceTag}`（类名取 @ 前段，多实例共享一个类与一份 `<Class>.g.cs`；window 收集器在 widget 根停递归，只拿到 widget-typed 字段 `m_{Class}{Instance}`），生成与多实例回绑流程见包根 `.agent/commands/new-ui.md` 的「Widget 控件（可复用组件）」

#### Editor

- `UIBindDataCollector.CollectWidget` 对 `@` 分隔的 widget 根节点改按「@ 前段作类名 + 全名后段作实例段」派生字段名（`m_{Class}{Instance}`），无 `@` 的单实例命名不变；`UIIdentifierFormatter.GetClassName` 同口径取 @ 前段

- 抽出 `PackageManagerWindowBase` 及其嵌套 `PackageManagerProfile`：菜单树、详情面、版本历史与装卸编排收进基类，派生包只需给出窗口标题、状态文件、检出落点、源缺省、宿主包类型与界面文案，即可在同一工程里管理另一条分支上的可安装包；`PackageManagerWindow` 改为框架插件源的首个实现，状态账本与宿主版本量取目标按包源注入

### Changed

#### UI

- ⚠ 删除按名/按路径查找 API：`UIBase.FindChild` / `UIBase.FindChildComponent<T>`（含 `Transform` 重载）与 `GameObjectExtensions.FindChildByName` 不再存在——窗口与 Widget 的节点引用一律走 `UIBindComponent` binder 生成面（框架内置 `LogUI` 已迁序列化 binder `LogUIBinder`）；迁移口径见 `.agent/commands/new-ui.md`

#### Audio

- 新增 `AudioBackendCapabilities`（由后端自报的 `SupportsDelayedStart` / `SupportsPlaybackTrim` / `SupportsSoloControl`，缺省为不支持）与构建期校验器 `AudioBackendCapabilityBuildValidator`：播放选项资产用到当前音频后端不消费的能力时逐条报出资产与字段，设 `MOIRAI_AUDIO_SETTINGS_STRICT=1` 改为挡包；运行期热路径不新增任何判定
- ⚠ `AudioPlayOptions` 的 `SoloSingleTrack` / `SoloAllTracks` 合并为 `EAudioSoloMode SoloMode`（`None` / `SingleTrack` / `AllTracks`），`AudioPlayOptionsSO` 与 `BackgroundMusic` 上的同名序列化字段一并合一；旧资产键不再读取，原 `SoloAllTracks` 为真的配置需重设为 `AllTracks`

#### Core

- `ProviderHandlerHost` 的处理器改为初始化成功后才发布：并发读不再取到半初始化实例，`OnInit` 抛错不再把污染实例永久留在缓存里（下次访问重试），同宿主初始化期递归取用改为明确报错而非栈溢出
- 后端切换赋值时，同一实例重复赋值幂等；新实例初始化失败则保留并继续服务旧实例，成功替换才关停旧实例一次

#### Editor

- `ProviderDropdown` 集合字段（数组 / `List<T>`，元素为 `[SerializeReference]`）由特性抽屉整体接管：字段行与全部子元素包在一层分组 box 里标示从属关系（同 Odin 分组盒），字段行整行点击收展、展开箭头紧贴字段标签左侧（同 Unity / Odin foldout 样式），右侧 `+` 弹同一类型弹窗新增元素，子元素以浅黑底色的 box 包裹（同 Odin 元素盒观感，与字段背景区分），行尾 `✕` 竖条纵贯元素盒高度且图标纵向居中（同 Odin 列表删除条）；非 managed reference 元素与只读集合维持 Odin 原生列表 UI

#### Input

- `InputServiceHandler.SetUIModal` 由 `internal abstract` 改为对派生方开放，非友元程序集的自定义输入后端现可按 `protected` 覆写实现（此前无法完成该契约）
