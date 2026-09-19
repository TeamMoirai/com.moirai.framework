# Save 存档服务

> 单文件多数据块容器、可插拔序列化/加密/压缩、文件级版本迁移总线与无代码组件保存。

Save 服务（`SaveService`）提供 AAA 级存档基础设施：**四种可插拔序列化后端**（JSON / MessagePack / MemoryPack / protobuf-net）、**文件级版本迁移总线**（版本链 + 审计 + 惰性回写）与**块级版本迁移**、**AES 加密管线**（密钥来源可插拔：静态口令 / 运行期口令注入 / HKDF 按用户派生）、**可选 GZip 压缩**与**无代码组件保存**（SourceGenerator 生成强类型捕获器）。命名空间 `Moirai.Atropos.Save`。

## 架构

```
SaveService（静态外观，写路径未就绪抛 GameException，读路径降级）
├── 存储管线（[SerializeReference] 可切换）
│     PlainSaveHandler        明文直通
│     AESEncryptedSaveHandler AES-256-CBC + HMAC（encrypt-then-MAC），内嵌 [SerializeReference] 密钥提供方
├── 存储后端（[SerializeReference] 可切换，ISaveStorage + SaveStorageBackend）
│     FileSaveStorageBackend  本地文件（临时文件 + Flush(true) + 原子替换，默认）
│     CloudSaveStorageBackend 本地镜像 + 远端 KV 双写（远端插拔件内置 RestCloudSaveKvStore / UnityCloudSaveKvStore）
├── 转换链（顺序固定：Serialize → Compress? → Encrypt? → CRC）
│     压缩：ICompressionProvider + SaveCompressionRegistry（GZip 内建 ID=1；未知 ID 读侧拒载）
│     密钥：ISaveKeyProvider + SaveKeyProvider（内嵌于 AES 处理器；Static 静态口令默认 / Passphrase 运行期注入 / HkdfPerUser 按用户派生）
├── 序列化后端（ESaveBackend + ISaveSerializer + SaveSerializerRegistry）
│     Json（内置，默认）/ MessagePack / MemoryPack / Protobuf / KeyValue（组件捕获格式保留标识）
│     开放注册：Register(ISaveSerializer)/Unregister(ESaveBackend)（重复后端 fail-fast，KeyValue 不可占用）
├── 多块容器（SaveFileContainer，手写二进制，块级 key/version/backend/bytes）
├── 数据模型（[SaveData] + SaveDataBlock.OnMigrate 版本迁移）
├── 迁移总线（SaveMigrationManager + ISaveMigrator：文件级版本链，加载/写入管线前置）
└── 无代码保存（[SaveField] + SaveComponent + SaveHost SourceGenerator 生成捕获器，[SaveComponentSchema] 模式版本）
```

## 文件格式

```
[32B 明文头 "MRSA"][载荷]
头：[4B 魔数][4B 格式版本=2][8B UTC ticks][4B 载荷长][4B 载荷 CRC32][4B 压缩提供方 ID][4B 标志]
载荷 = Compress?(Container)；加密处理器下再包 [16B IV][AES-256-CBC][32B HMAC]
容器：[4B 魔数 "MRSB"][4B 容器版本=2][4B 块数]
      逐块 [4B 键字节长][键 UTF8][4B 模式版本][2B 后端][4B 载荷长][4B 载荷 CRC32][载荷]
```

- 文件头永远明文（不解密即可读保存时间）；头 CRC 防整档存储损坏、HMAC 防篡改（先验 MAC 后解密）
- 容器 v2 逐块 CRC32 自校验（头 CRC 放行后的第二道隔离层）：载荷校验不符的坏块跳过并记告警日志，其余健康块照常可救（**部分恢复**）；块框架（长度/键字段）越界的结构性损坏因后续块边界不可知，保留已解析前缀后终止。`TryLoadBlock` 对坏块键返回 `Corrupted`（区别于真无块的 `FileNotFound`）；坏块在下次写回时自然剔除（健康块保留）
- 容器 v1 旧档硬切作废：读取判别为 `UnsupportedVersion`，不做双格式兼容读
- 转换链顺序固定：序列化 → **压缩（可选，加密前）** → 加密 → CRC；读侧反向（解密 → 按文件头 ID 查注册表解压）——未压缩旧档原样透传（魔数/flags sniff 幂等，新旧档共存）
- 文件头 offset 24-27 为压缩提供方 ID（0 = 未压缩）；未知 ID 判别为 `UnsupportedVersion`，标志位与 ID 不一致判别为 `Corrupted`
- 密钥来源（`ISaveKeyProvider`）：静态口令 PBKDF2（`StaticSaveKeyProvider`，未配置时的占位默认）/ 运行期口令注入（`PassphraseSaveKeyProvider`，口令仅内存不落盘，未注入时读 `InvalidArgument`、写 fail-fast）/ HKDF-SHA256 按用户派生（`HKDFPerUserSaveKeyProvider`，多账号存档互相不可读）
- 原子写入：临时文件 `xxx.sav.tmp-{guid}` → `Flush(true)` → `File.Replace`（经 `FileSaveStorageBackend`）；启动期后台清扫孤儿临时文件
- 读写全链路流式：写侧经 `WriteAtomic(Action<Stream>)` 委托写（占位头 → CRC 写透传 → 加密/压缩链 → 容器段流直灌 → 回填真头，零整档缓冲）；读侧头 32B → CRC 增量 → 解密/解压链 → 256KB 段池拉取 → CRC 校验 → 容器 `ReadOnlySequence` 跨段解析（单段快路径直通跨度解析器）；AES 档读为两遍流式（第一遍流式 HMAC 预验——先验证后解密杜绝填充 oracle；冻结 rewind 后第二遍限长 [IV‖密文] 解密链，HMAC 尾留段外，任意时刻关闭安全）
- 同文件读写经串行信号量排队（防并发读-改-写丢块）——串行门在 Handler 编排层，存储后端无感知
- 删除全族（单档/文件夹/根目录清空，同步与异步版）持「根目录 → 文件夹 → 文件」分层门（固定序取门防死锁），与块级读写互斥——存在性判定与删除同临界区完成，杜绝删除后并发写入复活文件；合规性数据清除（`DeleteAllSaveFiles`）结果可靠
- 存储层契约（`ISaveStorage`）：同步原语为契约核心（`Exists`/`TryReadAllBytes`/`WriteAtomic`/`DeleteFile`/`DeleteDirectory`/`EnumerateFiles`/`CreateBackup`/`RestoreBackup`）；流式原语下沉零整档缓冲 IO（`WriteAtomic(filePath, Action<Stream>)` 委托写——可寻址临时流内占位头→转换链→回填真头；`TryOpenRead` 可寻址只读流）；`TryGetWriteTimeUtc` 元数据查询支撑会话级增量守卫；异步包装默认线程池卸载（真异步后端覆盖并声明 `Capabilities`）；能力位含 `SyncReadsAuthoritative`（同步读是否权威——云后端 false，外观裸名同步读在非权威后端上记一次性告警引导改用异步 API）；读取错误分型返回、写入失败抛 `GameException`、删除幂等；实现必须纯 .NET（任意线程可调）

