using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// Unity 内置 Json 函数集处理器。
    /// </summary>
    [Serializable]
    public sealed class UnityJsonHandler : JsonHandler
    {
        protected override void OnInit()
        {
        }

        protected override void Shutdown()
        {
        }

        public override string ToJson(object obj, bool prettyPrint = false)
        {
            return UnityEngine.JsonUtility.ToJson(obj, prettyPrint);
        }
        
        public override T ToObject<T>(string json)
        {
            return UnityEngine.JsonUtility.FromJson<T>(json);
        }
        
        public override object ToObject(Type objectType, string json)
        {
            return UnityEngine.JsonUtility.FromJson(json, objectType);
        }
        
        public override void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            UnityEngine.JsonUtility.FromJsonOverwrite(json, objectToOverwrite);
        }
    }
}
