# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 资源

- 新增可序列化资源弱引用 `AssetReference` / `AssetReference<TObject>`（对齐 Addressables 的 AssetReferenceT 与 YooAsset 官方 AssetReference 扩展）：序列化面只有 `m_GUID` 与 `m_PackageName` 两个字符串，检视器经 GUID 引用资源（新 `AssetReferenceDrawer`：对象字段选中即写回 GUID 并显示打包归属提示；包名行走下拉——选项经编辑器桥 `ResourcePackageBridge` 实时取自后端，YooAsset 读收集器设置且首项为默认资源包，当前值不在清单中时置顶，无清单退回自由文本，Addressables 单隐式目录不消费包名、不绘制包名行），运行时经后端清单按 GUID 解析定位地址后按需异步加载，避免序列化强引用带来的内存占用；`LoadAssetAsync` 自持租约（已加载幂等回放、在途并发并入、失败/取消回 `null` 可重试），`ReleaseAsset` 释放。
- 新增 `ResourceService.TryGetLocationFromGuid(guid, out location, packageName)`（服务未就绪时编辑器回退 `AssetDatabase`）与后端接缝 `ResourceServiceHandler.TryGetLocationByGuid`（abstract，每个后端必须显式实现；YooAsset 经清单 `GetAssetInfoByGuid` 映射，Addressable 经目录 GUID key 定位、解析结果即 GUID 本身）：YooAsset 收集器设置须勾选 IncludeAssetGUID 并重新生成清单后才生效。包名清单的编辑器口径由 `ResourcePackageBridge.GetPackageNames(handler)` 提供（传 Handler 返回包名数组：null 为不消费包名、空数组为清单未就绪；仅编辑器可用，不落 Runtime 接缝）。
- 新增取用族 `ResourceService.TryLoadAsset<T>(location, out asset, packageName)` 与 `TryLoadAssetAsync<T>(location, cancellationToken, packageName)`：内部取租约、读出对象后立即归还，归还时按 `IdleAssetExpireTime`（默认 60 秒）保活，不把租约交给调用方。
- 取用族在服务未初始化时（编辑器非播放态）直读 `AssetDatabase`，不建记录也不取租约：可序列化资源引用类与编辑器预览因此共用同一个入口，不再各写 `#if UNITY_EDITOR` 分支；异步形以 `null` 表失败（异步方法不能带 `out` 参数）。
- 迁移口径：取到的对象只在保活窗口内稳定，需要长期持有或跨长周期保存引用的场景改用 Lease API 自持租约，长期显示的用法应定期回读本族。

#### JSON

- 集合形态补齐往返：`HashSet<T>`/`SortedSet<T>`/`ISet<T>`/`Queue<T>`/`Stack<T>`/`LinkedList<T>` 与 `IList<T>`/`ICollection<T>`/`IEnumerable<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>` 按枚举序写成 JSON 数组、读侧按序回填（`Stack<T>` 逆序压栈以保持栈顶与写出前一致，`ISet<T>` 落到 `HashSet<T>`，集合接口落到 `List<T>`）；读侧原先只认 `List<T>`/`Dictionary<K,V>`，接口形态会抛 "Cannot parse a JSON array into"。

#### 存档

- 序列化后端改为声明式登记：实现类标 `[RegisterSerializer]`（空标记，不带编号），SaveServiceCodegen 生成器静态取出实现 `BackendId` 的常量值后发 `SaveSerializerRegistry.Register(<编号>, typeof(Xxx))`，写进各自程序集的模块初始化器，引导代码为零。框架内置四个实现与项目实现同形登记；形状非法 / 编号非常量 / 编号落在框架保留区 0-255 / 同编译单元撞号分别报 MIRAI309/310/311/312 且一律 Error（注册跑在模块初始化期，抛出等于把编辑器整崩）。跨程序集撞号生成器不可见，运行期由 `Register(ushort, Type)` 记一次 Fatal 并保留先到那份——声明式登记路径刻意不抛：注册跑在模块初始化期，抛出实测会让 Unity 在源生成脚本扫描阶段原生崩溃。