## 存档路径

`Application.persistentDataPath/Data/{folderName}/{fileName}{扩展名}`；扩展名默认 `.sav`（`SaveServiceSettings` 配置，传入文件名自动剥离扩展名后重追加）。

## 手动存档数据脚本（含版本升级）

```csharp
[SaveData("PlayerStats", version = 3, Backend = ESaveBackend.MessagePack)]
public sealed class PlayerStatsData : SaveDataBlock
{
    public int Level;
    public long Gold;

    protected internal override void OnMigrate(int fromVersion)
    {
        // switch 级联惯例：v1→v2→v3 逐级 fallthrough，就地修正字段
        switch (fromVersion)
        {
            case 1: Gold = 0; goto case 2;
            case 2: Level = 1; break;
        }
    }
}
```

- 块写入时记录声明版本；加载时存档版本 < 声明版本 → 级联迁移（内存修正，下次写入持久化）；> 声明版本 → `UnsupportedVersion` 拒绝
- 二进制后端要求类型带各自 AOT 标注：MessagePack `[MessagePackObject]`+SG、MemoryPack `[MemoryPackable]` partial+SG、protobuf-net `[ProtoContract]`+BuildTools SG；未标注类型 IL2CPP 不受支持

## 版本迁移总线（文件级）

块级 `OnMigrate` 处理**单类型**模式演进；迁移总线（`SaveMigrationManager` + `ISaveMigrator`）处理**整档**数据版本——跨块改名、字段改型、废弃块清理等横向变更。两条轨道独立可组合，加载管线上总线**先于** `OnMigrate` 执行。

### 启用与版本模型

- 版本号 int 递增：`0` = 版本化前基线存档；游戏层启动期设置 `SaveService.CurrentSaveVersion = N`（默认 `0` = 总线完全旁路，零开销）
- 采纳仪式：启用版本化且存在旧档时，须注册自版本 `0` 起的迁移链（无元数据块的旧档按版本 `0` 处理；形状未变可注册空迁移器桥接 `0→1`）
- 版本盖章：总线激活后，任何落盘的存档自动在 `__meta` 元数据块写入当前 `SaveVersion`（游戏层无需手动赋值；语义化三段式可按 `主*10000+次*100+修订` 映射为 int，映射规则项目内固化）

### 迁移器（ISaveMigrator）

```csharp
public sealed class SaveMigratorV1ToV2 : ISaveMigrator
{
    public int FromVersion => 1;
    public int ToVersion => 2;
    public int Priority => 0;   // 同一边多个迁移器按 Priority 升序执行

    public UniTask Migrate(SaveMigrationContext ctx)
    {
        ctx.RenameBlock("oldKey", "newKey");
        ctx.RenameField<PlayerData>("gold", "coins");                   // 块键经 [SaveData] 声明解析（也可显式传键）
        ctx.RetypeField<int, string>("profile", "level", v => v.ToString());
        ctx.TransformBlock<PlayerData>("profile", old => new PlayerData { /* … */ });
        ctx.DeleteBlock("obsolete");
        return UniTask.CompletedTask;
    }
}
```

- 注册：实现类由 SaveHost SourceGenerator 扫描经模块初始化器自注册（AOT 安全；要求具体非抽象类、非私有嵌套、可访问无参构造，否则报 MIRAI302）；也可 `SaveService.RegisterMigrator(...)` 手动注册。非法版本边（`To <= From`）注册期抛 `ArgumentException`——仅允许升级方向，天然防环
- 执行约束：迁移在加载/写入管线内**同步执行**（串行门持有期，可能在主线程）——`Migrate` 必须同步完成，禁止切线程或返回未完成任务（fail-fast `MigrationFailed`）；禁止触达 Unity 主线程 API
- 目标块/字段不存在时操作为无操作（返回 `false`，兼容从未写过该块的旧档）；反序列化失败等真异常中止整条链
- 字段级操作（`RenameField`/`RetypeField`）支持 JSON（需 Newtonsoft.Json）与 KeyValue 块；**二进制后端不支持字段级操作**（记告警并跳过——用 `TransformBlock<T>` 保留旧类型整对象迁移，键序纪律见下方分析器）

### 链语义与错误分型

