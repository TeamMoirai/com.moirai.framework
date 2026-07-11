using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 提供 JSON 序列化和反序列化
    /// </summary>
    [Serializable]
    public sealed class SimpleJsonHandler : JsonHandler
    {
        [Tooltip("序列化的最大深度")]
        [SerializeField] private int m_MaxDepth = 25;

        [Tooltip("删除空值")]
        [SerializeField] private bool m_RemoveNulls = true;

        public SimpleJsonHandler()
        {
            SimpleJson.maxDepth = m_MaxDepth;
        }

        public override string ToJson(object obj, bool prettyPrint = false)
        {
            return SimpleJson.ToJSON(obj, m_RemoveNulls, prettyPrint);
        }

        public override T ToObject<T>(string json)
        {
            return SimpleJson.FromJSON<T>(json);
        }

        public override object ToObject(Type objectType, string json)
        {
            return SimpleJson.FromJSON(json, objectType);
        }

        public override void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            SimpleJson.FromJSONOverwrite(objectToOverwrite, json);
        }
    }
}