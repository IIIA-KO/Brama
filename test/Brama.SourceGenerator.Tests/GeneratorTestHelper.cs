using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Brama.SourceGenerator.Tests;

/// <summary>
/// Drives a Brama incremental generator over an in-memory compilation so tests can assert on the
/// code and diagnostics it produces.
/// </summary>
public static class GeneratorTestHelper
{
    public static GeneratorRunResult Run(IIncrementalGenerator generator, string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Touch the Brama assemblies so they are loaded before the sweep below: the generator
        // resolves ErrorKind and ApiConfig by metadata name, and a merely-referenced-but-unloaded
        // assembly would leave it looking at source with no Brama in it at all.
        EnsureLoaded(
            typeof(ErrorKind),
            typeof(Brama.Configuration.ApiConfig),
            typeof(Microsoft.Extensions.DependencyInjection.IServiceCollection),
            typeof(Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions));

        List<MetadataReference> references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            assemblyName: "Brama.SourceGenerator.Tests.Generated",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator.AsSourceGenerator());

        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation outputCompilation,
            out ImmutableArray<Diagnostic> generatorDiagnostics);

        ImmutableArray<Diagnostic> compilationDiagnostics = outputCompilation.GetDiagnostics();

        return new GeneratorRunResult(outputCompilation, generatorDiagnostics, compilationDiagnostics);
    }

    private static void EnsureLoaded(params Type[] types) => _ = types.Length;
}

/// <summary>
/// The outcome of a generator run: the post-generation compilation, the diagnostics the generator
/// itself reported, and the diagnostics of compiling the generated code.
/// </summary>
public sealed record GeneratorRunResult(
    Compilation OutputCompilation,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationDiagnostics)
{
    public IEnumerable<Diagnostic> Errors =>
        CompilationDiagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public string GetGeneratedSource(string hintNameSuffix) =>
        OutputCompilation.SyntaxTrees
            .Single(tree => tree.FilePath.EndsWith(hintNameSuffix, StringComparison.Ordinal))
            .ToString();
}