- 版本相等短路；`文件版本 > 当前版本` → `UnsupportedVersion`（拒绝降级）；链缺失/歧义（同起始版本多条不同目标边）/迁移器异常 → `MigrationFailed`（经 `LoadFailed` 事件 `Migrate` 阶段观测）
- **写入自愈**：任何读-改-写（块写入/组件块 upsert/块删除回写）触达旧版本档时先迁移再合并——任何落盘文件恒为当前版本，杜绝新形态块落入旧档后被迁移链误变换
- **回写策略**：加载触发迁移成功后按处理器的 `m_MigrationWriteBack`（默认开）惰性回写（避免每次加载重跑迁移链）；关闭时迁移仅作用于内存，同文件同会话不重复迁移（会话级缓存），盘上保持旧版本
- **审计**：每个迁移步向 `SaveMetadata.MigrationHistory` 追加 `"{from}->{to}|{迁移器类型全名}|{UTC ISO-8601}"`（随回写持久化）
- **显式迁移**：`SaveService.MigrateSave(fileName)` / `MigrateSaveAsync`——启动期批量修复旧档用；迁移成功**强制回写**（不受回写设置约束）；处理器未就绪返回 `HandlerNotReady`
- 会话缓存失效：`RestoreBackup` 与删除类操作自动失效对应路径（或全量）的会话缓存

### 组件模式版本（无代码链）

- 组件类标注 `[SaveComponentSchema(version)]`（缺省 1）→ 生成器发射捕获器 `SchemaVersion`；保存时按组件类型写入 KVT 块内 `$schemas` 作用域（该保留键不可能与类型全名冲突）
- 恢复时存档版本与捕获器当前版本不符：组件实现 `ISaveComponentMigrator` → 路由到 `OnMigrateComponent(fromVersion, ref reader, recordCount)`（须恰好消费 recordCount 条记录）；未实现 → 记告警并按键匹配容错恢复
- 旧格式块（无 `$schemas`）按当前版本处理（KVT 键匹配天然向后兼容）

### 二进制后端键序冻结（分析器 MIRAI400/401）

- `ServiceDependency.dll` 内置 `SaveSchemaAnalyzer`：`[SaveData(Backend=MessagePack/MemoryPack/Protobuf)]` 类型的成员键序号（`[Key]`/`[MemoryPackOrder]`/`[ProtoMember]`）与快照比对——MIRAI400 键序重排告警、MIRAI401 成员删除且未重写 `OnMigrate` 告警（均 Warning）
- 快照为附加文件 `.SaveSchemaSnapshot`（行格式 `类型全限定名|成员名:序号;…`，序号 -1 = MessagePack 字符串键模式），纳入版本控制随模式演进更新；快照缺失时分析器静默
- **快照生成**：菜单 `Tools/Moirai/Save/Export Schema Snapshot` 一键扫描全部二进制后端 [SaveData] 类型并写出项目根目录 `.SaveSchemaSnapshot`（提取规则与分析器逐条对齐，成员按名排序保证 diff 稳定）；二进制线格式变更前重新导出即推进基线
- Unity 编辑器无 AdditionalFiles 界面，经 `csc.rsp` 的 `/additionalfile:` 或 CI `dotnet build` 接线

## 无代码保存（组件勾选字段）

1. 玩法组件声明为 `partial class`，字段标 `[SaveField]`（可选显式存档键，重命名字段时保键稳定）：

```csharp
public partial class Player : MonoBehaviour
{
    [SaveField] private int _hp;
    [SaveField("bag_items")] private List<int> _items;
    [SaveField] private Dictionary<string, int> _inventory;
    [SaveField] private PlayerStats _stats;          // 嵌套 [SaveData] 数据类
    [SaveField] private EnemyAI _target;             // 场景引用（目标须挂 SaveObjectIdentity）
    [SaveField] private Texture2D _icon;             // 资产引用（须登记进 SaveAssetCatalog）
}
```

2. GameObject 挂 **Save Component**：Inspector 里添加目标组件绑定并勾选参与存档的字段（块键空缺时自动派生 `场景命名空间:物体路径`——已保存场景命名空间为场景资产路径（同名 Additive 场景防撞键），未保存/动态场景为场景名；物体路径为场景根到物体的**完整名称链**，同名兄弟/同名场景根自动追加 `[N]` 序号消歧，杜绝跨分支撞键覆盖）。
3. 运行期 `SaveService.SaveComponentsAsync(fileName)` / `LoadComponentsAsync(fileName)` 触发——编译期生成的强类型捕获器零反射捕获，按勾选掩码过滤；未知键跳过、缺失键保留当前值（字段增删天然向后兼容）。

### 支持的字段类型（SG v2）

- **标量**：基元/枚举/string/DateTime/TimeSpan/Unity 数学类型（Vector2/3/4、Quaternion、Color、Rect、Bounds）。
- **集合**：数组 `T[]`、`List<T>`、`Queue<T>`、`Stack<T>`、`HashSet<T>`、`Dictionary<K,V>`；元素递归支持标量与嵌套数据类（含集合套集合）；映射键仅限标量/枚举；**引用类型暂不支持作集合元素**（报 MIRAI308）。恢复为**替换语义**（读回新容器实例）；null 与空集合严格区分；`Stack<T>` 按栈顶→栈底写出、恢复逆序压栈还原 LIFO。
- **嵌套数据类**：标注 `[SaveData]` 的非 MonoBehaviour class，捕获其**全部 public 实例字段**（键 = 字段名，对齐 JSON 惯例；Key/Version 参数在嵌套语境不使用）。循环引用/抽象类/无可访问无参构造/struct 报 MIRAI307；`SaveDataBlock` 子类不能作嵌套字段（走手动轨块 API）。
- **场景对象引用**（GameObject/Component 派生字段）：捕获存目标 `SaveObjectIdentity` 的**稳定 ID**（编辑器期 OnValidate 烘焙 GUID，空则自动赋值），恢复经 `SaveEntityRegistry` 反查当前场景内同 ID 对象（Component 字段经 `GetComponent<T>` 解析）。目标未挂 SaveObjectIdentity 时捕获写 Null 并记告警；档内 ID 在当前场景不存在时恢复为 null 并记告警。复制物体（Ctrl+D）会连 ID 拷贝——重复 ID 运行期首到先得并记告警，清空 ID 字段可重新烘焙。每个场景引用字段生成 MIRAI305 Info 指引。
- **资产引用**（Texture/SO/Material 等其余 UnityEngine.Object 派生字段）：捕获经 `SaveServiceSettings.m_AssetCatalog`（SaveAssetCatalog SO）查 object → ResourceService 定位串写入；恢复按定位串经同一目录反查资产——**目录制双向解析，不触发运行时加载**（保持捕获器同步契约、零租约负担）。被引用资产须先登记入册；未登记/无目录时捕获写 Null 并记告警。声明为 `UnityEngine.Object` 基类的字段无法区分场景/资产，报 MIRAI306 且不参与捕获。程序化修改目录条目（编辑器工具/导入器直改 `m_Entries`）后必须调用 `SaveAssetCatalog.InvalidateLookup()`——查找表延迟构建，缓存不失效既看不到新条目也无法拦截重复登记。

