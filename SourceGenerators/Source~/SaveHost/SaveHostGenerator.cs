using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// SaveHost 增量源生成器 v1：扫描 <c>[SaveField]</c> 字段，为每个组件类型生成强类型键值捕获器
    /// （嵌套在组件类型内部以访问私有字段，零反射零装箱），并经模块初始化器自注册 <c>SaveCapturerRegistry</c>。
    /// <para>v1 支持字段类型：基元/枚举/string/DateTime/TimeSpan 与 Unity 数学类型（Vector2/3/4、Quaternion、Color、Rect、Bounds）；
    /// 集合与嵌套数据类字段报 MIRAI200（KVT 格式与写入器/读取器 API 已支持，生成器支持于后续版本扩展）。</para>
    /// <para>诊断：MIRAI200 不支持的字段类型；MIRAI201 存档键重复；MIRAI203 包含类型必须为 partial class；MIRAI204 字段必须为实例字段。</para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class SaveHostGenerator : IIncrementalGenerator
    {
        /// <summary>SaveFieldAttribute 的元数据全名（生成器触发锚点）。</summary>
        private const string SaveFieldAttributeMetadataName = "Moirai.Atropos.Save.SaveFieldAttribute";

        /// <summary>
        /// 注册增量管线。
        /// </summary>
        /// <param name="context">增量生成器初始化上下文。</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var fields = context.SyntaxProvider.ForAttributeWithMetadataName(
                SaveFieldAttributeMetadataName,
                static (node, _) => node is FieldDeclarationSyntax,
                static (ctx, ct) => SaveFieldModel.Create(ctx, ct));

            var collected = fields.Collect().Combine(context.CompilationProvider);
            context.RegisterSourceOutput(collected, static (spc, source) => Execute(spc, source.Left, source.Right));
        }

        /// <summary>
        /// 生成全部捕获器源码与诊断。
        /// </summary>
        private static void Execute(SourceProductionContext context, ImmutableArray<SaveFieldModel> allFields, Compilation compilation)
        {
            var diagnostics = new List<Diagnostic>();

            // 按包含类型分组（保持声明序）
            var fieldsByType = new Dictionary<string, List<SaveFieldModel>>(StringComparer.Ordinal);
            foreach (SaveFieldModel field in allFields)
            {
                if (field == null)
                {
                    continue;
                }

                if (!fieldsByType.TryGetValue(field.ContainingTypeFqn, out List<SaveFieldModel> list))
                {
                    list = new List<SaveFieldModel>();
                    fieldsByType[field.ContainingTypeFqn] = list;
                }

                list.Add(field);
            }

            var registrationLines = new List<string>();
            foreach (KeyValuePair<string, List<SaveFieldModel>> pair in fieldsByType)
            {
                List<SaveFieldModel> fields = pair.Value;
                SaveFieldModel head = fields[0];

                if (!head.IsClass || !head.IsPartial)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.TypeMustBePartial, head.Location, head.ContainingTypeName));
                }

                // 键唯一性 + 字段合法性过滤
                var validFields = new List<SaveFieldModel>(fields.Count);
                var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (SaveFieldModel field in fields)
                {
                    if (field.IsStaticOrConst)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.MustBeInstanceField, field.Location, head.ContainingTypeName, field.FieldName));
                        continue;
                    }

                    if (field.Kind == FieldKind.Unsupported)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedFieldType, field.Location, head.ContainingTypeName, field.FieldName, field.TypeDisplay));
                        continue;
                    }

                    if (!seenKeys.Add(field.Key))
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateKey, field.Location, head.ContainingTypeName, field.Key));
                        continue;
                    }

                    validFields.Add(field);
                }

                if (validFields.Count == 0)
                {
                    continue;
                }

                string hintName = $"{head.HintPrefix}{head.ContainingTypeName}.SaveCapture.g.cs";
                context.AddSource(hintName, SourceText.From(EmitCaptureClass(head, validFields), Encoding.UTF8));
                registrationLines.Add($"            global::Moirai.Atropos.Save.SaveCapturerRegistry.Register(typeof({head.ContainingTypeFqn}), new {head.ContainingTypeFqn}.SaveCaptureInner());");
            }

            if (registrationLines.Count > 0)
            {
                bool hasBuiltinModuleInitializer = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") != null;
                context.AddSource("SaveCaptureModuleInit.g.cs", SourceText.From(EmitModuleInitializer(registrationLines, hasBuiltinModuleInitializer), Encoding.UTF8));
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        /// <summary>
        /// 生成组件捕获器（嵌套于组件类型内部以访问私有字段）。
        /// </summary>
        private static string EmitCaptureClass(SaveFieldModel head, List<SaveFieldModel> fields)
        {
            var builder = new StringBuilder(4096);
            AppendGeneratedHeader(builder);
            if (!string.IsNullOrEmpty(head.ContainingNamespace))
            {
                builder.Append("namespace ").Append(head.ContainingNamespace).Append(";\n\n");
            }

            builder.AppendLine($"partial class {head.ContainingTypeName}");
            builder.AppendLine("{");
            builder.AppendLine("    internal sealed class SaveCaptureInner : global::Moirai.Atropos.Save.ISaveComponentCapturer");
            builder.AppendLine("    {");
            builder.AppendLine($"        public global::System.Type ComponentType => typeof({head.ContainingTypeFqn});");
            builder.AppendLine();
            builder.AppendLine("        public string[] FieldNames { get; } = new string[]");
            builder.AppendLine("        {");
            foreach (SaveFieldModel field in fields)
            {
                builder.AppendLine($"            {Literal(field.Key)},");
            }

            builder.AppendLine("        };");
            builder.AppendLine();
            builder.AppendLine("        private static readonly byte[][] s_KeyBytes = BuildKeyBytes();");
            builder.AppendLine();
            builder.AppendLine("        private static byte[][] BuildKeyBytes()");
            builder.AppendLine("        {");
            builder.AppendLine("            var keys = new byte[FieldNames.Length][];");
            builder.AppendLine("            for (int i = 0; i < keys.Length; i++)");
            builder.AppendLine("            {");
            builder.AppendLine("                keys[i] = System.Text.Encoding.UTF8.GetBytes(FieldNames[i]);");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            return keys;");
            builder.AppendLine("        }");
            builder.AppendLine();
            EmitCaptureMethod(builder, head, fields);
            builder.AppendLine();
            EmitRestoreMethod(builder, head, fields);
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        /// <summary>
        /// 生成 Capture 方法：类型名键嵌套作用域 + 启用字段子集写入。
        /// </summary>
        private static void EmitCaptureMethod(StringBuilder builder, SaveFieldModel head, List<SaveFieldModel> fields)
        {
            builder.AppendLine("        public void Capture(object component, ref global::Moirai.Atropos.Save.SaveKeyValueWriter writer, in global::Moirai.Atropos.Save.SaveFieldMask mask)");
            builder.AppendLine("        {");
            builder.AppendLine($"            var self = ({head.ContainingTypeFqn})component;");
            builder.AppendLine("            int enabledCount = 0;");
            for (int i = 0; i < fields.Count; i++)
            {
                builder.AppendLine($"            if (mask.IsEnabled({i})) enabledCount++;");
            }

            builder.AppendLine("            writer.BeginNestedObject(ComponentType.FullName, enabledCount);");
            for (int i = 0; i < fields.Count; i++)
            {
                SaveFieldModel field = fields[i];
                string access = $"self.{field.FieldName}";
                string key = Literal(field.Key);
                string write = $"writer.{WriterMethod(field)}({key}, {field.CastToUnderlying(access)});";
                if (field.Kind == FieldKind.String)
                {
                    builder.AppendLine($"            if (mask.IsEnabled({i}))");
                    builder.AppendLine("            {");
                    builder.AppendLine($"                if ({access} == null) writer.WriteNull({key}); else {write}");
                    builder.AppendLine("            }");
                    continue;
                }

                builder.AppendLine($"            if (mask.IsEnabled({i})) {write}");
            }

            builder.AppendLine("            writer.EndNested();");
            builder.AppendLine("        }");
        }

        /// <summary>
        /// 生成 Restore 方法：精确消费 recordCount 条记录，键匹配且启用则读回，否则跳过。
        /// </summary>
        private static void EmitRestoreMethod(StringBuilder builder, SaveFieldModel head, List<SaveFieldModel> fields)
        {
            builder.AppendLine("        public void Restore(object component, ref global::Moirai.Atropos.Save.SaveKeyValueReader reader, int recordCount, in global::Moirai.Atropos.Save.SaveFieldMask mask)");
            builder.AppendLine("        {");
            builder.AppendLine($"            var self = ({head.ContainingTypeFqn})component;");
            builder.AppendLine("            for (int consumed = 0; consumed < recordCount && reader.ReadRecord(out var key, out var type); consumed++)");
            builder.AppendLine("            {");
            for (int i = 0; i < fields.Count; i++)
            {
                SaveFieldModel field = fields[i];
                string access = $"self.{field.FieldName}";
                string readBack = field.Kind == FieldKind.String
                    ? $"if (type == global::Moirai.Atropos.Save.ESaveKvType.Null) {access} = null; else if (type == global::Moirai.Atropos.Save.ESaveKvType.String) {access} = reader.ReadString(); else reader.SkipRecordPayload();"
                    : $"if (type == global::Moirai.Atropos.Save.ESaveKvType.{EnumName(field.Kind)}) {access} = {field.CastFromUnderlying($"reader.{ReaderMethod(field)}()")}; else reader.SkipRecordPayload();";
                builder.AppendLine($"                if (key.SequenceEqual(s_KeyBytes[{i}]))");
                builder.AppendLine("                {");
                builder.AppendLine($"                    if (mask.IsEnabled({i})) {readBack} else reader.SkipRecordPayload();");
                builder.AppendLine("                    continue;");
                builder.AppendLine("                }");
            }

            builder.AppendLine("                reader.SkipRecordPayload();");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
        }

        /// <summary>
        /// 生成模块初始化器（含 ModuleInitializerAttribute 缺失定义——同程序集内私有副本，按完整类型名被编译器识别）。
        /// </summary>
        private static string EmitModuleInitializer(List<string> registrationLines, bool hasBuiltinModuleInitializer)
        {
            var builder = new StringBuilder(1024);
            AppendGeneratedHeader(builder);
            if (!hasBuiltinModuleInitializer)
            {
                builder.AppendLine("namespace System.Runtime.CompilerServices");
                builder.AppendLine("{");
                builder.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false, Inherited = false)]");
                builder.AppendLine("    internal sealed class ModuleInitializerAttribute : global::System.Attribute");
                builder.AppendLine("    {");
                builder.AppendLine("    }");
                builder.AppendLine("}");
                builder.AppendLine();
            }

            builder.AppendLine("internal static class SaveCaptureModuleInit");
            builder.AppendLine("{");
            builder.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
            builder.AppendLine("    internal static void Initialize()");
            builder.AppendLine("    {");
            foreach (string line in registrationLines)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        /// <summary>写入方法名（枚举走底层类型的写入方法）。</summary>
        private static string WriterMethod(SaveFieldModel field)
        {
            if (field.Kind == FieldKind.Enum)
            {
                return "Write" + field.EnumUnderlyingName;
            }

            return field.Kind switch
            {
                FieldKind.Bool => "WriteBoolean",
                FieldKind.SByte => "WriteSByte",
                FieldKind.Byte => "WriteByte",
                FieldKind.Int16 => "WriteInt16",
                FieldKind.UInt16 => "WriteUInt16",
                FieldKind.Int32 => "WriteInt32",
                FieldKind.UInt32 => "WriteUInt32",
                FieldKind.Int64 => "WriteInt64",
                FieldKind.UInt64 => "WriteUInt64",
                FieldKind.Single => "WriteSingle",
                FieldKind.Double => "WriteDouble",
                FieldKind.Decimal => "WriteDecimal",
                FieldKind.Char => "WriteChar",
                FieldKind.String => "WriteString",
                FieldKind.DateTime => "WriteDateTime",
                FieldKind.TimeSpan => "WriteTimeSpan",
                FieldKind.Vector2 => "WriteVector2",
                FieldKind.Vector3 => "WriteVector3",
                FieldKind.Vector4 => "WriteVector4",
                FieldKind.Quaternion => "WriteQuaternion",
                FieldKind.Color => "WriteColor",
                FieldKind.Rect => "WriteRect",
                FieldKind.Bounds => "WriteBounds",
                _ => "WriteNull",
            };
        }

        /// <summary>读取方法名。</summary>
        private static string ReaderMethod(SaveFieldModel field)
        {
            if (field.Kind == FieldKind.Enum)
            {
                return "Read" + field.EnumUnderlyingName;
            }

            return field.Kind switch
            {
                FieldKind.Bool => "ReadBoolean",
                FieldKind.SByte => "ReadSByte",
                FieldKind.Byte => "ReadByte",
                FieldKind.Int16 => "ReadInt16",
                FieldKind.UInt16 => "ReadUInt16",
                FieldKind.Int32 => "ReadInt32",
                FieldKind.UInt32 => "ReadUInt32",
                FieldKind.Int64 => "ReadInt64",
                FieldKind.UInt64 => "ReadUInt64",
                FieldKind.Single => "ReadSingle",
                FieldKind.Double => "ReadDouble",
                FieldKind.Decimal => "ReadDecimal",
                FieldKind.Char => "ReadChar",
                FieldKind.String => "ReadString",
                FieldKind.DateTime => "ReadDateTime",
                FieldKind.TimeSpan => "ReadTimeSpan",
                FieldKind.Vector2 => "ReadVector2",
                FieldKind.Vector3 => "ReadVector3",
                FieldKind.Vector4 => "ReadVector4",
                FieldKind.Quaternion => "ReadQuaternion",
                FieldKind.Color => "ReadColor",
                FieldKind.Rect => "ReadRect",
                FieldKind.Bounds => "ReadBounds",
                _ => "ReadBoolean",
            };
        }

        /// <summary>ESaveKvType 成员名。</summary>
        private static string EnumName(FieldKind kind)
        {
            return kind switch
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

        /// <summary>字符串字面量转义。</summary>
        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }

        /// <summary>生成头注释。</summary>
        private static void AppendGeneratedHeader(StringBuilder builder)
        {
            builder.AppendLine("// <auto-generated by SaveHostGenerator />");
        }
    }
}
