using System;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 提供 JSON 序列化和反序列化
    /// </summary>
    [Serializable]
    public sealed class DefaultJsonHandler : JsonHandler, IBufferJsonHandler
    {
        [Tooltip("序列化的最大深度（超限成员软截断并警告，不抛错）")]
        [SerializeField] private int m_MaxDepth = 64;

        [Tooltip("删除空值")]
        [SerializeField] private bool m_RemoveNulls = true;

        protected override void OnInit()
        {
            base.OnInit();

            DefaultJson.maxDepth = m_MaxDepth;
        }

        public override string ToJson(object obj, bool prettyPrint = false)
        {
            return DefaultJson.ToJson(obj, m_RemoveNulls, prettyPrint);
        }

        public override T ToObject<T>(string json)
        {
            return DefaultJson.FromJson<T>(json);
        }

        public override object ToObject(Type objectType, string json)
        {
            return DefaultJson.FromJson(json, objectType);
        }

        public override void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            DefaultJson.FromJsonOverwrite(objectToOverwrite, json);
        }

        public byte[] ToJsonBytes(object obj)
        {
            return DefaultJson.ToJsonBytes(obj, m_RemoveNulls);
        }

        public T ToObject<T>(byte[] json)
        {
            return DefaultJson.FromJson<T>(json);
        }

        public object ToObject(Type objectType, byte[] json)
        {
            return DefaultJson.FromJson(json, objectType);
        }
    }
}