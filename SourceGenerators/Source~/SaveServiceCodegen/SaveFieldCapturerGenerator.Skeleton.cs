using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <see cref="SaveFieldCapturerGenerator"/> 捕获器骨架发射（组件类型逐层 partial 包裹、键登记表与 SaveCaptureInner 声明）.
    /// </summary>
    public sealed partial class SaveFieldCapturerGenerator
    {

        /// <summary>
        /// 嵌套键数组登记表（数组名 → 键集）。
        /// </summary>
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
            EmitHelpers.AppendGeneratedHeader(builder);
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
    }
}
