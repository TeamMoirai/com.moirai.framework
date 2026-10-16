using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Moirai.Atropos
{
    /// <summary>
    /// 自定义的 JSON 序列化器。
    /// </summary>
    /// <remarks>
    /// 常驻框架、不随后端外置：<c>EventDebuggerEventRecord</c> 无守卫地按 <c>new CustomContractResolver()</c> 用它， <br />
    /// 而 <c>NewtonsoftJsonHandler</c>（Json 后端本体）已外置到 plugin 分支的 <c>newtonsoft-json</c> 插件； <br />
    /// Newtonsoft 是 package.json 的硬依赖，包摘不掉，所以这个类型不能跟着后端一起搬走。
    /// </remarks>
    public class CustomContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            
            // 检查属性类型是否属于不支持序列化的命名空间
            if (JsonTypeSupport.IsNotDataType(property.PropertyType))
            {
                property.Ignored = true;
                // Debug.Log($"Excluding property {property.PropertyName} because it belongs to UnityEngine namespace.");
            }
            else if (member.GetCustomAttribute(typeof(JsonPropertyAttribute)) is JsonPropertyAttribute attr)
            {
                property.Writable = attr.Serializable;
                property.Readable = attr.Deserializable;

                property.PropertyName = attr.SerializeName ?? property.PropertyName;
            }
      
            return property;
        }
    }
}
