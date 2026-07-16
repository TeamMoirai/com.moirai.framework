using System;
// ReSharper disable InconsistentNaming

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        /// <summary>
        /// 将 JSON 字符串转换为类型化对象
        /// </summary>
        /// <param name="json">要转换的字符串</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T FromJSON<T>(string json)
        {
            JsonDeserializationObject jdo = MemoryPool.Acquire<JsonDeserializationObject>();
            jdo.InitFromPool(json);
            T result = jdo.Deserialize<T>();
            MemoryPool.Release(jdo);
            return result;
        }

        /// <summary>
        /// 将 JSON 字符串转换为类型化对象
        /// </summary>
        /// <param name="json">要转换的字符串</param>
        /// <param name="type">要转换为的类型</param>
        /// <returns></returns>
        public static object FromJSON(string json, Type type)
        {
            JsonDeserializationObject jdo = MemoryPool.Acquire<JsonDeserializationObject>();
            jdo.InitFromPool(json);
            object result = jdo.Deserialize(type);
            MemoryPool.Release(jdo);
            return result;
        }

        /// <summary>
        /// 用 JSON 字符串中的值覆盖对象数据
        /// </summary>
        /// <param name="obj">要更新的对象</param>
        /// <param name="json">要使用的 JSON</param>
        public static void FromJSONOverwrite(object obj, string json)
        {
            JsonDeserializationObject jdo = MemoryPool.Acquire<JsonDeserializationObject>();
            jdo.InitFromPool(obj, json);
            MemoryPool.Release(jdo);
        }

        /// <summary>
        /// 序列化为 JSON 的简单方法。将对象转换为 JSON 字符串
        /// </summary>
        /// <param name="obj">要转换的对象</param>
        /// <param name="removeNulls">删除空值</param>
        /// <param name="readable">包括制表符（tab）和回车（return），使结果易于阅读</param>
        /// <returns></returns>
        public static string ToJSON(object obj, bool removeNulls = true, bool readable = false)
        {
            JsonSerializationObject jso = MemoryPool.Acquire<JsonSerializationObject>();
            jso.InitFromPool(obj, removeNulls, readable);
            string result = jso.Value;
            MemoryPool.Release(jso);
            return result;
        }
    }
}
