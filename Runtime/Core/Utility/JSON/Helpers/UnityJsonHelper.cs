using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// Unity 内置 JSON 函数集辅助器。
    /// </summary>
    public class UnityJsonHelper : JSONUtility.IJsonHelper
    {
        public string ToJson(object obj, bool prettyPrint = false)
        {
            return UnityEngine.JsonUtility.ToJson(obj, prettyPrint);
        }
        
        public T ToObject<T>(string json)
        {
            return UnityEngine.JsonUtility.FromJson<T>(json);
        }
        
        public object ToObject(Type objectType, string json)
        {
            return UnityEngine.JsonUtility.FromJson(json, objectType);
        }
        
        public void FromJsonOverwrite(string json, object objectToOverwrite)
        {
            UnityEngine.JsonUtility.FromJsonOverwrite(json, objectToOverwrite);
        }
    }
}
