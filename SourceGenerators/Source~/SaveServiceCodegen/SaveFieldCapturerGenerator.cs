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
    /// <c>[SaveField]</c> 捕获器生成器：扫描标注字段生成强类型键值捕获器，并把捕获器自注册行写进自己的模块初始化器 <c>SaveCaptureModuleInit</c>。
    /// </summary>
    /// <remarks>
    /// 捕获器嵌套在组件类型内部以访问私有字段（零反射零装箱），经 <c>SaveCapturerRegistry.Register</c> 自注册（AOT 安全，无运行期反射扫描）。<br />
    /// 同装配内按功能分立的兄弟生成器各占一个初始化器：<see cref="SaveMigratorRegistrationGenerator"/>（<c>SaveMigrationManager</c>）、<br />
    /// <see cref="SaveSerializerRegistrationGenerator"/>（<c>SaveSerializerRegistry</c>）、<see cref="SaveModuleInitializerShimGenerator"/>（<c>ModuleInitializerAttribute</c> 缺失时的同程序集私有副本，唯一归属方）。<br />
    /// 支持字段：基元/枚举/string/DateTime/TimeSpan、Unity 数学类型、集合（数组/List/Queue/Stack/HashSet/Dictionary，元素递归支持标量与嵌套数据类，引用元素不支持）、<br />
    /// 嵌套 <c>[SaveData]</c> 数据类、UnityEngine.Object 引用（场景对象存引用 ID，其余存资产定位串）。<br />
    /// 捕获器额外发射 <see cref="SaveFieldModel.SchemaVersion"/>（<c>[SaveComponentSchema]</c> 声明，缺省 1）；非 MonoBehaviour 类型上的 <c>[SaveField]</c> 不生成捕获器。<br />
    /// 诊断 MIRAI300/301/303/304/305/306/307/308：字段类型不支持 / 存档键重复 / 包含类型须为 partial class / 须为实例字段 /<br />
    /// 场景引用需 SaveObjectIdentity / 引用类型不明 / 嵌套数据类型无效 / 集合元素或映射键值类型不支持（MIRAI302 属迁移器，309~312 属后端）。
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed partial class SaveFieldCapturerGenerator : IIncrementalGenerator
    {
        /// <summary>
        /// 注册增量管线。
        /// </summary>
        /// <remarks>
        /// 字段扫描以带特性列表的字段声明粗筛再语义判定 <c>CreateSyntaxProvider</c>，而非 <c>ForAttributeWithMetadataName</c>（后者在本项目部分编译单元上静默不产出）。
        /// </remarks>
        /// <param name="context">增量生成器初始化上下文。</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var fields = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is FieldDeclarationSyntax declaration && declaration.AttributeLists.Count > 0,
                static (ctx, ct) => SaveFieldModel.Create(ctx, ct));

            context.RegisterSourceOutput(fields.Collect(), static (spc, source) => Execute(spc, source));
        }

        /// <summary>
        /// 生成全部捕获器源码、自注册行与诊断。
        /// </summary>
        private static void Execute(SourceProductionContext context, ImmutableArray<SaveFieldModel[]> fieldGroups)
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

            if (registrationLines.Count > 0)
            {
                context.AddSource("SaveCaptureModuleInit.g.cs", SourceText.From(ModuleInitializerFile.Emit("SaveCaptureModuleInit", registrationLines), Encoding.UTF8));
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }


        /// <summary>
        /// 字符串字面量转义。
        /// </summary>
        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, quote: true);
        }

        /// <summary>
        /// 剥离键字面量首尾引号（拼入告警消息用）。
        /// </summary>
        private static string TrimQuotes(string literal)
        {
            return literal.Length >= 2 && literal[0] == '"' && literal[literal.Length - 1] == '"'
                ? literal.Substring(1, literal.Length - 2)
                : literal;
        }
    }
}
