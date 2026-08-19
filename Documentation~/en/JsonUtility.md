# JsonUtility

> Framework JSON serialization/deserialization facade, providing a pluggable Handler architecture with support for both string and byte paths.

`JsonUtility` is the static facade for JSON processing in the framework. By default, it uses the self-developed `DefaultJsonHandler`, which supports reflection-based serialization, private field marking, custom name renaming, and pre-serialization/post-deserialization callbacks. When the `Handler` implements `IBufferJsonHandler`, `ToJsonBytes` / `ToObject(byte[])` uses the fast byte path, bypassing the intermediate string representation.

## Core Features

- Pluggable Handler: `DefaultJsonHandler` (default, self-developed reflection-based) / `NewtonsoftJsonHandler` (Newtonsoft.Json), switchable globally via `JsonUtility.Handler`
- Fast byte path: `IBufferJsonHandler` interface, skips UTF16/UTF8 transcoding in IO/network scenarios
- Attribute control: `[JsonSerialize]` / `[JsonDoNotSerialize]` / `[JsonSerializeAs("name")]` to control field serialization
- Lifecycle callbacks: `[JsonBeforeSerialization]` / `[JsonAfterDeserialization]` method attributes
- Type exclusion: `UnityEngine.Object` derived types and `UnityEvent` are automatically skipped
- Formatted output: `FormatJson(string)` with indentation and line breaks (pooled StringBuilder)

## Core Types

Namespace: `Moirai.Atropos`

| Class/Interface | Description |
|---------|------|
| `JsonUtility` | Static facade, providing `ToJson` / `ToObject` / `ToJsonBytes` / `FormatJson` methods |
| `JsonHandler` | Abstract base class, defining `ToJson` / `ToObject<T>` / `ToObject(Type)` / `FromJsonOverwrite` |
| `IBufferJsonHandler` | Byte path interface, optional implementation, providing `ToJsonBytes` / `ToObject<T>(byte[])` / `ToObject(Type, byte[])` |
| `DefaultJsonHandler` | Default implementation, self-developed reflection-based JSON serializer |
| `NewtonsoftJsonHandler` | Newtonsoft.Json adapter implementation |
| `JsonPropertyAttribute` | Base class for property attributes, controlling serializable/deserializable/serialization name |
| `JsonSerializeAttribute` | Marks private fields/properties as serializable |
| `JsonDoNotSerializeAttribute` | Marks public fields/properties as not serializable |
| `JsonSerializeAsAttribute` | Specifies a custom name for serialization |
| `JsonBeforeSerializationAttribute` | Marks a method to be called before serialization |
| `JsonAfterDeserializationAttribute` | Marks a method to be called after deserialization |

## Quick Start

```csharp
// Serialize to string
string json = JsonUtility.ToJson(playerData, prettyPrint: true);

// Deserialize to object
var data = JsonUtility.ToObject<PlayerData>(json);

// Serialize to bytes (compact format, recommended for save/network scenarios)
byte[] bytes = JsonUtility.ToJsonBytes(playerData);

// Deserialize from bytes
var data = JsonUtility.ToObject<PlayerData>(bytes);

// Overwrite existing object
JsonUtility.FromJsonOverwrite(json, existingObject);

// Format JSON string
string formatted = JsonUtility.FormatJson(json);
```

## Advanced Usage

### Custom Attribute Control

```csharp
public class PlayerData
{
    [JsonSerialize] // Mark private field for serialization
    private int _id;

    [JsonDoNotSerialize] // Exclude public field
    public string TempCache;

    [JsonSerializeAs("player_name")] // Rename
    public string Name;

    [JsonBeforeSerialization]
    private void OnBeforeSerialize() { /* Preparation before serialization */ }

    [JsonAfterDeserialization]
    private void OnAfterDeserialize() { /* Repair after deserialization */ }
}
```

### Switching Handler

```csharp
// Switch to Newtonsoft.Json implementation
JsonUtility.Handler = new NewtonsoftJsonHandler();

// Restore default
JsonUtility.Handler = new DefaultJsonHandler();
```

### Byte Path (IBufferJsonHandler)

`DefaultJsonHandler` implements `IBufferJsonHandler`. `ToJsonBytes` directly produces UTF8 bytes, and deserialization directly consumes bytes, avoiding the UTF16/UTF8 transcoding allocation of the intermediate string. `NewtonsoftJsonHandler` does not implement this interface and falls back to the string path automatically.

## Notes

- Setting `Handler` to null is ignored and does not reset; assigning a new value automatically calls `Shutdown()` on the old handler and `OnInit()` on the new handler
- By default, types derived from `UnityEngine.Object` (GameObject/Component/Sprite/Texture/Material, etc.) and `UnityEvent` are not serialized; reflection-based serialization would reach native-side objects
- By default, properties must have both getter and setter (a round-trip symmetry contract); get-only computed properties are automatically excluded
- `FromJsonOverwrite` deserializes JSON data onto an existing object, overwriting its current data

---
[« Back to Main README](../../README_EN.md)