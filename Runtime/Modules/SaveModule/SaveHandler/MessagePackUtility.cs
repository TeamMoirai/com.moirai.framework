#if MESSAGEPACK_INSTALLED
using System.Text;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using MessagePack.Unity;
using MessagePack.Unity.Extension;
using UnityEngine;
using UnityEngine.Events;

namespace Moirai.Atropos
{
    public static class MessagePackUtility
    {
        /// <summary>
        /// 序列化对象到二进制；
        /// </summary>
        /// <typeparam name="T">mp标记的对象类型</typeparam>
        /// <param name="obj">mp对象</param>
        /// <returns>序列化后的对象</returns>
        public static byte[] Serialize<T>(T obj)
        {
            return MessagePackSerializer.Serialize(obj, _options);
        }
        
        /// <summary>
        /// 反序列化二进制到对象；
        /// </summary>
        /// <typeparam name="T">mp标记的对象类型</typeparam>
        /// <param name="bytes">需要反序列化的数组</param>
        /// <returns>反序列化后的对象</returns>
        public static T Deserialize<T>(byte[] bytes)
        {
            return MessagePackSerializer.Deserialize<T>(bytes, _options);
        }

        /// <summary>
        /// 将类型为 T 的对象转换为 JSON 格式的字符串。
        /// </summary>
        /// <typeparam name="T">要转换的对象的类型。</typeparam>
        /// <param name="obj">要转换的对象。</param>
        /// <param name="prettyPrint">如果为 <c>true</c>，则设置输出格式以提高可读性。如果为 <c>false</c>，则为最小大小设置输出格式。默认值为 <c>false</c>。</param>
        /// <returns>表示对象的 JSON 格式的字符串。</returns>
        /// <remarks>调试用</remarks>
        public static string ToJson<T>(T obj, bool prettyPrint = false)
        {
            string json = MessagePackSerializer.SerializeToJson<T>(obj, _options);
            if (!prettyPrint)
            {
                return json;
            }
            
            return FormatJson(json);
        }
        
        /// <summary>
        /// byte[]转json字符串；
        /// </summary>
        /// <param name="jsonBytes">需要被转换成json的byte数组</param>
        /// <param name="prettyPrint">如果为 <c>true</c>，则设置输出格式以提高可读性。如果为 <c>false</c>，则为最小大小设置输出格式。默认值为 <c>false</c>。</param>
        /// <returns>转换后的json</returns>
        /// <remarks>调试用</remarks>
        public static string BytesToJson(byte[] jsonBytes, bool prettyPrint = false)
        {
            string json = MessagePackSerializer.ConvertToJson(jsonBytes, _options);
            
            if (!prettyPrint)
            {
                return json;
            }
            
            return FormatJson(json);
        }
        
        /// <summary>
        /// json字符串反序列化成对象；
        /// </summary>
        /// <typeparam name="T">mp标记的对象类型</typeparam>
        /// <param name="json">需要被转换的json</param>
        /// <returns>反序列化后的对象</returns>
        public static T JsonToObject<T>(string json)
        {
            return Deserialize<T>(JsonToBytes(json));
        }

        /// <summary>
        /// json字符串转byte[]；
        /// </summary>
        /// <param name="json">需要被转换成bytes的json</param>
        /// <returns>转换后的bytes</returns>
        public static byte[] JsonToBytes(string json)
        {
            return MessagePackSerializer.ConvertFromJson(json, _options);
        }
        
        /// <summary>
        /// 格式化 JSON 字符串，使其更易于阅读。
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        private static string FormatJson(string json)
        {
            StringBuilder formattedJson = new StringBuilder();
            int indentLength = 4; // 缩进长度
            int indentLevel = 0; // 当前缩进级别
            bool quoteMode = false; // 是否处于引号内
            
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                
                if (c == '"')
                {
                    quoteMode = !quoteMode;
                }

                if (!quoteMode)
                {
                    switch (c)
                    {
                        case ':':
                            formattedJson.Append($"{c} ");
                            break;
                        case '{':
                            formattedJson.Append(c);
                            if (json[i + 1] != '}')
                            {
                                formattedJson.AppendLine();
                                indentLevel++;
                                AddIndentation(formattedJson, indentLevel, indentLength);
                            }
                            break;
                        case '}':
                            if (json[i - 1] != '{')
                            {
                                formattedJson.AppendLine();
                                indentLevel--;
                                AddIndentation(formattedJson, indentLevel, indentLength);
                            }
                            formattedJson.Append(c);
                            break;
                        case '[':
                            formattedJson.Append(c);
                            if (json[i + 1] != ']')
                            {
                                formattedJson.AppendLine();
                                indentLevel++;
                                AddIndentation(formattedJson, indentLevel, indentLength);
                            }
                            break;
                        case ']':
                            if (json[i - 1] != '[')
                            {
                                formattedJson.AppendLine();
                                indentLevel--;
                                AddIndentation(formattedJson, indentLevel, indentLength);
                            }
                            formattedJson.Append(c);
                            break;
                        case ',':
                            formattedJson.Append(c);
                            formattedJson.AppendLine();
                            AddIndentation(formattedJson, indentLevel, indentLength);
                            break;
                        default:
                            formattedJson.Append(c);
                            break;
                    }
                }
                else
                {
                    formattedJson.Append(c);
                }
            }

            return formattedJson.ToString();
        }

        /// <summary>
        /// 添加缩进
        /// </summary>
        /// <param name="sb"></param>
        /// <param name="indentLevel"></param>
        /// <param name="indentLength"></param>
        private static void AddIndentation(StringBuilder sb, int indentLevel, int indentLength)
        {
            for (int i = 0; i < indentLevel * indentLength; i++)
            {
                sb.Append(' ');
            }
        }

        private static MessagePackSerializerOptions _options;
        /// <summary>
        /// 初始化 MessagePack 序列化器，可以自定义添加多个解析器。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        public static void Initialize()
        {
            if (_options != null) return;

            // // 创建解析器列表
            // var resolver = CompositeResolver.Create(
            //     new IMessagePackFormatter[]
            //     {
            //         // 注册无法序列化的 Unity 内置类型
            //         new IgnoreFormatter<Sprite>(),
            //         new IgnoreFormatter<Texture>(),
            //         new IgnoreFormatter<UnityEvent>(),
            //     },
            //     // 添加复合解析器
            //     new IFormatterResolver[]
            //     {
            //         // DynamicEnumAsStringResolver.Instance, // 枚举的解析器。序列化为 string。
            //         UnityResolver.Instance, // Unity 解析器
            //         UnityBlitResolver.Instance, // Unity 补充解析器。Vector2[], Vector3[], Vector4[], Quaternion[], Color[], Bounds[], Rect[]
            //         StandardResolver.Instance, // 默认复合解析器
            //     });

            // 注册并将组合解析器设置为默认解析器
            // _options = MessagePackSerializerOptions.Standard.WithResolver(resolver);
            _options = MessagePackSerializerOptions.Standard.WithResolver(UnityResolver.InstanceWithStandardResolver);
        }
    }
}
#endif