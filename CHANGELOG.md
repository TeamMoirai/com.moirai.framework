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
- 新增窗口自关策略（`UIWindow` 上 `TryClose` / `CanClose` / `OnCloseFail` / `ForceClose`）：自关先等可交互再过 `CanClose` 门，门为假落 `OnCloseFail` 且窗口留在栈上；`ForceClose` 是跳过等待与门的即时旁路，外部经 `CloseUI` 的关闭不经过这一道。策略在后端无关对象模型上，两轨窗口同形覆写；等待经既有代次与销毁守卫，被重开/销毁接管的续体静默终止、已销毁的窗不空转轮询。
- 新增开窗结果契约 `UIOpenResult` / `EUIOpenStatus`（Opened / Failed / Missing / Timeout / Cancelled）与门面腿 `ShowUIAwaitResult<T>`（uGUI 腿同形、UI Toolkit 腿多一枚 `panelSettings`）与 `GetUIAwaitResult<T>()`：就绪、失败、缺失、超时、取消各按状态档交回，装载当场失败或已被关闭的窗口同帧落定，跨帧装载按实际就绪帧落定——等待不再以 null 与超时混言成败。
- 新增装载失败回滚：`UIWindow.InternalLoad` 装载回 false 或抛出时，窗口从共享栈摘出（补对称 `Closed` 回执、刷新新栈顶与显隐深度）并置失败 / 作废位，不再永占栈位、不再让 `IsAnyLoading` 永真；真装载失败报一条 Error，装载被取消不报错。
- 新增装载取消贯通：窗口自持装载期取消源，装载在途被关闭即掐断；uGUI 轨 `LoadPanelAsync` 把令牌转发给 `ResourceService.LoadGameObjectAsync`（UI Toolkit 轨此前已转发）。
- 新增窗口注册表 `UIWindowRegistry` 与元数据描述符 `UIWindowDescriptor`：新源生成器 `UIWindowCodegen` 在编译期解析 `[Window]` 实参（四个构造器重载与命名实参全解），把描述符与 `static () => new X()` 工厂写进模块初始化器 `UIWindowModuleInit` 逐类型登记；`UIWindowLedger.CreateInstance` 改为按类型句柄查表取工厂与元数据，开窗路径零 `Activator`、零特性反射（IL2CPP 同构）。形状非法报 MIRAI500~503（标在非窗口类 / 缺公共无参构造 / 抽象或泛型 / 嵌套在私有类型内），模块初始化期重复登记记 Fatal 保留先到。迁移：窗口类嵌套须 internal 或公开（生成的初始化器够不到 private 嵌套），测试探针窗已同步迁移。
- 新增开/关过渡契约 `IUITransition` 与 `UIWindow.Transition` 虚属性：真过渡期间锁交互（模态窗占全局压制位）、被接管按取消令牌掐断；缺位即瞬时，瞬时档零锁零占用零取消源分配。
- 新增 `UIManager` 公共静态定位口 `ResolveWindowLocation(windowId)` / `ResolveFromResources`（新增，非破坏）：开窗腿的寻址接缝，配置表 / `Resources` 两档判据只此一份；按 `SingletonMono.Instance` 语义取实例——场景里没有时现场物化一枚（默认配置表档），应用退出/播放停止的关停窗口内取不到则 `GameException`，调用排在服务就绪守卫之后。
- 新增最小导航两成员 `UIService.NavigationDepth`（开启序历史的长度——栈按层级排序答不出「最近开的是谁」）与 `UIService.TryCloseTopWindow()`（关上最近开的那只，走既有 `CanClose` 政策；无历史 / 拒关 / 过渡中回假且历史不出栈）；`Type` 形入口另补等待腿 `ShowUIAsyncAwait(Type, …, UIPayload, ct)` 交回 `UniTask<UIWindow>`。认不出轨与「认出来却没人认领驱动」两档都当场抬错，不把窗口推进栈再等装载静默失败。
- 新增停放档 `[Window(cacheTimeToDestroy: …)]`：`0` = 不缓存（关闭即销毁，缺省即此）、正数 = 停放并在这么多秒后由账本移出停放表并终态销毁、负数 = 停放永久；重新取用即取消计时。
- 新增窗口开合回执静态事件 `UIService.onWindowShown` / `UIService.onWindowClosed`（`public static event Action<UIWindow>`）：入栈、出栈各恰一次，停放与销毁都发；取代经 `EventManager` 派发的 `UIServiceEvent`，订阅者自持配对退订，门面关停与归零门整批摘订阅。
- 新增双语文档：`Documentation~/zh|en/UIMigration.md`（外部业务工程一页迁移指南：DTO + 基类换形 / 写点两种去向 / 行为变更四条），`Documentation~/zh|en/UI.md` 补公开腿签名表（一轨 8 支 + 动态 3 支 + 导航 2 成员 + 取窗 3 支）、载荷双通道、在飞合并与取消分档、停放 TTL 与生命周期钩子隔离政策表。

