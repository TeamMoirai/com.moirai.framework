using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>字段捕获分类（与 KVT 记录类型一一对应）。</summary>
    internal enum FieldKind
    {
        Unsupported,
        Bool,
        SByte,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        Int64,
        UInt64,
        Single,
        Double,
        Decimal,
        Char,
        String,
        DateTime,
        TimeSpan,
        Vector2,
        Vector3,
        Vector4,
        Quaternion,
        Color,
        Rect,
        Bounds,
        Enum,
    }

    /// <summary>
    /// <c>[SaveField]</c> 字段模型（字符串化快照，支撑增量缓存等价比较）。
    /// </summary>
    internal sealed class SaveFieldModel
    {
        /// <summary>包含类型的全限定名（global:: 前缀，可用于 typeof）。</summary>
        public string ContainingTypeFqn { get; private set; }

        /// <summary>包含类型的元数据名（用于 partial class 声明）。</summary>
        public string ContainingTypeName { get; private set; }

        /// <summary>包含类型的命名空间（空 = 全局）。</summary>
        public string ContainingNamespace { get; private set; }

        /// <summary>AddSource 提示名前缀（按命名空间消重）。</summary>
        public string HintPrefix { get; private set; }

        /// <summary>包含类型是否为 class。</summary>
        public bool IsClass { get; private set; }

        /// <summary>包含类型是否声明为 partial。</summary>
        public bool IsPartial { get; private set; }

        /// <summary>字段是否为 static/const（实例字段校验用）。</summary>
        public bool IsStaticOrConst { get; private set; }

        /// <summary>存档键（显式指定或字段名）。</summary>
        public string Key { get; private set; }

        /// <summary>字段名。</summary>
        public string FieldName { get; private set; }

        /// <summary>字段类型显示名。</summary>
        public string TypeDisplay { get; private set; }

        /// <summary>字段分类。</summary>
        public FieldKind Kind { get; private set; }

        /// <summary>枚举底层类型的写入/读取方法后缀（非枚举为 null）。</summary>
        public string EnumUnderlyingName { get; private set; }

        /// <summary>枚举底层类型的 C# 类型名（cast 用；非枚举为 null）。</summary>
        public string EnumUnderlyingCsType { get; private set; }

        /// <summary>字段位置（诊断报告用）。</summary>
        public Location Location { get; private set; }

        /// <summary>
        /// 从属性语法上下文创建字段模型。
        /// </summary>
        /// <param name="context">属性提供上下文。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>字段模型（目标非字段时返回 null）。</returns>
        public static SaveFieldModel Create(GeneratorAttributeSyntaxContext context, System.Threading.CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.TargetSymbol is not IFieldSymbol fieldSymbol || fieldSymbol.ContainingType == null)
            {
                return null;
            }

            INamedTypeSymbol containingType = fieldSymbol.ContainingType;
            bool isPartial = false;
            foreach (SyntaxReference reference in containingType.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax typeDeclaration
                    && typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    isPartial = true;
                    break;
                }
            }

            string explicitKey = null;
            if (context.Attributes.Length > 0 && context.Attributes[0].ConstructorArguments.Length > 0
                && context.Attributes[0].ConstructorArguments[0].Value is string keyArgument)
            {
                explicitKey = keyArgument;
            }

            string typeFqn = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string fieldNamespace = containingType.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : containingType.ContainingNamespace.ToDisplayString();
            string hintPrefix = string.IsNullOrEmpty(fieldNamespace) ? string.Empty : fieldNamespace.Replace('.', '_') + ".";

            FieldKind kind = Classify(fieldSymbol.Type, out string enumUnderlyingName, out string enumUnderlyingCsType);
            var model = new SaveFieldModel
            {
                ContainingTypeFqn = typeFqn,
                ContainingTypeName = containingType.Name,
                ContainingNamespace = fieldNamespace,
                HintPrefix = hintPrefix,
                IsClass = containingType.TypeKind == TypeKind.Class,
                IsPartial = isPartial,
                IsStaticOrConst = fieldSymbol.IsStatic || fieldSymbol.IsConst,
                Key = explicitKey ?? fieldSymbol.Name,
                FieldName = fieldSymbol.Name,
                TypeDisplay = fieldSymbol.Type.ToDisplayString(),
                Kind = kind,
                EnumUnderlyingName = enumUnderlyingName,
                EnumUnderlyingCsType = enumUnderlyingCsType,
                Location = fieldSymbol.Locations.Length > 0 ? fieldSymbol.Locations[0] : Location.None,
            };
            return model;
        }

        /// <summary>
        /// 分类字段类型（写入/读取方法与 KVT 类型码对齐）。
        /// </summary>
        private static FieldKind Classify(ITypeSymbol type, out string enumUnderlyingName, out string enumUnderlyingCsType)
        {
            enumUnderlyingName = null;
            enumUnderlyingCsType = null;
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: return FieldKind.Bool;
                case SpecialType.System_SByte: return FieldKind.SByte;
                case SpecialType.System_Byte: return FieldKind.Byte;
                case SpecialType.System_Int16: return FieldKind.Int16;
                case SpecialType.System_UInt16: return FieldKind.UInt16;
                case SpecialType.System_Int32: return FieldKind.Int32;
                case SpecialType.System_UInt32: return FieldKind.UInt32;
                case SpecialType.System_Int64: return FieldKind.Int64;
                case SpecialType.System_UInt64: return FieldKind.UInt64;
                case SpecialType.System_Single: return FieldKind.Single;
                case SpecialType.System_Double: return FieldKind.Double;
                case SpecialType.System_Decimal: return FieldKind.Decimal;
                case SpecialType.System_Char: return FieldKind.Char;
                case SpecialType.System_String: return FieldKind.String;
                default:
                    break;
            }

            if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumSymbol && enumSymbol.EnumUnderlyingType != null)
            {
                (enumUnderlyingName, enumUnderlyingCsType) = enumSymbol.EnumUnderlyingType.SpecialType switch
                {
                    SpecialType.System_SByte => ("SByte", "sbyte"),
                    SpecialType.System_Byte => ("Byte", "byte"),
                    SpecialType.System_Int16 => ("Int16", "short"),
                    SpecialType.System_UInt16 => ("UInt16", "ushort"),
                    SpecialType.System_Int64 => ("Int64", "long"),
                    SpecialType.System_UInt64 => ("UInt64", "ulong"),
                    SpecialType.System_Int32 => ("Int32", "int"),
                    SpecialType.System_UInt32 => ("UInt32", "uint"),
                    _ => ("Int32", "int"),
                };
                return FieldKind.Enum;
            }

            switch (type.ToDisplayString())
            {
                case "System.DateTime": return FieldKind.DateTime;
                case "System.TimeSpan": return FieldKind.TimeSpan;
                case "UnityEngine.Vector2": return FieldKind.Vector2;
                case "UnityEngine.Vector3": return FieldKind.Vector3;
                case "UnityEngine.Vector4": return FieldKind.Vector4;
                case "UnityEngine.Quaternion": return FieldKind.Quaternion;
                case "UnityEngine.Color": return FieldKind.Color;
                case "UnityEngine.Rect": return FieldKind.Rect;
                case "UnityEngine.Bounds": return FieldKind.Bounds;
                default:
                    return FieldKind.Unsupported;
            }
        }

        /// <summary>
        /// 生成写入侧 cast 表达式（枚举 → 底层类型）。
        /// </summary>
        /// <param name="expression">值表达式。</param>
        /// <returns>cast 后的表达式。</returns>
        public string CastToUnderlying(string expression)
        {
            return Kind == FieldKind.Enum ? $"({EnumUnderlyingCsType}){expression}" : expression;
        }

        /// <summary>
        /// 生成读取侧 cast 表达式（底层类型 → 枚举）。
        /// </summary>
        /// <param name="expression">值表达式。</param>
        /// <returns>cast 后的表达式。</returns>
        public string CastFromUnderlying(string expression)
        {
            return Kind == FieldKind.Enum ? $"({TypeDisplay}){expression}" : expression;
        }
    }

    /// <summary>
    /// SaveHost 诊断描述符（MIRAI2xx 系列）。
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>诊断类别。</summary>
        private const string Category = "SaveHost";

        /// <summary>MIRAI200：字段类型不受生成器支持。</summary>
        public static readonly DiagnosticDescriptor UnsupportedFieldType = new(
            "MIRAI200",
            "SaveField 字段类型不受支持",
            "[SaveField] 字段 '{0}.{1}' 的类型 '{2}' 不受 SaveHost 生成器 v1 支持（支持：基元/枚举/string/DateTime/TimeSpan/Unity 数学类型）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI201：存档键重复。</summary>
        public static readonly DiagnosticDescriptor DuplicateKey = new(
            "MIRAI201",
            "SaveField 存档键重复",
            "类型 '{0}' 内存在重复的存档键 '{1}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI203：包含类型必须为 partial class。</summary>
        public static readonly DiagnosticDescriptor TypeMustBePartial = new(
            "MIRAI203",
            "包含类型必须为 partial class",
            "[SaveField] 所在类型 '{0}' 必须声明为 partial class（生成捕获器需嵌套其中以访问字段）",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>MIRAI204：字段必须为实例字段。</summary>
        public static readonly DiagnosticDescriptor MustBeInstanceField = new(
            "MIRAI204",
            "SaveField 必须标注实例字段",
            "[SaveField] 不能标注类型 '{0}' 的静态/常量字段 '{1}'",
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