#### UI

- 新增 UI Toolkit 轨：`UITKWindow`（`UIDocument` 壳与内容根装配、窗口级 `PanelSettings` 覆盖——缺省回共享那一份）与 `UITKHandler` 驱动者；开窗族多出 `where T : UITKWindow` 的同名腿（比 uGUI 腿多一枚 `panelSettings` 形参，同名重载按窗口基类约束分辨），寻址两档（AB / 内置资源）与 uGUI 轨同形同序。
- 新增多后端并存能力：各轨窗口并进同一条共享窗口栈（`UIWindowLedger`），关·隐·查询、层级深度、模态遮挡与交互租约不分轨；`UITrack` 轨道自述（认窗判据、有效性探针、Type 形分派、关停档位）由各轨 partial 静态自登记进门面目录，主入口只枚举目录——加一支后端＝三件套（窗口基类 / 驱动者 / partial）＋自登记＋启用清单加一项，`UIService.cs` 零改动。
- 新增窗口自关延后策略（`UIWindow` 上 `DeferCloseUntilInteractable` / `CanClose` / `TryClose` / `OnCloseFail`，默认立即结算、按需覆写为延后到可交互再过门）：策略在后端无关对象模型上，两轨窗口同形覆写；等待经既有代次与销毁守卫，被重开/销毁接管的续体静默终止、已销毁的窗不空转轮询。

#### 日志

- `LogHandler` 新增自述能力 `ErrorWithExceptionUsesExceptionChannel`（基类默认 `false`，ZLogger 旁路为 `true`）：带异常的 Error 条目在 `UnityEngine.Debug` 通道上究竟落 `LogType.Error` 还是 `LogType.Exception` 由处理器决定，测试装配拿不到后端的 `*_INSTALLED` 宏，级别只能按此判定，写死任一级别换处理器就假红。
- 新增 `LogUtility.IsEnabled(ELogLevel)`（internal）：全部日志入口在字符串格式化前先经它按 `MinimumLevel` 短路，T4 模板与生成文件同步该结构。

#### 本地化

- 新增 `GetTextFromId(string id)` 与 `GetTextFromIdLanguage(string id, Language language)` 无参重载（`LocalizationService` 门面与处理器同形）：零参查询不再固定绑定 `params object[]` 签名、在调用点构造参数数组，语义与原零参分支一致。

### Changed

#### 场景

- ⚠ 删除场景加载的 `gcCollect` 参数。如果需要，在合适时机自行调 `ResourceService.ForceUnloadUnusedAssets(true)`。
#### 日志

- 全部 `LogUtility` 入口在字符串格式化前按 `LogHandler.MinimumLevel` 前置短路：被过滤的日志此前仍会完成格式化并分配结果字符串，现在直接返回；输出与 `OnMessageLogged` 事件契约不变（事件本就只在通过过滤后触发），被过滤的调用不再产生 GC 分配。

#### 资源

- ⚠ `BackgroundMusic` 收敛为仅直接引用 `AudioClip`（`m_AudioClip`），移除「直接强引用 / AudioClipInfo 路径引用」双轨；`AudioClipInfo` 弃用（仅为旧资产反序列化保留，其 Drawer 删除）。直接引用的存量数据按原字段名自动保留，原路径引用（`m_SoundClip`）的存量场景需在 Inspector 重新指定音频；需要弱引用加载的场景改用 `AssetReference<TObject>`。

#### JSON

- 成员是否入档改为允许列表（`JsonTypeSupport.IsSupportedMemberType`）：此前除黑名单类型外一律反射兜底，接口/抽象成员被写成不带类型名的对象、`StringBuilder`/`Type`/`Tuple` 被写成私有内部结构、`UnityEvent<T>` 被写成 `m_PersistentCalls`，读回时构造不出实例却静默成档；现在这些形态连同委托、多维数组、BCL 具体类型一起不入选，写侧不出现在 JSON、读侧按未知字段忽略，两侧对称。
- ⚠ 泛型 `UnityEvent<T>`、`ISet`/集合接口以外的接口与抽象成员、`Type`/`IntPtr`/`MarshalByRefObject` 派生成员从"写出歪数据"变为不入选；类型自身无可序列化成员仍抛 `GameException`，`[SerializeField]`/`[JsonSerialize]` 均不覆盖该判据。

