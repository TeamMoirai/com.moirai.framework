using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 提供 JSON 序列化和反序列化
    /// </summary>
    [Serializable]
    public sealed class DefaultJsonHandler : JsonHandler
    {
        [Tooltip("序列化的最大深度")]
        [SerializeField] private int m_MaxDepth = 25;

        [Tooltip("删除空值")]
        [SerializeField] private bool m_RemoveNulls = true;

        protected override void OnInit()
        {
            DefaultJson.maxDepth = m_MaxDepth;
        }

        protected override void Shutdown()
        {
        }

        public override string ToJson(object obj, bool prettyPrint = false)
        {
            return DefaultJson.ToJSON(obj, m_RemoveNulls, prettyPrint);
        }

        public override T ToObject<T>(string json)
        {
            return DefaultJson.FromJSON<T>(json);
        }

        public override object ToObject(Type objectType, string json)
        {
            return DefaultJson.FromJSON(json, objectType);
        }

        public override void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            DefaultJson.FromJSONOverwrite(objectToOverwrite, json);
        }
    }
}