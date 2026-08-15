using System;

namespace Moirai.Atropos
{
    /// <summary>
    /// JSON 相关的实用函数。
    /// </summary>
    public static partial class JsonUtility
    {
        private static JsonHandler s_Handler = null;
        /// <summary>
        /// 获取/设置 JSON 工具实现。
        /// </summary>
        public static JsonHandler Handler
        {
            get
            {
                if (s_Handler == null) Handler =  new DefaultJsonHandler();
                return s_Handler;
            }
            set
            {
                if (s_Handler == value || value == null) return;

                s_Handler?.Internal_Shutdown();
                s_Handler = value;
                s_Handler.Internal_Init();
            }
        }

        /// <summary>
        /// 将对象序列化为 JSON 字符串。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <param name="prettyPrint">如果为 <c>true</c>，则以提高可读性的格式输出。如果为 <c>false</c>，则为紧凑格式输出。</param>
        /// <returns>序列化后的 JSON 字符串。</returns>
        public static string ToJson(object obj, bool prettyPrint = false)
        {
            try
            {
                return Handler.ToJson(obj, prettyPrint);
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(StringUtility.Format("Can not convert to JSON with exception '{0}'.", exception), exception);
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
            try
            {
                return Handler.ToObject<T>(json);
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(StringUtility.Format("Can not convert to object with exception '{0}'.", exception), exception);
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
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            try
            {
                return Handler.ToObject(objectType, json);
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(StringUtility.Format("Can not convert to object with exception '{0}'.", exception), exception);
            }
        }

        /// <summary>
        /// 将对象序列化为 UTF8 JSON 字节（紧凑格式）。
        /// </summary>
        /// <param name="obj">要序列化的对象。</param>
        /// <returns>UTF8 JSON 字节（调用方持有所有权）。</returns>
        /// <remarks>
        /// 当前 Handler 实现 <see cref="IBufferJsonHandler"/> 时走字节快速通路（无 string 中间态）；
        /// 否则自动回退 string 路径后 UTF8 编码，调用方无感。
        /// </remarks>
        public static byte[] ToJsonBytes(object obj)
        {
            try
            {
                return Handler is IBufferJsonHandler buffer
                    ? buffer.ToJsonBytes(obj)
                    : System.Text.Encoding.UTF8.GetBytes(Handler.ToJson(obj));
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(StringUtility.Format("Can not convert to JSON bytes with exception '{0}'.", exception), exception);
            }
        }

        /// <summary>
        /// 将 UTF8 JSON 字节反序列化为对象。
        /// </summary>
        /// <typeparam name="T">对象类型。</typeparam>
        /// <param name="json">要反序列化的 UTF8 JSON 字节。</param>
        /// <returns>反序列化后的对象。</returns>
        public static T ToObject<T>(byte[] json)
        {
            try
            {
                return Handler is IBufferJsonHandler buffer
                    ? buffer.ToObject<T>(json)
                    : Handler.ToObject<T>(System.Text.Encoding.UTF8.GetString(json));
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(StringUtility.Format("Can not convert to object with exception '{0}'.", exception), exception);
            }
        }

        /// <summary>
        /// 将 UTF8 JSON 字节反序列化为对象。
        /// </summary>
        /// <param name="objectType">对象类型。</param>
        /// <param name="json">要反序列化的 UTF8 JSON 字节。</param>
        /// <returns>反序列化后的对象。</returns>
        public static object ToObject(Type objectType, byte[] json)
        {
            if (objectType == null)
            {
                throw new GameException("Object type is invalid.");
            }

            try
            {
                return Handler is IBufferJsonHandler buffer
                    ? buffer.ToObject(objectType, json)
                    : Handler.ToObject(objectType, System.Text.Encoding.UTF8.GetString(json));
            }
            catch (Exception exception)
            {
                if (exception is GameException)
                {
                    throw;
                }

                throw new GameException(StringUtility.Format("Can not convert to object with exception '{0}'.", exception), exception);
            }
        }
        
        /// <summary>
        /// 格式化 Json 字符串
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        public static string FormatJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return string.Empty;

            int indentLevel = 0;
            bool inQuote = false;
            // 池化 builder：原先每次 new StringBuilder + 每行 string.Join 产生大量 GC
            var sb = StringUtility.CreateStringBuilder(json.Length + json.Length / 4);
            try
            {
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
                                sb.Append(' ', indentLevel * 4);
                            }
                            break;
                        case '}':
                        case ']':
                            if (!inQuote)
                            {
                                sb.AppendLine();
                                indentLevel--;
                                sb.Append(' ', indentLevel * 4);
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
                                sb.Append(' ', indentLevel * 4);
                            }
                            break;
                        case ':':
                            sb.Append(ch);
                            if (!inQuote) sb.Append(' ');
                            break;
                        default:
                            sb.Append(ch);
                            break;
                    }
                }

                return sb.ToString();
            }
            finally
            {
                sb.Dispose(); // 归还池
            }
        }
    }
}