#### 日志

- `LogHandler` 新增自述能力 `ErrorWithExceptionUsesExceptionChannel`（基类默认 `false`，ZLogger 旁路为 `true`）：带异常的 Error 条目在 `UnityEngine.Debug` 通道上究竟落 `LogType.Error` 还是 `LogType.Exception` 由处理器决定，测试装配拿不到后端的 `*_INSTALLED` 宏，级别只能按此判定，写死任一级别换处理器就假红。
- 新增 `LogUtility.IsEnabled(ELogLevel)`（internal）：全部日志入口在字符串格式化前先经它按 `MinimumLevel` 短路，T4 模板与生成文件同步该结构。

#### 本地化

- 新增 `GetTextFromId(string id)` 与 `GetTextFromIdLanguage(string id, Language language)` 无参重载（`LocalizationService` 门面与处理器同形）：零参查询不再固定绑定 `params object[]` 签名、在调用点构造参数数组，语义与原零参分支一致。

#### 编辑器

- 新增 `[ProviderDropdown]` 对数组 / `List<T>` 字段的支持：特性放在集合字段上时每个元素各自获得实现类下拉与子属性展开（元素经 Odin 集合特性透传逐个走单字段绘制），集合的增删与重排由 Inspector 默认列表 UI 承担；此前该特性放在集合字段上会按单引用处理直接报错。
- 新增 `[ProviderDisplay]` 类型级显示元数据（标注在候选实现类上，配合 `[ProviderDropdown]`）：`Title` 非空时替换下拉行与选中态的类型名显示，`Description` 非空时在下拉详情面板置顶优先显示（折行、高度随内容伸缩），无描述时面板回退显示 Type / Base / Assembly；两者全空等价于不标注。框架内置候选实现类（工具 Handler / 各服务后端 / 存档处理器与序列化器 / 密钥与加密提供方 / UI 双轨驱动者）已全部标注。

### Changed

#### 场景

- ⚠ 删除场景加载的 `gcCollect` 参数。如果需要，在合适时机自行调 `ResourceService.ForceUnloadUnusedAssets(true)`。
- ⚠ 静态生命周期事件改名以对齐 `GameApp` / `ProcedureService` 的命名：`MainSceneChanged` → `onMainSceneChanged`、`SubSceneLoaded` → `onSubSceneLoaded`、`SubSceneUnloaded` → `onSubSceneUnloaded`。

#### 设置

- ⚠ `GraphicsSettings` 的静态事件统一 `on` 前缀：`OnFullScreenChanged` → `onFullScreenChanged`、`OnResolutionChanged` → `onResolutionChanged`、`OnMaxResolutionChanged` → `onMaxResolutionChanged`、`OnVSyncChanged` → `onVSyncChanged`、`OnWindowModeChanged` → `onWindowModeChanged`。

#### 输入

- ⚠ `InputStateMachine` 的实例事件统一 `on` 前缀：`ResetRequested` → `onResetRequested`、`SuppressionChanged` → `onSuppressionChanged`。

#### 日志

- 全部 `LogUtility` 入口在字符串格式化前按 `LogHandler.MinimumLevel` 前置短路：被过滤的日志此前仍会完成格式化并分配结果字符串，现在直接返回；输出与 `onMessageLogged` 事件契约不变（事件本就只在通过过滤后触发），被过滤的调用不再产生 GC 分配。

#### 资源

- ⚠ `BackgroundMusic` 收敛为仅直接引用 `AudioClip`（`m_AudioClip`），移除「直接强引用 / AudioClipInfo 路径引用」双轨；`AudioClipInfo` 弃用（仅为旧资产反序列化保留，其 Drawer 删除）。直接引用的存量数据按原字段名自动保留，原路径引用（`m_SoundClip`）的存量场景需在 Inspector 重新指定音频；需要弱引用加载的场景改用 `AssetReference<TObject>`。

#### JSON