### 生成器诊断

MIRAI300 字段类型不支持；MIRAI301 存档键重复；MIRAI302 迁移器无法自注册；MIRAI303 所在类型及其嵌套外层链须均为 partial class；MIRAI304 需实例字段；MIRAI305 场景引用需 SaveObjectIdentity 指引（Info）；MIRAI306 引用类型声明为 UnityEngine.Object 基类（Warning，字段跳过）；MIRAI307 嵌套数据类型无效；MIRAI308 集合元素/映射键值类型不支持。修改生成器源码（`SourceGenerators/Source~/SaveHost/`）后必须 `dotnet build -c Release -t:Rebuild` 强制全量重建 `SourceGenerators/SaveHost.dll`（增量构建在输入缓存未过期时会跳过编译产出残缺 DLL）。

### 内置捕获器（引擎组件）

引擎组件无 `[SaveField]` 标注——框架内置手写捕获器，勾选清单来自捕获器字段表（Inspector 无 [SaveField] 字段时自动回退展示）：

| 组件 | 字段 | 说明 |
|---|---|---|
| `Transform` | `localPosition` / `localRotation` / `localScale` | 局部空间——实体父子接线后局部坐标天然正确 |
| `Rigidbody` | `linearVelocity` / `angularVelocity` | 物理运动态持久化；恢复仅作用于非运动学刚体（运动学速度由动画/脚本驱动，写回无物理意义） |
| `ParticleSystem` | `time` | 粒子播放进度；恢复直接写 `ParticleSystem.time`，播放态时方有视觉效果 |

## 动态实体持久化（预制体差分）

运行期经预制体生成的物体（怪物/掉落物/临时建筑等）按「生成表 + 每实体差分块」持久化：

1. **登记预制体**：创建 `SavePrefabRegistry` SO（Create → Moirai → Save Prefab Registry），为每个可持久化预制体登记**稳定键**（存档内引用标识，改名即毁档）与 **ResourceService 定位串**（YooAsset 地址），并在存档设置 `m_PrefabRegistry` 引用。
2. **实体根挂 SaveComponent 并勾选字段**（含 Transform 内置捕获器字段以持久化位置/旋转/缩放）。
3. 游戏代码用持久化生成/销毁对替代 `Instantiate`/`Destroy`：

```csharp
// 生成（登记进会话生成表；未激活临时父技巧——先注入稳定 ID/块键，就位后激活，Awake 即见最终状态）
GameObject goblin = SaveService.InstantiatePersistent("goblin", pos, rot);
// 销毁（动态实体移出生成表→数据块下次保存时清理；场景预置对象记入销毁表→恢复时销毁）
SaveService.DestroyPersistent(goblin);
// 保存/恢复（恢复 = DestroyUnwanted → SpawnMissing → 父子接线 → RestoreAll → 激活 + EntityRestored 事件）
await SaveService.SaveEntitiesAsync("slot1");
await SaveService.RestoreEntitiesAsync("slot1");
```

- **模板差分**：实体捕获与预制体模板基准（每稳定键会话级缓存一份基准 KVT）逐字段比对，**只写相对模板的变动字段**（嵌套对象递归差分；集合任一变动整条携带，元素级差分为 v2 范围）；恢复 = 实例化（天然模板默认值）+ 应用差分，存档增量最小化。基准不可用（预制体未登记/无根 SaveComponent）时退化为全量写入。
- **块布局**：实体表 = 保留块 `__entities`（生成记录 EntityId/PrefabKey/SceneName/ParentId + 预置对象销毁 ID）；实体数据 = 每实体一个 `entity:{EntityId}` 块（实体组件块键由管线在激活前改写）。**组件存取 API 跳过 `entity:` 前缀块**——完整世界存取 = `SaveEntitiesAsync` + `SaveComponentsAsync`，恢复 = `RestoreEntitiesAsync` + `LoadComponentsAsync`（顺序：先实体后组件）。
- **CarryForward 语义**：保存仅 upsert 活跃实体，未访问场景与生成失败实体的块原样滞留；绕过 `DestroyPersistent` 直接 `Object.Destroy` 的实体，其记录与块同样滞留（须走显式销毁移除）。恢复后会话生成/销毁表以档案状态整体替换。
- **增量保存**：会话级脏跟踪（按档键控基准 = 实体表字节 + 逐实体差分载荷 + 档写入时间）——均未变化时零 IO 跳过（无事件）；有变化时单趟合并（读档 → 自愈迁移 → 删陈旧 → upsert → 原子写回）仅写脏块（仅变化块触发 `BlockSaved`）；基准失效（首次保存 / 恢复后 / 档被外部改写——写入时间守卫经 `FileInfo.Refresh` 新鲜元数据判定，防 NTFS 缓存滞后误判）保守走全量合并（孤儿清理由读档后解析）。
- **线程归属**：实体/组件外观异步 API 的 IO 在工作线程，读档续体返回后先切回主线程再执行场景操作与字段写回（Unity API 恒主线程）。
- **父子与场景落位**：生成记录的 ParentId（父级须挂 SaveObjectIdentity，否则父子关系不持久化并记告警）在恢复第二轮接线；SceneName 场景已加载则落位其中，否则落位活跃场景并记告警——**落位回落不改写场景归属**（生成表保持档案原 SceneName，下次会话场景加载时实体回到原场景，不漂移）。稳定 ID 查询走 `SaveEntityRegistry`——场景作用域表（场景卸载清扫）+ 全局作用域表（DontDestroyOnLoad 对象常驻）双表。
- **恢复时序**：生成保持未激活直到差分块写回完成——Awake/OnEnable 即见最终父级与恢复后字段值（游戏逻辑须读档后状态时监听 `EntityRestored` 事件或在 Start 之后）。差分块损坏的实体记错误日志并按模板默认恢复（不阻断其它实体）。
- **降级契约**：`InstantiatePersistent`/`DestroyPersistent` 不依赖存档处理器（注册表与资源服务可用即可），预制体键未登记/加载失败记错误日志返回 `null`；`SaveEntitiesAsync` 在处理器未就绪时抛 `GameException`；`RestoreEntitiesAsync` 静默降级为空任务。
- **预制体资产不烘焙 ID**：`SaveObjectIdentity.OnValidate` 跳过预制体资产本体（资产上的 ID 会被全部实例共享而必然撞键）；场景内实例仍各自烘焙，动态实体由生成管线在激活前注入每实例唯一 ID。

