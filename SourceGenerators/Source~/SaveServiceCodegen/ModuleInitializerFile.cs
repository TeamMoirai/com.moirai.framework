using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 模块初始化器发射器：三个注册生成器各发一个属于自己的初始化器类。
    /// </summary>
    /// <remarks>
    /// 同一装配内多个 <c>ModuleInitializer</c> 合法且执行顺序不保证——捕获器/迁移器/后端落的是三张互不依赖的注册表，故顺序无关。 <br />
    /// <c>ModuleInitializerAttribute</c> 缺失时的同程序集副本不在这里发，归 <see cref="SaveModuleInitializerShimGenerator"/> 唯一所有：多处发同名 internal 类型即 CS0101。
    /// </remarks>
    internal static class ModuleInitializerFile
    {
        /// <summary>
        /// 组装 <c>internal static class className</c> 及其模块初始化方法体。
        /// </summary>
        /// <param name="className">初始化器类名（每个生成器各占一个，不得与他处重名）。</param>
        /// <param name="registrationLines">方法体内的注册语句。</param>
        /// <returns>完整源文件文本。</returns>
        internal static string Emit(string className, List<string> registrationLines)
        {
            var builder = new StringBuilder(1024);
            EmitHelpers.AppendGeneratedHeader(builder);
            builder.AppendLine("internal static class " + className);
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
    }
}