- 成员是否入档改为允许列表（`JsonTypeSupport.IsSupportedMemberType`）：此前除黑名单类型外一律反射兜底，接口/抽象成员被写成不带类型名的对象、`StringBuilder`/`Type`/`Tuple` 被写成私有内部结构、`UnityEvent<T>` 被写成 `m_PersistentCalls`，读回时构造不出实例却静默成档；现在这些形态连同委托、多维数组、BCL 具体类型一起不入选，写侧不出现在 JSON、读侧按未知字段忽略，两侧对称。
- ⚠ 泛型 `UnityEvent<T>`、`ISet`/集合接口以外的接口与抽象成员、`Type`/`IntPtr`/`MarshalByRefObject` 派生成员从"写出歪数据"变为不入选；类型自身无可序列化成员仍抛 `GameException`，`[SerializeField]`/`[JsonSerialize]` 均不覆盖该判据。

#### 本地化

- ⚠ 语言变更事件统一 `on` 前缀：外观的静态 `LocalizationService.OnLanguageChanged` 与处理器实例的 `LocalizationServiceHandler.OnLanguageChanged` 均改名 `onLanguageChanged`，派发时序与句柄订阅路径不变。

#### 存档

- ⚠ 九枚静态存档事件统一 `on` 前缀：`SlotChanged` → `onSlotChanged`、`BlockSaved` → `onBlockSaved`、`BlockDeleted` → `onBlockDeleted`、`SaveProgress` → `onSaveProgress`、`LoadProgress` → `onLoadProgress`、`EntityRestored` → `onEntityRestored`、`SaveFailed` → `onSaveFailed`、`LoadFailed` → `onLoadFailed`、`ScreenshotCaptured` → `onScreenshotCaptured`；派发时序、参数类型与 `EventManager` 桥事件均不变。
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
- ⚠ 装载失败的窗口不再留在共享栈上：当场回滚出栈且不进停放表，`IsAnyLoading` 不再被失败窗永真。迁移：依赖「开窗失败后窗口仍在栈上」的存量用法（含测试夹具的拒开探针）改走装载成功或 `UIOpenResult` 结果契约。
- ⚠ 未标 `[Window]` 的窗口类不再可开：`CreateInstance` 查不到注册当场 `GameException` 点名补特性，不再静默兜 `EUILayer.UI` + 10 秒隐藏关闭 + 类型名地址。迁移：存量无特性窗口补 `[Window(…)]`（层级、地址、缓存等取值显式声明）。
- ⚠ 开窗与关闭默认改瞬时：新增 `IUITransition`（`Play(open, ct)` / `Snap(open)`）与 `UIWindow.Transition` 虚属性（缺位即瞬时）——默认开窗不再有 0.5 秒延迟与半秒输入锁，模态窗不再默认占全局交互压制位；关闭即时停放（缓存窗 `SetActive(false)` 与出栈同帧）。移除 `OpenAnimation` / `CloseAnimation` / `TopRefreshWaiter` 三枚硬编码延迟虚方法。迁移：依赖默认延迟或默认输入锁的窗口改覆写 `Transition` 提供过渡实现；覆写三枚虚方法的存量窗口改实现 `IUITransition`（过渡期间锁交互、接管按取消令牌掐断的语义由窗口代次守卫接办）。
- ⚠ 窗口状态位封装：`IsLoadDone` / `IsDestroyed` 内部字段收成属性（私有写），`IsHide` / `HideTimerId` / `HideTimeToClose` 的写口收成 internal（读面不变）。迁移：外部直写这些位的用法改为经 `Init`（由注册表描述符接办）或窗口自身流程。
- ⚠ `UIBase.ChildList` 公共面收成 `IReadOnlyList<UIWidget>`：子级增删由控件创建/销毁流程经内部门缝接办，外部直改列表不再可写。`UIWidget.RestChildCanvas` 更名 `ResetChildCanvas`（拼写修正）。
- ⚠ `WindowAttribute` 构造器收敛为单一形状 `(EUILayer windowLayer, string location = null, bool fromResources = false, bool fullScreen = false, int hideTimeToClose = 10, EUIModal modal = EUIModal.Inherit, float cacheTimeToDestroy = 0f)`：移除整数层级形、`(EUILayer, string location)` 位置形等三个旧重载；特性字段改 PascalCase（`windowLayer`→`WindowLayer` 等，运行期消费方只剩源生成器）。迁移：`[Window(1, "path")]` 的整数层级改枚举或 `(EUILayer)1`；`[Window(EUILayer.UI, "path")]` 的位置地址改 `location:` 命名实参。
- ⚠ 层级枚举更名 `EUILayer`（原 `UILayer`）：与 `EUIModal` / `EUIOpenStatus` 同按命名表的「新代码口径：枚举一律 `E` 前缀」收口；底层类型保持 `int`（它是 `WindowAttribute.WindowLayer` 与深度算术的操作数，不属"运行期状态枚举显式 `: byte`"那一档）。迁移：`[Window(UILayer.UI)]` 改 `[Window(EUILayer.UI)]`，成员名与取值一字未动，除枚举名外没有任何签名形状改变。
- 模态解耦：新增 `EUIModal` 三态与 `[Window(modal: EUIModal.Modal | NonModal)]` 显式档（继承档缺省按层级结算）；压栈压下层交互位、租约占全局压制位、门面模态查询三处判据统一改读窗口结算的模态位——非模态层可强制模态、模态层可强制非模态（attribute 实参禁 nullable，三态由此枚举表达）。
- `UIWindow.Init` 收 internal：窗口初始化只经注册表链路与测试接缝，游戏代码经门面开窗不直接初始化。
- ⚠ `ShowUIAsyncAwait` / `GetUIAsyncAwait` / `GetUIAsync` 在装载失败或装载中被关闭时不再交回 / 回调未就绪窗口（改交 null、不调回调并各报一条 Warning）；等待超时档维持原行为照常交回。取窗找不到目标时从全静默改为报一条 Warning。
- `UGUIWindow.BindPanel` / `UITKWindow.BindPanel` 缺组件从裸 `Exception` 改抛 `GameException`；`CloseUI` / `HideUI` 对不在栈上的窗口从全静默改为补一条 Debug 级开发日志。
- UI 模块目录归位（命名空间一律不变，只动文件位置）：`UIOpenResult`/`EUIOpenStatus`、`UIWindowDescriptor`、`WindowAttribute`/`EUILayer`/`EUIModal`、`UIInteractionLease` 进 `Runtime/Services/UI/Models/`，`IUITransition` 进 `Abstractions/`，`ErrorLogger`/`LogUI` 进 `Handler/UGUI/Support/`；对象模型 `UIBase`/`UIWindow`/`UIWidget` 与 `UIWindowLedger`/`UITrack`/`UIWindowRegistry` 留在 `Kernel/`。
- ⚠ 载荷形态硬切：`params object[]` 从两轨全部公开腿消失，每轨改「无载荷 4 支 + 带载荷 4 支」平铺——带载荷那一族按 `TWindow : UGUIWindow<TArg>` / `UITKWindow<TArg>` 约束与无载荷那一族分辨，载荷排第一枚（`in TArg` 泛型直塞，struct 不装箱、一步都不经擦除），窗口名 / 地址 / 内置资源 / `panelSettings`（仅 UITK 腿）依次排其后，同名重载按约束而非形参个数落轨。全腿收 `CancellationToken ct = default`：不传零开销，且只在装载在途那段被消费（已就绪的复用与停放重取同步交回、不消费 `ct`；复用一只仍在装载的窗时令牌照样登记，撤销会掐断那一次在途装载，与在飞合并同段语义）。迁移：`ShowUIAsync<T>(name, location, false, userData: …)` 改 `ShowUIAsync<TWindow, TArg>(in payload, name, location, fromResources)`；等待腿 `ShowUIAsyncAwait<TWindow, TArg>` 因 `async` 禁 `in` 形参（CS1988）收普通 `TArg`。
- ⚠ `UIBase._params` / `UserData` / `Params` 删除，载荷落点换成 `UGUIWindow<TArg>.Payload` / `UITKWindow<TArg>.Payload`：每次开窗覆盖、关闭不清（残留到下一次覆盖为止，无「读一次即清」语义）。迁移：`UserData?.ToString()` / `(string)UserData` / `_params[i]` / `Params[i]` 与 `Params.Length` 判空各改读 `Payload.字段`，一枚 DTO 装齐全部字段，位置序号从此消失。
- ⚠ 带载荷窗口基类换形：`class X : UGUIWindow` → `class X : UGUIWindow<MyDto>`（UI Toolkit 轨同形）。不带槽的窗口被塞非空载荷当场 `GameException`（文案带窗口类名，指认漏换基类的调用点），按名命中的窗槽型不符、或门面按名取回的实例不是 `TWindow` 同样抬错并带期望/实际双类型名；抬错排在压栈与卸停放之前说的是停放重取与新开两条支路——既不压半只窗，也不消费停放态（那只实例仍从停放表取得回）；复用支路的 Pop→Push 排在验槽之前（那只窗本就完整在栈，验槽不过抬错，回执与挪序已发生）。迁移口径见 `Documentation~/zh/UIMigration.md` / `Documentation~/en/UIMigration.md`。
- ⚠ 动态腿（`Type` 形入口）载荷形参由 `params object[]` 改一枚 `UIPayload`（`default` 即空载荷，与 `null` 引用同判）：引用型只存引用（0 分配、到达后引用同一），值类型装箱一次，`To<T>` 失败面为 `GameException`、`TryGet<T>` 回假不抬错。同批 `EUIOpenStatus` 增 `Cancelled = 4`：调用方令牌撤销等待的结果档，与 `Timeout` 分档可辨（`Cancelled` 只说明本次等待以取消落定，装载是否续跑取决于其余等待者（无人在等则回滚），不得当就绪窗用）；撤销的落点按腿分档——void 腿静默回滚出栈不报 Error，等待腿原样上抛 `OperationCanceledException`，结果腿落 `Cancelled`。迁移：`userData: new object[] { dto }` 改 `UIPayload.From(dto)`；原先只判 `Timeout` 的结果消费点一并接住 `Cancelled`。
- ⚠ 同一只窗装载在途时再开，由「静默覆盖 / 重开发装载」改为**合并在飞 + 载荷 last-wins**：不重开发装载、不压第二只实例，载荷覆盖为最后一枚，`OnRefresh` 只在面板就绪那一次跑并见终载荷。迁移：依赖「两次 Show 各刷一次」的用法改等结果腿交回，或先关再开。
- ⚠ 缓存两档并成一枚：`[Window]` 的 `cacheInstance` 形参与 `WindowAttribute.CacheInstance` / `UIWindowDescriptor.CacheInstance` / `UIWindow.CacheInstance` 一并删除，停放与 TTL 全由 `cacheTimeToDestroy` 三态表达（`0` = 不缓存、正数 = 停放并到期销毁、负数 = 停放永久）。停放判据从此读那枚浮点的符号，`UIWindow.Init` 也随之少一枚形参（`modal` 前移一位，按位置传到尾参的调用点会当场编译报错）。迁移：`cacheInstance: true` 改 `cacheTimeToDestroy: -1f`；`cacheInstance: true, cacheTimeToDestroy: 秒` 只留那枚秒数；读 `window.CacheInstance` 的改读 `window.CacheTimeToDestroy` 符号或包内 `ParksOnClose`。