| API（实体分部） | 说明 |
|---|---|
| `InstantiatePersistent(prefabKey, position, rotation, parent)` | 同步生成持久化实体（模板经 ResourceService 加载；失败返回 `null`） |
| `InstantiatePersistentAsync(prefabKey, position, rotation, parent, ct)` | 异步生成（取消时返回 `null`） |
| `DestroyPersistent(target)` | 销毁并登记语义（动态实体移表 / 预置对象记销毁表 / 无身份物体仅销毁） |
| `SaveEntitiesAsync(fileName, folderName, ct)` | 写入实体表与全部活跃实体差分块（预热基准→差分捕获→增量判定→单趟合并；零变化零 IO 跳过；失败抛 `GameException`） |
| `RestoreEntitiesAsync(fileName, folderName, ct)` | 按档案状态整体重建实体（DestroyUnwanted→SpawnMissing→RestoreAll；逐只触发 `EntityRestored`） |

## 截图与元数据镜像

存档槽位缩略图管线：帧末捕获屏幕（`ScreenCapture.CaptureScreenshotAsTexture`，**仅运行态主线程**）→ GPU Blit 降采样到小尺寸 RenderTexture + 小图回读（保纵横比、不放大，最长边经 `m_ScreenshotMaxDimension` 配置，默认 256；规避 4K 全尺寸 CPU 滤波与回读卡顿）→ 主线程 PNG 编码一次 → 经存储层原子写 sidecar `{存档基名}.screenshot.png`（与存档同目录；云存储后端天然跟随）。

- **元数据镜像**：截图成功后镜像保留块 `__meta`——`ThumbnailFileName`（sidecar 文件名）与 `SceneName`（活动场景名）由管线自动填充；`PlayTimeTicks`（`TimeSpan` ticks）由游戏层按自身累计口径写入。既有元数据损坏时不覆盖（记告警并跳过镜像，保留抢救空间）。
- **保存联动**：`m_CaptureScreenshotOnSave` 开启后，`SaveBlockAsync` / `SaveComponentsAsync` 成功即自动捕获（保留块 `__` 前缀豁免——元数据镜像回写不递归）；联动失败不回传保存结果，联动取消不外溢到保存方令牌。
- **生命周期级联**：`DeleteSave` / `DeleteSaveAsync` 删除存档时级联删除 sidecar（防止同名新档复活陈旧缩略图）；目录级删除天然覆盖。
- **降级契约**：处理器未就绪返回 `HandlerNotReady`；非运行态/批处理模式返回 `NotSupported`（记告警）；sidecar 写入失败返回 `IoFailed` 并记错误日志（不上抛）。截图成功派发 `ScreenshotCaptured` 事件。

| API（截图分部） | 说明 |
|---|---|
| `CaptureScreenshotAsync(fileName, folderName, ct)` → `SaveError` | 捕获截图 + 写 sidecar + 镜像元数据 + 派发事件（仅运行态主线程） |

## 云存档一体（本地镜像 + 远端 KV）

`CloudSaveStorageBackend`（存储后端插拔件，经处理器的 `m_StorageBackend` 配置）：本地文件镜像 + 远端 KV 双写，读按冲突策略裁决。远端 KV 语义抽象为 `CloudSaveKvStore`（[SerializeReference] 插拔件）——框架内置两个具体后端（**`RestCloudSaveKvStore`** 自定义 REST / **`UnityCloudSaveKvStore`** UGS 条件编译），项目亦可实现自定义后端。

