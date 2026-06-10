using System;

namespace Moirai.Atropos
{
    // ReSharper disable once InconsistentNaming
    public static partial class JSONUtility
    {
        /// <summary>
        /// JSON 辅助器接口。
        /// </summary>
        public interface IJsonHelper
        {
            /// <summary>
            /// 序列化的最大深度
            /// </summary>
            public const int MAX_DEPTH = 25;

            /// <summary>
            /// 将对象序列化为 JSON 字符串。
            /// </summary>
            /// <param name="obj">要序列化的对象。</param>
            /// <param name="prettyPrint">如果为 <c>true</c>，则以提高可读性的格式输出。如果为 <c>false</c>，则为紧凑格式输出。</param>
            /// <returns>序列化后的 JSON 字符串。</returns>
            string ToJson(object obj, bool prettyPrint = false);

            /// <summary>
            /// 将 JSON 字符串反序列化为对象。
            /// </summary>
            /// <typeparam name="T">对象类型。</typeparam>
            /// <param name="json">要反序列化的 JSON 字符串。</param>
            /// <returns>反序列化后的对象。</returns>
            T ToObject<T>(string json);

            /// <summary>
            /// 将 JSON 字符串反序列化为对象。
            /// </summary>
            /// <param name="objectType">对象类型。</param>
            /// <param name="json">要反序列化的 JSON 字符串。</param>
            /// <returns>反序列化后的对象。</returns>
            object ToObject(Type objectType, string json);

            /// <summary>
            /// 使用 JSON 覆盖对象。
            /// </summary>
            /// <param name="json"></param>
            /// <param name="objectToOverwrite"></param>
            /// <remarks>将 JSON 数据反序列化到现有对象上，并覆盖现有数据</remarks>
            void FromJsonOverwrite(string json, object objectToOverwrite);
        }
    }
}