#### 文档

- README 双语重写为标准仓库结构（特性/安装/快速开始/架构/文档索引/编辑器工具/贡献），架构树按重构后的 Core 三分（Foundation/Infrastructure/Utilities）与 Pooling 更名改口径，启动链归属改指模板目录，失效的编辑器工具条目（JSON Benchmark）剔除并补 PlayerLoop Debugger 等实存菜单；README 里的订阅/派发异常分级与测试约定两节内迁到新增的 CONTRIBUTING.md，异常分级声明点更新为五处 RETHROW 常量（补 TaskRunner / MemoryPoolRegistry）。
- 双语文档按代码全量核校，修正与实现漂移的陈述：UI 快速上手与核心类型改按轨继承（业务窗口继承 `UGUIWindow`，`UIWindow` 是后端无关对象模型，绑定代码生成器同口径）；调试器启动参数更正为 `-show-debugger`；存档浏览器菜单更正为 `Tools/Moirai/Save/Save Browser`；Resource 绑定枚举更名 `EResourceBindingOption`、类型表行更正 `ResourceBindingTypes`；Scene 处理器空配置的口径改为回退 `DefaultSceneHandler`；移除两处不存在的 `YooAsset/Editor PlayMode` 菜单引用；MemoryPool 阶段切换点更正（Boot 由 `MemoryPoolSetting.OnInit` 设定，模板启动链只在 `ProcedurePrepare4Entrance` 切 Gameplay）；ObjectPool 目录树更正 `Object/` 子目录；Testing 测试桥路径更正为工程相对 `Temp/` 并刷新分布快照；Audio 更正 `BackgroundMusic` 直接引用口径与迁移说明；CodeComments 补齐尾部导航。

