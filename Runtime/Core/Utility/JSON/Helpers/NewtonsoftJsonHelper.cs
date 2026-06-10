#if NEWTONSOFT_JSON_INSTALLED
using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Moirai.Atropos
{
    /// <summary>
    /// 默认 JSON 函数集辅助器。
    /// </summary>
    public class NewtonsoftJsonHelper : JSONUtility.IJsonHelper
    {
        public string ToJson(object obj, bool prettyPrint = false)
        {
            return JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new CustomContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = prettyPrint ? Formatting.Indented : Formatting.None,
                MaxDepth = JSONUtility.IJsonHelper.MAX_DEPTH,
            });
        }
        
        public T ToObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        
        public object ToObject(Type objectType, string json)
        {
            return JsonConvert.DeserializeObject(json, objectType);
        }
        
        public void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            JsonConvert.PopulateObject(json, objectToOverwrite);
        }
    }
    
    /// <summary>
    /// 自定义的 JSON 序列化器。
    /// </summary>
    public class CustomContractResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            JsonProperty property = base.CreateProperty(member, memberSerialization);
            
            // 检查属性类型是否属于不支持序列化的命名空间
            if (JSONUtility.TypeIsForbidden(property.PropertyType))
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
#endif