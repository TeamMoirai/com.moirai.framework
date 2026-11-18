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

### Removed

#### Pooling

- ⚠ 删除通用对象池服务 `ObjectPoolService`/`IObjectPool<T>`/`ObjectBase` 及其调试窗口与设置资产：全仓无生产消费面，C# 对象复用走 `MemoryPool`、GameObject 复用走 `GameObjectPoolService`，无需迁移（外部引用者请改用上述两套）

#### Kernel

- `Runtime/Services/Kernel` 独立为根级 `Runtime/Kernel`：物理目录迁移，命名空间与公共 API 不变；Core 依赖棘轮豁免目录名册同步改判据（`ContainerKernelFileRoster` 已留账）

### Changed

#### Timer

- ⚠ 删除 `TimerServiceHandler` 抽象基类：计时器后端改为直接实现唯一契约 `ITimerService`（`DefaultTimerHandler` 签名变为 `FrameworkHandler, ITimerService`，原 `internal override` 全部转 public）；自定义后端迁移口径 = 直接实现 `ITimerService` + 继承 `FrameworkHandler` 生命周期基座，`TimerServiceSettings.TimerServiceHandler`、`Internal_UseHandler` 等签名同步改按接口类型

#### Editor

- `[ProviderHandlerHost]` 源生成器支持接口契约：默认实现校验放宽为「实现处理器契约（继承类或实现接口）+ 继承 FrameworkHandler 生命周期基座」，生成代码的生命周期调用按基座强转；类契约宿主（Log/GameTime/Json 等 Utility 族）行为不变

#### Audio

- ⚠ 删除 `AudioServiceHandler` 抽象基类：音频后端改为直接实现唯一契约 `IAudioService`（`UnityAudioHandler`/`MiddlewareAudioHandler` 签名变为 `FrameworkHandler, IAudioService`）；原基类共享语义（总线 Fade 六方法、`SetVoicePitch`、音轨暂停标记越界守卫、场景切换停音判据）收进 internal `AudioBackendPolicy`（this + 自持调度表作参数的组合替代继承）；`AudioServiceSettings` 字段、`AudioCategory.Handler`、`AudioAgent._audioHandler` 等签名改按接口类型。自定义后端迁移口径 = 直接实现 `IAudioService` + 继承 `FrameworkHandler`；过渡调度表（internal `AudioFadeScheduler`）仍留在各后端自持

### Fixed

#### PackageManager

- 勾选「安装/更新前先清目录」时，安装目标位上的外来同名目录视同已批准覆盖、随清目录一并先删再装；原实现仍走覆盖确认回调，回调缺席即整批中止，勾了清目录也装不下去。未勾清目录的拒绝路径行为不变

#### Editor

- `ProviderCatalogIndexTests` 归行判据夹具的 providerBase 默认值改取 `typeof(FakeBase).FullName` 运行时全名；原硬编码字符串随测试命名空间改名而静默失配，清单行整片铺不出来（3 个用例红）

#### Editor

- API Compatibility Level 切换为 .NET Standard 2.1：`IAudioService` 的可选能力/共享语义默认实现与 `ITimerService.GetLeftFrames/WaitAsync` 回收为接口默认方法（DIM），`UnityAudioHandler`/`MiddlewareAudioHandler`/`DefaultTimerHandler` 的显式补齐与纯默认转发行删除——DIM 仅在 .NET Standard 2.1 profile 下生效（.NET Framework 4.x profile 不支持）

#### ConfigTable / Debugger / Scene

- ⚠ 删除 `DebuggerServiceHandler`/`ConfigTableServiceHandler`/`SceneServiceHandler` 抽象基类：三服务后端改为直接实现唯一契约 `IDebuggerService`/`IConfigTableService`/`ISceneService`（`DefaultXxxHandler` 签名变为 `FrameworkHandler, I{Service}`）；`ConfigTableServiceSettings.InjectConfigTableHandler<T>` 泛型约束改 `where T : class, IConfigTableService, new()`（DIM 需 .NET Standard 2.1 API profile）。可选能力（按语言取列）走接口默认方法，自定义后端不覆写即回默认

#### Input / Procedure / GameObjectPool