- **键规范**：云端键 = 相对存档数据根目录（`persistentDataPath/Data/`）的路径，`/` 分隔（如 `Save/slot1.sav`）——不携带本机目录结构，跨设备一致。
- **错误语义**：远端不可达/失败/未登录一律抛异常，后端归一为**离线降级**（降级本地镜像直通并记告警）；缺档非错误（读 `null` / 存在性 `false` / 删除幂等）。
- **REST 后端（`RestCloudSaveKvStore`）**：配置端点根地址 / 键前缀（多租户命名空间）/ 认证头 / 超时；认证值经代码注入的动态令牌提供方优先于静态配置（防密钥随设置资产入库）。REST 契约：`GET/HEAD/PUT/DELETE {baseUrl}/{keyPrefix}{key}`（路径段逐段 URL 转义；读 404 = 缺档）+ `GET {baseUrl}?prefix=` 枚举下推（JSON 信封 `[{"key","size","modified","revision"}]`，返回键须含键前缀——本端剥离并二次校验，前缀外键防御性跳过）。修订号双通道（`X-Save-Revision` 首选 / `ETag` 回退，均无 = 0 回退时间戳裁决）；远端时间戳 `Last-Modified` → 响应 `Date` 回退链；超时归一 `TimeoutException`、非约定状态码归一 `IOException`。HttpClient 纯 .NET 传输（任意线程可调；**WebGL 无 raw socket 不可用**，WebGL 项目选 UGS 或 UnityWebRequest 自定义后端）。
- **UGS 后端（`UnityCloudSaveKvStore`）**：安装 `com.unity.services.cloudsave` 后经 versionDefine（`UNITY_CLOUD_SAVE_INSTALLED`）自动激活。前置 `UnityServices.InitializeAsync()` + 玩家登录（Authentication），未就绪抛异常走离线降级；缺档经 `CloudSaveException.Reason == NotFound` 判 null/false/幂等。Player Files API 承载（配额：每玩家 200 文件 / 1GB）；WriteLock 为 etag 语义字符串（非单调数值）——`WriteAsync` 恒返回 0，裁决走 `FileItem.Modified` 远端权威时间戳通道；SDK 不接收取消令牌（调用前协作式检查，已发出的请求无法中止）。
- **写双发**：本地镜像原子提交后远端跟随；远端失败**不阻断本地提交**——记入待回传集合，下次远端操作成功时 backfill 重放（待传上传 / 待删单键 / 待删前缀，按序弹出，失败即停余项保留）。镜像独有回传有失败冷却（默认 30s，冷却期内读路径不重试，积压由 backfill 兜底）。
- **枚举下推**：`EnumerateAsync(prefix, ct)` 前缀重载——默认实现全量枚举后客户端过滤；支持服务端前缀过滤的后端覆写即降低流量（槽位枚举与前缀删除均经此通道）。
- **读裁决**（`ESaveSyncPolicy`）：`Latest` 新者优先 / `LocalWins` 本地权威 / `CloudWins` 云端权威 / `Custom` 逐键委托 `SaveSyncConflictResolver`（未配置回退 Latest 并记告警；裁决条目携带双方尺寸与修订号——枚举路径远端尺寸取清单 SizeBytes，不从空载荷推导）。**裁决去时钟化**：远端提供单调修订号（`CloudKvEntry.Version` > 0）时按版本号裁决——镜像已同步修订号记录于 `{file}.cloudver` sidecar，镜像脏判定用镜像与 sidecar 的本地 mtime 失配（同一本地时钟，客户端与远端时钟偏移不参与）；无版本号后端回退时间戳比较（下载已把远端权威时间戳转写镜像）。单侧存在时自动对齐另一侧（远端独有 → 下载刷新镜像并保留远端时间戳与版本；镜像独有 → 回传补传远端）。`WriteAsync` 返回远端分配的修订号（0 = 后端不提供版本号）。
- **同步原语仅作用本地镜像**（同步裸名 API 不见远端；远端同步由异步 API 族驱动）；单槽备份（`.bak`）为本地概念不随云同步；目录级删除对远端按前缀尽力删除。
- **能力声明**：`Capabilities.SupportsTrueAsyncIO = true`（远端网络 IO 为真异步）；`SyncReadsAuthoritative = false`（同步读非权威——同步原语只读本地镜像不见远端，外观裸名同步读在本后端上记一次性告警，引导改用异步 API 获取裁决结果）；WebGL 等平台同步读不可用的约束不适用于本后端——同步 API 只读本地镜像恒可用。

## 工具链（调试器与编辑器）

- **游戏内调试器窗口** `Profiler/Save`（`SaveServiceDebuggerWindow`，`SaveService.OnInit` 自动注册）：管线状态（处理器/存储后端/压缩/默认后端/截图开关）、槽位清单与选中槽位详情（块表、元数据、坏块红色高亮、截图 sidecar 状态）。文件夹/槽位选择控件常驻，数据区 1s 节流重建。
- **存档浏览器编辑器窗口**（`Window/Moirai/Save Browser`）：浏览 `persistentDataPath/Data/` 下文件夹与槽位；块表（键/版本/后端/大小/逐块错误）；未加密档内容预览（JSON 块原文美化 / KVT 块**结构化树预览**（嵌套对象/集合/映射缩进展开，解析失败回退十六进制采样）/ 其余后端十六进制采样）；备份/恢复备份/删除（含截图 sidecar 级联）/打开目录。编辑器经设置的存档处理器读取（解密链/存储后端/压缩配置与运行时一致）——加密档照常预览；密钥材料不匹配的档按坏块/不可读呈现。
- **资产引用收集器**（`Tools/Moirai/Save/Collect Asset References into Catalog`）：扫描已打开场景的 SaveComponent，把资产引用字段当前引用的项目资产登记进 `SaveAssetCatalog`（定位串按文件名寻址约定推导，自定义寻址项目须复核；场景对象实例仅告警）——消除「漏登记 → 捕获写 Null」面。同一次扫描内同一资产只登记一次（本地去重集，不依赖目录延迟缓存）；有新增时自动 `InvalidateLookup` 并保存资产。
- **SaveComponentEditor 补强**：字段勾选清单标注引用类别（场景引用 = GameObject/Component 派生字段，资产引用 = 其余 UnityEngine.Object 字段）并给出 Identity/Catalog 配置提示；每个绑定显示模式版本（SG 发射值优先 → `[SaveComponentSchema]` 声明 → 缺省 1）。

## 公共 API（静态外观）

### 版本迁移

