using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// JSON 处理器基类。
    /// </summary>
    [Serializable]
    public abstract class JsonHandler
    {
        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <param name="prettyPrint">如果为 <c>true</c>，则以提高可读性的格式输出。如果为 <c>false</c>，则为紧凑格式输出。</param>
        /// <returns>序列化后的 JSON 字符串。</returns>
        public abstract string ToJson(object obj, bool prettyPrint = false);

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public abstract T ToObject<T>(string json);

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public abstract object ToObject(Type objectType, string json);

        /// <summary>
        /// 使用 JSON 覆盖对象。
        /// </summary>
        /// <param name="json"></param>
        /// <param name="objectToOverwrite"></param>
        /// <remarks>将 JSON 数据反序列化到现有对象上，并覆盖现有数据</remarks>
        public abstract void FromJsonOverwrite(string json, object objectToOverwrite);
    }
}