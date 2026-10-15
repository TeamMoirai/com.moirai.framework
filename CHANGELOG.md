# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 资源

- SpriteReference 精灵引用门面（自玩法层上移为框架类型）：数据为 `AssetReference<Sprite>` 弱引用 + 打包方式判别（Common 常规散图/合图、Config 经 `ConfigTableService.LoadSpriteByID` 取配置表精灵）+ 子图名/配置表 ID 各占一字段；`SetSprite(Image/SpriteRenderer)` 异步取像并赋给目标（普通与配置表精灵走保活窗口，合图子精灵按名经绑定服务挂目标元素），`Clone`/`Matches`/`DataSave`/`DataLoad` 存档块自带版本号；检视面为属性驱动（引用行拖入实捕子图名、包名行由 `[ResourcePackageDropdown]` 接管），编辑器面 `LoadEditorSubSprite` 按名直取子精灵
- SpriteReferenceTests：存档块往返与线格式（与玩法侧 IOExtensions 字节对齐）、版本守卫、等值比较与克隆独立契约用例
- 资源内核诊断面（仅编辑器）：`Profiler/Resource` 新增 LEASE PROVENANCE 段——开关打开后为每条新租约记一次取用来源（类型全名.方法名 + 源文件 + 行号，跳过资源内核自身帧），并留一窗有界卸载账（地址/类型/UTC 时刻，满 64 条覆盖最旧并计入 Dropped）；开关默认关闭，关掉时既不建调用栈也不建字符串

#### 核心

- `[ProviderHandlerHost]` 新增默认处理器声明（第二构造参数）：`ProviderHandlerHostGenerator` 据此生成 `CreateDefaultHandler` 兜底工厂，19 个宿主的手写兜底工厂移除；非法声明（未继承契约/抽象/无可访问无参构造）编译期 MIRAI103 报错，声明默认与手写工厂并存提示 MIRAI104

#### 编辑器

- 编辑器窗口异步数据装载管线 `EditorLoadPipeline`：即时步、主线程分帧切片步（编辑器 update 每拍预算毫秒内连吃条目）、后台任务步顺序执行，装载中可在主线程步内继续登记后续步、每步带完成回调，取消与进度计数齐备；起跑经编辑器 update 自摘除延一拍（不走 `delayCall`——它不随 EditMode 测试的模拟泵派发）
- `FrameworkMenuWindowBase` 装载接入面：`StartDataLoad`/`BuildDataLoadSteps` 登记装载步骤，`IsDataLoading`/`IsDataFailed`/`IsDataReady`（虚属性，派生窗可按热缓存扩展）供呈现装载态；冷装载（无可展示数据）默认绘制整窗遮罩（居中装载进度 + 进度条）并禁用输入，数据在屏的重扫不遮罩不锁交互；装载完成经 `ShouldRebuildTreeAfterDataLoad` 裁决单次重建树再置就绪（在屏数据与新结果一致时可跳过重建免无谓抖动；遮罩与树同拍切换不露空树帧）、`OnDisable` 取消在途装载，不接入异步装载的派生窗零成本
- EditorLoadPipelineTests：步序与动态登记、切片跨拍分摊与计数、后台线程与主线程续体、后台/切片取消观察、失败面用例

### Changed

#### 核心

- ⚠ 移除默认后端的安装探测自动优选：`LogUtility`/`StringUtility`/`TweenUtility`/`ObjectUtility` 的 `#if` 优选链删除，settings 未配置时一律回退声明默认（DefaultLogHandler/DefaultStringHandler/DefaultTweenHandler/UnityObjectHandler），新建 settings 资产的播种值同步变为声明默认；迁移口径 = 已安装 ZLogger/UnityLogging/ZString/LitMotion/PrimeTween/Fusion 的工程在 `GameAppSettings` 的 Provider 下拉框显式选择后端（已配置工程无感；`InputService` 保留引擎宏分派手写工厂不受影响）

#### 资源

- 真机下 `ResourceServiceSettings.PlayMode` 把 `EditorSimulate` 归一为 `OfflinePlay` 时不再打 Error：静默降级、不回写资产原值也不落一行日志，远程热更随之整体关闭；发行前该字段必须是 `HostPlay`/`WebPlay`（口径已写进 zh/en Resource.md）

#### 音频

- 播放链消除 `AudioPlayOptions` 的整份防御性拷贝：全部属性 getter 标 `readonly`，经 `in` 参数读属性不再隐式复制约 190 字节结构体（修复前单次 Play 全链 17-18 处拷贝），序列化形状与公共 API 不变。
- 并发检查与按 clip 遍历改经声部托管镜像（`PlayingClip`）预过滤：声部全表扫描不再逐个原生读 `AudioSource.clip`（命中才读 `isPlaying`，按 clip 遍历零原生读）；`AudioPlayOptionsSO` 的 `MaximumConcurrentInstances` 检查默认即走此路径。
- ⚠ 移除 4 个传统巨型 `Play` 签名（`AudioService`×2 门面与 `AudioServiceHandler`×2 virtual，各 41 参）及其翻译点 `BuildOptions`：常用形态由新入口 `Play(clip, EAudioTrack, Vector3)` 与 `AudioPlayOptions` 工厂（`Create`/`CreateLooping`/`CreateWithFade`）承接，全量定制走 `Play(clip, in AudioPlayOptions)` / `Play(clip, in AudioPlayRequest, cold)`；外部自定义后端若覆写过 virtual 巨兽，改为覆写 Options/Request 抽象成员。
- ⚠ `Play(path, in AudioPlayOptions, bAsync, bInPool)` 移除 `bInPool` 形参：留池语义统一由 `AudioPlayOptions.CachePolicy` 表达（旧 `bInPool: true` 等价 `CachePolicy = Ttl`）。
