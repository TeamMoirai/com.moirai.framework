# StringUtility

> 框架的字符串格式化与构建外观，提供可插拔的 Handler 架构，内置池化字符串构建器以减少 GC。

`StringUtility` 是框架的字符串处理静态外观。默认使用 `DefaultStringHandler`（基于 `StringBuilder` 池化）；当安装 ZString 时可切换为 `ZStringHandler`（基于 `Utf16ValueStringBuilder` 池化，零分配格式化）。提供 `Format`、`CreateStringBuilder`、`GetString` 三种使用模式，覆盖不同场景的字符串构建需求。

## 核心特性

- 可插拔 Handler：`DefaultStringHandler`（默认，StringBuilder 池化）/ `ZStringHandler`（ZString 零分配池化）
- 三种使用模式：便捷 `Format`、构建器模式 `CreateStringBuilder` + `ToStringAndDispose`、简化模式 `GetString`
- `Format` 泛型重载支持 1-16 个参数，`[StringFormatMethod]` 注解触发 Roslyn 静态分析
- `IStringBuilder` 统一接口：`StringBuilder` 与 `Utf16ValueStringBuilder` 的操作抽象
- 池化生命周期：`CreateStringBuilder` 返回的构建器使用后归还池，`Format` 内部自动管理

## 核心类型

命名空间：`Moirai.Atropos`

| 类/接口 | 说明 |
|---------|------|
| `StringUtility` | 静态外观，提供 `Format` / `CreateStringBuilder` / `GetString` / `Clear` |
| `StringHandler` | 抽象基类，定义 `CreateStringBuilder` / `GetString` / `Clear` |
| `StringHandler.IStringBuilder` | 字符串构建器适配器接口（partial），统一 `StringBuilder` 与 `Utf16ValueStringBuilder` 操作；Format 重载由 T4 模板生成 |
| `DefaultStringHandler` | 默认实现，基于 `StringBuilder` 池化 |
| `ZStringHandler` | ZString 实现，基于 `Utf16ValueStringBuilder` 池化，零分配 |

## 快速上手

```csharp
// 便捷格式化
string msg = StringUtility.Format("HP: {0}/{1}", hp, maxHp);

// 构建器模式（推荐高频场景）
var sb = StringUtility.CreateStringBuilder();
sb.Append("Hello ").Append(name);
string result = sb.ToStringAndDispose(); // 获取字符串并归还构建器

// 简化模式（自动管理生命周期）
string result = StringUtility.GetString(sb => {
    sb.Append("Hello ").Append(name);
});
```

## 进阶用法

### IStringBuilder 接口

`IStringBuilder` 支持丰富的 Append 重载：

```csharp
var sb = StringUtility.CreateStringBuilder();
sb.Append("value: ").Append(42).AppendLine();
sb.AppendFormat("Score: {0:F2}", score);
sb.Join(", ", names); // 使用分隔符连接数组
sb.Insert(0, "prefix");
sb.Replace("old", "new");
sb.Remove(0, 3);
sb.Clear(); // 清空后复用
```

### 格式化与连接

```csharp
// Format —— 类似 string.Format，但走池化构建器
StringUtility.Format("Name={0}, Level={1}, HP={2}/{3}", name, level, hp, maxHp);

// Concat —— 直接连接（静态快捷方法，自动管理池化构建器生命周期）
StringUtility.Concat("a", 1, true); // "a1True"

// Join —— 分隔符连接（静态快捷方法，自动管理池化构建器生命周期）
StringUtility.Join(", ", new[] { "a", "b", "c" }); // "a, b, c"
```

### 字符串操作（静态快捷方法）

`StringUtility` 提供 `Insert`、`Remove`、`Replace` 的静态便捷方法，接收源字符串，通过池化构建器执行操作后返回结果——无需手动管理构建器生命周期。

```csharp
// Insert —— 在源字符串中插入
StringUtility.Insert("Hello", 2, "XX");     // "HeXXllo"
StringUtility.Insert("Hello", 0, 'P');       // "PHello"
StringUtility.Insert("Hello", 2, "X", 3);    // "HeXXXllo"

// Remove —— 从源字符串中移除指定范围
StringUtility.Remove("Hello", 2, 2);         // "Heo"

// Replace —— 在源字符串中替换
StringUtility.Replace("Hello", 'l', 'x');            // "Hexxo"
StringUtility.Replace("Hello", "ll", "xx");          // "Hexxo"
StringUtility.Replace("Hello", 'l', 'x', 0, 3);       // "Hexlo"（指定范围）
StringUtility.Replace("Hello", "l", "x", 0, 3);      // "Hexlo"（指定范围）
```

所有操作方法在 `source` 为 null 时返回 `string.Empty`。

### 切换 Handler

```csharp
// 切换到 ZString（需安装 ZString 库）
StringUtility.Handler = new ZStringHandler();

// 恢复默认
StringUtility.Handler = new DefaultStringHandler();
```

## 注意事项

- `Format` 内部自动创建和归还 `IStringBuilder`，高频调用无内存泄漏
- `IStringBuilder` 使用后必须调用 `Dispose()` 或 `ToStringAndDispose()` 归还池，否则池泄漏
- `Handler` 赋 null 抛出 `ArgumentNullException`；赋新值时自动调用旧 handler 的 `Internal_Shutdown()` 和新 handler 的 `Internal_Init()`
- `Clear()` 清空所有缓存和池，通常在场景切换时调用

---
[« 返回主 README](../../README.md)