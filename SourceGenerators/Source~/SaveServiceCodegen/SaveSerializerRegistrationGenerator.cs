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
    /// 序列化后端自注册生成器：扫描 <c>[RegisterSerializer]</c> 标注的 <c>ISaveSerializer</c> 实现，把 <c>SaveSerializerRegistry.Register(&lt;静态编号&gt;, typeof(X))</c> 写进 <c>SaveSerializerModuleInit</c>。
    /// </summary>
    /// <remarks>
    /// 编号以实现的 <c>BackendId</c> 常量为唯一真源，静态取出后用于判重与判占号；<br />
    /// 形状非法 / 编号非常量 / 编号落在框架保留区 / 同编译单元撞号分别报 MIRAI309/310/311/312，一律 Error：<br />
    /// 注册跑在模块初始化期，放过去等于交付一份读不回旧档的存档；而运行期抛出会连累整个编辑器，故编译期必须挡下。 <br />
    /// 跨程序集撞号本生成器看不见，运行期由 <c>SaveSerializerRegistry.Register(ushort, Type)</c> 记 Fatal 并保留先到那份（该路径刻意不抛）。
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class SaveSerializerRegistrationGenerator : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var serializers = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax declaration && declaration.AttributeLists.Count > 0,
                static (ctx, ct) => SaveSerializerModel.Create(ctx, ct));

            var collected = serializers.Collect().Combine(context.CompilationProvider);
            context.RegisterSourceOutput(collected, static (spc, source) => Execute(spc, source.Left, source.Right));
        }

        /// <summary>
        /// 校验标注实现、产出自注册代码与诊断。
        /// </summary>
        /// <param name="context">源生成上下文。</param>
        /// <param name="allSerializers">扫描到的标注实现。</param>
        /// <param name="compilation">当前编译（读 <c>SaveBackendIds</c> 常量用）。</param>
        private static void Execute(SourceProductionContext context, ImmutableArray<SaveSerializerModel> allSerializers, Compilation compilation)
        {
            var diagnostics = new List<Diagnostic>();
            List<string> registrationLines = BuildSerializerRegistrations(allSerializers, compilation, diagnostics);

            if (registrationLines.Count > 0)
            {
                context.AddSource("SaveSerializerModuleInit.g.cs", SourceText.From(ModuleInitializerFile.Emit("SaveSerializerModuleInit", registrationLines), Encoding.UTF8));
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }


        /// <summary>
        /// 校验 <c>[RegisterSerializer]</c> 实现并产出注册行：形状非法、编号非常量、占号相撞、同编译单元重复分别报 MIRAI309~312 并跳过该行。
        /// </summary>
        /// <remarks>
        /// 生成的注册在模块初始化期执行，撞号即程序集加载期抛异常——故占号与重复一律判为 Error，不给 Warning 放行。
        /// </remarks>
        private static List<string> BuildSerializerRegistrations(ImmutableArray<SaveSerializerModel> allSerializers, Compilation compilation, List<Diagnostic> diagnostics)
        {
            var lines = new List<string>();
            if (allSerializers.IsDefaultOrEmpty)
            {
                return lines;
            }

            bool hasIdRules = TryReadBackendIdRules(compilation, out List<int> builtInIds, out int reservedMax, out int keyValueId);
            var owners = new Dictionary<int, SaveSerializerModel>();
            foreach (SaveSerializerModel serializer in allSerializers)
            {
                if (serializer == null)
                {
                    continue;
                }

                if (!serializer.IsRegistrable)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.InvalidSaveSerializer, serializer.Location, serializer.TypeDisplay));
                    continue;
                }

                if (!serializer.HasBackendId)
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.BackendIdNotConstant, serializer.Location, serializer.TypeDisplay));
                    continue;
                }

                int backendId = serializer.BackendId;
                // 保留区判据与运行期 SaveSerializerRegistry.Register 同一份数字；框架程序集未被引用时读不到常量，交由运行期 fail-fast 兜
                if (hasIdRules && (backendId == keyValueId || (backendId <= reservedMax && !builtInIds.Contains(backendId))))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.ReservedSaveBackendId, serializer.Location,
                        serializer.TypeDisplay, backendId, reservedMax, string.Join("/", builtInIds), keyValueId));
                    continue;
                }

                if (owners.TryGetValue(backendId, out SaveSerializerModel owner))
                {
                    diagnostics.Add(Diagnostic.Create(Diagnostics.DuplicateSaveBackendId, serializer.Location,
                        serializer.TypeDisplay, backendId, owner.TypeDisplay));
                    continue;
                }

                owners[backendId] = serializer;
                lines.Add($"            global::Moirai.Atropos.Save.SaveSerializerRegistry.Register({backendId}, typeof({serializer.TypeFqn}));");
            }

            return lines;
        }

        /// <summary>
        /// 读编译中 <c>SaveBackendIds</c> 的常量（内置标识、组件捕获保留标识、保留区上界）——不在生成器里另立一份字面量。
        /// </summary>
        private static bool TryReadBackendIdRules(Compilation compilation, out List<int> builtInIds, out int reservedMax, out int keyValueId)
        {
            builtInIds = new List<int>(4);
            reservedMax = 0;
            keyValueId = 0;

            if (compilation.GetTypeByMetadataName("Moirai.Atropos.Save.SaveBackendIds") is not INamedTypeSymbol ids)
            {
                return false;
            }

            foreach (string name in new[] { "JSON", "MESSAGE_PACK", "MEMORY_PACK", "PROTOBUF" })
            {
                if (!TryReadConst(ids, name, out int value))
                {
                    return false;
                }

                builtInIds.Add(value);
            }

            return TryReadConst(ids, "KEY_VALUE", out keyValueId) && TryReadConst(ids, "RESERVED_MAX", out reservedMax);
        }

        /// <summary>
        /// 读某类型的 public const 整型值。
        /// </summary>
        private static bool TryReadConst(INamedTypeSymbol type, string memberName, out int value)
        {
            value = 0;
            foreach (ISymbol member in type.GetMembers(memberName))
            {
                if (member is IFieldSymbol { IsConst: true } field && TryToInt32(field.ConstantValue, out value))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 常量装箱值折算为 int（const ushort 按自身类型装箱，字面量按 int 装箱，两者都收）。
        /// </summary>
        private static bool TryToInt32(object? constant, out int value)
        {
            switch (constant)
            {
                case byte number:
                    value = number;
                    return true;
                case sbyte number:
                    value = number;
                    return true;
                case short number:
                    value = number;
                    return true;
                case ushort number:
                    value = number;
                    return true;
                case int number:
                    value = number;
                    return true;
                case uint number:
                    value = checked((int)number);
                    return true;
                case long number:
                    value = checked((int)number);
                    return true;
                default:
                    value = 0;
                    return false;
            }
        }
    }
}
