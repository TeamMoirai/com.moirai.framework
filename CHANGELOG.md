# Changelog

格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)。

本文件只留 `[Unreleased]` 一段，按**后覆盖**维护：只记尚未发行的净结果，发版时该段定名后移到 [GitHub Releases](https://github.com/TeamMoirai/com.moirai.framework/releases) 并清空。被后续变更推翻的中间态不留条目——同一件事被推翻时改掉或删掉原条目。排版：段首可写一段摘要（第一个 `###` 之前，发版时进 Release notes 的 `>` 引言之后、也是 release commit 的 body）；`###` 是变更类型，段内 `####` 按模块分组，一条只说一个事实、写成一行不折行，发版时整段进 `<details>` 详细日志。面向外部读者压缩精简，只写净结果与迁移口径，诊断与来龙去脉不进本文件。标记 ⚠ 的是破坏性变更。

## [Unreleased]

### Added

#### 资源

- 新增取用族 `ResourceService.TryLoadAsset<T>(location, out asset, packageName)` 与 `TryLoadAssetAsync<T>(location, cancellationToken, packageName)`：内部取租约、读出对象后立即归还，归还时按 `IdleAssetExpireTime`（默认 60 秒）保活，不把租约交给调用方。
- 取用族在服务未初始化时（编辑器非播放态）直读 `AssetDatabase`，不建记录也不取租约：可序列化资源引用类与编辑器预览因此共用同一个入口，不再各写 `#if UNITY_EDITOR` 分支；异步形以 `null` 表失败（异步方法不能带 `out` 参数）。
- 迁移口径：取到的对象只在保活窗口内稳定，需要长期持有或跨长周期保存引用的场景改用 Lease API 自持租约，长期显示的用法应定期回读本族。

#### JSON

- 集合形态补齐往返：`HashSet<T>`/`SortedSet<T>`/`ISet<T>`/`Queue<T>`/`Stack<T>`/`LinkedList<T>` 与 `IList<T>`/`ICollection<T>`/`IEnumerable<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>` 按枚举序写成 JSON 数组、读侧按序回填（`Stack<T>` 逆序压栈以保持栈顶与写出前一致，`ISet<T>` 落到 `HashSet<T>`，集合接口落到 `List<T>`）；读侧原先只认 `List<T>`/`Dictionary<K,V>`，接口形态会抛 "Cannot parse a JSON array into"。

#### 日志

- `LogHandler` 新增自述能力 `ErrorWithExceptionUsesExceptionChannel`（基类默认 `false`，ZLogger 旁路为 `true`）：带异常的 Error 条目在 `UnityEngine.Debug` 通道上究竟落 `LogType.Error` 还是 `LogType.Exception` 由处理器决定，测试装配拿不到后端的 `*_INSTALLED` 宏，级别只能按此判定，写死任一级别换处理器就假红。

### Changed

#### JSON

- 成员是否入档改为允许列表（`JsonTypeSupport.IsSupportedMemberType`）：此前除黑名单类型外一律反射兜底，接口/抽象成员被写成不带类型名的对象、`StringBuilder`/`Type`/`Tuple` 被写成私有内部结构、`UnityEvent<T>` 被写成 `m_PersistentCalls`，读回时构造不出实例却静默成档；现在这些形态连同委托、多维数组、BCL 具体类型一起不入选，写侧不出现在 JSON、读侧按未知字段忽略，两侧对称。
- ⚠ 泛型 `UnityEvent<T>`、`ISet`/集合接口以外的接口与抽象成员、`Type`/`IntPtr`/`MarshalByRefObject` 派生成员从"写出歪数据"变为不入选；类型自身无可序列化成员仍抛 `GameException`，`[SerializeField]`/`[JsonSerialize]` 均不覆盖该判据。

#### 存档

- ⚠ 删 `ESaveBackend` 枚举，块后端标识改为 `ushort` 常量表 `SaveBackendIds`（`Json=0`/`MessagePack=1`/`MemoryPack=2`/`Protobuf=3`/`KeyValue=254` 数值逐一沿用）：契约成员是 `ISaveSerializer.BackendId`，`[SaveData(Backend = SaveBackendIds.MESSAGE_PACK)]`，注册表按 ID 建表——项目自定义后端直接登记自己的 ID（0..255 框架保留，从 1000 起分配），不再借道 `(ESaveBackend)999` 这类魔法数。存档线格式零变化：块头那 2 字节的偏移与数值都未动，容器与文件头版本保持 2，存量存档原样读回。
- `SaveServiceSettings` 的默认序列化后端按类型名配置（`m_DefaultSerializerTypeName`，ProviderDropdown 类型名模式，与 `UIGeneratorSettings` 同款）：首次读取时解析成实例并按名缓存，并确保该 ID 在 `SaveSerializerRegistry` 里有主，写读落进同一实现；无状态的内置序列化器不再为引用序列化而带 `[Serializable]`。设置资产没配这一项时按内置 Json 走，已在 Inspector 配过默认后端的需在改版后重选一次。
- 注册表把"0..255 框架保留"从文档约定变成门禁：`SaveSerializerRegistry.Register` 拒绝保留区内任何非内建标识（此前只挡 `KEY_VALUE`），内建扩号不会再与项目已注册的后端静默相撞；类型名解析不到实现时按内置 JSON 回退，且同一份错配置只报一次 Fatal（此前每存一块都重报一次并新建实例）；构建期由 `SaveSettingsBuildValidator` 与占位密钥一并报出，开严同样挡包。

### Removed

#### 资源

- ⚠ 移除 `ResourceService.LoadAssetForEditor` 与后端接缝 `ResourceServiceHandler.LoadAssetForEditor`（`virtual` 的地址换算钩子，移除时零覆写），非播放态取资产改用 `TryLoadAsset<Object>`；接入按文件名或包清单寻址的后端时，换算需并进取用族的编辑分支本身。