### Removed

#### 资源

- ⚠ 移除 `ResourceService.LoadAssetForEditor` 与后端接缝 `ResourceServiceHandler.LoadAssetForEditor`（`virtual` 的地址换算钩子，移除时零覆写），非播放态取资产改用 `TryLoadAsset<Object>`；接入按文件名或包清单寻址的后端时，换算需并进取用族的编辑分支本身。

#### UI

- ⚠ 移除 `UIWindowEvent` 整枚事件类（`Show<T>` / `Show(Type, …)` 各含带载荷两形、`Close` 两形、`Hide` 两形、`CloseAll`）：开合窗不再经「发事件 + `UIManager` 订阅转手」的中转，调用点直调门面腿即时执行。迁移：`Show<T>(id)` → `UIService.ShowUIAsync<T>(id)`（同步档 `ShowUI<T>`）、`Show(type, id)` → `UIService.ShowUIAsync(type, id)`、`Close<T>(id)` / `Close(type, id)` → `UIService.CloseUI<T>(id)` / `UIService.CloseUI(type, id)`、`Hide` 两形 → `UIService.HideUI`、`CloseAll()` → `UIService.CloseAll()`；带载荷的写点另按迁移指南换静态/动态腿，寻址经新增的 `UIManager.ResolveWindowLocation` / `UIManager.ResolveFromResources` 这一对。`UIManager` 自此只剩寻址职责。
- ⚠ 移除 `UIServiceEvent`（含 `EMode` 全档）与标记接口 `IUIEvent`：窗口开合回执不再经 `EventManager` 派发。迁移：订阅点改 `UIService.onWindowShown` / `UIService.onWindowClosed`（`public static event Action<UIWindow>`，形参即那枚窗口），入栈/出栈各恰一次、停放与销毁都发；退订由订阅者自己配对，门面关停与归零门整批摘掉。实现 `IUIEvent` 的业务事件（如提示类事件）去掉该接口即可，本身照旧走 `EventManager`。
- ⚠ 移除 `UIManager.LoadUGUI<T>`（framework 侧公开虚方法，零消费者零文档，实测无调用点）。迁移：直调 `UIService` 的开窗腿。
- ⚠ 移除 `UIServiceHandler` 上 25 枚纯转发 `UIWindowLedger` 的转发口：`public virtual` 二十枚（查询族 `GetTopWindow()`/`GetTopWindow(int)`/`GetTopWindowName(int)`/`IsAnyLoading`/`HasWindow`/`GetWindow<T>`/`IsBlockedByModal`/`IsModal` 与属性 `CurrentModal`、关隐族 `CloseUI`/`HideUI`/`CloseAll`/`CloseAllWithOut`、取窗族 `GetUIAsyncAwait`/`GetUIAsync`）与 `protected` 五枚（`GetWindow(string)`/`IsContains` 两道查询、`OnWindowPrepare`/`Push`/`Pop` 三枚栈钩子）。包内 Runtime/Editor/Samples~/Templates~ 与同宿主各包零调用方——各包对这些名字的引用全部走门面。迁移：`handler.X(…)` 改 `UIService.X(…)`，形参与语义一字未动；派生后端里自调栈钩子的改叫 `UIService.SharedLedger`（框架装配内可达）。

