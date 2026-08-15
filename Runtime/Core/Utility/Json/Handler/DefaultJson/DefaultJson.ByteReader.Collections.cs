using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Moirai.Atropos
{
    public static partial class DefaultJson
    {
        // ByteReader 的集合解析部分（partial 拆分自 DefaultJson.ByteReader.cs）：
        // 类型化注册表 + 列表/数组/字典（标准与 legacy 格式）。
        internal sealed partial class ByteReader
        {
            #region 类型化数组注册 [TYPED ARRAY REGISTRY]
            /// <summary>类型化读取器注册表：按元素类型闭包静态化，消除值类型数组解析的逐元素装箱。</summary>
            private static class TypedRead<T> where T : struct
            {
                public static Func<ByteReader, T> Read;
            }

            static ByteReader()
            {
                TypedRead<bool>.Read = r => r.ReadBooleanToken();
                TypedRead<sbyte>.Read = r => r.ReadIntegralSByte();
                TypedRead<byte>.Read = r => r.ReadIntegralByte();
                TypedRead<short>.Read = r => r.ReadIntegralInt16();
                TypedRead<ushort>.Read = r => r.ReadIntegralUInt16();
                TypedRead<int>.Read = r => r.ReadIntegralInt32();
                TypedRead<uint>.Read = r => r.ReadIntegralUInt32();
                TypedRead<long>.Read = r => r.ReadInt64Token();
                TypedRead<ulong>.Read = r => r.ReadUInt64Token();
                TypedRead<float>.Read = r => r.ReadSingleToken();
                TypedRead<double>.Read = r => r.ReadDoubleToken();
                TypedRead<decimal>.Read = r => r.ReadDecimalToken();
            }

            #endregion

            #region 集合 [COLLECTIONS]
            private void ParseList(Type type, IList list)
            {
                Type itemType = type.GenericTypeArguments[0];

                list.Clear();

                // 类型化基元列表快速路径：具体类型模式匹配（AOT 安全），消除逐元素 ParseValue 分派与 IList.Add 装箱
                switch (list)
                {
                    case List<int> l:
                        ParseTypedList(l, TypedRead<int>.Read);
                        return;
                    case List<long> l:
                        ParseTypedList(l, TypedRead<long>.Read);
                        return;
                    case List<float> l:
                        ParseTypedList(l, TypedRead<float>.Read);
                        return;
                    case List<double> l:
                        ParseTypedList(l, TypedRead<double>.Read);
                        return;
                    case List<bool> l:
                        ParseTypedList(l, TypedRead<bool>.Read);
                        return;
                    case List<sbyte> l:
                        ParseTypedList(l, TypedRead<sbyte>.Read);
                        return;
                    case List<byte> l:
                        ParseTypedList(l, TypedRead<byte>.Read);
                        return;
                    case List<short> l:
                        ParseTypedList(l, TypedRead<short>.Read);
                        return;
                    case List<ushort> l:
                        ParseTypedList(l, TypedRead<ushort>.Read);
                        return;
                    case List<uint> l:
                        ParseTypedList(l, TypedRead<uint>.Read);
                        return;
                    case List<ulong> l:
                        ParseTypedList(l, TypedRead<ulong>.Read);
                        return;
                    case List<decimal> l:
                        ParseTypedList(l, TypedRead<decimal>.Read);
                        return;
                }

                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    _depth++;
                    object value = ParseValue(itemType);
                    _depth--;
                    list.Add(value);

                    SkipWhitespace();
                    byte c = Peek();
                    if (c == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == (byte)']')
                    {
                        _pos++;
                        return;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            /// <summary>类型化基元列表解析骨架（基元元素不可嵌套，无需深度计数）。</summary>
            private void ParseTypedList<T>(List<T> list, Func<ByteReader, T> read) where T : struct
            {
                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                    return;
                }

                while (true)
                {
                    list.Add(read(this));

                    SkipWhitespace();
                    byte c = Peek();
                    if (c == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == (byte)']')
                    {
                        _pos++;
                        return;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                }
            }

            private object ParseArray(Type type)
            {
                Type elementType = type.GetElementType();

                // 类型化零装箱快速路径（值类型基元数组 + string）
                if (elementType == typeof(string))
                {
                    return ParseStringArray();
                }

                // 枚举数组快路径：必须置于 TypeCode switch 之前——Type.GetTypeCode(enumType) 返回底层类型码
                // （如 Int32），枚举数组会被误路由进 ParsePrimitiveArray<int> 返回 int[]（元素类型错误）。
                // 按底层类型快读 + Enum.ToObject，免除逐元素 ChangeType。
                if (elementType.IsEnum)
                {
                    return ParseEnumArray(elementType);
                }

                switch (Type.GetTypeCode(elementType))
                {
                    case TypeCode.Boolean: return ParsePrimitiveArray<bool>();
                    case TypeCode.SByte: return ParsePrimitiveArray<sbyte>();
                    case TypeCode.Byte: return ParsePrimitiveArray<byte>();
                    case TypeCode.Int16: return ParsePrimitiveArray<short>();
                    case TypeCode.UInt16: return ParsePrimitiveArray<ushort>();
                    case TypeCode.Int32: return ParsePrimitiveArray<int>();
                    case TypeCode.UInt32: return ParsePrimitiveArray<uint>();
                    case TypeCode.Int64: return ParsePrimitiveArray<long>();
                    case TypeCode.UInt64: return ParsePrimitiveArray<ulong>();
                    case TypeCode.Single: return ParsePrimitiveArray<float>();
                    case TypeCode.Double: return ParsePrimitiveArray<double>();
                    case TypeCode.Decimal: return ParsePrimitiveArray<decimal>();
                }

                // 回退路径（char/对象元素）
                var elements = new List<object>();

                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        _depth++;
                        object value = ParseValue(elementType);
                        _depth--;

                        if (!elementType.IsInstanceOfType(value) && value != null)
                        {
                            try
                            {
                                value = Convert.ChangeType(value, elementType, CultureInfo.InvariantCulture);
                            }
                            catch (Exception)
                            {
                                Throw(StringUtility.Format("Cannot convert '{0}' to element type '{1}'.", value, elementType.Name));
                            }
                        }

                        elements.Add(value);

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                Array result = Array.CreateInstance(elementType, elements.Count);
                for (int i = 0; i < elements.Count; i++)
                {
                    result.SetValue(elements[i], i);
                }

                return result;
            }

            /// <summary>基元数组零装箱解析（经 TypedRead 注册表按元素类型分派）。</summary>
            private T[] ParsePrimitiveArray<T>() where T : struct
            {
                Func<ByteReader, T> read = TypedRead<T>.Read;

                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<T>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add(read(this));

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            /// <summary>枚举数组解析：按底层整数类型走类型化 token 读取 + Enum.ToObject（免除逐元素 ChangeType）。</summary>
            private Array ParseEnumArray(Type elementType)
            {
                Type underlying = Enum.GetUnderlyingType(elementType);

                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<object>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        // 名称形式的枚举元素（字符串）走通用 ParseValue；数值形式按底层类型快读
                        SkipWhitespace();
                        object raw = Peek() == (byte)'"'
                            ? ParseValue(elementType)
                            : ParseNumberSpanBytes(underlying, ScanNumberToken());
                        tmp.Add(raw is IConvertible ? Enum.ToObject(elementType, raw) : raw);

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                Array result = Array.CreateInstance(elementType, tmp.Count);
                for (int i = 0; i < tmp.Count; i++)
                {
                    result.SetValue(tmp[i], i);
                }

                return result;
            }

            private string[] ParseStringArray()
            {
                Expect((byte)'[');

                SkipWhitespace();
                var tmp = new List<string>(16);
                if (Peek() == (byte)']')
                {
                    _pos++;
                }
                else
                {
                    while (true)
                    {
                        tmp.Add((string)ParseValue(typeof(string)));

                        SkipWhitespace();
                        byte c = Peek();
                        if (c == (byte)',')
                        {
                            _pos++;
                            continue;
                        }

                        if (c == (byte)']')
                        {
                            _pos++;
                            break;
                        }

                        Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)c));
                    }
                }

                return tmp.ToArray();
            }

            private object ParseDictionary(Type type, IDictionary existing)
            {
                IDictionary dict = existing ?? (IDictionary)Activator.CreateInstance(type);
                Type keyType = type.GenericTypeArguments[0];
                Type valueType = type.GenericTypeArguments[1];

                if (existing != null) dict.Clear();

                Expect((byte)'{');

                SkipWhitespace();
                if (Peek() == (byte)'}')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != (byte)'"')
                    {
                        Throw(StringUtility.Format("Expected a dictionary key (string) but found '{0}'.", (char)Peek()));
                    }

                    string keyString = ReadStringMaterialized();
                    object key = ConvertDictionaryKey(keyString, keyType);

                    SkipWhitespace();
                    Expect((byte)':');

                    _depth++;
                    object value = ParseValue(valueType);
                    _depth--;

                    dict[key] = value;

                    SkipWhitespace();
                    byte c = Peek();
                    if (c == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (c == (byte)'}')
                    {
                        _pos++;
                        return dict;
                    }

                    Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                }
            }

            private object ParseDictionaryLegacy(Type type, IDictionary existing = null)
            {
                IDictionary dict = existing ?? (IDictionary)Activator.CreateInstance(type);
                Type keyType = type.GenericTypeArguments[0];
                Type valueType = type.GenericTypeArguments[1];

                if (existing != null) dict.Clear(); // 覆盖语义：清空后按 JSON 重建

                Expect((byte)'[');

                SkipWhitespace();
                if (Peek() == (byte)']')
                {
                    _pos++;
                    return dict;
                }

                while (true)
                {
                    Expect((byte)'{');

                    object key = null;
                    object value = null;
                    bool keyAssigned = false;
                    bool valueAssigned = false;

                    SkipWhitespace();
                    if (Peek() == (byte)'}')
                    {
                        _pos++;
                    }
                    else
                    {
                        while (true)
                        {
                            SkipWhitespace();
                            string member = ReadStringMaterialized();

                            SkipWhitespace();
                            Expect((byte)':');

                            if (member == TypeConverter.KeyMember)
                            {
                                if (keyAssigned) Throw("Duplicate key found.");
                                _depth++;
                                key = ParseValue(keyType);
                                _depth--;
                                keyAssigned = true;
                            }
                            else if (member == TypeConverter.ValueMember)
                            {
                                if (valueAssigned) Throw("Duplicate value found.");
                                _depth++;
                                value = ParseValue(valueType);
                                _depth--;
                                valueAssigned = true;
                            }
                            else
                            {
                                Throw(StringUtility.Format("Invalid dictionary entry member '{0}'.", member));
                            }

                            SkipWhitespace();
                            byte c = Peek();
                            if (c == (byte)',')
                            {
                                _pos++;
                                continue;
                            }

                            if (c == (byte)'}')
                            {
                                _pos++;
                                break;
                            }

                            Throw(StringUtility.Format("Expected ',' or '}}' but found '{0}'.", (char)c));
                        }
                    }

                    if (!keyAssigned || !valueAssigned)
                    {
                        Throw("Dictionary entry requires both 'key' and 'value'.");
                    }

                    dict[key] = value;

                    SkipWhitespace();
                    byte cc = Peek();
                    if (cc == (byte)',')
                    {
                        _pos++;
                        continue;
                    }

                    if (cc == (byte)']')
                    {
                        _pos++;
                        return dict;
                    }

                    Throw(StringUtility.Format("Expected ',' or ']' but found '{0}'.", (char)cc));
                }
            }

            #endregion
        }
    }
}
