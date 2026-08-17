using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Brama.SourceGenerator.Tests;

/// <summary>
/// Covers <c>BRAMA001</c>: the build-time proof that no <c>ErrorKind</c> can reach a client with a
/// status code nobody chose.
/// </summary>
public class ErrorKindExhaustivenessTests
{
    private const string AllKinds = """
        api.ProblemDetails(p => p
            .Map(ErrorKind.Validation, 400)
            .Map(ErrorKind.NotFound, 404)
            .Map(ErrorKind.Conflict, 409)
            .Map(ErrorKind.Unauthorized, 401)
            .Map(ErrorKind.Forbidden, 403)
            .Map(ErrorKind.Problem, 400));
        """;

    [Fact]
    public void A_complete_mapping_table_reports_nothing()
    {
        GeneratorRunResult result = RunOnConfiguration(AllKinds);

        Assert.Empty(result.GeneratorDiagnostics);
    }

    [Fact]
    public void A_missing_mapping_is_an_error_naming_the_kind()
    {
        GeneratorRunResult result = RunOnConfiguration("""
            api.ProblemDetails(p => p
                .Map(ErrorKind.Validation, 400)
                .Map(ErrorKind.NotFound, 404)
                .Map(ErrorKind.Conflict, 409)
                .Map(ErrorKind.Forbidden, 403)
                .Map(ErrorKind.Problem, 400));
            """);

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("BRAMA001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("ErrorKind.Unauthorized", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void This_is_the_Lanka_bug_a_forgotten_Unauthorized_mapping()
    {
        GeneratorRunResult result = RunOnConfiguration("""
            api.ProblemDetails(p => p
                .Map(ErrorKind.Validation, 400)
                .Map(ErrorKind.NotFound, 404)
                .Map(ErrorKind.Conflict, 409)
                .Map(ErrorKind.Forbidden, 403)
                .Map(ErrorKind.Problem, 400));

            api.Group("Orders").MapGet<string>("/", (IEndpointContext ctx) => ctx.Unauthorized());
            """);

        Assert.Contains(
            result.GeneratorDiagnostics,
            diagnostic => diagnostic.Id == "BRAMA001" && diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("Unauthorized", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_missing_kind_is_reported_not_only_the_first()
    {
        GeneratorRunResult result = RunOnConfiguration("api.ProblemDetails(p => p.Map(ErrorKind.NotFound, 404));");

        Assert.Equal(5, result.GeneratorDiagnostics.Length);
        Assert.All(result.GeneratorDiagnostics, diagnostic => Assert.Equal("BRAMA001", diagnostic.Id));
    }

    [Fact]
    public void Mappings_split_across_several_calls_count_as_one_table()
    {
        GeneratorRunResult result = RunOnConfiguration("""
            api.ProblemDetails(p => p.Map(ErrorKind.Validation, 400).Map(ErrorKind.NotFound, 404));
            api.ProblemDetails(p => p.Map(ErrorKind.Conflict, 409).Map(ErrorKind.Unauthorized, 401));
            api.ProblemDetails(p => p.Map(ErrorKind.Forbidden, 403).Map(ErrorKind.Problem, 400));
            """);

        Assert.Empty(result.GeneratorDiagnostics);
    }

    [Fact]
    public void Configuration_handed_to_another_method_reports_BRAMA011_instead_of_guessing()
    {
        GeneratorRunResult result = RunOnConfiguration("""
            Configure(api);
            api.ProblemDetails(p => p.Map(ErrorKind.NotFound, 404));
            """);

        Diagnostic diagnostic = Assert.Single(result.GeneratorDiagnostics);
        Assert.Equal("BRAMA011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("Configure", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public void BRAMA011_never_breaks_a_build()
    {
        GeneratorRunResult result = RunOnConfiguration("Configure(api); api.ProblemDetails(p => p.UseLocalization());");

        Assert.All(
            result.GeneratorDiagnostics,
            diagnostic => Assert.NotEqual(DiagnosticSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void Source_with_no_Brama_configuration_at_all_reports_nothing()
    {
        GeneratorRunResult result = GeneratorTestHelper.Run(
            new BramaGenerator(),
            "namespace Sample; public class Marker { }");

        Assert.Empty(result.GeneratorDiagnostics);
    }

    private static GeneratorRunResult RunOnConfiguration(string configuration) =>
        GeneratorTestHelper.Run(new BramaGenerator(), $$"""
            using Brama;
            using Brama.Configuration;

            namespace Sample;

            public static class Api
            {
                public static void Build()
                {
                    var api = new ApiConfig();
                    {{configuration}}
                }

                private static void Configure(ApiConfig api)
                {
                }
            }
            """);
}
