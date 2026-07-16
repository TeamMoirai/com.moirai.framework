using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.Serialization;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        internal class JsonDeserializationObject : MemoryObject
        {
            #region 变量 [VARIABLES]

            private string _fullJson;

            #endregion

            #region 构造函数 [CONSTRUCTOR]

            public override void InitFromPool()
            {
                _fullJson = null;
            }

            public void InitFromPool(string json)
            {
                _fullJson = json;
            }

            public void InitFromPool(object objectToUpdate, string json)
            {
                _fullJson = json;
                Deserialize(ref objectToUpdate, objectToUpdate.GetType());
            }

            public override void RecycleToPool()
            {
            }

            public override void Clear()
            {
                _fullJson = null;
            }

            #endregion

            #region 公共方法 [PUBLIC METHODS]

            public T Deserialize<T>()
            {
                return (T)Deserialize(typeof(T));
            }

            public object Deserialize(Type objectType)
            {
                if (objectType == null) return null;

                object instance = Activator.CreateInstance(objectType);
                if (!objectType.Name.StartsWith("Nullable"))
                {
                    if (instance is null || instance.ToString() == "null")
                    {
                        throw new GameException("Cannot deserialize JSON to new instances of '" + objectType.Name + "'");
                    }
                }

                Deserialize(ref instance, objectType);

                return instance;
            }

            #endregion

            #region 私有方法 [PRIVATE METHODS]

            private object BuildArray(string json, Type type)
            {
                List<object> list = new List<object>();

                int startIndex = 1;
                Type objType = type.GetElementType();

                ReadWhitespace(json, ref startIndex);
                while (startIndex < json.Length - 1)
                {
                    list.Add(GetValueNoList(objType, json, ref startIndex));
                    ReadWhitespace(json, ref startIndex);
                    if (json[startIndex] == ',') startIndex++;
                }

                Array result = Array.CreateInstance(objType, new[] { list.Count });
                for (int i = 0; i < list.Count; i++)
                {
                    result.SetValue(Convert.ChangeType(list[i], objType), i);
                }

                return result;
            }

            private object BuildDictionary(string json, Type type)
            {
                IDictionary instance = (IDictionary)Activator.CreateInstance(type);
                if (instance is null || instance.ToString() == "null")
                {
                    throw new GameException("Cannot deserialize JSON to new instances of '" + type.Name + "'");
                }


                Type keyType = type.GenericTypeArguments[0];
                Type itemType = type.GenericTypeArguments[1];
                int startIndex = 1;
                Tuple<object, object> keyValue;

                while (startIndex < json.Length - 2)
                {
                    keyValue = ReadKeyAndValue(json, keyType, itemType, ref startIndex);
                    instance.Add(keyValue.Item1, keyValue.Item2);
                    ReadWhitespace(json, ref startIndex);
                    if (json[startIndex] == ',') startIndex++;
                }

                return instance;
            }

            private object BuildList(string json, Type type)
            {
                IList instance = (IList)Activator.CreateInstance(type);
                if (instance is null || instance.ToString() == "null")
                {
                    throw new GameException("Cannot deserialize JSON to new instances of '" + type.Name + "'");
                }

                Type itemType = type.GenericTypeArguments[0];
                int startIndex = 1;

                ReadWhitespace(json, ref startIndex);
                while (startIndex < json.Length - 2)
                {
                    instance.Add(GetValueNoList(itemType, json, ref startIndex));
                    ReadWhitespace(json, ref startIndex);
                    if (json[startIndex] == ',') startIndex++;
                }

                return instance;
            }

            private void BuildObject(string json, ref object obj, Type type, int startIndex, bool requireClose = true)
            {
                if (json == "null" || json.Length == 0) return;

                List<FieldInfo> fields = GetAppropriateFields(type, obj);
                List<PropertyInfo> properties = GetAppropriateProperties(type, obj);


                ReadWhitespace(json, ref startIndex);

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    Type arg = type.GetGenericArguments()[0];
                    obj = GetValue(arg, json, ref startIndex, false);
                    return;
                }

                if (json[startIndex] != '{' && json[startIndex] != '[')
                {
                    throw new GameException($"Invalid object: {json[startIndex]} \n {json}");
                }

                // 分配对象值
                bool objectClosed = false;
                FieldInfo field;
                PropertyInfo property;
                while (!objectClosed && startIndex < json.Length)
                {
                    switch (json[startIndex])
                    {
                        case ' ': // 空行
                        case '\t':
                        case '\r':
                        case '\n':
                            startIndex++;
                            break;
                        case '{': // 新对象开始
                            startIndex++;
                            break;
                        case '[': // 新数组开始
                            startIndex++;
                            break;
                        case '"': // 内容开始
                            // 获取键
                            string literal = ReadKeyName(json, ref startIndex);

                            // 读取分隔符
                            ReadSeparatorAndWhitespace(json, ref startIndex);

                            // 读取值
                            field = GetMatchingField(fields, literal);
                            if (field != null)
                            {
                                field.SetValue(obj, GetValue(field.FieldType, json, ref startIndex));
                            }
                            else
                            {
                                property = GetMatchingProperty(properties, literal);
                                if (property != null)
                                {
                                    property.SetValue(obj, GetValue(property.PropertyType, json, ref startIndex));
                                }
                                else
                                {
                                    throw new GameException(StringUtility.Format("Object does not have a field or property named '{0}'", literal));
                                }
                            }

                            break;
                        case ']':
                            startIndex++;
                            break;
                        case '}':
                            objectClosed = true;
                            startIndex++;
                            break;
                        default:
                            throw new GameException(StringUtility.Format("Unexpected character '{0}' at {1}", json[startIndex], startIndex));
                    }
                }

                if (!objectClosed)
                {
                    throw new GameException("Unexpected end of file");
                }
            }

            private void Deserialize(ref object instance, Type objectType, bool requireClose = true)
            {
                if (IsTypeSimple(objectType) || objectType.IsEnum)
                {
                    BuildObject(_fullJson, ref instance, objectType, 0);
                }
                else
                {
                    if (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        instance = BuildList(_fullJson, objectType);
                    }
                    else if (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        instance = BuildDictionary(_fullJson, objectType);
                    }
                    else if (objectType.IsArray)
                    {
                        instance = BuildArray(_fullJson, objectType);
                    }
                    else
                    {
                        BuildObject(_fullJson, ref instance, objectType, 0);
                    }
                }

                // 反序列化后
                MethodInfo[] methods = objectType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo info in methods)
                {
                    if (info.GetCustomAttribute<JsonAfterDeserializationAttribute>() != null)
                    {
                        info.Invoke(instance, null);
                    }
                }
            }

            private int FindFieldEnd(string json, int startIndex, bool requireClose = true)
            {
                bool inQuotes = false;
                while (startIndex < json.Length)
                {
                    switch (json[startIndex])
                    {
                        case '"':
                            if (startIndex == 0 || json[startIndex - 1] != '\\')
                            {
                                inQuotes = !inQuotes;
                            }

                            break;
                        case ',':
                        case ']':
                        case '}':
                        case ' ':
                        case '\t':
                        case '\r':
                        case '\n':
                            if (!inQuotes)
                            {
                                return startIndex;
                            }

                            break;
                    }

                    startIndex++;
                }

                if (requireClose)
                {
                    throw new GameException("Could not find end of field");
                }

                return json.Length;
            }

            private List<FieldInfo> GetAppropriateFields(Type type, object obj)
            {
                List<FieldInfo> result = new List<FieldInfo>();
                if (obj == null) return result;

                FieldInfo[] fi = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool forceExclude, forceInclude;
                foreach (FieldInfo field in fi)
                {
                    forceExclude = field.Name[0] == '<';

                    if (!forceExclude)
                    {
                        forceInclude = field.GetCustomAttribute<SerializeField>() != null ||
                                       field.GetCustomAttribute<JsonSerializeAttribute>() != null ||
                                       field.GetCustomAttribute<JsonSerializeAsAttribute>() != null;

                        if (forceInclude || (!field.IsInitOnly && !field.IsLiteral && !field.IsPrivate))
                        {
                            result.Add(field);
                        }
                    }
                }

                if (type.BaseType != null && type.BaseType != typeof(object))
                {
                    result.AddRange(GetAppropriateFields(type.BaseType, obj));
                }

                return result;
            }

            private List<PropertyInfo> GetAppropriateProperties(Type type, object obj)
            {
                List<PropertyInfo> result = new List<PropertyInfo>();
                if (obj == null)
                {
                    return result;
                }

                PropertyInfo[] pi = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool forceExclude;
                foreach (PropertyInfo property in pi)
                {
                    forceExclude = property.Name[0] == '<';
                    if (!forceExclude)
                    {
                        foreach (var attrib in property.CustomAttributes)
                        {
                            if (attrib.AttributeType == typeof(JsonDoNotSerializeAttribute))
                            {
                                forceExclude = true;
                                break;
                            }
                        }

                        if (!forceExclude)
                        {
                            try
                            {
                                if (property.CanRead && property.CanWrite)
                                {
                                    result.Add(property);
                                }
                            }
                            catch { }
                        }
                    }
                }

                return result;
            }

            private FieldInfo GetMatchingField(List<FieldInfo> fields, string name)
            {
                foreach (FieldInfo info in fields)
                {
                    JsonSerializeAsAttribute serializeAs = (JsonSerializeAsAttribute)info.GetCustomAttribute(typeof(JsonSerializeAsAttribute));
                    FormerlySerializedAsAttribute formerSerialize = (FormerlySerializedAsAttribute)info.GetCustomAttribute(typeof(FormerlySerializedAsAttribute));
                    if (serializeAs != null && serializeAs.SerializeName == name)
                    {
                        return info;
                    }
                    else if (formerSerialize != null && formerSerialize.oldName == name)
                    {
                        return info;
                    }
                    else if (info.Name == name)
                    {
                        return info;
                    }
                }

                return null;
            }

            private PropertyInfo GetMatchingProperty(List<PropertyInfo> properties, string name)
            {
                foreach (PropertyInfo info in properties)
                {
                    JsonSerializeAsAttribute serializeAs = (JsonSerializeAsAttribute)info.GetCustomAttribute(typeof(JsonSerializeAsAttribute));
                    if (serializeAs != null && serializeAs.SerializeName == name)
                    {
                        return info;
                    }
                    else if (info.Name == name)
                    {
                        return info;
                    }
                }

                return null;
            }

            private object GetValue(Type type, string json, ref int startIndex, bool requireClose = true)
            {
                if (type.IsEnum)
                {
                    return Enum.Parse(type, ReadString(json, ref startIndex, requireClose));
                }
                else if (type == typeof(string))
                {
                    return ReadString(json, ref startIndex, requireClose);
                }
                else if (type == typeof(bool))
                {
                    return ReadBoolean(json, ref startIndex, requireClose);
                }
                else if (IsTypeNumeric(type))
                {
                    return ReadNumber(json, type, ref startIndex, requireClose);
                }
                else
                {
                    string localObj = ReadObject(json, ref startIndex);
                    if (startIndex < json.Length && json[startIndex] == ',') startIndex++;

                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        return BuildList(localObj, type);
                    }
                    else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        return BuildDictionary(localObj, type);
                    }
                    else if (type.IsArray)
                    {
                        return BuildArray(localObj, type);
                    }
                    else
                    {
                        JsonDeserializationObject jso = MemoryPool.Acquire<JsonDeserializationObject>();
                        jso.InitFromPool(localObj);
                        object result = jso.Deserialize(type);
                        MemoryPool.Release(jso);
                        return result;
                    }
                }
            }

            private object GetValueNoList(Type type, string json, ref int startIndex)
            {
                int orgStart = startIndex;
                if (type.IsEnum)
                {
                    return Enum.Parse(type, ReadString(json, ref startIndex));
                }
                else if (type == typeof(string))
                {
                    return ReadString(json, ref startIndex);
                }
                else if (type == typeof(bool))
                {
                    return ReadBoolean(json, ref startIndex);
                }
                else if (IsTypeNumeric(type))
                {
                    return ReadNumber(json, type, ref startIndex);
                }
                else
                {
                    string localObj = ReadObject(json, ref startIndex);
                    if (string.IsNullOrEmpty(localObj))
                    {
                        return null;
                    }

                    JsonDeserializationObject jso = MemoryPool.Acquire<JsonDeserializationObject>();
                    jso.InitFromPool(localObj);
                    object result = jso.Deserialize(type);
                    MemoryPool.Release(jso);
                    return result;
                }
            }

            private bool IsTypeNumeric(Type type)
            {
                return type == typeof(decimal) ||
                       type == typeof(byte) || type == typeof(sbyte) ||
                       type == typeof(short) || type == typeof(ushort) ||
                       type == typeof(int) || type == typeof(uint) ||
                       type == typeof(long) || type == typeof(ulong) ||
                       type == typeof(float) || type == typeof(double);
            }

            /// <summary>
            /// 检查字段是否可以直接序列化
            /// </summary>
            /// <param name="type"></param>
            /// <returns></returns>
            private bool IsTypeSimple(Type type)
            {
                return type.IsPrimitive || type == typeof(string) || IsTypeNumeric(type);
            }

            private bool ReadBoolean(string json, ref int startIndex, bool requireClose = true)
            {
                ReadWhitespace(json, ref startIndex);
                int e = FindFieldEnd(json, startIndex, requireClose);

                string result = json.Substring(startIndex, e - startIndex);

                startIndex = e++;
                if (json.Length >= e && json[startIndex] == ',') startIndex++;

                switch (result)
                {
                    case "false":
                    case "FALSE":
                    case "0":
                        return false;
                    case "true":
                    case "TRUE":
                    case "1":
                    case "-1":
                        return true;
                    default:
                        throw new GameException(StringUtility.Format("Invalid value for boolean object: {0}", result));
                }
            }

            private object ReadDictionaryObject(string json, Type type, ref int startIndex)
            {
                if (type.IsEnum)
                {
                    return Enum.Parse(type, ReadString(json, ref startIndex));
                }
                else if (type == typeof(string))
                {
                    return ReadString(json, ref startIndex);
                }
                else if (type == typeof(bool))
                {
                    return ReadBoolean(json, ref startIndex);
                }
                else if (IsTypeNumeric(type))
                {
                    return ReadNumber(json, type, ref startIndex);
                }
                else
                {
                    string localObj = ReadObject(json, ref startIndex);
                    if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        return BuildList(localObj, type);
                    }
                    else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        return BuildDictionary(localObj, type);
                    }
                    else if (type.IsArray)
                    {
                        return BuildArray(localObj, type);
                    }
                    else
                    {
                        JsonDeserializationObject jso = MemoryPool.Acquire<JsonDeserializationObject>();
                        jso.InitFromPool(localObj);
                        object result = jso.Deserialize(type);
                        MemoryPool.Release(jso);
                        return result;
                    }
                }
            }

            private string ReadKeyName(string json, ref int startIndex)
            {
                int i = startIndex + 1;
                while (true)
                {
                    startIndex++;
                    switch (json[startIndex])
                    {
                        case '"':
                            if (json[startIndex - 1] == '\\') continue;
                            startIndex++;
                            return json.Substring(i, startIndex - 1 - i);
                    }
                }
            }

            private Tuple<object, object> ReadKeyAndValue(string json, Type keyType, Type type, ref int startIndex)
            {
                bool keyAssigned = false;
                bool valueAssigned = false;
                object key = null;
                object value = null;

                ReadWhitespace(json, ref startIndex);

                if (json[startIndex++] != '{')
                {
                    throw new GameException("Missing '{'");
                }

                ReadWhitespace(json, ref startIndex);

                string v1 = ReadKeyName(json, ref startIndex);
                switch (v1)
                {
                    case "key":
                        ReadSeparatorAndWhitespace(json, ref startIndex);
                        key = ReadDictionaryObject(json, keyType, ref startIndex);
                        keyAssigned = true;
                        break;
                    case "value":
                        ReadSeparatorAndWhitespace(json, ref startIndex);
                        value = ReadDictionaryObject(json, type, ref startIndex);
                        valueAssigned = true;
                        break;
                    default:
                        throw new GameException(StringUtility.Format("Invalid key name '{0}'", v1));
                }

                ReadWhitespace(json, ref startIndex);

                v1 = ReadKeyName(json, ref startIndex);
                switch (v1)
                {
                    case "key":
                        if (keyAssigned)
                        {
                            throw new GameException("Duplicate key found");
                        }

                        ReadSeparatorAndWhitespace(json, ref startIndex);
                        key = ReadDictionaryObject(json, keyType, ref startIndex);
                        keyAssigned = true;
                        break;
                    case "value":
                        if (valueAssigned)
                        {
                            throw new GameException("Duplicate value found");
                        }

                        ReadSeparatorAndWhitespace(json, ref startIndex);
                        value = ReadDictionaryObject(json, type, ref startIndex);
                        valueAssigned = true;
                        break;
                    default:
                        throw new GameException(StringUtility.Format("Invalid key name '{0}'", v1));
                }

                ReadWhitespace(json, ref startIndex);

                if (json[startIndex++] != '}')
                {
                    throw new GameException("Missing '}'");
                }

                if (json[startIndex] == ',') startIndex++;

                if (!keyAssigned) throw new GameException("No key assigned");
                if (!valueAssigned) throw new GameException("No value assigned");

                return new Tuple<object, object>(key, value);
            }

            private object ReadNumber(string json, Type type, ref int startIndex, bool requireClose = true)
            {
                ReadWhitespace(json, ref startIndex);
                int e = FindFieldEnd(json, startIndex, requireClose);
                string value = json.Substring(startIndex, e - startIndex);

                startIndex = e++;
                if (json.Length >= e && json[startIndex] == ',') startIndex++;

                if (value.StartsWith("\"") && value.EndsWith("\""))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                // 浮点类型
                if (type == typeof(decimal))
                {
                    return decimal.Parse(value, CultureInfo.InvariantCulture);
                }
                else if (type == typeof(float))
                {
                    return float.Parse(value, CultureInfo.InvariantCulture);
                }
                else if (type == typeof(double))
                {
                    return double.Parse(value, CultureInfo.InvariantCulture);
                }

                // 整数类型
                return Convert.ChangeType(value, type);
            }

            private string ReadObject(string json, ref int startIndex)
            {
                ReadWhitespace(json, ref startIndex);

                int i = startIndex;
                int arrayCount = 0;
                int objectCount = 0;
                bool isObject = json[startIndex] == '{';
                bool isArray = json[startIndex] == '[';
                bool inQuotes = false;

                if (!isObject && !isArray)
                {
                    return ReadString(json, ref startIndex);
                }

                while (startIndex < json.Length)
                {
                    switch (json[startIndex])
                    {
                        case '{':
                            if (!inQuotes)
                            {
                                objectCount += 1;
                            }

                            break;
                        case '[':
                            if (!inQuotes)
                            {
                                arrayCount += 1;
                            }

                            break;
                        case '}':
                            if (!inQuotes)
                            {
                                objectCount -= 1;
                                if (isObject && objectCount == 0)
                                {
                                    startIndex++;
                                    return json.Substring(i, startIndex - i);
                                }
                            }

                            break;
                        case ']':
                            if (!inQuotes)
                            {
                                arrayCount -= 1;
                                if (isArray && arrayCount == 0)
                                {
                                    startIndex++;
                                    return json.Substring(i, startIndex - i);
                                }
                            }

                            break;
                        case '\"':
                            if (json[startIndex - 1] != '\\')
                            {
                                inQuotes = !inQuotes;
                            }

                            break;
                        case ',':
                        case '\r':
                        case '\n':
                        case '\t':
                        case ' ':
                            if (!inQuotes)
                            {
                                if (isObject && objectCount == 0)
                                {
                                    startIndex++;
                                    return json.Substring(i, startIndex - 1 - i);
                                }
                                else if (isArray && arrayCount == 0)
                                {
                                    startIndex++;
                                    return json.Substring(i, startIndex - 1 - i);
                                }
                                else if (!isObject && !isArray)
                                {
                                    // 到此结束
                                    if (objectCount != 0)
                                    {
                                        throw new GameException("Missing '}'");
                                    }

                                    if (arrayCount != 0)
                                    {
                                        throw new GameException("Missing ']'");
                                    }

                                    startIndex++;
                                    return json.Substring(i, startIndex - 1 - i);
                                }
                            }

                            break;
                    }

                    startIndex++;
                }

                return json.Substring(i);
            }

            private void ReadSeparator(string json, ref int startIndex)
            {
                while (true)
                {
                    switch (json[startIndex])
                    {
                        case ':':
                            startIndex++;
                            return;
                        case ' ':
                        case '\r':
                        case '\n':
                        case '\t':
                            break;
                        default:
                            throw new GameException(StringUtility.Format("Unexpected character at {0}", startIndex));
                    }

                    startIndex++;
                }
            }

            private void ReadSeparatorAndWhitespace(string json, ref int startIndex)
            {
                ReadSeparator(json, ref startIndex);
                ReadWhitespace(json, ref startIndex);
            }

            private string ReadString(string json, ref int startIndex, bool requireClose = true)
            {
                ReadWhitespace(json, ref startIndex);
                int e = FindFieldEnd(json, startIndex, requireClose);
                string value = json.Substring(startIndex, e - startIndex);

                startIndex = e++;
                if (json.Length >= e && json[startIndex] == ',') startIndex++;

                if (value.Length > 0 && value[0] == '"')
                {
                    if (value[value.Length - 1] != '"')
                    {
                        // 带有未终止引号的字符串
                        throw new GameException("String with unterminated quotes");
                    }
                    else
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                }

                return UnescapeString(value);
            }

            private void ReadWhitespace(string json, ref int startIndex)
            {
                if (startIndex >= json.Length) return;

                while (true)
                {
                    switch (json[startIndex])
                    {
                        case ' ':
                        case '\r':
                        case '\n':
                        case '\t':
                            break;
                        default:
                            return;
                    }

                    startIndex++;
                }
            }

            private string UnescapeString(string input)
            {
                var unescaped = StringUtility.CreateStringBuilder(input.Length);

                for (int i = 0; i < input.Length; i++)
                {
                    char currentChar = input[i];

                    // 检查转义字符 \
                    if (currentChar == '\\')
                    {
                        i++; // 移动到 \ 之后的下一个字符
                        if (i >= input.Length)
                            break;

                        char escapeChar = input[i];
                        switch (escapeChar)
                        {
                            case '"':
                                unescaped.Append('"');
                                break;
                            case '\\':
                                unescaped.Append('\\');
                                break;
                            case '/':
                                unescaped.Append('/');
                                break;
                            case 'b':
                                unescaped.Append('\b');
                                break;
                            case 'f':
                                unescaped.Append('\f');
                                break;
                            case 'n':
                                unescaped.Append('\n');
                                break;
                            case 'r':
                                unescaped.Append('\r');
                                break;
                            case 't':
                                unescaped.Append('\t');
                                break;
                            case 'u':
                                // 处理 Unicode 转义序列
                                if (i + 4 < input.Length && uint.TryParse(input.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out uint unicodeValue))
                                {
                                    unescaped.Append((char)unicodeValue);
                                    i += 4;
                                }
                                else
                                {
                                    // 无效的 Unicode 转义序列，将其视为常规"u"字符
                                    unescaped.Append('u');
                                }

                                break;
                            default:
                                // 无法识别的转义序列，忽略转义字符
                                unescaped.Append(escapeChar);
                                break;
                        }
                    }
                    else
                    {
                        unescaped.Append(currentChar);
                    }
                }

                return unescaped.ToStringAndDispose();
            }

            #endregion

        }
    }
}
