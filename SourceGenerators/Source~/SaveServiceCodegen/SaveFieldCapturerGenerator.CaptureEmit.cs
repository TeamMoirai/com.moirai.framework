using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <see cref="SaveFieldCapturerGenerator"/> 捕获侧发射（逐字段写键、集合与嵌套展开）.
    /// </summary>
    public sealed partial class SaveFieldCapturerGenerator
    {

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
                builder.AppendLine($"            if (mask.IsEnabled({i}))");
                builder.AppendLine("            {");
                EmitWriteKeyed(builder, "                ", Literal(field.Key), $"self.{field.FieldName}", field.Value, "_" + i);
                builder.AppendLine("            }");
            }

            builder.AppendLine("            writer.EndNested();");
            builder.AppendLine("        }");
        }

        /// <summary>
        /// 发射键控记录写入（对象级，带键）。
        /// </summary>
        /// <param name="builder">输出。</param>
        /// <param name="indent">缩进。</param>
        /// <param name="keyLiteral">键字面量。</param>
        /// <param name="access">值表达式。</param>
        /// <param name="model">值模型。</param>
        /// <param name="path">变量名后缀路径。</param>
        private static void EmitWriteKeyed(StringBuilder builder, string indent, string keyLiteral, string access, SaveValueModel model, string path)
        {
            switch (model.Kind)
            {
                case FieldKind.String:
                    builder.AppendLine($"{indent}if ({access} == null) writer.WriteNull({keyLiteral}); else writer.WriteString({keyLiteral}, {access});");
                    return;
                case FieldKind.SceneReference:
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    var identity{path} = global::Moirai.Atropos.Save.SaveObjectIdentity.Resolve({access});");
                    builder.AppendLine($"{indent}    if (identity{path} == null || string.IsNullOrEmpty(identity{path}.Id))");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        if ({access} != null) global::Moirai.Atropos.LogUtility.Warning({Literal("[SaveService] Scene reference field '" + TrimQuotes(keyLiteral) + "' target has no SaveObjectIdentity, writing null.")});");
                    builder.AppendLine($"{indent}        writer.WriteNull({keyLiteral});");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    else");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        writer.WriteString({keyLiteral}, identity{path}.Id);");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}}}");
                    return;
                case FieldKind.AssetReference:
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    if ({access} == null)");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        writer.WriteNull({keyLiteral});");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    else");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        var catalog{path} = global::Moirai.Atropos.Save.SaveServiceSettings.AssetCatalog;");
                    builder.AppendLine($"{indent}        string location{path};");
                    builder.AppendLine($"{indent}        if (catalog{path} != null && catalog{path}.TryGetLocation({access}, out location{path}))");
                    builder.AppendLine($"{indent}        {{");
                    builder.AppendLine($"{indent}            writer.WriteString({keyLiteral}, location{path});");
                    builder.AppendLine($"{indent}        }}");
                    builder.AppendLine($"{indent}        else");
                    builder.AppendLine($"{indent}        {{");
                    builder.AppendLine($"{indent}            global::Moirai.Atropos.LogUtility.Warning({Literal("[SaveService] Asset reference field '" + TrimQuotes(keyLiteral) + "' is not registered in SaveAssetCatalog, writing null.")});");
                    builder.AppendLine($"{indent}            writer.WriteNull({keyLiteral});");
                    builder.AppendLine($"{indent}        }}");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}}}");
                    return;
                case FieldKind.Sequence:
                    builder.AppendLine($"{indent}if ({access} == null)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.WriteNull({keyLiteral});");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.BeginSequence({keyLiteral}, {access}.{CountMember(model)});");
                    builder.AppendLine($"{indent}    foreach (var item{path} in {access})");
                    builder.AppendLine($"{indent}    {{");
                    EmitWriteElement(builder, indent + "        ", $"item{path}", model.Element, path + "_e");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    writer.EndNested();");
                    builder.AppendLine($"{indent}}}");
                    return;
                case FieldKind.Map:
                    builder.AppendLine($"{indent}if ({access} == null)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.WriteNull({keyLiteral});");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.BeginMap({keyLiteral}, {access}.Count);");
                    builder.AppendLine($"{indent}    foreach (var pair{path} in {access})");
                    builder.AppendLine($"{indent}    {{");
                    EmitWriteElement(builder, indent + "        ", $"pair{path}.Key", model.Key, path + "_k");
                    EmitWriteElement(builder, indent + "        ", $"pair{path}.Value", model.Value, path + "_v");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    writer.EndNested();");
                    builder.AppendLine($"{indent}}}");
                    return;
                case FieldKind.NestedObject:
                    builder.AppendLine($"{indent}if ({access} == null)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.WriteNull({keyLiteral});");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.BeginNestedObject({keyLiteral}, {model.Fields.Count});");
                    for (int i = 0; i < model.Fields.Count; i++)
                    {
                        SaveNestedFieldModel nestedField = model.Fields[i];
                        EmitWriteKeyed(builder, indent + "    ", Literal(nestedField.Key), $"{access}.{nestedField.FieldName}", nestedField.Value, path + "_" + i);
                    }

                    builder.AppendLine($"{indent}    writer.EndNested();");
                    builder.AppendLine($"{indent}}}");
                    return;
                default:
                    builder.AppendLine($"{indent}writer.{KeyedWriterMethod(model)}({keyLiteral}, {model.CastToUnderlying(access)});");
                    return;
            }
        }

        /// <summary>
        /// 发射元素记录写入（集合元素级，无键）。
        /// </summary>
        private static void EmitWriteElement(StringBuilder builder, string indent, string access, SaveValueModel model, string path)
        {
            switch (model.Kind)
            {
                case FieldKind.String:
                    builder.AppendLine($"{indent}writer.WriteStringElement({access});");
                    return;
                case FieldKind.NestedObject:
                    builder.AppendLine($"{indent}if ({access} == null)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.WriteNullElement();");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.BeginNestedObjectElement({model.Fields.Count});");
                    for (int i = 0; i < model.Fields.Count; i++)
                    {
                        SaveNestedFieldModel nestedField = model.Fields[i];
                        EmitWriteKeyed(builder, indent + "    ", Literal(nestedField.Key), $"{access}.{nestedField.FieldName}", nestedField.Value, path + "_" + i);
                    }

                    builder.AppendLine($"{indent}    writer.EndNested();");
                    builder.AppendLine($"{indent}}}");
                    return;
                case FieldKind.Sequence:
                    builder.AppendLine($"{indent}if ({access} == null)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.WriteNullElement();");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.BeginSequenceElement({access}.{CountMember(model)});");
                    builder.AppendLine($"{indent}    foreach (var item{path} in {access})");
                    builder.AppendLine($"{indent}    {{");
                    EmitWriteElement(builder, indent + "        ", $"item{path}", model.Element, path + "_e");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    writer.EndNested();");
                    builder.AppendLine($"{indent}}}");
                    return;
                case FieldKind.Map:
                    builder.AppendLine($"{indent}if ({access} == null)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.WriteNullElement();");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    writer.BeginMapElement({access}.Count);");
                    builder.AppendLine($"{indent}    foreach (var pair{path} in {access})");
                    builder.AppendLine($"{indent}    {{");
                    EmitWriteElement(builder, indent + "        ", $"pair{path}.Key", model.Key, path + "_k");
                    EmitWriteElement(builder, indent + "        ", $"pair{path}.Value", model.Value, path + "_v");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    writer.EndNested();");
                    builder.AppendLine($"{indent}}}");
                    return;
                default:
                    builder.AppendLine($"{indent}writer.{ElementWriterMethod(model)}({model.CastToUnderlying(access)});");
                    return;
            }
        }

        /// <summary>
        /// 序列容器计数成员名（数组 Length / 集合 Count）。
        /// </summary>
        private static string CountMember(SaveValueModel model)
        {
            return model.Container == SequenceContainer.Array ? "Length" : "Count";
        }
    }
}
