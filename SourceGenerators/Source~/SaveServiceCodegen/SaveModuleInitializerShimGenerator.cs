using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// <c>ModuleInitializerAttribute</c> 补丁生成器：本编译单元绑不到可用定义时，发一份同程序集 internal 副本（按完整类型名被编译器识别）。
    /// </summary>
    /// <remarks>
    /// 这份副本由本生成器唯一所有：捕获器/迁移器/后端三个初始化器各自成文件，任何一处重复定义同一个 internal 类型都是 CS0101。 <br />
    /// 判据要问"绑定阶段用得上吗"，不能只问"引用集里有同名类型吗"：本工程引用集里 <c>MessagePack</c>/<c>ZLogger</c>/<c>Serilog</c>/<c>UniTask</c>/<c>PrimeTween</c>/<c>LitMotion</c>/<c>Unity.Addressables</c>
    /// 各带一份 <b>internal</b> 同名副本，它们不进 <c>GetTypeByMetadataName</c> 的答案（实测），却是绑定时能解析到的候选——本装配没有可用定义时特性名就落在它们身上，
    /// 刷 <c>CS0122 'ModuleInitializerAttribute' is inaccessible due to its protection level</c>（离线实测：同引用集去掉自身定义即复现，列位与生成文件的 4,46 对齐）。 <br />
    /// 转发目标缺席时按 <c>TypeKind.Error</c> 现形，同样照发补丁。
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
        /// 仅当本编译单元绑不到可用定义时发射补丁：源码里已有、或引用集里有且本装配可访问，才算绑得到。
        /// </summary>
        /// <param name="context">源生成上下文。</param>
        /// <param name="compilation">当前编译。</param>
        private static void Execute(SourceProductionContext context, Compilation compilation)
        {
            INamedTypeSymbol found = compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.ModuleInitializerAttribute");
            if (found != null && found.TypeKind != TypeKind.Error && CanBind(compilation, found))
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

        /// <summary>
        /// 判绑定阶段能否用上这个定义：本编译单元自己声明的，或引用集里对本装配可访问的。
        /// </summary>
        /// <param name="compilation">当前编译。</param>
        /// <param name="found">按完整类型名查到的符号。</param>
        /// <returns>可绑为 <c>true</c>，此时不必再发补丁。</returns>
        private static bool CanBind(Compilation compilation, ISymbol found)
        {
            if (!found.DeclaringSyntaxReferences.IsEmpty)
            {
                return true;
            }

            return found.ContainingAssembly != null
                && compilation.IsSymbolAccessibleWithin(found, compilation.Assembly);
        }
    }
}
