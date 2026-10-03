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

### Changed

#### JSON

- 成员是否入档改为允许列表（`JsonTypeSupport.IsSupportedMemberType`）：此前除黑名单类型外一律反射兜底，接口/抽象成员被写成不带类型名的对象、`StringBuilder`/`Type`/`Tuple` 被写成私有内部结构、`UnityEvent<T>` 被写成 `m_PersistentCalls`，读回时构造不出实例却静默成档；现在这些形态连同委托、多维数组、BCL 具体类型一起不入选，写侧不出现在 JSON、读侧按未知字段忽略，两侧对称。
- ⚠ 泛型 `UnityEvent<T>`、`ISet`/集合接口以外的接口与抽象成员、`Type`/`IntPtr`/`MarshalByRefObject` 派生成员从"写出歪数据"变为不入选；类型自身无可序列化成员仍抛 `GameException`，`[SerializeField]`/`[JsonSerialize]` 均不覆盖该判据。

### Removed

#### 资源

- ⚠ 移除 `ResourceService.LoadAssetForEditor` 与后端接缝 `ResourceServiceHandler.LoadAssetForEditor`（`virtual` 的地址换算钩子，移除时零覆写），非播放态取资产改用 `TryLoadAsset<Object>`；接入按文件名或包清单寻址的后端时，换算需并进取用族的编辑分支本身。
