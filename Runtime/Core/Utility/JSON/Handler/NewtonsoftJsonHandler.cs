#if NEWTONSOFT_JSON_INSTALLED
using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// Newtonsoft Json 函数集处理器。
    /// </summary>
    [Serializable]
    public sealed class NewtonsoftJsonHandler : JsonHandler
    {
        [Tooltip("序列化的最大深度")]
        [SerializeField] private int m_MaxDepth = 25;

        protected override void OnInit()
        {
        }

        protected override void Shutdown()
        {
        }

        public override string ToJson(object obj, bool prettyPrint = false)
        {
            return JsonConvert.SerializeObject(obj, new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new CustomContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = prettyPrint ? Formatting.Indented : Formatting.None,
                MaxDepth = m_MaxDepth,
            });
        }
        
        public override T ToObject<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        
        public override object ToObject(Type objectType, string json)
        {
            return JsonConvert.DeserializeObject(json, objectType);
        }
        
        public override void FromJsonOverwrite(string json, object objectToOverwrite)
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