| API | 说明 |
|---|---|
| `CurrentSaveVersion { get; set; }` | 当前存档数据版本（int 递增；启动期主线程设置，默认 0 = 迁移总线未激活） |
| `RegisterMigrator(ISaveMigrator)` | 手动注册迁移器（自注册之外的补充通道；非法版本边抛 `ArgumentException`） |
| `MigrateSave(fileName, folderName)` / `MigrateSaveAsync(...)` | 显式迁移整档到当前版本（迁移成功强制回写；缺档 `FileNotFound`，未就绪 `HandlerNotReady`） |

### 块级（主体）

| API | 说明 |
|---|---|
| `SaveBlockAsync<T>(data, fileName, key, folderName, ct)` | 读-改-写合并块（原子替换；同文件写串行排队） |
| `LoadBlockAsync<T>(fileName, key, folderName, ct)` | 读块，失败返回 default |
| `TryLoadBlockAsync<T>(...)` → `SaveResult<T>` | 错误判别（FileNotFound/Corrupted/IntegrityCheckFailed/UnsupportedVersion…） |
| `DeleteBlockAsync(fileName, key, folderName, ct)` | 删块（最后一块删除时整档移除） |
| `GetBlockInfos(fileName, folderName)` | 块元信息枚举（键/版本/后端/大小/逐块错误分型；坏块列入，`Error != None` 时框架字段仅 `HasMetadata` 为真可信） |
| 同步对 `SaveBlock` / `LoadBlock` / `TryLoadBlock` / `DeleteBlock` | 主线程阻塞版（退出前落盘等场景） |

### 便捷（单对象快速通道，映射保留块 `__main__`）

`SaveAsync<T>` / `LoadAsync<T>` / `TryLoadAsync<T>` / `Save` / `Load` / `TryLoad`——单对象读写快捷入口。

### 元数据 / 槽位 / 备份

| API | 说明 |
|---|---|
| `SaveMetadataAsync` / `TryLoadMetadataAsync`（+同步对） | 槽位元数据（保留块 `__meta`，JSON 后端） |
| `GetSaveFiles(folderName)` / `GetSaveFilesAsync` | 槽位枚举（按时间倒序） |
| `FileExists` / `DetermineSavePath` | 查询与路径 |
| `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles`（+Async 对） | 删除（退避重试；单档删除级联截图 sidecar） |
| `TryDeleteSave` / `TryDeleteSaveFolder` / `TryDeleteAllSaveFiles`（+Async 对） | 带存在性判别的删除（返回 bool——`true` = 存在并已删除；`false` = 目标本不存在或处理器未就绪，未发生删除；事件行为与 void 族一致） |
| `CreateBackup` / `RestoreBackup` | 单槽 `.bak` 备份/恢复（原子替换） |

### 降级契约（处理器未就绪）

写路径（`SaveBlock`/`SaveBlockAsync`/`SaveComponentsAsync`/`SaveEntitiesAsync`/`SaveMetadata`/`SaveMetadataAsync`）**抛 `GameException`**——不静默丢档；读返回 default；`TryLoad*` 返回 `Failure(HandlerNotReady)`；删除/枚举 no-op 或空数组；`TryDelete*` 族返回 `false`。

### 事件（SaveService.Events）

静态事件（默认零开销通道）+ `EventManager` 桥事件（`SaveSlotChangedEvent` 等，订阅侧二选一）。全部主线程派发：主线程操作内联直发（零闭包），异步操作工作线程完成后经 `MainThreadDispatcher.Post<TState>` 状态化入队（池化工作项 + 静态 lambda 缓存——稳态零分配）。`OnShutdown` 不清理订阅者——订阅方自行退订；调试可用 `SaveService.UnsubscribeAll()` 一键清空。参数均为只读值类型（≤32B）。

| 静态事件 | 桥事件 | 时机 |
|---|---|---|
| `SlotChanged` | `SaveSlotChangedEvent` | 槽位写入（`Saved` 创建/更新合并语义）/删除/备份创建/备份恢复；目录级批量删除 `FileName` 为 null |
| `BlockSaved` / `BlockDeleted` | `SaveBlockChangedEvent` | 块保存/删除完成（fileName+key+后端+字节数）；幂等空删不触发 |
| `SaveProgress` / `LoadProgress` | `SaveProgressEvent` | 组件存取按批回报（每 8 个一批 + 最终必报；`ShouldReportProgress`） |
| `SaveFailed` / `LoadFailed` | `SaveFailedEvent` | 失败（`ESaveFailureStage` 阶段 + `SaveError`）；写路径同时 fail-fast 上抛 `GameException`；缺档/无块（FileNotFound）不触发 |
| `EntityRestored` | `SaveEntityRestoredEvent` | `RestoreEntitiesAsync` 恢复管线逐只实体触发（激活后；参数 = 实体 ID + 预制体键 + 实例） |
| `ScreenshotCaptured` | `SaveScreenshotEvent` | 截图管线成功完成（fileName+sidecar 文件名+缩略图宽高） |

## 配置（SaveServiceSettings）

| 字段 | 说明 |
|---|---|
| `m_SaveServiceHandler` | 存储管线处理器（PlainSaveHandler / AESEncryptedSaveHandler；存储后端/压缩提供方/迁移回写与密钥提供方均内嵌在处理器上配置——见下表） |
| `m_DefaultBackend` | 默认序列化后端（未声明 `[SaveData]` 的块） |
| `m_SaveFileExtension` | 存档文件扩展名（默认 `.sav`） |
| `m_AssetCatalog` | 资产引用目录（SaveAssetCatalog SO）：无代码保存的资产引用字段经目录双向解析定位串；空 = 资产引用字段捕获恒写 Null |
| `m_PrefabRegistry` | 预制体注册表（SavePrefabRegistry SO）：可持久化动态实体登记（稳定键 → ResourceService 定位串）；空 = `InstantiatePersistent` 不可用、实体生成记录恢复按未登记键跳过 |
| `m_CaptureScreenshotOnSave` | 保存时截图（默认关）：`SaveBlockAsync`/`SaveComponentsAsync` 成功后自动捕获截图并镜像 sidecar 与元数据（仅运行态主线程生效） |
| `m_ScreenshotMaxDimension` | 截图缩略图最长边（像素，保纵横比不放大，默认 256） |

