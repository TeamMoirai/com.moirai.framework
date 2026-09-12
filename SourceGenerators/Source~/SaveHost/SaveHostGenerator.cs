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
    /// SaveHost 增量源生成器 v2：扫描 <c>[SaveField]</c> 字段，为每个组件类型生成强类型键值捕获器
    /// （嵌套在组件类型内部以访问私有字段，零反射零装箱），并经模块初始化器自注册 <c>SaveCapturerRegistry</c>。
    /// 同管线扫描 <c>ISaveMigrator</c> 实现类，生成 <c>SaveMigrationManager.Register</c> 自注册（AOT 安全）。
    /// <para>v2 支持字段类型：基元/枚举/string/DateTime/TimeSpan 与 Unity 数学类型（Vector2/3/4、Quaternion、Color、Rect、Bounds）、
    /// 集合（数组/List/Queue/Stack/HashSet/Dictionary，元素递归支持标量与嵌套数据类，引用元素暂不支持）、
    /// 嵌套 <c>[SaveData]</c> 数据类（全部 public 实例字段递归捕获）、UnityEngine.Object 引用
    /// （GameObject/Component 派生 = 场景引用，存 SaveObjectIdentity 稳定 ID；其余 = 资产引用，存 SaveAssetCatalog 定位串）。
    /// 捕获器额外发射 <see cref="SaveFieldModel.SchemaVersion"/>（[SaveComponentSchema] 声明，缺省 1）。
    /// 非 MonoBehaviour 类型上的 [SaveField] 不生成捕获器（嵌套数据类经 public 字段内联展开，无需标注）。</para>
    /// <para>诊断：MIRAI300 不支持的字段类型；MIRAI301 存档键重复；MIRAI302 迁移器无法自注册；MIRAI303 包含类型必须为 partial class；
    /// MIRAI304 字段必须为实例字段；MIRAI305 场景引用需 SaveObjectIdentity 指引（Info）；MIRAI306 引用类型不明（UnityEngine.Object 基类）；
    /// MIRAI307 嵌套数据类型无效；MIRAI308 集合元素/映射键值类型不受支持。</para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class SaveHostGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// 注册增量管线。
        /// <para>字段扫描用 CreateSyntaxProvider 语义判定而非 ForAttributeWithMetadataName——后者在本项目部分编译单元上静默不产出（实证，改用带特性列表的字段声明粗筛）。</para>
        /// </summary>
        /// <param name="context">增量生成器初始化上下文。</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var fields = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is FieldDeclarationSyntax declaration && declaration.AttributeLists.Count > 0,
                static (ctx, ct) => SaveFieldModel.Create(ctx, ct));

            // 迁移器实现无特性锚点——按「带基类列表的 class」粗筛后语义检查接口
            var migrators = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax declaration && declaration.BaseList != null,
                static (ctx, ct) => MigratorModel.Create(ctx, ct));

            var collected = fields.Collect().Combine(migrators.Collect()).Combine(context.CompilationProvider);
            context.RegisterSourceOutput(collected, static (spc, source) => Execute(spc, source.Left.Left, source.Left.Right, source.Right));
        }

        /// <summary>
        /// 生成全部捕获器源码与诊断。
        /// </summary>
        private static void Execute(SourceProductionContext context, ImmutableArray<SaveFieldModel[]> fieldGroups, ImmutableArray<MigratorModel> allMigrators, Compilation compilation)
        {
            var diagnostics = new List<Diagnostic>();

            // 按包含类型分组（保持声明序）
            var fieldsByType = new Dictionary<string, List<SaveFieldModel>>(StringComparer.Ordinal);
            foreach (SaveFieldModel[] group in fieldGroups)
            {
                if (group == null)
                {
                    continue;
                }

                foreach (SaveFieldModel field in group)
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
            }

            var registrationLines = new List<string>();
            var migratorRegistrationLines = new List<string>();
            foreach (MigratorModel migrator in allMigrators)
            {
                if (migrator == null)
                {
                    continue;
                }

                if (!migrator.IsRegistrable)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidMigrator, migrator.Location, migrator.TypeDisplay));
                    continue;
                }

                migratorRegistrationLines.Add($"            global::Moirai.Atropos.Save.SaveMigrationManager.Register(new {migrator.TypeFqn}());");
            }

            foreach (KeyValuePair<string, List<SaveFieldModel>> pair in fieldsByType)
            {
                List<SaveFieldModel> fields = pair.Value;
                SaveFieldModel head = fields[0];

                // 非组件类型（嵌套数据类等）上的 [SaveField] 不生成捕获器——嵌套数据经 public 字段内联展开参与，无需标注
                if (!head.IsMonoBehaviour)
                {
                    continue;
                }

                if (!head.IsClass || !head.IsPartial)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.TypeMustBePartial, head.Location, head.ContainingTypeName));
                }

                // 嵌套链每一级都须为 partial class（生成代码逐层 partial 包裹以命中同一类型）
                if (head.ContainingChain != null)
                {
                    foreach (ContainingTypeInfo level in head.ContainingChain)
                    {
                        if (!level.IsClass || !level.IsPartial)
                        {
                            diagnostics.Add(Diagnostic.Create(Diagnostics.TypeMustBePartial, head.Location, head.ContainingTypeName + "（嵌套链 " + level.Name + " 级）"));
                        }
                    }
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

                    if (field.Value.Diagnostics != null)
                    {
                        foreach (DiagnosticInfo info in field.Value.Diagnostics)
                        {
                            diagnostics.Add(Diagnostic.Create(info.Descriptor, info.Location, info.Args));
                        }
                    }

                    if (field.Kind == FieldKind.Unsupported)
                    {
                        // 分类期未给出更精确诊断（MIRAI306/307/308）时回退 MIRAI300
                        if (field.Value.Diagnostics == null || field.Value.Diagnostics.Count == 0)
                        {
                            diagnostics.Add(Diagnostic.Create(Diagnostics.UnsupportedFieldType, field.Location, head.ContainingTypeName, field.FieldName, field.TypeDisplay));
                        }

                        continue;
                    }

                    if (field.Kind == FieldKind.SceneReference)
                    {
                        diagnostics.Add(Diagnostic.Create(Diagnostics.SceneReferenceGuidance, field.Location, field.FieldName));
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

            if (registrationLines.Count > 0 || migratorRegistrationLines.Count > 0)
            {
                bool hasBuiltinModuleInitializer = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") != null;
                context.AddSource("SaveCaptureModuleInit.g.cs", SourceText.From(EmitModuleInitializer(registrationLines, migratorRegistrationLines, hasBuiltinModuleInitializer), Encoding.UTF8));
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }

        #region 捕获器骨架 [CAPTURER SKELETON]

        /// <summary>嵌套键数组登记表（数组名 → 键集）。</summary>
        private sealed class KeyArrayTable
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<string[]> Keys = new List<string[]>();

            public void Add(string name, string[] keys)
            {
                Names.Add(name);
                Keys.Add(keys);
            }
        }

        /// <summary>
        /// 生成组件捕获器（嵌套于组件类型内部以访问私有字段）。
        /// </summary>
        private static string EmitCaptureClass(SaveFieldModel head, List<SaveFieldModel> fields)
        {
            // 预收集嵌套作用域键数组（嵌套对象/集合元素内嵌套对象的字段键）
            var keyArrays = new KeyArrayTable();
            for (int i = 0; i < fields.Count; i++)
            {
                CollectKeyArrays(fields[i].Value, "_" + i, keyArrays);
            }

            var builder = new StringBuilder(8192);
            AppendGeneratedHeader(builder);
            // 块级命名空间（Unity 工程 LangVersion 为 C# 9，file-scoped namespace 需 C# 10）
            bool hasNamespace = !string.IsNullOrEmpty(head.ContainingNamespace);
            if (hasNamespace)
            {
                builder.Append("namespace ").Append(head.ContainingNamespace).AppendLine();
                builder.AppendLine("{");
            }

            // 嵌套链逐层 partial 包裹（自外向内，外层缩进 0 起）
            int nestDepth = head.ContainingChain?.Count ?? 0;
            for (int i = nestDepth - 1; i >= 0; i--)
            {
                int levelIndent = (nestDepth - 1 - i) * 4;
                builder.Append(' ', levelIndent);
                builder.AppendLine($"partial class {head.ContainingChain[i].Name}");
                builder.Append(' ', levelIndent);
                builder.AppendLine("{");
            }

            builder.Append(' ', nestDepth * 4);
            builder.AppendLine($"partial class {head.ContainingTypeName}");
            builder.Append(' ', nestDepth * 4);
            builder.AppendLine("{");
            builder.AppendLine("    internal sealed class SaveCaptureInner : global::Moirai.Atropos.Save.ISaveComponentCapturer");
            builder.AppendLine("    {");
            builder.AppendLine($"        public global::System.Type ComponentType => typeof({head.ContainingTypeFqn});");
            builder.AppendLine();
            builder.AppendLine($"        public int SchemaVersion => {head.SchemaVersion};");
            builder.AppendLine();
            builder.AppendLine("        public string[] FieldNames { get; } = new string[]");
            builder.AppendLine("        {");
            foreach (SaveFieldModel field in fields)
            {
                builder.AppendLine($"            {Literal(field.Key)},");
            }

            builder.AppendLine("        };");
            builder.AppendLine();
            // 键字节数组一律字面量初始化——静态字段初始化器不得引用实例成员 FieldNames
            builder.Append("        private static readonly byte[][] s_KeyBytes = ToKeyBytes(new string[] { ");
            for (int i = 0; i < fields.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(Literal(fields[i].Key));
            }

            builder.AppendLine(" });");
            for (int i = 0; i < keyArrays.Names.Count; i++)
            {
                builder.Append($"        private static readonly byte[][] ").Append(keyArrays.Names[i]).Append(" = ToKeyBytes(new string[] { ");
                for (int k = 0; k < keyArrays.Keys[i].Length; k++)
                {
                    if (k > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(Literal(keyArrays.Keys[i][k]));
                }

                builder.AppendLine(" });");
            }

            builder.AppendLine();
            builder.AppendLine("        private static byte[][] ToKeyBytes(string[] keys)");
            builder.AppendLine("        {");
            builder.AppendLine("            var result = new byte[keys.Length][];");
            builder.AppendLine("            for (int i = 0; i < keys.Length; i++)");
            builder.AppendLine("            {");
            builder.AppendLine("                result[i] = System.Text.Encoding.UTF8.GetBytes(keys[i]);");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            return result;");
            builder.AppendLine("        }");
            builder.AppendLine();
            EmitCaptureMethod(builder, head, fields);
            builder.AppendLine();
            EmitRestoreMethod(builder, head, fields);
            builder.AppendLine("    }");
            builder.Append(' ', nestDepth * 4);
            builder.AppendLine("}");
            for (int i = nestDepth - 1; i >= 0; i--)
            {
                builder.Append(' ', (nestDepth - 1 - i) * 4);
                builder.AppendLine("}");
            }

            if (hasNamespace)
            {
                builder.AppendLine("}");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 收集值模型子树内的嵌套作用域键数组。
        /// </summary>
        /// <param name="model">值模型。</param>
        /// <param name="path">索引路径（顶层字段序号为根，逐段 _序号/_e/_v 延展）。</param>
        /// <param name="table">登记表。</param>
        private static void CollectKeyArrays(SaveValueModel model, string path, KeyArrayTable table)
        {
            switch (model.Kind)
            {
                case FieldKind.NestedObject:
                {
                    var keys = new string[model.Fields.Count];
                    for (int i = 0; i < model.Fields.Count; i++)
                    {
                        keys[i] = model.Fields[i].Key;
                    }

                    table.Add("s_KeyBytes" + path, keys);
                    for (int i = 0; i < model.Fields.Count; i++)
                    {
                        CollectKeyArrays(model.Fields[i].Value, path + "_" + i, table);
                    }

                    break;
                }
                case FieldKind.Sequence:
                    CollectKeyArrays(model.Element, path + "_e", table);
                    break;
                case FieldKind.Map:
                    CollectKeyArrays(model.Value, path + "_v", table);
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region 捕获发射 [CAPTURE EMISSION]

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

        /// <summary>序列容器计数成员名（数组 Length / 集合 Count）。</summary>
        private static string CountMember(SaveValueModel model)
        {
            return model.Container == SequenceContainer.Array ? "Length" : "Count";
        }

        #endregion

        #region 恢复发射 [RESTORE EMISSION]

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

        #endregion

        #region 名称映射 [NAME MAPPING]

        /// <summary>键控写入方法名（枚举走底层类型的写入方法）。</summary>
        private static string KeyedWriterMethod(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return "Write" + model.EnumUnderlyingName;
            }

            return "Write" + KindSuffix(model.Kind);
        }

        /// <summary>元素写入方法名。</summary>
        private static string ElementWriterMethod(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return "Write" + model.EnumUnderlyingName + "Element";
            }

            return "Write" + KindSuffix(model.Kind) + "Element";
        }

        /// <summary>读取方法名。</summary>
        private static string ReaderMethod(SaveValueModel model)
        {
            if (model.Kind == FieldKind.Enum)
            {
                return "Read" + model.EnumUnderlyingName;
            }

            return "Read" + KindSuffix(model.Kind);
        }

        /// <summary>类型名后缀（KVT 写入/读取方法族共用词干）。</summary>
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

        /// <summary>ESaveKvType 成员名（枚举映射到底层整型类型码）。</summary>
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

        #endregion

        #region 模块初始化器 [MODULE INITIALIZER]

        /// <summary>
        /// 生成模块初始化器（含 ModuleInitializerAttribute 缺失定义——同程序集内私有副本，按完整类型名被编译器识别）。
        /// </summary>
        /// <param name="registrationLines">捕获器注册行。</param>
        /// <param name="migratorRegistrationLines">迁移器注册行。</param>
        /// <param name="hasBuiltinModuleInitializer">编译单元是否已带 ModuleInitializerAttribute。</param>
        private static string EmitModuleInitializer(List<string> registrationLines, List<string> migratorRegistrationLines, bool hasBuiltinModuleInitializer)
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

            foreach (string line in migratorRegistrationLines)
            {
                builder.AppendLine(line);
            }

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        #endregion

        /// <summary>字符串字面量转义。</summary>
        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }

        /// <summary>剥离键字面量首尾引号（拼入告警消息用）。</summary>
        private static string TrimQuotes(string literal)
        {
            return literal.Length >= 2 && literal[0] == '"' && literal[literal.Length - 1] == '"'
                ? literal.Substring(1, literal.Length - 2)
                : literal;
        }

        /// <summary>生成头注释。</summary>
        private static void AppendGeneratedHeader(StringBuilder builder)
        {
            builder.AppendLine("// <auto-generated by SaveHostGenerator />");
        }
    }
}
