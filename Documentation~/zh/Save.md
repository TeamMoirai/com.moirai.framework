# Save

## 概述

Save 服务（`SaveService`）为游戏提供 AAA 级存档基础设施：**单文件多数据块**容器、**四种可插拔序列化后端**（JSON / MessagePack / MemoryPack / protobuf-net）、**块级版本迁移**、**AES 加密管线**（密钥来源可插拔：静态口令 / 运行期口令注入 / HKDF 按用户派生）、**可选 GZip 压缩**与**无代码组件保存**（SourceGenerator 生成强类型捕获器）。命名空间 `Moirai.Atropos.Save`。

## 架构

```
SaveService（静态外观，s_Handler null 时静默降级）
├── 存储管线（[SerializeReference] 可切换）
│     PlainSaveHandler        明文直通
│     AesEncryptedSaveHandler AES-256-CBC + HMAC（encrypt-then-MAC），密钥经 ISaveKeyProvider 直给
├── 存储后端（[SerializeReference] 可切换，ISaveStorage + SaveStorageBackend）
│     FileSaveStorageBackend  本地文件（临时文件 + Flush(true) + 原子替换，默认）
├── 转换链（顺序固定：Serialize → Compress? → Encrypt? → CRC）
│     压缩：ICompressionProvider + SaveCompressionRegistry（GZip 内建 ID=1；未知 ID 读侧拒载）
│     密钥：ISaveKeyProvider + SaveKeyProvider（Static 静态口令默认 / Passphrase 运行期注入 / HkdfPerUser 按用户派生）
├── 序列化后端（ESaveBackend + ISaveSerializer + SaveSerializerRegistry）
│     Json（内置，默认）/ MessagePack / MemoryPack / Protobuf / KeyValue（组件专用）
├── 多块容器（SaveFileContainer，手写二进制，块级 key/version/backend/bytes）
├── 数据模型（[SaveData] + SaveDataBlock.OnMigrate 版本迁移）
└── 无代码保存（[SaveField] + SaveComponent + SaveHost SourceGenerator 生成捕获器）
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
- 密钥来源（`ISaveKeyProvider`）：静态口令 PBKDF2（`StaticSaveKeyProvider`，默认，与 V2 逐参一致）/ 运行期口令注入（`PassphraseSaveKeyProvider`，口令仅内存不落盘，未注入时读 `InvalidArgument`、写 fail-fast）/ HKDF-SHA256 按用户派生（`HkdfPerUserSaveKeyProvider`，多账号存档互相不可读）
- 原子写入：临时文件 `xxx.sav.tmp-{guid}` → `Flush(true)` → `File.Replace`（经 `FileSaveStorageBackend`）；启动期后台清扫孤儿临时文件
- 同文件写路径经串行信号量排队（防并发读-改-写丢块）——串行门在 Handler 编排层，存储后端无感知
- 存储层契约（`ISaveStorage`）：同步原语为契约核心（`Exists`/`TryReadAllBytes`/`WriteAtomic`/`DeleteFile`/`DeleteDirectory`/`EnumerateFiles`/`CreateBackup`/`RestoreBackup`），异步包装默认线程池卸载（真异步后端覆盖并声明 `Capabilities`）；读取错误分型返回、写入失败抛 `GameException`、删除幂等；实现必须纯 .NET（任意线程可调）

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

## 无代码保存（组件勾选字段）

1. 玩法组件声明为 `partial class`，字段标 `[SaveField]`（可选显式存档键，重命名字段时保键稳定）：

```csharp
public partial class Player : MonoBehaviour
{
    [SaveField] private int _hp;
    [SaveField("bag_items")] private List<int> _items;   // 集合元素/嵌套类为生成器后续版本扩展，当前报 MIRAI300
}
```

2. GameObject 挂 **Save Component**：Inspector 里添加目标组件绑定并勾选参与存档的字段（块键空缺时自动派生 `场景名:物体路径`；物体路径为场景根到物体的**完整名称链**，同名兄弟/同名场景根自动追加 `[N]` 序号消歧，杜绝跨分支撞键覆盖）。
3. 运行期 `SaveService.SaveComponentsAsync(fileName)` / `LoadComponentsAsync(fileName)` 触发——编译期生成的强类型捕获器零反射捕获，按勾选掩码过滤；未知键跳过、缺失键保留当前值（字段增删天然向后兼容）。

生成器诊断：MIRAI300 类型不支持、MIRAI301 键重复、MIRAI303 需 partial class、MIRAI304 需实例字段。修改生成器源码（`SourceGenerators/Source~/SaveHost/`）后必须 `dotnet build -c Release` 重建 `SourceGenerators/SaveHost.dll`。

## 公共 API（静态外观）

### 块级（主体）

| API | 说明 |
|---|---|
| `SaveBlockAsync<T>(data, fileName, key, folderName, ct)` | 读-改-写合并块（原子替换；同文件写串行排队） |
| `LoadBlockAsync<T>(fileName, key, folderName, ct)` | 读块，失败返回 default |
| `TryLoadBlockAsync<T>(...)` → `SaveResult<T>` | 错误判别（FileNotFound/Corrupted/IntegrityCheckFailed/UnsupportedVersion…） |
| `DeleteBlockAsync(fileName, key, folderName, ct)` | 删块（最后一块删除时整档移除） |
| `GetBlockInfos(fileName, folderName)` | 块元信息枚举（键/版本/后端/大小/逐块错误分型；坏块列入，`Error != None` 时框架字段仅 `HasMetadata` 为真可信） |
| 同步对 `SaveBlock` / `LoadBlock` / `TryLoadBlock` / `DeleteBlock` | 主线程阻塞版（退出前落盘等场景） |

### 兼容（旧单对象 API，映射保留块 `__main__`）

`SaveAsync<T>` / `LoadAsync<T>` / `TryLoadAsync<T>` / `Save` / `Load` / `TryLoad`——签名与 v1 版一致。

### 元数据 / 槽位 / 备份

| API | 说明 |
|---|---|
| `SaveMetadataAsync` / `TryLoadMetadataAsync`（+同步对） | 槽位元数据（保留块 `__meta`，JSON 后端） |
| `GetSaveFiles(folderName)` / `GetSaveFilesAsync` | 槽位枚举（按时间倒序） |
| `FileExists` / `DetermineSavePath` | 查询与路径 |
| `DeleteSave` / `DeleteSaveFolder` / `DeleteAllSaveFiles`（+Async 对） | 删除（退避重试） |
| `CreateBackup` / `RestoreBackup` | 单槽 `.bak` 备份/恢复（原子替换） |

### 降级契约（处理器未就绪）

写/删除 no-op；读返回 default；`TryLoad*` 返回 `Failure(HandlerNotReady)`；枚举返回空数组。

## 配置（SaveServiceSettings）

| 字段 | 说明 |
|---|---|
| `m_SaveServiceHandler` | 存储管线处理器（PlainSaveHandler / AesEncryptedSaveHandler） |
| `m_StorageBackend` | 存储后端（IO 下沉目标，默认 FileSaveStorageBackend；置空回退文件后端；云存档等继承 `SaveStorageBackend` 接入） |
| `m_CompressionProvider` | 压缩提供方（空 = 不压缩；内置 GZipCompressionProvider） |
| `m_KeyProvider` | 密钥提供方（空 = 静态密钥；可选 PassphraseSaveKeyProvider / HkdfPerUserSaveKeyProvider） |
| `m_DefaultBackend` | 默认序列化后端（未声明 `[SaveData]` 的块） |
| `m_EncryptionKey` / `m_Pbkdf2Iterations` | 静态密钥参数（**SECURITY: 上线前必须替换占位密钥**；仅在密钥提供方为空时生效；派生密钥按实例缓存） |
| `m_SaveFileExtension` | 存档文件扩展名（默认 `.sav`） |

## 依赖

MessagePack 3.1.8、protobuf-net 3.3.8（+Core 内嵌 BuildTools SG）、MemoryPack 1.21.4（NuGetForUnity 引入，运行时 DLL 自动引用；分析器 DLL 需 RoslynAnalyzer 标签）。缺 DLL 时对应后端在 `SaveSerializerRegistry.GetRequired` fail-fast。

## 测试

`Tests/EditorMode/Save/`：容器布局与 v2 逐块校验（`SaveFileContainerTests`：往返/坏块跳过/结构性前缀保留/v1 硬切、`SaveContainerV2Tests`：头 CRC 重算放行的部分恢复/整档拒绝/坏块列报/写回收留）、组合器、Handler 管线（原子写/清扫/损坏分型/参数校验）、存储后端契约（`FileSaveStorageBackendTests`：原子写/幂等删除/精确枚举/备份恢复/能力自描述）、压缩转换链（`SaveCompressionTests`：GZip 往返/压加组合/旧档兼容读/头部分型/注册表）、密钥提供方（`SaveKeyProviderTests`：静态等价/口令注入/HKDF 按用户隔离）、加密全链路、四后端往返、迁移级联、组件捕获器（生成代码）。