#### 存档

- ⚠ 删 `ESaveBackend` 枚举，块后端标识改为 `ushort` 常量表 `SaveBackendIds`（`JSON=0`/`MESSAGE_PACK=1`/`MEMORY_PACK=2`/`PROTOBUF=3`/`KEY_VALUE=254` 数值逐一沿用）：契约成员是 `ISaveSerializer.BackendId`，`[SaveData(Backend = SaveBackendIds.MESSAGE_PACK)]`，注册表按 ID 建表——项目自定义后端直接登记自己的 ID（0..255 框架保留，从 1000 起分配），不再借道 `(ESaveBackend)999` 这类魔法数。存档线格式零变化：块头那 2 字节的偏移与数值都未动，容器与文件头版本保持 2，存量存档原样读回。
- `SaveServiceSettings` 的默认序列化后端按类型名配置（`m_DefaultSerializerTypeName`，ProviderDropdown 类型名模式，与 `UIGeneratorSettings` 同款）：首次读取时解析成实例并按名缓存，并确保该 ID 在 `SaveSerializerRegistry` 里有主，写读落进同一实现；无状态的内置序列化器不再为引用序列化而带 `[Serializable]`。设置资产没配这一项时按内置 Json 走，已在 Inspector 配过默认后端的需在改版后重选一次。
- 注册表把"0..255 框架保留"从文档约定变成门禁：`SaveSerializerRegistry.Register` 拒绝保留区内任何非内建标识（此前只挡 `KEY_VALUE`），内建扩号不会再与项目已注册的后端静默相撞；类型名解析不到实现时按内置 JSON 回退，且同一份错配置只报一次 Fatal（此前每存一块都重报一次并新建实例）；构建期由 `SaveSettingsBuildValidator` 与占位密钥一并报出，开严同样挡包。
- `SaveSerializerRegistry` 不再硬编码内置后端：注册按 ID→类型挂账，首次查询到该后端才实例化那一个（实现均无状态）；新增 `Register(ushort, Type)` 与 `Register<T>()` 两个登记入口，`Unregister(ushort)` 连 ID→类型记录一起摘除（只删实例会被下一次查询复活），换后端为「先 `Unregister` 再 `Register`」；重号判据同查两张表，同标识实现不再静默盖掉已登记那份。
- 存档源生成器工程 `SaveHost` 更名 `SaveServiceCodegen`（文件夹 / csproj / 入库 dll 三名同步，命名空间不变），并按功能拆成四个生成器类：`SaveFieldCapturerGenerator`（`[SaveField]` 捕获器）、`SaveMigratorRegistrationGenerator`（迁移器自注册）、`SaveSerializerRegistrationGenerator`（`[RegisterSerializer]` 后端自注册）、`SaveModuleInitializerShimGenerator`（`ModuleInitializerAttribute` 缺失副本的唯一归属方），三者各占一个模块初始化器类；诊断描述符同时从 `SaveFieldModel.cs` 独立成 `Diagnostics.cs`（Category 由 `SaveHost` 改 `Save`）。生成的捕获器与注册内容不变。
- 存档模式分析器 `SaveSchemaAnalyzer`（MIRAI400/401）从 `ServiceDependency.dll` 归到 `SaveServiceCodegen.dll`：诊断 ID、判据与快照格式不变，只是 Save 的判据与 Save 的生成器同装配；生成器内部另把 `SaveFieldModel.cs` 按类型拆出 `SaveValueClassifier.cs` 与 `DiagnosticInfo.cs`。

#### UI

