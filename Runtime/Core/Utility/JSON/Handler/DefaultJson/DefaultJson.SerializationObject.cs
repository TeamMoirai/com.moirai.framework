using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        internal static int maxDepth = 25;

        internal class JsonSerializationObject : MemoryObject
        {

            #region 结构体 [STRUCTURES]

            private struct JsonTargetData
            {

                #region 变量 [VARIABLES]

                public Type type;
                public string serializeName;
                public object value;

                #endregion

                #region 构造函数 [CONSTRUCTORS]

                public JsonTargetData(Type type, object value)
                {
                    this.type = type;
                    this.value = value;
                    serializeName = type.Name;
                }

                public JsonTargetData(string name, Type type, object value)
                {
                    serializeName = name;
                    this.type = type;
                    this.value = value;
                }

                #endregion

            }

            #endregion

            #region 变量 [VARIABLES]

            private StringHandler.IStringBuilder _sb;
            private List<Tuple<string, string>> _values;

            #endregion

            #region 属性 [PROPERTIES]

            public string Value => _sb.ToStringAndDispose();

            #endregion

            #region 构造函数 [CONSTRUCTOR]

            public JsonSerializationObject()
            {
                _values = new List<Tuple<string, string>>();
            }

            public override void InitFromPool()
            {
                _sb = null;
                _values.Clear();
            }

            public void InitFromPool(object obj, bool removeNulls, bool readable, int indentLevel = 0)
            {
                _sb = StringUtility.CreateStringBuilder();
                _values.Clear();

                if (indentLevel >= maxDepth)
                {
                    Log.Warning("<color=#2E9219>[DefaultJson]</color> Max depth reached:{0}", obj.ToString());
                    return;
                }

                if (obj == null)
                {
                    _sb.Append("null");
                    return;
                }

                Type objectType = obj.GetType();

                // 预序列化
                MethodInfo[] methods = objectType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (MethodInfo info in methods)
                {
                    if (info.GetCustomAttribute<JsonBeforeSerializationAttribute>() != null)
                    {
                        info.Invoke(obj, null);
                    }
                }

                if (IsTypeSimple(objectType) || objectType.IsEnum)
                {
                    _sb.Append(GetSimpleSerializedValue(obj));
                }
                else
                {
                    if (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(List<>))
                    {
                        BuildList(obj, removeNulls, readable, indentLevel);
                    }
                    else if (objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        BuildDictionary(obj, removeNulls, readable, indentLevel);
                    }
                    else if (objectType.IsArray)
                    {
                        BuildArray(obj, removeNulls, readable, indentLevel);
                    }
                    else
                    {
                        BuildObject(obj, objectType, removeNulls, readable, indentLevel);
                    }
                }
            }

            public override void RecycleToPool()
            {
            }

            public override void Clear()
            {
                _sb = null;
                _values.Clear();
            }

            #endregion

            #region 私有方法 [PRIVATE METHODS]

            private string SerializeChild(object childObj, bool removeNulls, bool readable, int level)
            {
                JsonSerializationObject jso = MemoryPool.Acquire<JsonSerializationObject>();
                jso.InitFromPool(childObj, removeNulls, readable, level);
                string result = jso.Value;
                MemoryPool.Release(jso);
                return result;
            }

            private void BuildArray(object obj, bool removeNulls, bool readable, int level)
            {
                Array list = (Array)obj;

                if (list.Length == 0)
                {
                    _sb.Append("[]");
                    return;
                }

                Type type = obj.GetType().GetElementType();
                bool isSimple = IsTypeSimple(type) || type.IsEnum;
                if (!isSimple && readable)
                {
                    _sb.Append("\r\n");
                    _sb.Append('\t', level);
                }

                _sb.Append("[");
                level++;
                for (int i = 0; i < list.Length; i++)
                {
                    if (isSimple)
                    {
                        if (i > 0) _sb.Append(", ");
                        _sb.Append(GetSimpleSerializedValue(list.GetValue(i)));
                    }
                    else
                    {
                        if (i > 0) _sb.Append(readable ? ", " : ",");

                        _sb.Append(SerializeChild(list.GetValue(i), removeNulls, readable, level));
                    }
                }

                level--;
                if (!isSimple && readable)
                {
                    _sb.Append("\r\n");
                    _sb.Append('\t', level);
                }

                _sb.Append("]");
            }

            private void BuildDictionary(object obj, bool removeNulls, bool readable, int level)
            {
                IDictionary dictionary = (IDictionary)obj;
                if (dictionary.Count == 0)
                {
                    _sb.Append("[]");
                    return;
                }

                Type[] types = obj.GetType().GetGenericArguments();

                bool isKeySimple = IsTypeSimple(types[0]) || types[0].IsEnum;

                bool isSimple = IsTypeSimple(types[1]) || types[1].IsEnum;

                if (readable)
                {
                    _sb.Append("\r\n");
                    _sb.Append('\t', level);
                }

                _sb.Append("[");
                level++;

                bool isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        _sb.Append(readable ? ", " : ",");
                    }

                    if (readable)
                    {
                        _sb.Append("\r\n");
                        _sb.Append('\t', level);
                    }

                    _sb.Append("{");
                    level++;
                    if (readable)
                    {
                        _sb.Append("\r\n");
                        _sb.Append('\t', level);
                    }

                    // Key
                    _sb.Append("\"key\":");
                    if (readable) _sb.Append(' ');

                    if (isKeySimple)
                    {
                        _sb.Append(GetSimpleSerializedValue(entry.Key));
                    }
                    else
                    {
                        _sb.Append(SerializeChild(entry.Key, removeNulls, readable, level));
                    }

                    _sb.Append(",");

                    // Entry
                    if (readable)
                    {
                        _sb.Append("\r\n");
                        _sb.Append('\t', level);
                    }

                    _sb.Append("\"value\":");
                    if (readable) _sb.Append(' ');
                    if (isSimple)
                    {
                        _sb.Append(GetSimpleSerializedValue(entry.Value));
                    }
                    else
                    {
                        _sb.Append(SerializeChild(entry.Value, removeNulls, readable, level));
                    }

                    level--;
                    if (readable)
                    {
                        _sb.Append("\r\n");
                        _sb.Append('\t', level);
                    }

                    _sb.Append("}");
                }

                level--;
                if (readable)
                {
                    _sb.Append("\r\n");
                    _sb.Append('\t', level);
                }

                _sb.Append("]");
            }

            private void BuildList(object obj, bool removeNulls, bool readable, int level)
            {
                IList list = (IList)obj;

                if (list.Count == 0)
                {
                    _sb.Append("[]");
                    return;
                }

                Type type = list.GetType().GenericTypeArguments[0];
                bool isSimple = IsTypeSimple(type) || type.IsEnum;
                if (!isSimple && readable)
                {
                    _sb.Append("\r\n");
                    _sb.Append('\t', level);
                }

                _sb.Append("[");
                level++;
                for (int i = 0; i < list.Count; i++)
                {
                    if (isSimple)
                    {
                        if (i > 0) _sb.Append(", ");
                        _sb.Append(GetSimpleSerializedValue(list[i]));
                    }
                    else
                    {
                        if (i > 0) _sb.Append(readable ? ", " : ",");

                        _sb.Append(SerializeChild(list[i], removeNulls, readable, level));
                    }
                }

                level--;
                if (!isSimple && readable)
                {
                    _sb.Append("\r\n");
                    _sb.Append('\t', level);
                }

                _sb.Append("]");
            }

            private void BuildObject(object obj, Type type, bool removeNulls, bool readable, int level)
            {
                Type entryType;
                _sb.Clear();
                _values.Clear();

                // 处理字段
                List<JsonTargetData> fields = GetAppropriateFields(type, obj, removeNulls);
                foreach (JsonTargetData field in fields)
                {
                    entryType = field.type;
                    if (IsTypeSimple(entryType) || entryType.IsEnum)
                    {
                        _values.Add(new Tuple<string, string>(field.serializeName, GetSimpleSerializedValue(field.value)));
                    }
                    else
                    {
                        string serialized = SerializeChild(field.value, removeNulls, readable, level + 1);
                        _values.Add(new Tuple<string, string>(field.serializeName, serialized));
                    }
                }

                // 处理属性
                List<JsonTargetData> properties = GetAppropriateProperties(type, obj, removeNulls);
                foreach (JsonTargetData property in properties)
                {
                    entryType = property.type;
                    if (IsTypeSimple(entryType) || entryType.IsEnum)
                    {
                        _values.Add(new Tuple<string, string>(property.serializeName, GetSimpleSerializedValue(property.value)));
                    }
                    else
                    {
                        string serialized = SerializeChild(property.value, removeNulls, readable, level + 1);
                        _values.Add(new Tuple<string, string>(property.serializeName, serialized));
                    }
                }

                // 构建字符串
                if (readable)
                {
                    if (level > 0) _sb.Append("\r\n");
                    _sb.Append('\t', level++);
                    _sb.Append('{');

                    for (int i = 0; i < _values.Count; i++)
                    {
                        if (i > 0) _sb.Append(",");
                        _sb.Append("\r\n");
                        _sb.Append('\t', level);
                        _sb.Append('"');
                        _sb.Append(_values[i].Item1);
                        _sb.Append("\": ");
                        _sb.Append(_values[i].Item2);
                    }

                    _sb.Append("\r\n");
                    level--;
                    _sb.Append('\t', level);
                    _sb.Append('}');
                }
                else
                {
                    _sb.Append('{');
                    for (int i = 0; i < _values.Count; i++)
                    {
                        if (i > 0) _sb.Append(",");
                        _sb.Append('"');
                        _sb.Append(_values[i].Item1);
                        _sb.Append("\":");
                        _sb.Append(_values[i].Item2);
                    }

                    _sb.Append('}');
                }
            }

            private string EscapeString(string s)
            {
                if (s == null || s.Length == 0)
                {
                    return "";
                }

                char c;
                int i;
                int len = s.Length;
                var sb = StringUtility.CreateStringBuilder(len + 4);
                string t;

                for (i = 0; i < len; i += 1)
                {
                    c = s[i];
                    switch (c)
                    {
                        case '\\':
                        case '"':
                            sb.Append('\\');
                            sb.Append(c);
                            break;
                        case '/':
                            sb.Append('\\');
                            sb.Append(c);
                            break;
                        case '\b':
                            sb.Append("\\b");
                            break;
                        case '\t':
                            sb.Append("\\t");
                            break;
                        case '\n':
                            sb.Append("\\n");
                            break;
                        case '\f':
                            sb.Append("\\f");
                            break;
                        case '\r':
                            sb.Append("\\r");
                            break;
                        default:
                            if (c < ' ')
                            {
                                t = "000" + string.Format("X", c);
                                sb.Append("\\u" + t.Substring(t.Length - 4));
                            }
                            else
                            {
                                sb.Append(c);
                            }

                            break;
                    }
                }

                return sb.ToString();
            }

            private List<JsonTargetData> GetAppropriateFields(Type type, object obj, bool removeNulls)
            {
                string useName;
                List<JsonTargetData> result = new List<JsonTargetData>();
                if (obj == null || IsTypeForbidden(type)) return result;
                object fieldValue;

                FieldInfo[] fi = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool forceExclude, forceInclude;
                foreach (FieldInfo field in fi)
                {
                    forceExclude = field.Name[0] == '<' || IsTypeForbidden(field.FieldType) ||
                                   field.GetCustomAttribute<JsonDoNotSerializeAttribute>() != null;

                    if (!forceExclude)
                    {
                        forceInclude = field.GetCustomAttribute<SerializeField>() != null ||
                                       field.GetCustomAttribute<JsonSerializeAttribute>() != null ||
                                       field.GetCustomAttribute<JsonSerializeAsAttribute>() != null;
                        if (forceInclude || (!field.IsInitOnly && !field.IsLiteral && !field.IsPrivate))
                        {
                            useName = GetUseName(field.Name,
                                field.GetCustomAttribute(typeof(JsonSerializeAsAttribute)));

                            fieldValue = field.GetValue(obj);
                            if (fieldValue != null || !removeNulls)
                            {
                                result.Add(new JsonTargetData
                                    { serializeName = useName, type = field.FieldType, value = fieldValue });
                            }
                        }
                    }
                }

                if (type.BaseType != null && type.BaseType != typeof(object))
                {
                    List<JsonTargetData> subResults = GetAppropriateFields(type.BaseType, obj, removeNulls);
                    foreach (var data in subResults)
                    {
                        if (result.Where(_ => _.serializeName == data.serializeName).Count() == 0)
                        {
                            result.Add(data);
                        }
                    }
                }

                return result;
            }

            private List<JsonTargetData> GetAppropriateProperties(Type type, object obj, bool removeNulls)
            {
                string useName;
                List<JsonTargetData> result = new List<JsonTargetData>();
                if (obj == null || IsTypeForbidden(type)) return result;
                object propertyValue;

                PropertyInfo[] pi = type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                bool forceExclude, forceInclude;
                foreach (PropertyInfo property in pi)
                {
                    forceExclude = property.Name[0] == '<' || IsTypeForbidden(property.PropertyType) ||
                                   property.GetCustomAttribute<JsonDoNotSerializeAttribute>() != null ||
                                   property.GetIndexParameters().Length > 0;
                    forceInclude = property.GetCustomAttribute<SerializeField>() != null ||
                                   property.GetCustomAttribute<JsonSerializeAttribute>() != null ||
                                   property.GetCustomAttribute<JsonSerializeAsAttribute>() != null;

                    if (!forceExclude)
                    {
                        if (forceInclude || (property.CanRead && property.GetSetMethod() != null))
                        {
                            useName = GetUseName(property.Name, property.GetCustomAttribute(typeof(JsonSerializeAsAttribute)));

                            propertyValue = type.GetProperty(property.Name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(obj);
                            if (propertyValue != null || !removeNulls)
                            {
                                result.Add(new JsonTargetData
                                {
                                    serializeName = useName,
                                    type = property.PropertyType,
                                    value = propertyValue
                                });
                            }
                        }
                    }
                }

                return result;
            }

            private string GetSimpleSerializedValue(object value)
            {
                if (value == null)
                {
                    return "null";
                }

                if (value is bool)
                {
                    return ((bool)value) ? "true" : "false";
                }

                if (value is string)
                {
                    return "\"" + EscapeString((string)value) + "\"";
                }

                if (value is int || value is long || value is float || value is double || value is byte ||
                    value is short || value is uint || value is ulong || value is ushort || value is sbyte ||
                    value is decimal)
                {
                    string result = value.ToString();
                    if (result.Contains('.') || result.Contains(','))
                    {
                        return "\"" + result + "\"";
                    }

                    return result;
                }

                return "\"" + EscapeString(value.ToString()) + "\"";
            }

            private string GetUseName(string defaultName, Attribute attribute)
            {
                if (attribute == null) return defaultName;
                return ((JsonSerializeAsAttribute)attribute).SerializeName;
            }

            private bool IsTypeForbidden(Type type)
            {
                return
                    type == typeof(UnityEvent) ||
                    type == typeof(Sprite)
                    ;
            }

            /// <summary>
            /// 检查字段是否可以直接序列化
            /// </summary>
            /// <param name="type"></param>
            /// <returns></returns>
            private bool IsTypeSimple(Type type)
            {
                return type.IsPrimitive ||
                       type == typeof(bool) ||
                       type == typeof(string) ||
                       type == typeof(decimal) ||
                       type == typeof(byte) || type == typeof(sbyte) ||
                       type == typeof(short) || type == typeof(ushort) ||
                       type == typeof(int) || type == typeof(uint) ||
                       type == typeof(long) || type == typeof(ulong) ||
                       type == typeof(float) || type == typeof(double);
            }

            #endregion
        }
    }
}
