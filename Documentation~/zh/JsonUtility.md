# JsonUtility

> 框架的 JSON 序列化/反序列化外观，提供可插拔的 Handler 架构，支持字符串与字节两种通路。

`JsonUtility` 是框架的 JSON 处理静态外观。默认使用自研 `DefaultJsonHandler`，支持反射式序列化、私有字段标记、自定义名称重命名、序列化前/反序列化后回调。当 `Handler` 实现 `IBufferJsonHandler` 时，`ToJsonBytes` / `ToObject(byte[])` 走字节快速通路，跳过 string 中间态。

## 核心特性

- 可插拔 Handler：`DefaultJsonHandler`（默认，自研反射式）/ `NewtonsoftJsonHandler`（Newtonsoft.Json），通过 `JsonUtility.Handler` 全局切换
- 字节快速通路：`IBufferJsonHandler` 接口，IO/网络场景跳过 UTF16/UTF8 转码
- 属性控制：`[JsonSerialize]` / `[JsonDoNotSerialize]` / `[JsonSerializeAs("name")]` 控制字段序列化
- 生命周期回调：`[JsonBeforeSerialization]` / `[JsonAfterDeserialization]` 方法特性
- 成员筛选走允许列表：只有能写出也能读回的类型入档；非数据家族（`UnityEngine.Object` 派生、委托、`UnityEventBase` 派生）与不可往返的形态（非集合接口/抽象成员、多维数组、BCL 内部类型）自动跳过
- 格式化输出：`FormatJson(string)` 带缩进和换行的格式化方法（池化 StringBuilder）

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `JsonUtility` | 静态外观，提供 `ToJson` / `ToObject` / `ToJsonBytes` / `FormatJson` 等方法 |
| `JsonHandler` | 抽象基类，定义 `ToJson` / `ToObject<T>` / `ToObject(Type)` / `FromJsonOverwrite` |
| `IBufferJsonHandler` | 字节通路接口，可选实现，提供 `ToJsonBytes` / `ToObject<T>(byte[])` / `ToObject(Type, byte[])` |
| `DefaultJsonHandler` | 默认实现，自研反射式 JSON 序列化器 |
| `NewtonsoftJsonHandler` | Newtonsoft.Json 适配实现 |
| `JsonPropertyAttribute` | 属性特性基类，控制可序列化/可反序列化/序列化名称 |
| `JsonSerializeAttribute` | 标记私有字段/属性可以序列化 |
| `JsonDoNotSerializeAttribute` | 标记公开字段/属性不序列化 |
| `JsonSerializeAsAttribute` | 指定序列化时的重命名 |
| `JsonBeforeSerializationAttribute` | 标记序列化前调用的方法 |
| `JsonAfterDeserializationAttribute` | 标记反序列化后调用的方法 |

## 快速上手

```csharp
// 序列化为字符串
string json = JsonUtility.ToJson(playerData, prettyPrint: true);

// 反序列化为对象
var data = JsonUtility.ToObject<PlayerData>(json);

// 序列化为字节（紧凑格式，推荐存档/网络场景）
byte[] bytes = JsonUtility.ToJsonBytes(playerData);

// 从字节反序列化
var data = JsonUtility.ToObject<PlayerData>(bytes);

// 格式化 JSON 字符串
string formatted = JsonUtility.FormatJson(json);
```

## 进阶用法

### 自定义属性控制

```csharp
public class PlayerData
{
    [JsonSerialize] // 标记私有字段序列化
    private int _id;

    [JsonDoNotSerialize] // 排除公开字段
    public string TempCache;

    [JsonSerializeAs("player_name")] // 重命名
    public string Name;

    [JsonBeforeSerialization]
    private void OnBeforeSerialize() { /* 序列化前准备 */ }

    [JsonAfterDeserialization]
    private void OnAfterDeserialize() { /* 反序列化后修复 */ }
}
```

### 切换 Handler

```csharp
// 切换到 Newtonsoft.Json 实现
JsonUtility.Handler = new NewtonsoftJsonHandler();

// 恢复默认
JsonUtility.Handler = new DefaultJsonHandler();
```

### 字节通路（IBufferJsonHandler）

`DefaultJsonHandler` 实现了 `IBufferJsonHandler`，`ToJsonBytes` 直接产出 UTF8 字节，反序列化直接消费字节，避免 string 中间态的 UTF16/UTF8 转码分配。`NewtonsoftJsonHandler` 未实现该接口，自动回退 string 路径。

## 注意事项

- `Handler` 赋 null 抛出 `ArgumentNullException`；赋新值时自动调用旧 handler 的 `Internal_Shutdown()` 和新 handler 的 `Internal_Init()`
- 成员是否入档由 `JsonTypeSupport.IsSupportedMemberType` 判定（允许列表）：标量/枚举/`decimal`/`DateTime` 等 BCL 值类型、引擎与项目结构体（`Vector3`/`Color`/`Quaternion`…）、一维数组、`List`/`Dictionary`、以数组承载的集合（`HashSet`/`SortedSet`/`ISet`/`Queue`/`Stack`/`LinkedList` 与 `IList`/`ICollection`/`IEnumerable`/`IReadOnlyList`/`IReadOnlyCollection`，按枚举序往返）及项目 POCO 入选；`UnityEngine.Object` 派生、委托（含 C# 事件与 `Action`/`Func`）、`UnityEventBase` 派生（含泛型 `UnityEvent<T>`）、非集合的接口与抽象成员、多维数组、其余 BCL 具体类型（`StringBuilder`/`Tuple`/`Type`…）不入选
- 被排除的成员写侧不出现在 JSON、读侧按未知字段忽略，两侧对称；`[SerializeField]` 与 `[JsonSerialize]` 都不覆盖该判据（Unity 侧同样静默跳过不支持的字段类型）
- 类型自身没有任何可序列化成员仍然抛 `GameException`：允许列表只排除成员，不会让一个"看起来存了其实是 `{}`"的数据类静默通过
- 默认要求属性同时具备 get/set（读写兼备的往返对称契约），get-only 计算属性自动排除
- `JsonHandler.FromJsonOverwrite` 将 JSON 数据反序列化到现有对象上并覆盖现有数据

---
[« 返回文档索引](Index.md) · [主 README](../../README.md) · [StringUtility](StringUtility.md) · [Save](Save.md)
