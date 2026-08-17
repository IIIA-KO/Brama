using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Brama.SourceGenerator.Contracts;

namespace Brama.SourceGenerator;

/// <summary>
/// What one <c>ProblemDetails(…)</c> configuration call was found to do.
/// </summary>
/// <param name="Location">Where to report a diagnostic about it.</param>
/// <param name="MissingKinds">Enum members with no mapping, empty when the table is complete.</param>
/// <param name="EscapedTo">
/// The method the configuration was handed to, if it left the generator's view. When set, no
/// exhaustiveness claim is made — see <see cref="BramaDiagnostics.ConfigurationNotStaticallyAnalyzable"/>.
/// </param>
/// <param name="Contracts">The API surface this scope declares, for the Contracts build step.</param>
internal sealed record ProblemDetailsAnalysis(
    Location Location,
    ImmutableArray<string> MissingKinds,
    string? EscapedTo,
    IReadOnlyList<ContractGroup> Contracts);

/// <summary>
/// Reads <c>ProblemDetails(…)</c> configuration out of source syntax.
/// </summary>
/// <remarks>
/// Deliberately not an interpreter of the config tree — the tree is executable C# that only exists
/// once the application runs. This recognises the shapes the configuration is actually written in
/// (<c>.Map(ErrorKind.X, …)</c> chains inside a configuration method) and declines to guess about
/// anything else.
/// </remarks>
internal static class ProblemDetailsAnalyzer
{
    private const string ApiConfigMetadataName = "Brama.Configuration.ApiConfig";
    private const string ProblemDetailsConfigMetadataName = "Brama.Configuration.ProblemDetailsConfig";
    private const string ErrorKindMetadataName = "Brama.ErrorKind";

    public static bool IsCandidate(SyntaxNode node) =>
        node is InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ProblemDetails" },
        };

    public static ProblemDetailsAnalysis? Analyze(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        SemanticModel model = context.SemanticModel;

        INamedTypeSymbol? apiConfigType = model.Compilation.GetTypeByMetadataName(ApiConfigMetadataName);
        INamedTypeSymbol? errorKindType = model.Compilation.GetTypeByMetadataName(ErrorKindMetadataName);
        if (apiConfigType is null || errorKindType is null)
        {
            return null;
        }

        if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType, apiConfigType))
        {
            return null;
        }

        SyntaxNode scope = ConfigurationScopeOf(invocation);

        string? escapedTo = FindEscape(scope, model, apiConfigType, cancellationToken);
        if (escapedTo is not null)
        {
            return new ProblemDetailsAnalysis(invocation.GetLocation(), ImmutableArray<string>.Empty, escapedTo, []);
        }

        Dictionary<string, int> mapped = MappedKindsIn(scope, model, cancellationToken);
        ImmutableArray<string> missing = errorKindType.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => field.ConstantValue is not null && !mapped.ContainsKey(field.Name))
            .Select(field => $"ErrorKind.{field.Name}")
            .ToImmutableArray();

        IReadOnlyList<ContractGroup> contracts = ContractAnalyzer.Analyze(
            scope,
            model,
            [.. mapped.OrderBy(entry => entry.Key, StringComparer.Ordinal)],
            cancellationToken);

        return new ProblemDetailsAnalysis(invocation.GetLocation(), missing, EscapedTo: null, contracts);
    }

    /// <summary>
    /// The body the configuration is assembled in: the enclosing member, or the compilation unit
    /// for a top-level <c>Program.cs</c>. Widening to the whole member is what lets several
    /// <c>ProblemDetails(…)</c> calls, or a chain split across statements, be seen as one table.
    /// </summary>
    private static SyntaxNode ConfigurationScopeOf(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is MemberDeclarationSyntax or CompilationUnitSyntax)
            {
                return current;
            }
        }

        return node;
    }

    /// <summary>
    /// The <c>ErrorKind</c> to status-code mappings visible in a scope. The status code is read as a
    /// compile-time constant, so <c>StatusCodes.Status404NotFound</c> and a bare <c>404</c> are the
    /// same thing to a contract.
    /// </summary>
    private static Dictionary<string, int> MappedKindsIn(
        SyntaxNode scope,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        Dictionary<string, int> mapped = new(StringComparer.Ordinal);

        foreach (InvocationExpressionSyntax invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (invocation.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Map" }
                || invocation.ArgumentList.Arguments.Count == 0)
            {
                continue;
            }

            if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
                || method.ContainingType?.ToDisplayString() != ProblemDetailsConfigMetadataName)
            {
                continue;
            }

            if (model.GetSymbolInfo(invocation.ArgumentList.Arguments[0].Expression, cancellationToken).Symbol
                is not IFieldSymbol { ContainingType.Name: "ErrorKind" } kind)
            {
                continue;
            }

            int statusCode = invocation.ArgumentList.Arguments.Count > 1
                && model.GetConstantValue(invocation.ArgumentList.Arguments[1].Expression, cancellationToken) is { HasValue: true, Value: int value }
                    ? value
                    : 0;

            mapped[kind.Name] = statusCode;
        }

        return mapped;
    }

    /// <summary>
    /// Whether the configuration object is handed to some other method, which would take its
    /// mappings out of view.
    /// </summary>
    private static string? FindEscape(
        SyntaxNode scope,
        SemanticModel model,
        INamedTypeSymbol apiConfigType,
        CancellationToken cancellationToken)
    {
        foreach (InvocationExpressionSyntax invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
            {
                TypeInfo typeInfo = model.GetTypeInfo(argument.Expression, cancellationToken);
                if (SymbolEqualityComparer.Default.Equals(typeInfo.Type, apiConfigType))
                {
                    return model.GetSymbolInfo(invocation, cancellationToken).Symbol?.Name ?? "another method";
                }
            }
        }

        return null;
    }
}