### 处理器内嵌配置（SaveServiceHandler）

存储管线相关配置内聚于处理器实例（随处理器类型一并替换）：

| 字段 | 说明 |
|---|---|
| `m_StorageBackend` | 存储后端（IO 下沉目标，默认 FileSaveStorageBackend；置空回退文件后端；云存档选 `CloudSaveStorageBackend`——组合远端 KV 插拔件（内置 `RestCloudSaveKvStore` 自定义 REST / `UnityCloudSaveKvStore` UGS 条件编译）+ 冲突策略 + 自定义裁决器） |
| `m_CompressionProvider` | 压缩提供方（空 = 不压缩；内置 GZipCompressionProvider） |
| `m_MigrationWriteBack` | 迁移回写（默认开）：加载触发迁移成功后惰性回写存档；关闭则迁移仅作用于当次加载的内存数据 |
| `m_KeyProvider`（AES 处理器） | 密钥提供方（空 = 回退 `StaticSaveKeyProvider.Default` 占位默认；可选 StaticSaveKeyProvider / PassphraseSaveKeyProvider / HKDFPerUserSaveKeyProvider） |

## 依赖

| 包 | 版本 | 说明 |
|---|---|---|
| MessagePack | 3.1.8 | NuGetForUnity 引入；运行时 DLL 自动引用，分析器 DLL 需 `RoslynAnalyzer` 标签 |
| protobuf-net | 3.3.8 | 同上（+Core 内嵌 BuildTools SG） |
| MemoryPack | 1.21.4 | 同上 |
| Unity Cloud Save | 可选 | 安装 `com.unity.services.cloudsave` 后经 versionDefine（`UNITY_CLOUD_SAVE_INSTALLED`）激活 `UnityCloudSaveKvStore` |
| LZ4（K4os.Compression.LZ4） | 预留 | versionDefine 槽位 `LZ4_INSTALLED` 已预留（安装 `org.nuget.k4os.compression.lz4` 激活）；压缩提供方实现待后续补全 |

缺 DLL 时对应后端在 `SaveSerializerRegistry.GetRequired` fail-fast。

## 测试

目录：`Tests/EditorMode/Service/Save/`

| 测试类 | 覆盖点 |
|---|---|
| `SaveFileContainerTests` | 容器往返、坏块跳过、结构性前缀保留、v1 硬切 |
| `SaveContainerV2Tests` | 头 CRC 重算放行的部分恢复、整档拒绝、坏块列报、写回收留 |
| `SaveEventTests` | 事件触发时机/次数/参数、失败阶段分型、后台派发主线程化、进度批次 |
| `SaveServiceHandlerTests` | 原子写、孤儿清扫、损坏分型、参数校验、便捷映射与降级、RawBlocks 往返 |
| `FileSaveStorageBackendTests` | 原子写、幂等删除、精确枚举、备份恢复、能力自描述 |
| `SaveCompressionTests` | GZip 往返、压+加组合、未压缩档兼容读、头部分型、注册表 |
| `SaveKeyProviderTests` | 静态派生、口令注入、HKDF 按用户隔离 |
| `AesEncryptedSaveHandlerTests` | 加密全链路 |
| `SaveEncryptorTests` | AES/HMAC 机件 |
| `SaveMigrationAndBackendTests` | 四后端往返、块级版本迁移级联 |
| `SaveMigrationBusTests` | 版本链、改名改型、整块变换、回写开关、审计、写入自愈、显式迁移 |
| `SaveCapturerTests` / `SaveCapturerV2Tests` | 组件捕获器：字段增删、集合/嵌套/场景引用/资产引用矩阵 |
| `SaveSerializerRegistryTests` | 注册校验、重复 fail-fast、保留标识、注销 |
| `SaveKeyValueElementTests` | KVT 元素级：序列/映射/嵌套、null、类型不符、缓冲区边界 |
| `SaveObjectIdentityTests` | 注册/注销、空 ID、重复 ID 首到先得、销毁失效、Resolve |
| `SaveAssetCatalogTests` | 资产目录双向查找、类型不符、重复首到先得、缓存失效、InvalidateLookup 程序化契约 |
| `SaveKvDifferTests` | 模板差分：标量/嵌套/集合/新增/类型漂移/体积收缩/坏档 |
| `SaveEntityTableTests` | 实体表往返、空表、可空字段、未知记录容错 |
| `SaveEntityPersistenceTests` | 动态实体闭环、差分、销毁标记、父子接线、EntityRestored |
| `SaveEntityIncrementalTests` | 实体增量保存闭环：零变化跳过、仅脏块写回、销毁清陈旧、外部改写/恢复后走全量 |
| `RestCloudSaveKvStoreTests` | REST 后端：读写往返（修订号/时间戳）、存在探测、幂等删除、前缀枚举、认证头优先级、远端失败/超时归一、ETag 回退 |
| `SaveBuiltInCapturerTests` | Transform / Rigidbody / ParticleSystem 内置捕获器 |
| `SaveScreenshotTests` | sidecar、缩略图、PNG 编码、元数据镜像、级联删除、非运行态降级 |
| `SaveCloudStorageBackendTests` | 写双发、读策略矩阵、单侧对齐、离线降级、backfill 重放 |
| `SaveBlockComposerTests` | 块组合器 |
| `SaveV3HardeningTests` | 加固路径回归 |

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [Resource](Resource.md) · [Core](Core.md)
