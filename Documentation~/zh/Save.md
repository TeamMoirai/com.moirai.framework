# Save

## 概述

Save 服务（`SaveService`）为游戏提供 AAA 级存档基础设施：**单文件多数据块**容器、**四种可插拔序列化后端**（JSON / MessagePack / MemoryPack / protobuf-net）、**块级版本迁移**、**AES 加密管线**与**无代码组件保存**（SourceGenerator 生成强类型捕获器）。命名空间 `Moirai.Atropos.Save`。

## 架构

```
SaveService（静态外观，s_Handler null 时静默降级）
├── 存储管线（[SerializeReference] 可切换）
│     PlainSaveHandler        明文直通
│     AesEncryptedSaveHandler AES-256-CBC + HMAC（encrypt-then-MAC）+ PBKDF2
├── 序列化后端（ESaveBackend + ISaveSerializer + SaveSerializerRegistry）
│     Json（内置，默认）/ MessagePack / MemoryPack / Protobuf / KeyValue（组件专用）
├── 多块容器（SaveFileContainer，手写二进制，块级 key/version/backend/bytes）
├── 数据模型（[SaveData] + SaveDataBlock.OnMigrate 版本迁移）
└── 无代码保存（[SaveField] + SaveComponent + SaveHost SourceGenerator 生成捕获器）
```

## 文件格式 v2

```
[32B 明文头 "MRSA"][载荷]
头：[4B 魔数][4B 格式版本=2][8B UTC ticks][4B 载荷长][4B 载荷 CRC32][4B 标志]
载荷 = Compress?(Container)；加密处理器下再包 [16B IV][AES-256-CBC][32B HMAC]
容器：[4B 魔数 "MRSB"][4B 容器版本][4B 块数]
      逐块 [4B 键字节长][键 UTF8][4B 模式版本][2B 后端][4B 载荷长][载荷]
```

- 文件头永远明文（不解密即可读保存时间）；CRC 防存储损坏、HMAC 防篡改（先验 MAC 后解密）
- v1（28B 头单块旧格式）读取判别为 `UnsupportedVersion` 作废（项目未上线裁定，不做兼容读）
- 原子写入：临时文件 `xxx.sav.tmp-{guid}` → `Flush(true)` → `File.Replace`；启动期后台清扫孤儿临时文件
- 同文件写路径经串行信号量排队（防并发读-改-写丢块）

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
    [SaveField("bag_items")] private List<int> _items;   // 集合元素/嵌套类为生成器后续版本扩展，当前报 MIRAI200
}
```

2. GameObject 挂 **Save Component**：Inspector 里添加目标组件绑定并勾选参与存档的字段（块键空缺时自动派生 `场景名:物体路径`）。
3. 运行期 `SaveService.SaveComponentsAsync(fileName)` / `LoadComponentsAsync(fileName)` 触发——编译期生成的强类型捕获器零反射捕获，按勾选掩码过滤；未知键跳过、缺失键保留当前值（字段增删天然向后兼容）。

生成器诊断：MIRAI200 类型不支持、MIRAI201 键重复、MIRAI203 需 partial class、MIRAI204 需实例字段。修改生成器源码（`SourceGenerators/Source~/SaveHost/`）后必须 `dotnet build -c Release` 重建 `SourceGenerators/SaveHost.dll`。

## 公共 API（静态外观）

### 块级（主体）

| API | 说明 |
|---|---|
| `SaveBlockAsync<T>(data, fileName, key, folderName, ct)` | 读-改-写合并块（原子替换；同文件写串行排队） |
| `LoadBlockAsync<T>(fileName, key, folderName, ct)` | 读块，失败返回 default |
| `TryLoadBlockAsync<T>(...)` → `SaveResult<T>` | 错误判别（FileNotFound/Corrupted/IntegrityCheckFailed/UnsupportedVersion…） |
| `DeleteBlockAsync(fileName, key, folderName, ct)` | 删块（最后一块删除时整档移除） |
| `GetBlockInfos(fileName, folderName)` | 块元信息枚举（键/版本/后端/大小） |
| 同步对 `SaveBlock` / `LoadBlock` / `TryLoadBlock` / `DeleteBlock` | 主线程阻塞版（退出前落盘等场景） |

### 兼容（旧单对象 API，映射保留块 `__main__`）

`SaveAsync<T>` / `LoadAsync<T>` / `TryLoadAsync<T>` / `Save` / `Load` / `TryLoad`——签名与 A+ 版一致。

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
| `m_DefaultBackend` | 默认序列化后端（未声明 `[SaveData]` 的块） |
| `m_EncryptionKey` / `m_Pbkdf2Iterations` | 加密参数（**SECURITY: 上线前必须替换占位密钥**；派生密钥按实例缓存） |
| `m_SaveFileExtension` | 存档文件扩展名（默认 `.sav`） |

## 依赖

MessagePack 3.1.8、protobuf-net 3.3.8（+Core 内嵌 BuildTools SG）、MemoryPack 1.21.4（NuGetForUnity 引入，运行时 DLL 自动引用；分析器 DLL 需 RoslynAnalyzer 标签）。缺 DLL 时对应后端在 `SaveSerializerRegistry.GetRequired` fail-fast。

## 测试

`Tests/EditorMode/Save/`：容器布局、组合器、Handler 管线（原子写/清扫/损坏分型/参数校验）、加密全链路、四后端往返、迁移级联、组件捕获器（生成代码）。
