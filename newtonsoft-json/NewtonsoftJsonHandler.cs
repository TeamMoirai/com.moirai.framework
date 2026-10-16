#if NEWTONSOFT_JSON_INSTALLED
using System;
using Newtonsoft.Json;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// Newtonsoft Json 函数集处理器。
    /// </summary>
    /// <remarks>
    /// 后端本体住插件装配，但它的 <c>ContractResolver</c> 用的是框架常驻类型 <see cref="CustomContractResolver"/>： <br />
    /// 那个类型被事件调试器的记录（<c>EventDebuggerEventRecord</c>）无守卫使用，不随本后端外置。
    /// </remarks>
    [ProviderDisplay(title: "Newtonsoft", description: "Json.NET 全功能序列化（ContractResolver 等）；需 Newtonsoft 包")]
    [Serializable]
    internal sealed class NewtonsoftJsonHandler : JsonHandler
    {
        [Tooltip("序列化的最大深度")]
        [SerializeField] private int m_MaxDepth = 25;

        // 缓存的序列化设置（每次 ToJson 新建 Settings + ContractResolver 会产生显著 GC）
        private static JsonSerializerSettings s_CompactSettings;
        private static JsonSerializerSettings s_PrettySettings;

        private static JsonSerializerSettings GetSettings(bool prettyPrint, int maxDepth)
        {
            if (prettyPrint)
            {
                return s_PrettySettings ??= CreateSettings(Formatting.Indented, maxDepth);
            }

            return s_CompactSettings ??= CreateSettings(Formatting.None, maxDepth);
        }

        private static JsonSerializerSettings CreateSettings(Formatting formatting, int maxDepth)
        {
            return new JsonSerializerSettings
            {
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                ContractResolver = new CustomContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                Formatting = formatting,
                MaxDepth = maxDepth,
            };
        }

        public override string ToJson(object obj, bool prettyPrint = false)
        {
            return JsonConvert.SerializeObject(obj, GetSettings(prettyPrint, m_MaxDepth));
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
}
#endif
