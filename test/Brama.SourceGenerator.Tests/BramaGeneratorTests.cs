using Microsoft.CodeAnalysis;
using Xunit;

namespace Brama.SourceGenerator.Tests;

public class BramaGeneratorTests
{
    [Fact]
    public void Emits_greeting_type_into_the_consuming_compilation()
    {
        GeneratorRunResult result = GeneratorTestHelper.Run(
            new BramaGenerator(),
            source: "namespace Sample; public class Marker { }");

        Assert.Empty(result.GeneratorDiagnostics);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.OutputCompilation.GetTypeByMetadataName("Brama.Generated.Greeting"));
    }
}
