using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Moirai.Atropos.SourceGenerators
{
    [Generator]
    public sealed class ServiceRegistryGenerator : IIncrementalGenerator
    {
        private static readonly DiagnosticDescriptor Invalid = new DiagnosticDescriptor(
            "MOIRAI700", "Invalid service",
            "Service '{0}' must be accessible, concrete, closed, assignable to its contract, and have one public constructor",
            "Composition", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Dependency = new DiagnosticDescriptor(
            "MOIRAI701", "Invalid dependency", "Service '{0}' has an unsupported dependency '{1}'",
            "Composition", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Duplicate = new DiagnosticDescriptor(
            "MOIRAI702", "Duplicate service contract",
            "Contract '{0}' is registered by more than one service in this assembly",
            "Composition", DiagnosticSeverity.Error, true);

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            IncrementalValuesProvider<Candidate> candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
                "Moirai.Atropos.Kernel.Composition.ServiceAttribute",
                (node, _) => node is ClassDeclarationSyntax,
                (source, _) => ReadCandidate(source));
            var inputs = candidates.Collect()
                .Combine(context.CompilationProvider.Select((compilation, _) => compilation.AssemblyName));
            context.RegisterSourceOutput(inputs, (production, value) => Emit(production, value.Left, value.Right));
        }

        private static Candidate ReadCandidate(GeneratorAttributeSyntaxContext context)
        {
            AttributeData attribute = context.Attributes[0];
            var type = (INamedTypeSymbol)context.TargetSymbol;
            ITypeSymbol contract = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as ITypeSymbol
                : null;
            int lifetime = 0;
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
                if (argument.Key == "Lifetime") lifetime = Convert.ToInt32(argument.Value.Value);
            return new Candidate(type, contract, lifetime);
        }

        private void Emit(SourceProductionContext context, ImmutableArray<Candidate> candidates, string assemblyName)
        {
            if (candidates.Length == 0) return;
            var services = new List<Candidate>();
            var seenTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var seenContracts = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            bool failed = false;
            foreach (Candidate candidate in candidates.OrderBy(item => Name(item.Type), StringComparer.Ordinal))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (!seenTypes.Add(candidate.Type)) continue;
                IMethodSymbol[] constructors = candidate.Type.InstanceConstructors
                    .Where(item => item.DeclaredAccessibility == Accessibility.Public).ToArray();
                if (candidate.Contract == null || candidate.Type.IsAbstract || candidate.Type.IsStatic ||
                    !AccessibleAndClosed(candidate.Type) || !ReferenceContract(candidate.Contract) ||
                    !Assignable(candidate.Type, candidate.Contract) || constructors.Length != 1 ||
                    candidate.Lifetime < 0 || candidate.Lifetime > 1)
                {
                    context.ReportDiagnostic(Diagnostic.Create(Invalid, candidate.Type.Locations[0], Name(candidate.Type)));
                    failed = true;
                    continue;
                }
                candidate.Constructor = constructors[0];
                foreach (IParameterSymbol parameter in candidate.Constructor.Parameters)
                {
                    string dependency = parameter.Type.ToDisplayString();
                    if (parameter.RefKind != RefKind.None || !ReferenceContract(parameter.Type) ||
                        dependency == "System.IServiceProvider" ||
                        dependency == "Moirai.Atropos.Kernel.Composition.ServiceContainer")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(Dependency, candidate.Type.Locations[0],
                            Name(candidate.Type), dependency));
                        failed = true;
                    }
                }
                if (!seenContracts.Add(candidate.Contract))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Duplicate, candidate.Type.Locations[0],
                        Name(candidate.Contract)));
                    failed = true;
                }
                services.Add(candidate);
            }
            if (failed) return;
            var module = new StringBuilder("CompiledModule_");
            foreach (char character in assemblyName ?? "Assembly")
                module.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            var code = new StringBuilder();
            code.Append("public sealed class ").Append(module)
                .AppendLine(" : global::Moirai.Atropos.Kernel.Composition.IServiceModule");
            code.AppendLine("{");
            code.AppendLine("    public void Register(global::Moirai.Atropos.Kernel.Composition.IServiceRegistrar registrar)");
            code.AppendLine("    {");
            foreach (Candidate service in services)
            {
                ImmutableArray<IParameterSymbol> parameters = service.Constructor.Parameters;
                code.AppendLine("        registrar.Add(new global::Moirai.Atropos.Kernel.Composition.ServiceDescriptor(");
                code.Append("            typeof(").Append(Name(service.Contract)).AppendLine("),");
                if (parameters.Length == 0) code.AppendLine("            global::System.Type.EmptyTypes,");
                else
                {
                    code.Append("            new global::System.Type[] { ");
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        if (i != 0) code.Append(", ");
                        code.Append("typeof(").Append(Name(parameters[i].Type)).Append(')');
                    }
                    code.AppendLine(" },");
                }
                code.Append("            global::Moirai.Atropos.Kernel.Composition.ServiceLifetime.")
                    .Append(service.Lifetime == 0 ? "Root" : "Scoped").AppendLine(",");
                code.Append("            static provider => new ").Append(Name(service.Type)).Append('(');
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i != 0) code.Append(", ");
                    string type = Name(parameters[i].Type);
                    code.Append('(').Append(type).Append(")provider.GetService(typeof(").Append(type).Append("))");
                }
                code.AppendLine(")));");
            }
            code.AppendLine("    }");
            code.AppendLine("}");
            context.AddSource(module + ".g.cs", SourceText.From(code.ToString(), Encoding.UTF8));
        }

        private static bool AccessibleAndClosed(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                if (current.TypeParameters.Length != 0) return false;
                Accessibility access = current.DeclaredAccessibility;
                if (access != Accessibility.Public && access != Accessibility.Internal &&
                    access != Accessibility.ProtectedOrInternal) return false;
            }
            return true;
        }

        private static bool ReferenceContract(ITypeSymbol type)
        {
            if (!type.IsReferenceType) return false;
            if (type is INamedTypeSymbol named)
            {
                return !named.IsUnboundGenericType &&
                    !named.TypeArguments.Any(item => item.TypeKind == TypeKind.TypeParameter);
            }
            return type.TypeKind == TypeKind.Array;
        }

        private static bool Assignable(INamedTypeSymbol service, ITypeSymbol contract)
        {
            if (service.AllInterfaces.Any(item => SymbolEqualityComparer.Default.Equals(item, contract))) return true;
            for (INamedTypeSymbol current = service; current != null; current = current.BaseType)
                if (SymbolEqualityComparer.Default.Equals(current, contract)) return true;
            return false;
        }

        private static string Name(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        private sealed class Candidate
        {
            internal readonly INamedTypeSymbol Type;
            internal readonly ITypeSymbol Contract;
            internal readonly int Lifetime;
            internal IMethodSymbol Constructor;

            internal Candidate(INamedTypeSymbol type, ITypeSymbol contract, int lifetime)
            {
                Type = type;
                Contract = contract;
                Lifetime = lifetime;
            }
        }
    }
}
