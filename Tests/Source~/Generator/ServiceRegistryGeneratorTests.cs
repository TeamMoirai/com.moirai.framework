using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Moirai.Atropos.SourceGenerators;
using NUnit.Framework;

namespace Generator
{
    public sealed class ServiceRegistryGeneratorTests
    {
        private const string FrameworkStubs = @"
namespace Moirai.Atropos.Kernel.Composition
{
    public enum ServiceLifetime : byte { Root = 0, Scoped = 1 }
    public sealed class ServiceAttribute : System.Attribute
    {
        public ServiceAttribute(System.Type contract) { Contract = contract; }
        public System.Type Contract { get; }
        public ServiceLifetime Lifetime { get; set; }
    }
    public readonly struct ServiceDescriptor
    {
        public ServiceDescriptor(System.Type contract, System.Type[] dependencies,
            ServiceLifetime lifetime, System.Func<System.IServiceProvider, object> factory)
        { Contract = contract; Dependencies = dependencies; Lifetime = lifetime; Factory = factory; }
        public System.Type Contract { get; }
        public System.Type[] Dependencies { get; }
        public ServiceLifetime Lifetime { get; }
        public System.Func<System.IServiceProvider, object> Factory { get; }
    }
    public interface IServiceRegistrar { void Add(in ServiceDescriptor descriptor); }
    public interface IServiceModule { void Register(IServiceRegistrar registrar); }
}
";
        [Test]
        public void ValidService_GeneratesAssemblyModule_WithDeclaredDependency()
        {
            const string source = @"
using Moirai.Atropos.Kernel.Composition;
public interface IClock { }
[Service(typeof(IClock))] public sealed class Clock : IClock { }
public interface IConsumer { }
[Service(typeof(IConsumer))] public sealed class Consumer : IConsumer
{
    public Consumer(IClock clock) { Clock = clock; }
    public IClock Clock { get; }
}";
            (Compilation output, GeneratorDriverRunResult result) = Generate(source, "Moirai.Atropos");
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedTrees, Has.Length.EqualTo(1));
            StringAssert.Contains("CompiledModule_Moirai_Atropos", result.GeneratedTrees[0].ToString());
            Assert.That(output.GetDiagnostics().Any(d => d.Severity == DiagnosticSeverity.Error), Is.False);
        }

        [TestCase("[Service(typeof(IItem))] public sealed class Item : IItem { public Item(){} public Item(IItem item){} }", "MOIRAI700")]
        [TestCase("[Service(typeof(IItem))] public sealed class Item<T> : IItem { }", "MOIRAI700")]
        [TestCase("[Service(typeof(IItem))] public sealed class Item { }", "MOIRAI700")]
        [TestCase("[Service(typeof(IItem))] public sealed class Item : IItem { public Item(System.IServiceProvider provider){} }", "MOIRAI701")]
        public void InvalidService_ReportsDiagnostic(string declaration, string expected)
        {
            Generate("using Moirai.Atropos.Kernel.Composition; public interface IItem { } " + declaration,
                "Invalid.Probe", out GeneratorDriverRunResult result);
            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain(expected));
        }

        [Test]
        public void DuplicateContract_RejectsAmbiguousRegistration()
        {
            const string source = @"
using Moirai.Atropos.Kernel.Composition;
public interface IItem { }
[Service(typeof(IItem))] public sealed class First : IItem { }
[Service(typeof(IItem))] public sealed class Second : IItem { }";
            Generate(source, "Duplicate.Probe", out GeneratorDriverRunResult result);
            Assert.That(result.Diagnostics.Select(item => item.Id), Does.Contain("MOIRAI702"));
        }

        private static (Compilation Output, GeneratorDriverRunResult Result) Generate(string source, string assembly)
        {
            var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
                .Split(Path.PathSeparator)
                .Distinct();
            var references = paths.Where(File.Exists).Select(path => MetadataReference.CreateFromFile(path));
            var options = new CSharpParseOptions(LanguageVersion.CSharp9);
            CSharpCompilation compilation = CSharpCompilation.Create(assembly,
                new[] { CSharpSyntaxTree.ParseText(source + FrameworkStubs, options) }, references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            GeneratorDriver driver = CSharpGeneratorDriver.Create(
                new[] { new ServiceRegistryGenerator().AsSourceGenerator() }, parseOptions: options);
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out _);
            return (output, driver.GetRunResult());
        }

        private static (Compilation Output, GeneratorDriverRunResult Result) Generate(
            string source, string assembly, out GeneratorDriverRunResult result)
        {
            (Compilation output, GeneratorDriverRunResult run) = Generate(source, assembly);
            result = run;
            return (output, run);
        }
    }
}