### Fixed

#### UI

- 停放窗不再续留全局事件订阅：关闭进停放即反注册（`UnregisterEvent` 恰一次），停放态窗口不挂事件；装载在途被作废的窗口（`AbortFailedLoad`）补上守卫式反注册，抛出型 `RegisterEvent` 不再把已订阅的窗留在销毁态。关停扫尾（`CloseAllWhere(isShutDown: true)`）现在真的销毁被这一轨认得的停放窗——出停放表、`OnDestroy` 随之触发一次（此前只摘表不销毁）。
- 停放窗不再带半截过渡姿态：缓存停放前先把面板 `Snap` 拨到关窗终态（此前只 `ParkPanel` 不拨，下次取用从半截动画起播）；过渡 `Play` 抛出时退到 `Snap` 落终态、不把窗口卡在半开，`Snap` 自身也抛出才记一条 Error 并照常走完。
- 生命周期与 Tick 钩子抛出不再黑屏或半开窗：`OnCreate` / `OnRefresh` / `OnClose` / `OnDestroy` / `OnUpdate` 等钩子抛出改为记一条 Error 后继续走后续流程（此前一处抛出会中断整帧驱动、或让窗口停在半开态回不来）。

#### 工具

- `PathUtility.FormatToSysFilePath(null)` 从抛 `NullReferenceException` 改为返回 `null`，与 `FormatToUnityPath(null)` 的既有行为对称（UNC 格式化漏了同款 null 守卫）。
