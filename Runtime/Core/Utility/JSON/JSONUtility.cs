using System;
using System.Linq;

namespace Moirai.Atropos
{
    /// <summary>
    /// JSON 相关的实用函数。
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public static partial class JSONUtility
    {
        private static IJsonHelper s_JsonHelper = new UnityJsonHelper();

        /// <summary>
        /// 设置 JSON 辅助器。
        /// </summary>
        /// <param name="jsonHelper">要设置的 JSON 辅助器。</param>
        public static void SetJsonHelper(IJsonHelper jsonHelper)
        {
            s_JsonHelper = jsonHelper;
        }

        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <param name="prettyPrint">如果为 <c>true</c>，则以提高可读性的格式输出。如果为 <c>false</c>，则为紧凑格式输出。</param>
        /// <returns>序列化后的 JSON 字符串。</returns>
        public static string ToJson(object obj, bool prettyPrint = false)
        {
            if (s_JsonHelper == null)
            {
                throw new GameException("JSON helper is invalid.");
            }

            try
            {
                return s_JsonHelper.ToJson(obj, prettyPrint);
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(TextUtility.Format("Can not convert to JSON with exception '{0}'.", exception), exception);
            }
        }

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public static T ToObject<T>(string json)
        {
            if (s_JsonHelper == null)
            {
                throw new GameException("JSON helper is invalid.");
            }

            try
            {
                return s_JsonHelper.ToObject<T>(json);
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(TextUtility.Format("Can not convert to object with exception '{0}'.", exception), exception);
            }
        }

        /// <summary>
        /// 将 JSON 字符串反序列化为对象。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="json">要反序列化的 JSON 字符串。</param>
        /// <returns>反序列化后的对象。</returns>
        public static object ToObject(Type objectType, string json)
        {
            if (s_JsonHelper == null)
            {
                throw new GameException("JSON helper is invalid.");
            }

            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            try
            {
                return s_JsonHelper.ToObject(objectType, json);
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(TextUtility.Format("Can not convert to object with exception '{0}'.", exception), exception);
            }
        }
        
        /// <summary>
        /// 格式化 Json 字符串
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static string FormatJson(string json)
        {
            int indentLevel = 0;
            string indent = "    "; // 缩进字符（4空格）
            bool inQuote = false;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            foreach (char ch in json)
            {
                switch (ch)
                {
                    case '{':
                    case '[':
                        sb.Append(ch);
                        if (!inQuote)
                        {
                            sb.AppendLine();
                            indentLevel++;
                            sb.Append(string.Join("", Enumerable.Repeat(indent, indentLevel)));
                        }
                        break;
                    case '}':
                    case ']':
                        if (!inQuote)
                        {
                            sb.AppendLine();
                            indentLevel--;
                            sb.Append(string.Join("", Enumerable.Repeat(indent, indentLevel)));
                        }
                        sb.Append(ch);
                        break;
                    case '"':
                        sb.Append(ch);
                        inQuote = !inQuote;
                        break;
                    case ',':
                        sb.Append(ch);
                        if (!inQuote)
                        {
                            sb.AppendLine();
                            sb.Append(string.Join("", Enumerable.Repeat(indent, indentLevel)));
                        }
                        break;
                    case ':':
                        sb.Append(ch);
                        if (!inQuote) sb.Append(" ");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
