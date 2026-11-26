# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

架构评审落地：发布保障与故障可观测性一批。

### Fixed

- `#### Save`
  - ⚠ 出包前占位存档密钥与默认序列化后端配置检查改为发布构建（非 Development 包）默认开严——配置瑕疵直接挡包（破坏性行为变更：CI 出包未显式设置 `MOIRAI_SAVE_SETTINGS_STRICT=0` 时，占位密钥或后端配错将从警告升级为构建失败；升级后首次出包前请先替换存档密钥或在 CI 显式关严）。开发构建保持只告警；环境变量为显式覆写通道：1 强制开严、0 强制关严。
- `#### Asset`
  - ⚠ 出包前资源设置检查同口径开严（`MOIRAI_RESOURCE_SETTINGS_STRICT` 为显式覆写通道；坏配置从警告升级为构建失败）。
- `#### Audio`
  - ⚠ 出包前音频后端能力分叉检查同口径开严（`MOIRAI_AUDIO_SETTINGS_STRICT` 为显式覆写通道）。
- `#### Kernel`
  - 组合根初始化失败即回滚：先关停服务世界（未激活桶兜底关闭）再复位启动面，然后才广播 `onBootFailed`——半初始化世界不再带着 PlayerLoop 继续轮询。
  - 服务 `OnInit` 抛异常时立即摘出轮询列表再上抛，未完成初始化的服务不再被逐帧驱动；世界销毁时未激活桶兜底关闭语义保持不变。
  - 服务轮询异常日志限流：同一服务同一轮询类别仅首错 Fatal 带栈记录，后续失败静默计数，熔断摘除仍汇总告警一次——连续故障不再逐帧刷屏。
  - 熔断外显：服务诊断信息新增 `TrippedFromPoll` 标记，服务内核调试窗口的服务行以 `T` 标志展示——「在位但停转」对排障者可见。
  - 服务关停路径的调试器解析从 `GetRequiredService` 改为 `GetService`（可选）：世界销毁兜底对未激活服务驱动关停时，调试服务可能已先被拆走，不再抛解析异常。
- `#### 文档`
  - 修正 Install.md 的 .NET 版本要求（.NET 4.x → .NET Standard 2.1）、README 目录名漂移（Resource/Pooling → Asset/ObjectPool）、源生成器 README 的 MA2001/MA2002 严重级别标注，以及规范层四处悬空路径指针。

### Added

- `#### Kernel`
  - 服务诊断信息与拦截器接入熔断状态（见 Fixed）。
- `#### Foundation`
  - `LogUtility.onFatalLogged` 公共遥测接缝：Fatal 级日志通过过滤后触发，供宿主接崩溃/异常上报；订阅方异常就地隔离不影响日志管线。
- `#### Asset`
  - 新增 `SpriteAssetIndex` 图文混排引用（TMP 图集弱引用 + 精灵下标）与检视绘制器：弱引用一行复刻 `AssetReferenceDrawer` 口径（对象字段、Package 下拉、GUID 悬空报错），下标滑动条按图集精灵数钳制（图集未配置退回整数字段），64px 精灵预览紧贴字段左侧。
  - `AssetReferenceDrawer` 兜底加固：Unity 属性路径解析落到非树目标（Odin 分组/深层路径包装对象）时回退默认绘制，不再把选值写入包装对象。

### CI

  - PR 门禁新增 convention-guards 作业：层界棘轮与门面接缝两条可执行守卫进 CI（config-surface 守卫的 B/C/D 段量具在无前端装配的环境取不到声明集，暂不上 CI，本地跑继续承担）。
