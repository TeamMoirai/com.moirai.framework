using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <see cref="SaveFieldCapturerGenerator"/> 读写方法名与类型后缀映射表.
    /// </summary>
    public sealed partial class SaveFieldCapturerGenerator
    {

        /// <summary>
        /// 键控写入方法名（枚举走底层类型的写入方法）。
        /// </summary>
        private static string KeyedWriterMethod(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return "Write" + model.EnumUnderlyingName;
            }

            return "Write" + KindSuffix(model.Kind);
        }

        /// <summary>
        /// 元素写入方法名。
        /// </summary>
        private static string ElementWriterMethod(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return "Write" + model.EnumUnderlyingName + "Element";
            }

            return "Write" + KindSuffix(model.Kind) + "Element";
        }

        /// <summary>
        /// 读取方法名。
        /// </summary>
        private static string ReaderMethod(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return "Read" + model.EnumUnderlyingName;
            }

            return "Read" + KindSuffix(model.Kind);
        }

        /// <summary>
        /// 类型名后缀（KVT 写入/读取方法族共用词干）。
        /// </summary>
        private static string KindSuffix(FieldKind kind)
        {
            return kind switch
            {
                FieldKind.Bool => "Boolean",
                FieldKind.SByte => "SByte",
                FieldKind.Byte => "Byte",
                FieldKind.Int16 => "Int16",
                FieldKind.UInt16 => "UInt16",
                FieldKind.Int32 => "Int32",
                FieldKind.UInt32 => "UInt32",
                FieldKind.Int64 => "Int64",
                FieldKind.UInt64 => "UInt64",
                FieldKind.Single => "Single",
                FieldKind.Double => "Double",
                FieldKind.Decimal => "Decimal",
                FieldKind.Char => "Char",
                FieldKind.String => "String",
                FieldKind.DateTime => "DateTime",
                FieldKind.TimeSpan => "TimeSpan",
                FieldKind.Vector2 => "Vector2",
                FieldKind.Vector3 => "Vector3",
                FieldKind.Vector4 => "Vector4",
                FieldKind.Quaternion => "Quaternion",
                FieldKind.Color => "Color",
                FieldKind.Rect => "Rect",
                FieldKind.Bounds => "Bounds",
                _ => "Null",
            };
        }

        /// <summary>
        /// ESaveKvType 成员名（枚举映射到底层整型类型码）。
        /// </summary>
        private static string EnumName(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return model.EnumUnderlyingName;
            }

            return model.Kind switch
            {
                FieldKind.Bool => "Bool",
                FieldKind.SByte => "SByte",
                FieldKind.Byte => "Byte",
                FieldKind.Int16 => "Int16",
                FieldKind.UInt16 => "UInt16",
                FieldKind.Int32 => "Int32",
                FieldKind.UInt32 => "UInt32",
                FieldKind.Int64 => "Int64",
                FieldKind.UInt64 => "UInt64",
                FieldKind.Single => "Single",
                FieldKind.Double => "Double",
                FieldKind.Decimal => "Decimal",
                FieldKind.Char => "Char",
                FieldKind.String => "String",
                FieldKind.DateTime => "DateTime",
                FieldKind.TimeSpan => "TimeSpan",
                FieldKind.Vector2 => "Vector2",
                FieldKind.Vector3 => "Vector3",
                FieldKind.Vector4 => "Vector4",
                FieldKind.Quaternion => "Quaternion",
                FieldKind.Color => "Color",
                FieldKind.Rect => "Rect",
                FieldKind.Bounds => "Bounds",
                _ => "Null",
            };
        }
    }
}