- ⚠ 删除 `InputServiceHandler`/`ProcedureServiceHandler`/`GameObjectPoolServiceHandler` 抽象基类：三服务后端改为直接实现唯一契约 `IInputService`/`IProcedureService`/`IGameObjectPoolService`（3 个输入后端 UIMobile/UnityInputManager/UnityInputSystem 与 DefaultProcedureHandler/DefaultGameObjectPoolHandler 签名变为 `FrameworkHandler, I{Service}`）；`ProcedureServiceHandler` 的切换历史/广播承载收进 internal `ProcedureBackendPolicy`（后端自持状态 + 一行转发）；`GameObjectPoolService.TryResolveInstance` 因参数类型 internal 移入 internal `IGameObjectPoolInternals`；下游消费方（CharacterActions/PlayerActions）签名同步接口化。`UIServiceHandler` 保留——它是 uGUI/UITK 双轨并存的家族基座（多实例托管，非单选策略契约）

#### Asset（原 Resource）

- ⚠ 服务整体由 `Resource` 更名为 `Asset`：命名空间 `Moirai.Atropos.Resource` → `Moirai.Atropos.Asset`，类型族（`ResourceService`/`IResourceService`/`ResourceServiceHandler`/`ResourceServiceSettings`/`ResourceOwner`/`ResourceKey`/`ResourceAssetLease` 等）改按 `Asset*` 命名（复合名并入既有资产类型族：`ResourceAssetInfo`→`AssetInfo`、`ResourceAssetLease`→`AssetLease`、`EResourceAssetKind`→`EAssetKind`），服务方法（`ProcessResourceMaintenance`→`ProcessAssetMaintenance`、`RetainResourceKey`→`RetainAssetKey` 等）与目录 `Services/Resource`→`Services/Asset`、文档 `Resource.md`→`Asset.md`、设置资产 `ResourceServiceSettings.asset`→`AssetServiceSettings.asset`（GUID 不变、托管引用同步）一并迁移；`ResourceServiceSettings.asset`/测试字符串全名已同步，已配置自定义后端实例无损。迁移动机：`Resource` 只描述「资源」而未贴合服务本体（YooAsset 资产加载 + Lease/Binding 生命周期的资产服务），`Asset` 与类型族 `AssetReference`/`AssetSlot`/YooAsset 对齐。Unity `Resources` API、`Resources/` 特殊目录与 YooAsset `package.CreateResourceDownloader` 等外部 API 不在迁移范围，保持原名；下游需同步替换 using 与类型引用
- 新增唯一契约接口 `IAssetService`（60 个公共契约成员收编，语义文档与门面同源）；`AssetServiceHandler` 抽象基座保留并实现接口（abstract 成员满足契约义务，72 成员实现链零改动）——Asset 采用「契约接口 + 引擎基座」混合形态，addressables 等外部插件兼容性无损；internal `IAssetLeaseSource` 租约窄缝提取独立文件，internal `IAssetLeaseDiagnostics` 诊断附属接口（`GetLeaseSourceInfos` 显式实现，internal 参数类型不进公共契约）；`AssetServiceSettings` 字段、`IPrefabLoader`/`OperationsWindow`/`AssetBindingExtension`/编辑器包名桥与探针、消费方测试签名全部接口化

#### Localization

- ⚠ 命名空间 `Moirai.Atropos.Localization` 迁移为 `Moirai.Atropos.Localization`（类型与目录不变；`LocalizationServiceSettings.asset` 的托管引用 ns 已同步迁移，已配置的自定义后端实例无损；下游 using 需同步替换）
- 新增唯一消费契约 `ILocalizationService`（门面公共 API 收编，语义文档同源）：引擎基座 `LocalizationServiceHandler` 保留（1500+ 行共享实现豁免契约化），后端经派生基座落地；internal 协作面（偏好解析/编辑器预览）不进公共契约，框架内部按引擎基座引用访问；`Settings` 字段与消费方测试签名按契约类型持有

#### Save

- 新增唯一消费契约 `ISaveService`（28 个门面公共 API 收编，语义文档同源）：引擎基座 `SaveServiceHandler` 保留（2600+ 行共享实现豁免契约化），后端经派生基座落地；internal 协作面（存储后端选择/原始块读写/截图写入）不进公共契约，框架内部按引擎基座引用访问；`Settings` 字段、调试窗口与消费方测试签名按契约类型持有
