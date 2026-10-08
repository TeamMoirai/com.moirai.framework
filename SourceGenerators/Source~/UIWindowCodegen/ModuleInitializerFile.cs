using System.Collections.Generic;
using System.Text;

namespace Moirai.Atropos.SourceGenerators
{
    /// <summary>
    /// 模块初始化器发射器：窗口注册生成器发属于自己的初始化器类。
    /// </summary>
    /// <remarks>
    /// <c>ModuleInitializerAttribute</c> 缺失时的同程序集副本不在这里发：同包的 <c>SaveServiceCodegen</c> 已有
    /// <c>SaveModuleInitializerShimGenerator</c> 唯一持有该副本，两支生成器同包进同一批编译单元，多处发同名 internal 类型即 CS0101。
    /// </remarks>
    internal static class ModuleInitializerFile
    {
        /// <summary>
        /// 组装 <c>internal static class className</c> 及其模块初始化方法体。
        /// </summary>
        /// <param name="className">初始化器类名（不得与同程序集其它生成产物重名）。</param>
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
