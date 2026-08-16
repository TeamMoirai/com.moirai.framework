using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace Moirai.Atropos
{
    /// <summary>
    /// 框架内置的反射式 Json 序列化器（商业化加固版）。
    /// </summary>
    /// <remarks>
    /// <para><b>正确性</b>：数值固定以 InvariantCulture 输出/解析（浮点 "R" 往返格式）；字典输出标准 Json 对象格式
    /// （复杂 key 回退 legacy 条目数组，两种格式均可解析）；<see cref="DateTime"/>/<see cref="Guid"/>/<see cref="TimeSpan"/>
    /// 等无公开字段类型显式转字符串，不再静默丢失；截断/畸形输入一律抛错，绝不静默丢数据。</para>
    /// <para><b>健壮性</b>：未知字段默认忽略（存档前向/后向兼容）；序列化与反序列化双侧深度守卫
    /// （防引用环与深嵌套栈溢出）；错误信息带偏移/行列位置。</para>
    /// <para><b>性能</b>：反射元数据经 <see cref="ReflectionCache"/> 缓存（线程安全）；解析基于 span 零拷贝
    /// （key 匹配与数值读取无中间字符串）；写入单遍直写（无中间列表/子序列化字符串）。</para>
    /// <para><b>AOT 约束</b>：不使用表达式树/Reflection.Emit，IL2CPP + HybridCLR 安全。</para>
    /// <para>通用属性标识见 <c>JsonUtility.Attributes.cs</c>（各 JsonHandler 共享）。</para>
    /// </remarks>
    public static partial class DefaultJson
    {
        /// <summary>序列化/反序列化的最大嵌套深度（由 <see cref="DefaultJsonHandler"/> 配置）。超限成员软截断（跳过+警告），不抛错。</summary>
        internal static int maxDepth = 64;

        #region 公共 API [PUBLIC API]
        /// <summary>
        /// 将 JSON 字符串转换为类型化对象
        /// </summary>
        /// <param name="json">要转换的字符串</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T FromJson<T>(string json)
        {
            return (T)Parse(json, typeof(T), null);
        }

        /// <summary>
        /// 将 JSON 字符串转换为类型化对象
        /// </summary>
        /// <param name="json">要转换的字符串</param>
        /// <param name="type">要转换为的类型</param>
        /// <returns></returns>
        public static object FromJson(string json, Type type)
        {
            return Parse(json, type, null);
        }

        /// <summary>
        /// 用 JSON 字符串中的值覆盖对象数据
        /// </summary>
        /// <param name="obj">要更新的对象</param>
        /// <param name="json">要使用的 JSON</param>
        public static void FromJsonOverwrite(object obj, string json)
        {
            if (obj == null)
            {
                throw new GameException("Object to overwrite is invalid.");
            }

            Parse(json, obj.GetType(), obj);
        }

        /// <summary>
        /// 序列化为 JSON 的简单方法。将对象转换为 JSON 字符串
        /// </summary>
        /// <param name="obj">要转换的对象</param>
        /// <param name="removeNulls">删除空值</param>
        /// <param name="readable">包括制表符（tab）和回车（return），使结果易于阅读</param>
        /// <returns></returns>
        public static string ToJson(object obj, bool removeNulls = true, bool readable = false)
        {
            LoopGuard.Begin();
            StringHandler.IStringBuilder sb = StringUtility.CreateStringBuilder();
            try
            {
                var sink = new CharSink(sb);
                JsonWriter<CharSink>.WriteAll(ref sink, obj, removeNulls, readable, maxDepth);
                return sb.ToStringAndDispose();
            }
            catch
            {
                sb.Dispose(); // 异常路径也要归还池，避免池化 builder 泄漏
                throw;
            }
            finally
            {
                LoopGuard.End();
            }
        }

        /// <summary>
        /// 序列化为 UTF8 JSON 字节（紧凑格式，与 <see cref="ToJson"/> 输出 UTF8 编码逐字节等价）。
        /// </summary>
        /// <param name="obj">要转换的对象</param>
        /// <param name="removeNulls">删除空值</param>
        /// <returns>UTF8 JSON 字节（调用方持有所有权）</returns>
        public static byte[] ToJsonBytes(object obj, bool removeNulls = true)
        {
            LoopGuard.Begin();
            byte[] scratch = ByteScratch.Rent();
            var sink = new Utf8Sink(scratch);
            try
            {
                JsonWriter<Utf8Sink>.WriteAll(ref sink, obj, removeNulls, false, maxDepth);
            }
            finally
            {
                ByteScratch.Return(sink.Buffer);
                LoopGuard.End();
            }

            byte[] result = new byte[sink.Position];
            Array.Copy(sink.Buffer, result, sink.Position);
            return result;
        }

        /// <summary>
        /// 将 UTF8 JSON 字节转换为类型化对象（与 <see cref="FromJson{T}(string)"/> 接受相同的输入集合）
        /// </summary>
        /// <param name="json">要转换的 UTF8 字节</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static T FromJson<T>(byte[] json)
        {
            return (T)ParseBytes(json, typeof(T), null);
        }

        /// <summary>
        /// 将 UTF8 JSON 字节转换为类型化对象（与 <see cref="FromJson(string, Type)"/> 接受相同的输入集合）
        /// </summary>
        /// <param name="json">要转换的 UTF8 字节</param>
        /// <param name="type">要转换为的类型</param>
        /// <returns></returns>
        public static object FromJson(byte[] json, Type type)
        {
            return ParseBytes(json, type, null);
        }

        private static object Parse(string json, Type type, object existing)
        {
            if (type == null)
            {
                throw new GameException("Target type is invalid.");
            }

            if (json == null)
            {
                throw new GameException("Json string is invalid (null).");
            }

            if (json.Length == 0)
            {
                throw new GameException("Json string is empty.");
            }

            var lexer = new CharLexer(json, maxDepth);
            return JsonReader<CharLexer>.Parse(lexer, type, existing);
        }

        private static object ParseBytes(byte[] json, Type type, object existing)
        {
            if (type == null)
            {
                throw new GameException("Target type is invalid.");
            }

            if (json == null)
            {
                throw new GameException("Json bytes are invalid (null).");
            }

            // 长度 0 或仅 BOM 头视为空输入（BOM 3 字节由 Reader 跳过）
            if (json.Length == 0 || (json.Length == 3 && json[0] == 0xEF && json[1] == 0xBB && json[2] == 0xBF))
            {
                throw new GameException("Json bytes are empty.");
            }

            var lexer = new ByteLexer(json, maxDepth);
            return JsonReader<ByteLexer>.Parse(lexer, type, existing);
        }

        #endregion

        /// <summary>
        /// 类型反射元数据缓存（线程安全）。
        /// 避免每次序列化/反序列化重复执行 GetFields/GetProperties/GetMethods 与特性扫描，
        /// 这是反射式 Json 的最大 GC 与性能开销来源。
        /// </summary>
        internal static class ReflectionCache
        {
            internal sealed class TypeMeta
            {
                /// <summary>可序列化字段（含基类，已解析序列化名）。</summary>
                public (string Name, FieldInfo Field)[] SerializeFields = Array.Empty<(string, FieldInfo)>();

                /// <summary>可反序列化字段（含 JsonSerializeAs / FormerlySerializedAs / 本名 别名表）。</summary>
                public (FieldInfo Field, string[] Names)[] DeserializeFields = Array.Empty<(FieldInfo, string[])>();

                /// <summary>可反序列化字段的 UTF8 编码别名表（与 <see cref="DeserializeFields"/> 平行，供字节解析零拷贝 key 匹配）。</summary>
                public byte[][][] DeserializeFieldNamesUtf8 = Array.Empty<byte[][]>();

                /// <summary>可序列化属性（已解析序列化名）。</summary>
                public (string Name, PropertyInfo Property)[] SerializeProperties = Array.Empty<(string, PropertyInfo)>();

                /// <summary>可反序列化属性（含别名表）。</summary>
                public (PropertyInfo Property, string[] Names)[] DeserializeProperties = Array.Empty<(PropertyInfo, string[])>();

                /// <summary>可反序列化属性的 UTF8 编码别名表（与 <see cref="DeserializeProperties"/> 平行）。</summary>
                public byte[][][] DeserializePropertyNamesUtf8 = Array.Empty<byte[][]>();

                public MethodInfo[] BeforeSerializeMethods = Array.Empty<MethodInfo>();
                public MethodInfo[] AfterDeserializeMethods = Array.Empty<MethodInfo>();
            }

            private static readonly ConcurrentDictionary<Type, TypeMeta> s_Cache = new();

            internal static TypeMeta Get(Type type)
            {
                return s_Cache.GetOrAdd(type, Build);
            }

            private static TypeMeta Build(Type type)
            {
                var meta = new TypeMeta();
                BuildSerializeFields(type, meta);
                BuildDeserializeFields(type, meta);
                BuildSerializeProperties(type, meta);
                BuildDeserializeProperties(type, meta);
                BuildCallbacks(type, meta);
                EncodeUtf8NameTables(meta);
                return meta;
            }

            /// <summary>将反序列化别名表预编码为 UTF8 字节（一次性成本，字节解析热路径零分配）。</summary>
            private static void EncodeUtf8NameTables(TypeMeta meta)
            {
                meta.DeserializeFieldNamesUtf8 = EncodeNameTable(meta.DeserializeFields.Length, i => meta.DeserializeFields[i].Names);
                meta.DeserializePropertyNamesUtf8 = EncodeNameTable(meta.DeserializeProperties.Length, i => meta.DeserializeProperties[i].Names);
            }

            private static byte[][][] EncodeNameTable(int count, Func<int, string[]> namesOf)
            {
                var table = new byte[count][][];
                for (int i = 0; i < count; i++)
                {
                    string[] names = namesOf(i);
                    var encoded = new byte[names.Length][];
                    for (int j = 0; j < names.Length; j++)
                    {
                        encoded[j] = System.Text.Encoding.UTF8.GetBytes(names[j]);
                    }

                    table[i] = encoded;
                }

                return table;
            }

            #region 字段 [FIELDS]

            private static void BuildSerializeFields(Type type, TypeMeta meta)
            {
                var result = new List<(string, FieldInfo)>();
                var seen = new HashSet<string>();
                CollectSerializeFields(type, result, seen);
                meta.SerializeFields = result.ToArray();
            }

            private static void CollectSerializeFields(Type type, List<(string, FieldInfo)> result, HashSet<string> seen)
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    bool forceExclude = field.Name[0] == '<' ||
                                        JsonUtility.TypeIsForbidden(field.FieldType) ||
                                        field.GetCustomAttribute<JsonDoNotSerializeAttribute>() != null;
                    if (forceExclude) continue;

                    bool forceInclude = field.GetCustomAttribute<SerializeField>() != null ||
                                        field.GetCustomAttribute<JsonSerializeAttribute>() != null ||
                                        field.GetCustomAttribute<JsonSerializeAsAttribute>() != null;
                    if (!forceInclude && (field.IsInitOnly || field.IsLiteral || field.IsPrivate)) continue;

                    string name = field.GetCustomAttribute<JsonSerializeAsAttribute>()?.SerializeName ?? field.Name;
                    if (seen.Add(name))
                        result.Add((name, field));
                }

                if (type.BaseType != null && type.BaseType != typeof(object))
                    CollectSerializeFields(type.BaseType, result, seen);
            }

            private static void BuildDeserializeFields(Type type, TypeMeta meta)
            {
                var result = new List<(FieldInfo, string[])>();
                CollectDeserializeFields(type, result);
                meta.DeserializeFields = result.ToArray();
            }

            private static void CollectDeserializeFields(Type type, List<(FieldInfo, string[])> result)
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (FieldInfo field in fields)
                {
                    if (field.Name[0] == '<') continue;

                    // 与序列化侧对齐：禁止类型（UnityEngine.Object 派生等）不参与反序列化，对应 key 按未知字段忽略
                    if (JsonUtility.TypeIsForbidden(field.FieldType)) continue;

                    bool forceInclude = field.GetCustomAttribute<SerializeField>() != null ||
                                        field.GetCustomAttribute<JsonSerializeAttribute>() != null ||
                                        field.GetCustomAttribute<JsonSerializeAsAttribute>() != null;
                    if (!forceInclude && (field.IsInitOnly || field.IsLiteral || field.IsPrivate)) continue;

                    result.Add((field, BuildFieldNames(field)));
                }

                if (type.BaseType != null && type.BaseType != typeof(object))
                    CollectDeserializeFields(type.BaseType, result);
            }

            private static string[] BuildFieldNames(FieldInfo field)
            {
                var names = new List<string>(3);

                var serializeAs = field.GetCustomAttribute<JsonSerializeAsAttribute>();
                if (serializeAs?.SerializeName != null)
                    names.Add(serializeAs.SerializeName);

                foreach (FormerlySerializedAsAttribute formerly in field.GetCustomAttributes<FormerlySerializedAsAttribute>())
                    names.Add(formerly.oldName);

                names.Add(field.Name);
                return names.ToArray();
            }

            #endregion

            #region 属性 [PROPERTIES]

            private static void BuildSerializeProperties(Type type, TypeMeta meta)
            {
                var result = new List<(string, PropertyInfo)>();
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in properties)
                {
                    bool forceExclude = property.Name[0] == '<' ||
                                        JsonUtility.TypeIsForbidden(property.PropertyType) ||
                                        property.GetCustomAttribute<JsonDoNotSerializeAttribute>() != null ||
                                        property.GetIndexParameters().Length > 0;
                    if (forceExclude) continue;

                    bool forceInclude = property.GetCustomAttribute<SerializeField>() != null ||
                                        property.GetCustomAttribute<JsonSerializeAttribute>() != null ||
                                        property.GetCustomAttribute<JsonSerializeAsAttribute>() != null;

                    // 默认仅序列化"可读+可写"的属性——
                    // ① 往返对称：凡是能写出的都能读回（存档安全）；
                    // ② 从构造上排除计算属性（Vector3.normalized 等 get-only 属性每次求值返回
                    //    新装箱副本，会形成引用栈无法识别的无限链）。需要输出计算值时显式标注 [JsonSerialize]。
                    if (!forceInclude && !(property.CanRead && property.GetSetMethod() != null)) continue;

                    string name = property.GetCustomAttribute<JsonSerializeAsAttribute>()?.SerializeName ?? property.Name;
                    result.Add((name, property));
                }

                meta.SerializeProperties = result.ToArray();
            }

            private static void BuildDeserializeProperties(Type type, TypeMeta meta)
            {
                var result = new List<(PropertyInfo, string[])>();
                CollectDeserializeProperties(type, result);
                meta.DeserializeProperties = result.ToArray();
            }

            private static void CollectDeserializeProperties(Type type, List<(PropertyInfo, string[])> result)
            {
                PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (PropertyInfo property in properties)
                {
                    if (property.Name[0] == '<') continue;
                    if (property.GetCustomAttribute<JsonDoNotSerializeAttribute>() != null) continue;
                    if (property.GetIndexParameters().Length > 0) continue;

                    // 与序列化侧对齐：禁止类型不参与反序列化
                    if (JsonUtility.TypeIsForbidden(property.PropertyType)) continue;

                    try
                    {
                        if (property.CanRead && property.CanWrite)
                        {
                            var serializeAs = property.GetCustomAttribute<JsonSerializeAsAttribute>();
                            string[] names = serializeAs?.SerializeName != null
                                ? new[] { serializeAs.SerializeName, property.Name }
                                : new[] { property.Name };
                            result.Add((property, names));
                        }
                    }
                    catch
                    {
                        // 某些属性的 CanRead/CanWrite 访问会抛异常（如显式接口实现），跳过
                    }
                }

                if (type.BaseType != null && type.BaseType != typeof(object))
                    CollectDeserializeProperties(type.BaseType, result);
            }

            #endregion

            #region 回调 [CALLBACKS]

            private static void BuildCallbacks(Type type, TypeMeta meta)
            {
                var before = new List<MethodInfo>();
                var after = new List<MethodInfo>();
                CollectCallbacks(type, before, after);
                meta.BeforeSerializeMethods = before.ToArray();
                meta.AfterDeserializeMethods = after.ToArray();
            }

            /// <summary>
            /// 按继承链收集回调（基类在前、派生类在后）。
            /// 反序列化回调按此顺序执行——与 C# 构造顺序 / Newtonsoft OnDeserialized 惯例一致，
            /// 保证基类的初始化回调先于派生类运行（派生类回调可能依赖基类状态就绪）。
            /// </summary>
            private static void CollectCallbacks(Type type, List<MethodInfo> before, List<MethodInfo> after)
            {
                if (type.BaseType != null && type.BaseType != typeof(object))
                    CollectCallbacks(type.BaseType, before, after);

                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                foreach (MethodInfo method in methods)
                {
                    if (method.GetCustomAttribute<JsonBeforeSerializationAttribute>() != null)
                        before.Add(method);
                    if (method.GetCustomAttribute<JsonAfterDeserializationAttribute>() != null)
                        after.Add(method);
                }
            }

            #endregion
        }
    }
}
