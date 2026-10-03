using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <see cref="SaveFieldCapturerGenerator"/> 恢复侧发射（逐字段读键、集合/映射/嵌套回填）.
    /// </summary>
    public sealed partial class SaveFieldCapturerGenerator
    {

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
                builder.AppendLine($"                if (global::System.MemoryExtensions.SequenceEqual(key, s_KeyBytes[{i}]))");
                builder.AppendLine("                {");
                builder.AppendLine($"                    if (mask.IsEnabled({i}))");
                builder.AppendLine("                    {");
                EmitReadKeyedInto(builder, "                        ", $"self.{field.FieldName}", field.Value, "_" + i, "type");
                builder.AppendLine("                    }");
                builder.AppendLine("                    else");
                builder.AppendLine("                    {");
                builder.AppendLine("                        reader.SkipRecordPayload();");
                builder.AppendLine("                    }");
                builder.AppendLine();
                builder.AppendLine("                    continue;");
                builder.AppendLine("                }");
            }

            builder.AppendLine("                reader.SkipRecordPayload();");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
        }

        /// <summary>
        /// 发射键控记录读回（记录头已消费，类型变量在作用域内）。
        /// </summary>
        /// <param name="builder">输出。</param>
        /// <param name="indent">缩进。</param>
        /// <param name="target">赋值目标表达式。</param>
        /// <param name="model">值模型。</param>
        /// <param name="path">变量名后缀路径。</param>
        /// <param name="typeVar">当前记录类型变量名。</param>
        private static void EmitReadKeyedInto(StringBuilder builder, string indent, string target, SaveValueModel model, string path, string typeVar)
        {
            switch (model.Kind)
            {
                case FieldKind.String:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {target} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.String) {target} = reader.ReadString();");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.SceneReference:
                {
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {target} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.String)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    var id{path} = reader.ReadString();");
                    builder.AppendLine($"{indent}    global::Moirai.Atropos.Save.SaveObjectIdentity identity{path};");
                    builder.AppendLine($"{indent}    if (global::Moirai.Atropos.Save.SaveEntityRegistry.TryFind(id{path}, out identity{path}) && identity{path} != null)");
                    builder.AppendLine($"{indent}    {{");
                    if (model.IsGameObject)
                    {
                        builder.AppendLine($"{indent}        {target} = identity{path}.gameObject;");
                    }
                    else
                    {
                        builder.AppendLine($"{indent}        {target} = identity{path}.GetComponent<{model.TypeFqn}>();");
                    }

                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    else");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        global::Moirai.Atropos.LogUtility.Warning({Literal("[SaveService] Scene reference target id '{0}' not found in SaveEntityRegistry, restoring null.")}, id{path});");
                    builder.AppendLine($"{indent}        {target} = null;");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                }
                case FieldKind.AssetReference:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {target} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.String)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    var location{path} = reader.ReadString();");
                    builder.AppendLine($"{indent}    var catalog{path} = global::Moirai.Atropos.Save.SaveServiceSettings.AssetCatalog;");
                    builder.AppendLine($"{indent}    {model.TypeFqn} asset{path};");
                    builder.AppendLine($"{indent}    if (catalog{path} != null && catalog{path}.TryResolve(location{path}, out asset{path}))");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        {target} = asset{path};");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}    else");
                    builder.AppendLine($"{indent}    {{");
                    builder.AppendLine($"{indent}        global::Moirai.Atropos.LogUtility.Warning({Literal("[SaveService] Asset reference location '{0}' not found in SaveAssetCatalog, restoring null.")}, location{path});");
                    builder.AppendLine($"{indent}        {target} = null;");
                    builder.AppendLine($"{indent}    }}");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.Sequence:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {target} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Sequence)");
                    builder.AppendLine($"{indent}{{");
                    EmitReadSequenceBody(builder, indent + "    ", target, model, path);
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.Map:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {target} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Map)");
                    builder.AppendLine($"{indent}{{");
                    EmitReadMapBody(builder, indent + "    ", target, model, path);
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.NestedObject:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {target} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Object)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    int count{path} = reader.ReadChildCount();");
                    builder.AppendLine($"{indent}    if ({target} == null) {target} = new {model.TypeFqn}();");
                    EmitReadNestedBody(builder, indent + "    ", target, model, path);
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                default:
                {
                    string readExpr = model.CastFromUnderlying($"reader.{ReaderMethod(model)}()");
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.{EnumName(model)}) {target} = {readExpr}; else reader.SkipRecordPayload();");
                    return;
                }
            }
        }

        /// <summary>
        /// 发射序列读取主体（ReadChildCount 已消费前——本方法从元素循环开始；容器构建替换语义）。
        /// </summary>
        private static void EmitReadSequenceBody(StringBuilder builder, string indent, string target, SaveValueModel model, string path)
        {
            string elementFqn = model.Element.TypeFqn;
            builder.AppendLine($"{indent}int count{path} = reader.ReadChildCount();");
            builder.AppendLine($"{indent}var items{path} = new global::System.Collections.Generic.List<{elementFqn}>(count{path});");
            builder.AppendLine($"{indent}for (int i{path} = 0; i{path} < count{path}; i{path}++)");
            builder.AppendLine($"{indent}{{");
            builder.AppendLine($"{indent}    global::Moirai.Atropos.Save.ESaveKvType et{path};");
            builder.AppendLine($"{indent}    if (!reader.ReadElement(out et{path})) break;");
            EmitReadElementInto(builder, indent + "    ", $"items{path}", model.Element, path + "_e", $"et{path}");
            builder.AppendLine($"{indent}}}");

            switch (model.Container)
            {
                case SequenceContainer.Array:
                    builder.AppendLine($"{indent}{target} = items{path}.ToArray();");
                    break;
                case SequenceContainer.Queue:
                    builder.AppendLine($"{indent}{target} = new global::System.Collections.Generic.Queue<{elementFqn}>(items{path});");
                    break;
                case SequenceContainer.Stack:
                    // 捕获期按枚举序（栈顶→栈底）写出，恢复时逆序压栈还原 LIFO 状态
                    builder.AppendLine($"{indent}var stack{path} = new global::System.Collections.Generic.Stack<{elementFqn}>(items{path}.Count);");
                    builder.AppendLine($"{indent}for (int j{path} = items{path}.Count - 1; j{path} >= 0; j{path}--)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    stack{path}.Push(items{path}[j{path}]);");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine();
                    builder.AppendLine($"{indent}{target} = stack{path};");
                    break;
                case SequenceContainer.HashSet:
                    builder.AppendLine($"{indent}{target} = new global::System.Collections.Generic.HashSet<{elementFqn}>(items{path});");
                    break;
                default:
                    builder.AppendLine($"{indent}{target} = items{path};");
                    break;
            }
        }

        /// <summary>
        /// 发射映射读取主体（键标量直读，值按模型递归；重复键索引器覆盖兜底）。
        /// </summary>
        private static void EmitReadMapBody(StringBuilder builder, string indent, string target, SaveValueModel model, string path)
        {
            builder.AppendLine($"{indent}int count{path} = reader.ReadChildCount();");
            builder.AppendLine($"{indent}var map{path} = new global::System.Collections.Generic.Dictionary<{model.Key.TypeFqn}, {model.Value.TypeFqn}>(count{path});");
            builder.AppendLine($"{indent}for (int i{path} = 0; i{path} < count{path}; i{path}++)");
            builder.AppendLine($"{indent}{{");
            builder.AppendLine($"{indent}    global::Moirai.Atropos.Save.ESaveKvType kt{path};");
            builder.AppendLine($"{indent}    if (!reader.ReadElement(out kt{path})) break;");
            builder.AppendLine($"{indent}    {model.Key.TypeFqn} keyV{path} = default;");
            EmitReadScalarValue(builder, indent + "    ", $"keyV{path}", model.Key, $"kt{path}");
            builder.AppendLine($"{indent}    global::Moirai.Atropos.Save.ESaveKvType vt{path};");
            builder.AppendLine($"{indent}    if (!reader.ReadElement(out vt{path})) break;");
            builder.AppendLine($"{indent}    {model.Value.TypeFqn} valueV{path} = default;");
            EmitReadValueLocal(builder, indent + "    ", $"valueV{path}", model.Value, path + "_v", $"vt{path}");
            if (model.Key.Kind == FieldKind.String)
            {
                builder.AppendLine($"{indent}    if (keyV{path} != null) map{path}[keyV{path}] = valueV{path};");
            }
            else
            {
                builder.AppendLine($"{indent}    map{path}[keyV{path}] = valueV{path};");
            }

            builder.AppendLine($"{indent}}}");
            builder.AppendLine($"{indent}{target} = map{path};");
        }

        /// <summary>
        /// 发射元素记录读回并 Add 入序列缓冲（元素头已消费）。
        /// </summary>
        /// <param name="itemsExpr">元素收集列表表达式。</param>
        private static void EmitReadElementInto(StringBuilder builder, string indent, string itemsExpr, SaveValueModel model, string path, string typeVar)
        {
            switch (model.Kind)
            {
                case FieldKind.NestedObject:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Object)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    int count{path} = reader.ReadChildCount();");
                    builder.AppendLine($"{indent}    var value{path} = new {model.TypeFqn}();");
                    EmitReadNestedBody(builder, indent + "    ", $"value{path}", model, path);
                    builder.AppendLine($"{indent}    {itemsExpr}.Add(value{path});");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    return;
                case FieldKind.Sequence:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Sequence)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    {model.TypeFqn} value{path} = null;");
                    EmitReadSequenceBody(builder, indent + "    ", $"value{path}", model, path);
                    builder.AppendLine($"{indent}    {itemsExpr}.Add(value{path});");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    return;
                case FieldKind.Map:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Map)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    {model.TypeFqn} value{path} = null;");
                    EmitReadMapBody(builder, indent + "    ", $"value{path}", model, path);
                    builder.AppendLine($"{indent}    {itemsExpr}.Add(value{path});");
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    return;
                case FieldKind.String:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.String) {itemsExpr}.Add(reader.ReadString());");
                    builder.AppendLine($"{indent}else {{ reader.SkipRecordPayload(); {itemsExpr}.Add(null); }}");
                    return;
                default:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.{EnumName(model)}) {itemsExpr}.Add({model.CastFromUnderlying($"reader.{ReaderMethod(model)}()")});");
                    builder.AppendLine($"{indent}else {{ reader.SkipRecordPayload(); {itemsExpr}.Add(default); }}");
                    return;
            }
        }

        /// <summary>
        /// 发射嵌套对象作用域记录循环（ReadChildCount 已由调用方消费；按键匹配读入 <paramref name="target"/> 的字段）。
        /// </summary>
        private static void EmitReadNestedBody(StringBuilder builder, string indent, string target, SaveValueModel model, string path)
        {
            builder.AppendLine($"{indent}for (int r{path} = 0; r{path} < count{path}; r{path}++)");
            builder.AppendLine($"{indent}{{");
            builder.AppendLine($"{indent}    global::System.ReadOnlySpan<byte> nkey{path};");
            builder.AppendLine($"{indent}    global::Moirai.Atropos.Save.ESaveKvType ntype{path};");
            builder.AppendLine($"{indent}    if (!reader.ReadRecord(out nkey{path}, out ntype{path})) break;");
            for (int i = 0; i < model.Fields.Count; i++)
            {
                SaveNestedFieldModel nestedField = model.Fields[i];
                builder.AppendLine($"{indent}    if (global::System.MemoryExtensions.SequenceEqual(nkey{path}, s_KeyBytes{path}[{i}]))");
                builder.AppendLine($"{indent}    {{");
                EmitReadKeyedInto(builder, indent + "        ", $"{target}.{nestedField.FieldName}", nestedField.Value, path + "_" + i, $"ntype{path}");
                builder.AppendLine($"{indent}        continue;");
                builder.AppendLine($"{indent}    }}");
            }

            builder.AppendLine($"{indent}    reader.SkipRecordPayload();");
            builder.AppendLine($"{indent}}}");
        }

        /// <summary>
        /// 发射标量/枚举值读取（映射键等无 Null 语义的槽位；类型不符跳过载荷保留 default）。
        /// </summary>
        private static void EmitReadScalarValue(StringBuilder builder, string indent, string target, SaveValueModel model, string typeVar)
        {
            if (model.Kind == FieldKind.String)
            {
                builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.String) {target} = reader.ReadString(); else reader.SkipRecordPayload();");
                return;
            }

            string readExpr = model.CastFromUnderlying($"reader.{ReaderMethod(model)}()");
            builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.{EnumName(model)}) {target} = {readExpr}; else reader.SkipRecordPayload();");
        }

        /// <summary>
        /// 发射值读取到已声明局部变量（映射值槽位；Null → null，类型不符跳过保留 default）。
        /// </summary>
        private static void EmitReadValueLocal(StringBuilder builder, string indent, string local, SaveValueModel model, string path, string typeVar)
        {
            switch (model.Kind)
            {
                case FieldKind.String:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) {{ reader.SkipRecordPayload(); {local} = null; }}");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.String) {local} = reader.ReadString();");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.NestedObject:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) reader.SkipRecordPayload();");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Object)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    int count{path} = reader.ReadChildCount();");
                    builder.AppendLine($"{indent}    {local} = new {model.TypeFqn}();");
                    EmitReadNestedBody(builder, indent + "    ", local, model, path);
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.Sequence:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) reader.SkipRecordPayload();");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Sequence)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    {local} = null;");
                    EmitReadSequenceBody(builder, indent + "    ", local, model, path);
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                case FieldKind.Map:
                    builder.AppendLine($"{indent}if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Null) reader.SkipRecordPayload();");
                    builder.AppendLine($"{indent}else if ({typeVar} == global::Moirai.Atropos.Save.ESaveKvType.Map)");
                    builder.AppendLine($"{indent}{{");
                    builder.AppendLine($"{indent}    {local} = null;");
                    EmitReadMapBody(builder, indent + "    ", local, model, path);
                    builder.AppendLine($"{indent}}}");
                    builder.AppendLine($"{indent}else reader.SkipRecordPayload();");
                    return;
                default:
                    EmitReadScalarValue(builder, indent, local, model, typeVar);
                    return;
            }
        }
    }
}
