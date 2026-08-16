# StringUtility

> Framework string formatting and building facade, providing a pluggable Handler architecture with a pooled string builder to reduce GC.

`StringUtility` is the static facade for string processing in the framework. By default, it uses `DefaultStringHandler` (based on pooled `StringBuilder`); when ZString is installed, it can be switched to `ZStringHandler` (based on pooled `Utf16ValueStringBuilder`, zero-allocation formatting). It provides three usage modes: `Format`, `CreateStringBuilder`, and `GetString`, covering different string building scenarios.

## Core Features

- Pluggable Handler: `DefaultStringHandler` (default, pooled StringBuilder) / `ZStringHandler` (ZString zero-allocation pooling)
- Three usage modes: Convenient `Format`, builder mode `CreateStringBuilder` + `ToStringAndDispose`, simplified mode `GetString`
- `Format` generic overloads support 1-16 parameters, `[StringFormatMethod]` annotation triggers Roslyn static analysis
- `IStringBuilder` unified interface: abstracts operations for `StringBuilder` and `Utf16ValueStringBuilder`
- Pooling lifecycle: The builder returned by `CreateStringBuilder` is returned to the pool after use; `Format` manages this internally

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `StringUtility` | Static facade, providing `Format` / `CreateStringBuilder` / `GetString` / `Clear` |
| `StringHandler` | Abstract base class, defining `CreateStringBuilder` / `GetString` / `Clear` |
| `StringHandler.IStringBuilder` | String builder adapter interface, unifying `StringBuilder` and `Utf16ValueStringBuilder` operations |
| `DefaultStringHandler` | Default implementation, based on pooled `StringBuilder` |
| `ZStringHandler` | ZString implementation, based on pooled `Utf16ValueStringBuilder`, zero-allocation |

## Quick Start

```csharp
// Convenient formatting
string msg = StringUtility.Format("HP: {0}/{1}", hp, maxHp);

// Builder mode (recommended for high-frequency scenarios)
var sb = StringUtility.CreateStringBuilder();
sb.Append("Hello ").Append(name);
string result = sb.ToStringAndDispose(); // Get the string and return the builder to the pool

// Simplified mode (automatic lifecycle management)
string result = StringUtility.GetString(sb => {
    sb.Append("Hello ").Append(name);
});
```

## Advanced Usage

### IStringBuilder Interface

`IStringBuilder` supports a rich set of Append overloads:

```csharp
var sb = StringUtility.CreateStringBuilder();
sb.Append("value: ").Append(42).AppendLine();
sb.AppendFormat("Score: {0:F2}", score);
sb.Join(", ", names); // Join an array with a separator
sb.Insert(0, "prefix");
sb.Replace("old", "new");
sb.Remove(0, 3);
sb.Clear(); // Clear and reuse
```

### Formatting and Concatenation

```csharp
// Format -- similar to string.Format, but uses a pooled builder
StringUtility.Format("Name={0}, Level={1}, HP={2}/{3}", name, level, hp, maxHp);

// Concat -- direct concatenation
sb.Concat("a", 1, true); // "a1True"

// Join -- concatenation with a separator
sb.Join(", ", new[] { "a", "b", "c" }); // "a, b, c"
```

### Switching Handler

```csharp
// Switch to ZString (requires ZString library)
StringUtility.Handler = new ZStringHandler();

// Restore default
StringUtility.Handler = new DefaultStringHandler();
```

## Notes

- `Format` internally creates and returns the `IStringBuilder` automatically; no memory leaks in high-frequency calls
- `IStringBuilder` must be disposed via `Dispose()` or `ToStringAndDispose()` after use, otherwise the pool will leak
- Setting `Handler` to null is ignored; assigning a new value automatically calls `Shutdown()` on the old handler and `OnInit()` on the new handler
- `Clear()` clears all caches and pools, typically called during scene transitions

---
[« Back to Main README](../README_EN.md)