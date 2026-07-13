using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace Moirai.Atropos
{
    /// <summary>
    /// 该类包含扩展Stream的方法。
    /// </summary>
    public static class IOExtensions
    {

        #region 字段 [FIELDS]

        // 线程本地缓冲区，以避免在热点路径中进行内存分配。
        [ThreadStatic] private static byte[] s_Buffer2;
        [ThreadStatic] private static byte[] s_Buffer4;
        [ThreadStatic] private static byte[] s_Buffer8;
        [ThreadStatic] private static byte[] s_Buffer16;

        #endregion

        #region 枚举 [ENUMERATIONS]

        public enum StreamObjectTypes : byte
        {
            Boolean = 0,
            Color = 1,
            Double = 2,
            Float = 3,
            Int = 4,
            Long = 5,
            Quaternion = 6,
            Rect = 7,
            String = 8,
            Vector2 = 9,
            Vector3 = 10,
            Binary = 11,
            JSON = 12,
            Char = 13,
            Short = 14,
            UInt = 15,
            ULong = 16,
            UShort = 17,
            SByte = 18,
            Null = 255,
        }

        #endregion

        #region 公共方法 [PUBLIC METHODS]

        /// <summary>
        /// 从流中读取Color值的更改。
        /// </summary>
        /// <param name="stream">要读取的流</param>
        /// <param name="original">原始值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Color ChangeRead(this Stream stream, Color original)
        {
            if (stream.ReadByte() == 0) return original;
            return stream.ReadColor();
        }

        /// <summary>
        /// 从流中读取字符串值的更改。
        /// </summary>
        /// <param name="stream">要读取的流</param>
        /// <param name="original">原始值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static string ChangeRead(this Stream stream, string original, Encoding encoding = null)
        {
            if (stream.ReadByte() == 0) return original;
            return stream.ReadStringPacket(encoding);
        }

        /// <summary>
        /// 从流中读取字符串列表值的更改。
        /// </summary>
        /// <param name="stream">要读取的流</param>
        /// <param name="original">原始值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static List<string> ChangeReadStringList(this Stream stream, List<string> original, Encoding encoding = null)
        {
            if (stream.ReadByte() == 0) return original;
            List<string> result = new List<string>();
            int count = stream.ReadInt();
            for (int i = 0; i < count; i++)
            {
                if (stream.ReadByte() == 0)
                {
                    result.Add(original[i]);
                }
                else
                {
                    result.Add(stream.ReadStringPacket(encoding));
                }
            }

            return result;
        }

        /// <summary>
        /// 将Color值的更改写入流中。
        /// </summary>
        /// <param name="stream">要写入的流</param>
        /// <param name="original">原始值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void ChangeWrite(this Stream stream, Color current, Color original)
        {
            if (current == original)
            {
                stream.WriteByte(0);
            }
            else
            {
                stream.WriteByte(1);
                stream.WriteColor(current);
            }
        }

        /// <summary>
        /// 将字符串值的更改写入流中。
        /// </summary>
        /// <param name="stream">要写入的流</param>
        /// <param name="original">原始值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void ChangeWrite(this Stream stream, string current, string original, Encoding encoding = null)
        {
            if (current == original)
            {
                stream.WriteByte(0);
            }
            else
            {
                stream.WriteByte(1);
                stream.WriteStringPacket(current, encoding);
            }
        }

        /// <summary>
        /// 将字符串列表值的更改写入流中。
        /// </summary>
        /// <param name="stream">要写入的流</param>
        /// <param name="original">原始值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void ChangeWriteStringList(this Stream stream, List<string> current, List<string> original, Encoding encoding = null)
        {
            if (current == null && original == null)
            {
                stream.WriteByte(0);    // 未更改
                return;
            }

            if (current == null && original != null)
            {
                stream.WriteByte(1);    // 已更改
                stream.WriteInt(0);     // 零计数
                return;
            }

            if (current != null && original == null)
            {
                stream.WriteByte(1);    // 已更改
                WriteStringPacketList(stream, current, encoding); // 正常写入
                return;
            }

            if (current.Count == original.Count)
            {
                bool foundChanges = false;
                for (int i = 0; i < current.Count; i++)
                {
                    if (current[i] != original[i])
                    {
                        foundChanges = true;
                        break;
                    }
                }
                if (!foundChanges)
                {
                    stream.WriteByte(0);    // 未更改
                    return;
                }
            }

            stream.WriteByte(1);                // 已更改
            stream.WriteInt(current.Count);     // 当前计数
            for (int i = 0; i < current.Count; i++)
            {
                if (i < original.Count && current[i] == original[i])
                {
                    stream.WriteByte(0);
                }
                else
                {
                    stream.WriteByte(1);
                    stream.WriteStringPacket(current[i], encoding);
                }
            }
        }

        /// <summary>
        /// 从流中读取布尔值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static bool ReadBool(this Stream stream)
        {
            if (stream.ReadByte() > 0) return true;
            return false;
        }

        /// <summary>
        /// 从流中读取可空布尔值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static bool? ReadBoolNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            if (stream.ReadByte() > 0) return true;
            return false;
        }

        /// <summary>
        /// 从流中读取字符。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static char ReadCharacter(this Stream stream)
        {
            byte[] b = GetBuffer2();
            stream.Read(b, 0, 2);
            return BitConverter.ToChar(b, 0);
        }

        /// <summary>
        /// 从流中读取颜色值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Color ReadColor(this Stream stream)
        {
            return new Color(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取可空颜色值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Color? ReadColorNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            return new Color(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取双精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static double ReadDouble(this Stream stream)
        {
            byte[] b = GetBuffer8();
            stream.Read(b, 0, 8);
            return BitConverter.ToDouble(b, 0);
        }

        /// <summary>
        /// 从流中读取可空双精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static double? ReadDoubleNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            byte[] b = GetBuffer8();
            stream.Read(b, 0, 8);
            return BitConverter.ToDouble(b, 0);
        }

        /// <summary>
        /// 从流中读取单精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static float ReadFloat(this Stream stream)
        {
            byte[] b = GetBuffer4();
            stream.Read(b, 0, 4);
            return BitConverter.ToSingle(b, 0);
        }

        /// <summary>
        /// 从流中读取可空单精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static float? ReadFloatNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            byte[] b = GetBuffer4();
            stream.Read(b, 0, 4);
            return BitConverter.ToSingle(b, 0);
        }

        /// <summary>
        /// 从流中读取整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static int ReadInt(this Stream stream)
        {
            byte[] b = GetBuffer4();
            stream.Read(b, 0, 4);
            return BitConverter.ToInt32(b, 0);
        }

        /// <summary>
        /// 从流中读取可空整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static int? ReadIntNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            byte[] b = GetBuffer4();
            stream.Read(b, 0, 4);
            return BitConverter.ToInt32(b, 0);
        }

        /// <summary>
        /// 从流中读取长整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static long ReadLong(this Stream stream)
        {
            byte[] b = GetBuffer8();
            stream.Read(b, 0, 8);
            return BitConverter.ToInt64(b, 0);
        }

        /// <summary>
        /// 从流中读取可空长整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static long? ReadLongNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            byte[] b = GetBuffer8();
            stream.Read(b, 0, 8);
            return BitConverter.ToInt64(b, 0);
        }

        /// <summary>
        /// 从流中读取四元数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Quaternion ReadQuaternion(this Stream stream)
        {
            return new Quaternion(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取可空四元数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Quaternion? ReadQuaternionNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            return new Quaternion(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取矩形。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Rect ReadRect(this Stream stream)
        {
            return new Rect(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取可空矩形。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Rect? ReadRectNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            return new Rect(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取有符号字节。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static sbyte ReadSByte(this Stream stream)
        {
            return unchecked((sbyte)stream.ReadByte());
        }

        /// <summary>
        /// 从流中读取短整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static short ReadShort(this Stream stream)
        {
            byte[] b = GetBuffer2();
            stream.Read(b, 0, 2);
            return BitConverter.ToInt16(b, 0);
        }

        /// <summary>
        /// 从流中读取字符串（数据包格式）。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static string ReadStringPacket(this Stream stream, Encoding encoding = null)
        {
            if (encoding == null) encoding = Encoding.UTF8;
            byte[] b = new byte[stream.ReadLong()];
            stream.Read(b, 0, b.Length);
            return new string(encoding.GetChars(b));
        }

        /// <summary>
        /// 从流中读取数据包格式的字符串列表。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static List<string> ReadStringPacketList(this Stream stream, Encoding encoding = null)
        {
            List<string> result = new List<string>();

            int count = stream.ReadInt();
            for (int i = 0; i < count; i++)
            {
                result.Add(stream.ReadStringPacket(encoding));
            }

            return result;
        }

        /// <summary>
        /// 从流中读取可空字符串（数据包格式）。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static string ReadStringPacketNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            byte[] b = new byte[stream.ReadLong()];
            stream.Read(b, 0, b.Length);
            return new string(Encoding.UTF8.GetChars(b));
        }

        /// <summary>
        /// 从流中读取可空字符串（数据包格式）。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static string ReadStringPacketNullable(this Stream stream, Encoding encoding)
        {
            if (stream.ReadByte() == 0) return null;
            byte[] b = new byte[stream.ReadLong()];
            stream.Read(b, 0, b.Length);
            return new string(encoding.GetChars(b));
        }

        /// <summary>
        /// 从流中读取类型编码的值。基本类型会尽可能以最小格式写入，完整对象则以JSON字符串形式写入。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static object ReadTypeEncodedValue(this Stream stream, Encoding encoding = null)
        {
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }

            switch ((StreamObjectTypes)stream.ReadByte())
            {
                case StreamObjectTypes.Binary:
                    long len = stream.ReadLong();
                    byte[] b = new byte[len];
                    stream.Read(b, 0, b.Length);
                    return b;
                case StreamObjectTypes.Boolean:
                    return stream.ReadBool();
                case StreamObjectTypes.Color:
                    return stream.ReadColor();
                case StreamObjectTypes.Double:
                    return stream.ReadDouble();
                case StreamObjectTypes.Float:
                    return stream.ReadFloat();
                case StreamObjectTypes.Int:
                    return stream.ReadInt();
                case StreamObjectTypes.JSON:
                    Type t = Type.GetType(stream.ReadStringPacket());
                    return JSONUtility.ToObject(t, stream.ReadStringPacket(encoding));
                case StreamObjectTypes.Long:
                    return stream.ReadLong();
                case StreamObjectTypes.Quaternion:
                    return stream.ReadQuaternion();
                case StreamObjectTypes.Rect:
                    return stream.ReadRect();
                case StreamObjectTypes.String:
                    return stream.ReadStringPacket(encoding);
                case StreamObjectTypes.Vector2:
                    return stream.ReadVector2();
                case StreamObjectTypes.Vector3:
                    return stream.ReadVector3();
                case StreamObjectTypes.Char:
                    return stream.ReadCharacter();
                case StreamObjectTypes.Short:
                    return stream.ReadShort();
                case StreamObjectTypes.UInt:
                    return stream.ReadUInt();
                case StreamObjectTypes.ULong:
                    return stream.ReadULong();
                case StreamObjectTypes.UShort:
                    return stream.ReadUShort();
                case StreamObjectTypes.SByte:
                    return stream.ReadSByte();
                default:
                    return null;
            }
        }

        /// <summary>
        /// 从流中读取无符号整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static uint ReadUInt(this Stream stream)
        {
            byte[] b = GetBuffer4();
            stream.Read(b, 0, 4);
            return BitConverter.ToUInt32(b, 0);
        }

        /// <summary>
        /// 从流中读取无符号长整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static ulong ReadULong(this Stream stream)
        {
            byte[] b = GetBuffer8();
            stream.Read(b, 0, 8);
            return BitConverter.ToUInt64(b, 0);
        }

        /// <summary>
        /// 从流中读取无符号短整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static ushort ReadUShort(this Stream stream)
        {
            byte[] b = GetBuffer2();
            stream.Read(b, 0, 2);
            return BitConverter.ToUInt16(b, 0);
        }

        /// <summary>
        /// 从流中读取二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Vector2 ReadVector2(this Stream stream)
        {
            return new Vector2(stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取可空二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Vector2? ReadVector2Nullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            return new Vector2(stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取整数型二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Vector2Int ReadVector2Int(this Stream stream)
        {
            return new Vector2Int(stream.ReadInt(), stream.ReadInt());
        }

        /// <summary>
        /// 从流中读取可空整数型二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Vector2Int? ReadVector2IntNullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            return new Vector2Int(stream.ReadInt(), stream.ReadInt());
        }

        /// <summary>
        /// 从流中读取三维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Vector3 ReadVector3(this Stream stream)
        {
            return new Vector3(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 从流中读取可空三维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static Vector3? ReadVector3Nullable(this Stream stream)
        {
            if (stream.ReadByte() == 0) return null;
            return new Vector3(stream.ReadFloat(), stream.ReadFloat(), stream.ReadFloat());
        }

        /// <summary>
        /// 读取一个整数并与版本号进行比较。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="maxVersion">允许的最大版本号</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static int VersionCheck(this Stream stream, int maxVersion)
        {
            int version = stream.ReadInt();
            if (version < 0 || version > maxVersion) throw new InvalidDataException("Invalid version: " + version);
            return version;
        }

        /// <summary>
        /// 向流中写入布尔值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteBool(this Stream stream, bool value)
        {
            if (value)
            {
                stream.WriteByte(1);
            }
            else
            {
                stream.WriteByte(0);
            }
        }

        /// <summary>
        /// 向流中写入可空布尔值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteBoolNullable(this Stream stream, bool? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            if (value.Value)
            {
                stream.WriteByte(1);
            }
            else
            {
                stream.WriteByte(0);
            }
        }

        /// <summary>
        /// 向流中写入字符。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteCharacter(this Stream stream, char value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入颜色值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteColor(this Stream stream, Color value)
        {
            stream.WriteFloat(value.r);
            stream.WriteFloat(value.g);
            stream.WriteFloat(value.b);
            stream.WriteFloat(value.a);
        }

        /// <summary>
        /// 向流中写入可空颜色值。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteColorNullable(this Stream stream, Color? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteColor((Color)value);
        }

        /// <summary>
        /// 向流中写入双精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteDouble(this Stream stream, double value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入可空双精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteDoubleNullable(this Stream stream, double? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteDouble(value.Value);
        }

        /// <summary>
        /// 向流中写入单精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteFloat(this Stream stream, float value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入可空单精度浮点数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteFloatNullable(this Stream stream, float? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteFloat(value.Value);
        }

        /// <summary>
        /// 向流中写入整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteInt(this Stream stream, int value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入可空整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteIntNullable(this Stream stream, int? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteInt(value.Value);
        }

        /// <summary>
        /// 向流中写入长整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteLong(this Stream stream, long value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入可空长整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteLongNullable(this Stream stream, long? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteLong(value.Value);
        }

        /// <summary>
        /// 向流中写入四元数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteQuaternion(this Stream stream, Quaternion value)
        {
            stream.WriteFloat(value.x);
            stream.WriteFloat(value.y);
            stream.WriteFloat(value.z);
            stream.WriteFloat(value.w);
        }

        /// <summary>
        /// 向流中写入可空四元数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteQuaternionNullable(this Stream stream, Quaternion? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteQuaternion(value.Value);
        }

        /// <summary>
        /// 向流中写入矩形。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteRect(this Stream stream, Rect rect)
        {
            stream.WriteFloat(rect.x);
            stream.WriteFloat(rect.y);
            stream.WriteFloat(rect.width);
            stream.WriteFloat(rect.height);
        }

        /// <summary>
        /// 向流中写入可空矩形。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteRectNullable(this Stream stream, Rect? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteRect(value.Value);
        }

        /// <summary>
        /// 向流中写入有符号字节。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteSByte(this Stream stream, sbyte value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入短整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteShort(this Stream stream, short value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入非数据包格式的字符串。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <param name="encoding">要使用的编码</param>
        [DebuggerStepThrough]
        public static void WriteString(this Stream stream, string value, Encoding encoding = null)
        {
            if (encoding == null) encoding = Encoding.UTF8;
            if (value == null) value = string.Empty;
            byte[] b = encoding.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入字符串（数据包格式）。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteStringPacket(this Stream stream, string value, Encoding encoding = null)
        {
            if (encoding == null) encoding = Encoding.UTF8;
            if (value == null) value = string.Empty;
            byte[] b = encoding.GetBytes(value);
            stream.WriteLong(b.Length);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 写入数据包格式的字符串列表。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <param name="encoding">要使用的编码</param>
        [DebuggerStepThrough]
        public static void WriteStringPacketList(this Stream stream, List<string> value, Encoding encoding = null)
        {
            if (value == null)
            {
                stream.WriteInt(0);
                return;
            }

            if (encoding == null) encoding = Encoding.UTF8;

            stream.WriteInt(value.Count);
            foreach (string val in value)
            {
                stream.WriteStringPacket(val, encoding);
            }
        }

        /// <summary>
        /// 向流中写入可空字符串（数据包格式）。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteStringPacketNullable(this Stream stream, string value, Encoding encoding = null)
        {
            stream.WriteByte((byte)(value != null ? 1 : 0));
            if (value == null) return;
            if (encoding == null) encoding = Encoding.UTF8;

            stream.WriteStringPacket(value, encoding);
        }

        /// <summary>
        /// 向流中写入类型编码的值。基本类型会尽可能以最小格式写入，完整对象则以JSON字符串形式写入。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="encoding">要使用的编码</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteTypeEncodedValue<T>(this Stream stream, T value, Encoding encoding = null)
        {
            if (encoding == null)
            {
                encoding = Encoding.UTF8;
            }

            if (value is null)
            {
                stream.WriteByte(0);
            }
            else if (value is bool boolVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Boolean);
                stream.WriteBool(boolVal);
            }
            else if (value is Color colorVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Color);
                stream.WriteColor(colorVal);
            }
            else if (value is double doubleVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Double);
                stream.WriteDouble(doubleVal);
            }
            else if (value is float floatVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Float);
                stream.WriteFloat(floatVal);
            }
            else if (value is int intVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Int);
                stream.WriteInt(intVal);
            }
            else if (value is long longVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Long);
                stream.WriteLong(longVal);
            }
            else if (value is Quaternion quaternionVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Quaternion);
                stream.WriteQuaternion(quaternionVal);
            }
            else if (value is Rect rectVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Rect);
                stream.WriteRect(rectVal);
            }
            else if (value is string stringVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.String);
                stream.WriteStringPacket(stringVal, encoding);
            }
            else if (value is Vector2 vector2Val)
            {
                stream.WriteByte((byte)StreamObjectTypes.Vector2);
                stream.WriteVector2(vector2Val);
            }
            else if (value is Vector3 vector3Val)
            {
                stream.WriteByte((byte)StreamObjectTypes.Vector3);
                stream.WriteVector3(vector3Val);
            }
            else if (value is byte[] byteArray)
            {
                stream.WriteByte((byte)StreamObjectTypes.Binary);
                stream.WriteLong(byteArray.Length);
                stream.Write(byteArray, 0, byteArray.Length);
            }
            else if (value is char character)
            {
                stream.WriteByte((byte)StreamObjectTypes.Char);
                stream.WriteCharacter(character);
            }
            else if (value is short shortVal)
            {
                stream.WriteByte((byte)StreamObjectTypes.Short);
                stream.WriteShort(shortVal);
            }
            else if (value is uint unsignedInt)
            {
                stream.WriteByte((byte)StreamObjectTypes.UInt);
                stream.WriteUInt(unsignedInt);
            }
            else if (value is ulong unsignedLong)
            {
                stream.WriteByte((byte)StreamObjectTypes.ULong);
                stream.WriteULong(unsignedLong);
            }
            else if (value is ushort unsignedShort)
            {
                stream.WriteByte((byte)StreamObjectTypes.UShort);
                stream.WriteUShort(unsignedShort);
            }
            else if (value is sbyte signedByte)
            {
                stream.WriteByte((byte)StreamObjectTypes.SByte);
                stream.WriteSByte(signedByte);
            }
            else
            {
                stream.WriteByte((byte)StreamObjectTypes.JSON);
                stream.WriteStringPacket(value.GetType().AssemblyQualifiedName);
                stream.WriteStringPacket(JSONUtility.ToJson(value));
            }
        }

        /// <summary>
        /// 向流中写入无符号整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteUInt(this Stream stream, uint value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入无符号长整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteULong(this Stream stream, ulong value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入无符号短整数。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteUShort(this Stream stream, ushort value)
        {
            byte[] b = BitConverter.GetBytes(value);
            stream.Write(b, 0, b.Length);
        }

        /// <summary>
        /// 向流中写入二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteVector2(this Stream stream, Vector2 value)
        {
            stream.WriteFloat(value.x);
            stream.WriteFloat(value.y);
        }

        /// <summary>
        /// 向流中写入可空二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteVector2Nullable(this Stream stream, Vector2? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteVector2(value.Value);
        }

        /// <summary>
        /// 向流中写入整数型二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteVector2Int(this Stream stream, Vector2Int value)
        {
            stream.WriteInt(value.x);
            stream.WriteInt(value.y);
        }

        /// <summary>
        /// 向流中写入可空整数型二维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteVector2IntNullable(this Stream stream, Vector2Int? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteVector2Int(value.Value);
        }

        /// <summary>
        /// 向流中写入三维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteVector3(this Stream stream, Vector3 value)
        {
            stream.WriteFloat(value.x);
            stream.WriteFloat(value.y);
            stream.WriteFloat(value.z);
        }

        /// <summary>
        /// 向流中写入可空三维向量。
        /// </summary>
        /// <param name="stream">要使用的流</param>
        /// <param name="value">要写入的值</param>
        /// <returns></returns>
        [DebuggerStepThrough]
        public static void WriteVector3Nullable(this Stream stream, Vector3? value)
        {
            stream.WriteByte((byte)(value.HasValue ? 1 : 0));
            if (!value.HasValue) return;

            stream.WriteVector3(value.Value);
        }

        #endregion

        #region 私有方法 [PRIVATE METHODS]

        private static byte[] GetBuffer2()
        {
            return s_Buffer2 ??= new byte[2];
        }

        private static byte[] GetBuffer4()
        {
            return s_Buffer4 ??= new byte[4];
        }

        private static byte[] GetBuffer8()
        {
            return s_Buffer8 ??= new byte[8];
        }

        private static byte[] GetBuffer16()
        {
            return s_Buffer16 ??= new byte[16];
        }

        #endregion

    }
}