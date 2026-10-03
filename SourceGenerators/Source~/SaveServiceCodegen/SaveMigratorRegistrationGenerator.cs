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
    /// 迁移器自注册生成器：扫描 <c>ISaveMigrator</c> 具体实现，把 <c>SaveMigrationManager.Register(new X())</c> 写进 <c>SaveMigratorModuleInit</c>。
    /// </summary>
    /// <remarks>
    /// 迁移器无特性锚点，按「带基类列表的 class」粗筛后语义检查接口；抽象基类合法存在但不注册（静默跳过），无法实例化注册的实现报 MIRAI302。
    /// </remarks>
    [Generator(LanguageNames.CSharp)]
    public sealed class SaveMigratorRegistrationGenerator : IIncrementalGenerator
    {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var migrators = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax declaration && declaration.BaseList != null,
                static (ctx, ct) => MigratorModel.Create(ctx, ct));

            context.RegisterSourceOutput(migrators.Collect(), static (spc, source) => Execute(spc, source));
        }

        /// <summary>
        /// 校验迁移器实现形状并产出自注册代码与诊断。
        /// </summary>
        /// <param name="context">源生成上下文。</param>
        /// <param name="allMigrators">扫描到的迁移器实现。</param>
        private static void Execute(SourceProductionContext context, ImmutableArray<MigratorModel> allMigrators)
        {
            var diagnostics = new List<Diagnostic>();
            var registrationLines = new List<string>();
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

                registrationLines.Add($"            global::Moirai.Atropos.Save.SaveMigrationManager.Register(new {migrator.TypeFqn}());");
            }

            if (registrationLines.Count > 0)
            {
                context.AddSource("SaveMigratorModuleInit.g.cs", SourceText.From(ModuleInitializerFile.Emit("SaveMigratorModuleInit", registrationLines), Encoding.UTF8));
            }

            foreach (Diagnostic diagnostic in diagnostics)
            {
                context.ReportDiagnostic(diagnostic);
            }
        }
    }
}