- ⚠ UI 服务拆为后端无关窗口模型 + 各轨窗口基类：面板实现自 `UIWindow` 下沉至 `UGUIWindow`（uGUI 轨）；开窗腿泛型约束由 `where T : UIWindow` 改为按轨收在 `UGUIWindow` / `UITKWindow` 上，`Type` 形入口按目录认轨分派、认不出轨当场报错。迁移：业务窗口类从 `: UIWindow` 改继承 `UGUIWindow`（UI Toolkit 界面继承 `UITKWindow`）；开窗族的面板地址形参 `assetName` 更名 `assetLocation`。
- ⚠ 后端启用改由 `UIServiceSettings.EnabledHandlers` 清单驱动（`[SerializeReference]` 托管引用数组，可同时列多支；清单为空初始化当场报错；原 `[ProviderDropdown]` 单选后端退役）。迁移：UI 设置资产须在启用清单显式列出 `UGUIHandler`，需要并存的再列 `UITKHandler`。
- ⚠ 启用清单类型改为 `UIServiceHandler[]` 且序列化字段更名 `m_enabledHandlers` → `m_EnabledHandlers`，并带代码默认值（uGUI 一支）。迁移：存量设置资产的旧键名脱钩后清单回落到代码默认（仅启用 uGUI 一支），已在 Inspector 配过的工程需在新字段下重新列出要启用的后端。
- 主入口不再持有任何后端单点：`UIService.Handler` 懒加载属性随 `[HandlerHost]` 一并退役，取用一律走静态门面；未启用那一轨的开窗按轨道名当场报错并点名去哪一处启用，轨专有查询（`UIRoot` / `UICamera`）未启用时答 `null` 不抬错。
- 关停次序由各轨自报档位表述：持有别轨面板挂靠宿主根的 uGUI 轨取 `UITrack.SHUTDOWN_ORDER_HOST` 最后收，其余取默认档先收；模态动画期间的全局交互压制改由共享租约 `UIInteractionLease` 按归属仲裁，被重开/销毁接管的旧动画续体不再解锁也不再隐藏。

#### 文档

- README 双语重写为标准仓库结构（特性/安装/快速开始/架构/文档索引/编辑器工具/贡献），架构树按重构后的 Core 三分（Foundation/Infrastructure/Utilities）与 Pooling 更名改口径，启动链归属改指模板目录，失效的编辑器工具条目（JSON Benchmark）剔除并补 PlayerLoop Debugger 等实存菜单；README 里的订阅/派发异常分级与测试约定两节内迁到新增的 CONTRIBUTING.md，异常分级声明点更新为五处 RETHROW 常量（补 TaskRunner / MemoryPoolRegistry）。
- 双语文档按代码全量核校，修正与实现漂移的陈述：UI 快速上手与核心类型改按轨继承（业务窗口继承 `UGUIWindow`，`UIWindow` 是后端无关对象模型，绑定代码生成器同口径）；调试器启动参数更正为 `-show-debugger`；存档浏览器菜单更正为 `Tools/Moirai/Save/Save Browser`；Resource 绑定枚举更名 `EResourceBindingOption`、类型表行更正 `ResourceBindingTypes`；Scene 处理器空配置的口径改为回退 `DefaultSceneHandler`；移除两处不存在的 `YooAsset/Editor PlayMode` 菜单引用；MemoryPool 阶段切换点更正（Boot 由 `MemoryPoolSetting.OnInit` 设定，模板启动链只在 `ProcedurePrepare4Entrance` 切 Gameplay）；ObjectPool 目录树更正 `Object/` 子目录；Testing 测试桥路径更正为工程相对 `Temp/` 并刷新分布快照；Audio 更正 `BackgroundMusic` 直接引用口径与迁移说明；CodeComments 补齐尾部导航。

### Removed

#### 资源

- ⚠ 移除 `ResourceService.LoadAssetForEditor` 与后端接缝 `ResourceServiceHandler.LoadAssetForEditor`（`virtual` 的地址换算钩子，移除时零覆写），非播放态取资产改用 `TryLoadAsset<Object>`；接入按文件名或包清单寻址的后端时，换算需并进取用族的编辑分支本身。

### Fixed

#### 工具

- `PathUtility.FormatToSysFilePath(null)` 从抛 `NullReferenceException` 改为返回 `null`，与 `FormatToUnityPath(null)` 的既有行为对称（UNC 格式化漏了同款 null 守卫）。
