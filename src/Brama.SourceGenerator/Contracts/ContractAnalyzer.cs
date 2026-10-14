using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Brama.SourceGenerator.Contracts;

/// <summary>
/// Reads the API surface out of a configuration written in the shapes Brama recognises.
/// </summary>
/// <remarks>
/// This is deliberately a whitelist, not an interpreter. It understands a group declared as a local
/// from a literal name, groups nested on such a local, and endpoints mapped on one — which is how
/// the configuration in every example is written. Anything else is reported as
/// <c>BRAMA011</c> rather than quietly producing a contract that covers less than it claims.
/// </remarks>
internal static class ContractAnalyzer
{
    private const string ApiConfigMetadataName = "Brama.Configuration.ApiConfig";
    private const string EndpointGroupMetadataName = "Brama.Configuration.EndpointGroup";

    private static readonly string[] MapMethods = ["MapGet", "MapPost", "MapPut", "MapPatch", "MapDelete"];

    public static IReadOnlyList<ContractGroup> Analyze(
        SyntaxNode scope,
        SemanticModel model,
        IReadOnlyList<KeyValuePair<string, int>> errorStatusCodes,
        CancellationToken cancellationToken
    )
    {
        Dictionary<ISymbol, GroupNode> groups = CollectGroups(scope, model, cancellationToken);

        if (groups.Count == 0)
        {
            return [];
        }

        foreach (InvocationExpressionSyntax invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (invocation.Expression is not MemberAccessExpressionSyntax access
                || !MapMethods.Contains(access.Name.Identifier.ValueText)
                || access.Name is not GenericNameSyntax generic)
            {
                continue;
            }

            if (model.GetSymbolInfo(access.Expression, cancellationToken).Symbol is not { } receiver
                || !groups.TryGetValue(receiver, out GroupNode? group))
            {
                continue;
            }

            if (ReadEndpoint(invocation, generic, access, model, cancellationToken) is { } endpoint)
            {
                group.Endpoints.Add(endpoint);
            }
        }

        return
        [
            .. groups.Values
                .Where(group => group.Endpoints.Count > 0 && group.EffectiveVersion() is not null)
                .Select(group => new ContractGroup(
                    group.Name,
                    group.EffectiveVersion()!.Value,
                    group.AcceptBreakingChangeReason,
                    [
                        .. group.Endpoints.Select(endpoint =>
                            endpoint with { Route = group.RoutePrefix() + endpoint.Route })
                    ],
                    errorStatusCodes))
                .OrderBy(group => group.Name, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// Finds <c>var orders = api.Group("Orders", g =&gt; …);</c> declarations and links each to its
    /// parent, so a nested group inherits its ancestors' route segments and version.
    /// </summary>
    private static Dictionary<ISymbol, GroupNode> CollectGroups(
        SyntaxNode scope,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        Dictionary<ISymbol, GroupNode> groups = new(SymbolEqualityComparer.Default);

        foreach (VariableDeclaratorSyntax declarator in scope.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (declarator.Initializer?.Value is not InvocationExpressionSyntax invocation
                || invocation.Expression is not MemberAccessExpressionSyntax
                {
                    Name.Identifier.ValueText: "Group"
                } access
                || invocation.ArgumentList.Arguments.Count == 0
                || model.GetDeclaredSymbol(declarator, cancellationToken) is not { } local)
            {
                continue;
            }

            if (LiteralString(invocation.ArgumentList.Arguments[0].Expression) is not { } name)
            {
                continue;
            }

            if (!IsConfigNode(model, access.Expression, cancellationToken))
            {
                continue;
            }

            var node = new GroupNode(name);
            ReadGroupSettings(invocation, node);

            if (model.GetSymbolInfo(access.Expression, cancellationToken).Symbol is { } receiver
                && groups.TryGetValue(receiver, out GroupNode? parent))
            {
                node.Parent = parent;
            }

            groups[local] = node;
        }

        return groups;
    }

    private static bool IsConfigNode(SemanticModel model, ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        string? type = model.GetTypeInfo(expression, cancellationToken).Type?.ToDisplayString();

        return type is ApiConfigMetadataName or EndpointGroupMetadataName;
    }

    /// <summary>Reads the settings that change a group's contract identity out of its configure lambda.</summary>
    private static void ReadGroupSettings(InvocationExpressionSyntax groupCall, GroupNode node)
    {
        foreach (InvocationExpressionSyntax call in groupCall.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (call.Expression is not MemberAccessExpressionSyntax access || call.ArgumentList.Arguments.Count == 0)
            {
                continue;
            }

            ExpressionSyntax argument = call.ArgumentList.Arguments[0].Expression;

            switch (access.Name.Identifier.ValueText)
            {
                case "Version" when argument is LiteralExpressionSyntax { Token.Value: int version }:
                    node.Version = version;
                    break;

                case "BaseRoute" when LiteralString(argument) is { } route:
                    node.BaseRoute = route;
                    break;

                case "AcceptBreakingChange" when LiteralString(argument) is { } reason:
                    node.AcceptBreakingChangeReason = reason;
                    break;

                default:
                    break;
            }
        }
    }

    private static ContractEndpoint? ReadEndpoint(
        InvocationExpressionSyntax invocation,
        GenericNameSyntax generic,
        MemberAccessExpressionSyntax access,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        if (invocation.ArgumentList.Arguments.Count < 2
            || LiteralString(invocation.ArgumentList.Arguments[0].Expression) is not { } route
            || generic.TypeArgumentList.Arguments.Count != 1)
        {
            return null;
        }

        ITypeSymbol? response = model.GetTypeInfo(generic.TypeArgumentList.Arguments[0], cancellationToken).Type;

        if (response is null)
        {
            return null;
        }

        string method = access.Name.Identifier.ValueText.Substring("Map".Length).ToUpperInvariant();
        IReadOnlyList<ContractShape> requests =
            ReadRequestShapes(invocation.ArgumentList.Arguments[1], model, cancellationToken);

        return new ContractEndpoint(method, route.TrimEnd('/'), ShapeOf(response), requests);
    }

    /// <summary>
    /// The handler's own complex parameters — the request bodies a client sends. Framework
    /// parameters carry no observable shape and are skipped.
    /// </summary>
    private static IReadOnlyList<ContractShape> ReadRequestShapes(
        ArgumentSyntax handler,
        SemanticModel model,
        CancellationToken cancellationToken)
    {
        IEnumerable<ParameterSyntax> parameters = handler.Expression switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => lambda.ParameterList.Parameters,
            SimpleLambdaExpressionSyntax lambda => [lambda.Parameter],
            _ => [],
        };

        return
        [
            .. parameters
                .Select(parameter => model.GetDeclaredSymbol(parameter, cancellationToken) as IParameterSymbol)
                .Where(parameter => parameter is not null)
                .Select(parameter => parameter!.Type)
                .Where(IsRequestShape)
                .Select(ShapeOf)
        ];
    }

    /// <summary>
    /// Whether a handler parameter is a request body rather than framework plumbing.
    /// </summary>
    /// <remarks>
    /// The test is "declared in this compilation's own source": request types are the application's,
    /// while <c>IEndpointContext</c>, <c>CancellationToken</c> and injected services all come from
    /// referenced assemblies. Matching on namespace prefixes instead would wrongly exclude an
    /// application whose own namespace happens to begin with <c>Brama</c> — as this repository's
    /// sample does.
    /// </remarks>
    private static bool IsRequestShape(ITypeSymbol type) =>
        type.SpecialType == SpecialType.None
        && type.TypeKind != TypeKind.Interface
        && type.Locations.Any(location => location.IsInSource);

    private static ContractShape ShapeOf(ITypeSymbol type) => new(
        type.ToDisplayString(),
        [
            .. type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(property => property.DeclaredAccessibility == Accessibility.Public && !property.IsStatic)
                .Select(property => new ContractField(property.Name, property.Type.ToDisplayString()))
        ]);

    private static string? LiteralString(ExpressionSyntax expression) =>
        expression is LiteralExpressionSyntax { Token.Value: string value } ? value : null;

    private sealed class GroupNode
    {
        public GroupNode(string name) => this.Name = name;

        public string Name { get; }

        public GroupNode? Parent { get; set; }

        public int? Version { get; set; }

        public string? BaseRoute { get; set; }

        public string? AcceptBreakingChangeReason { get; set; }

        public List<ContractEndpoint> Endpoints { get; } = [];

        public int? EffectiveVersion() => this.Version ?? this.Parent?.EffectiveVersion();

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            category: "Globalization",
            checkId: "CA1308:Normalize strings to uppercase",
            Justification = "Mirrors the runtime's own route-segment casing, which is lower case by URL convention.")]
        public string RoutePrefix()
        {
            var segment = "/" + (this.BaseRoute ?? this.Name.ToLowerInvariant()).Trim('/');

            return this.Parent is null ? segment : this.Parent.RoutePrefix() + segment;
        }
    }
}
