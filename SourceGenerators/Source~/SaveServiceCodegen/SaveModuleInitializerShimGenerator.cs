using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <c>ModuleInitializerAttribute</c> 补丁生成器：目标编译单元引用的核心库没有该属性时，发一份同程序集 internal 副本（按完整类型名被编译器识别）。
    /// </summary>
    /// <remarks>
    /// 这份副本由本生成器唯一所有：捕获器/迁移器/后端三个初始化器各自成文件，任何一处重复定义同一个 internal 类型都是 CS0101。 <br />
    /// 本工程实测（字节探针 + 正对照）<c>unity-4.8-api/mscorlib.dll</c> 与 netstandard facade 里都没有 <c>ModuleInitializerAttribute</c>，即补丁分支是活路而非死代码。
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class SaveModuleInitializerShimGenerator : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) => Execute(spc, compilation));
        }

        /// <summary>
        /// 仅在编译单元缺该属性时发射补丁。
        /// </summary>
        /// <param name="context">源生成上下文。</param>
        /// <param name="compilation">当前编译。</param>
        private static void Execute(SourceProductionContext context, Compilation compilation)
        {
            if (compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute") != null)
            {
                return;
            }

            var builder = new StringBuilder(256);
            EmitHelpers.AppendGeneratedHeader(builder);
            builder.AppendLine("namespace System.Runtime.CompilerServices");
            builder.AppendLine("{");
            builder.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = false, Inherited = false)]");
            builder.AppendLine("    internal sealed class ModuleInitializerAttribute : global::System.Attribute");
            builder.AppendLine("    {");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            context.AddSource("SaveModuleInitializerShim.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
        }
    }
}
