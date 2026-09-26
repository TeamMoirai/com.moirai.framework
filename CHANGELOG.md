# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 测试与门禁

- 发版自动化 `build-release.yaml`：把 CHANGELOG 的 `[Unreleased]` 段切成 Release notes 并 Publish，随后自动开一个清空该段的 PR。

#### 存档

- 出厂占位密钥有了门禁：`SaveKeyProvider.UsesPlaceholderCredentials` 统一判生效材料是否为 `CHANGE_ME_*` 或空，三个内置提供方各自覆写（Static 看口令+盐、HKDF 看主密钥、Passphrase 看盐文），Inspector 据此标红，构建期由 `SaveSettingsBuildValidator` 用同一判据再报一次——默认只告警，设 `MOIRAI_SAVE_SETTINGS_STRICT=1` 转为拦停。运行期不拦（已有存档可能正是占位密钥写的）。

### Changed

#### 音频

- ⚠ 公开枚举 AudioPlayFlags 更名为 EAudioPlayFlags（对齐 E 前缀命名规范）；**迁移**：全局替换类型名，成员名不变。
- ⚠ AudioPlayOptions 的 18 个空间整形字段（声像/3D 混合/旁通×3/混响/多普勒/扩散/衰减模式/距离×2/四组自定义曲线）整体拆入新公开类型 AudioSpatialOptions，经 `options.Spatial.X` / `cold.Spatial.X` 读写；AudioPlayColdParams 的 17 个平铺空间字段同构收敛为单字段 `Spatial`。**迁移**：`options.SpatialBlend = v` 改为 `options.Spatial.SpatialBlend = v`（成员式赋值，`Spatial` 为公共字段）；`cold.X` 同理；Location/AttachToTransform/Priority 留在播放选项不动。
- 整景切换（Single）才自动 StopAllButPersistent，Additive（叠加/流式分区）加载永不自动停音——需要收口的游戏流程在自己的切换点显式调用；跨场景音频仍设 Persistent = true。
- BgmPlaylist 显式分层 ID 撞车或填负数改 fail-fast（报 Error 且本实例不播放，不再静默改派自动 ID）；自动分配迁到负区间（-1 起递减），与显式正数 ID 值域分离，0 仍是「自动分配」。
- AudioPlayOptionsSO 首次 Play 也做并发上限检查（旧写法只在本 SO 播过之后才查，首播会无条件放行）。

### Fixed

#### 音频

- AudioPlayOptionsSO 的 PlaybackTime / PlaybackDuration（含随机区间）随每次 Play 真正透传进播放请求（修复前恒为默认 0，起播位置与自定义时长都不生效）。
- AudioPlayOptionsSO 的 MaximumConcurrentInstances / DoNotPlayIfClipAlreadyPlaying 改按「本次候选 clip」判定：随机曲集下旧写法看上一曲或本 SO 上次句柄，会误拦新曲或放过已在播的候选；播放失败（句柄 0）不再清掉上次成功句柄。
- OnShutdown 无条件复位 AudioListener.pause：外部渠道（测试宿主、编辑器脚本、第三方）置位的暂停不再寄生到下一场景或编辑器会话。
- AudioPlayOptions 的 Create / CreateLooping / CreateWithFade 此前不初始化任何空间字段，产出多普勒 0、最小/最大距离 0、混响 0 的零值声学（靠默认 2D 掩盖）；现统一以 AudioSpatialOptions.Default（对齐 Unity AudioSource 声学缺省）为空间起点。

#### UI

- 错误日志的启用判据方向反了：原写法在「不启用错误日志」时才注册 `ErrorLogger`，于是发布包（默认 `OnlyOpenWhenDevelopment` 且非开发构建）每次异常弹出 `LogUI`，编辑器与开发包反而静默。现按同一判据单向成立，双语 `UI.md` 里把反写成约定的那条说明一并改掉。
- ⚠ UI 根不再按物体名字查找：改由场景物体上的 `UIRootBinding` 登记（`SingletonMono` 先到先得，后到者整物体销毁；取用一律走 `TryGetInstance()`——不自动创建，场景里没有根就是没有），后端缺绑定或缺 Canvas 都只报一条问题并每帧续等，晚到的场景/实例化根/事后补上的 Canvas 都补得上。**迁移**：给原本那个名为 `UIRoot` 的物体加挂 `UIRootBinding` 即可，其上的 Canvas 等配置不动；原先写 `UIRootBinding.Current` 的地方改为 `UIRootBinding.TryGetInstance()`。

#### 调试器

- 内置窗口改随激活注册：`OnInit` 不再无条件构造 27 个窗口，未激活的构建（`AlwaysClose`，或非开发构建下的 `OnlyOpenWhenDevelopment`）注册表为空；运行期从关切到开时补齐一次，重复开启不重注册。

#### Tasks

- `SequenceTask.Tick` 不再在空队列上取空引用：`TryPeek` 失败即按完成收口。
- `SequenceTask.Reset` 回收时把仍挂在队列里的子任务逐一 `Dispose`：旧写法只 `Clear()`，子任务欠 `Append` 的那次引用一起丢掉、从此回不了池，`DelayTask` 还连带漏掉 Timer 句柄取消（回调回头会把 Completed 写在已复用的实例上）。
- `PooledTaskBase.Dispose` 挡住引用下穿：0 持有时多还一次当场报错，而不是把计数拖成负数让这只任务永远凑不齐归还；`GetPooled` 的「取来即 0 持有，先 `Acquire` 再 `Dispose`」契约写进注释。
- `TaskRunner` 的每帧轮询补异常隔离：抛出任务先置停、再按房内 `RETHROW_*` 分级处理（开发期上抛、发布期续跑），同帧其余任务不再被它带走。
