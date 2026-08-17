using Microsoft.AspNetCore.Builder;

namespace Brama.Configuration;

/// <summary>
/// A leaf of the config tree: one HTTP method at one route, handled by one delegate. Being a node
/// means an endpoint can override anything its group set, for the one case that genuinely differs.
/// </summary>
public sealed class EndpointDefinition : ConfigNode<EndpointDefinition>
{
    private readonly Action<RouteHandlerBuilder> _applyResponseTyping;

    internal EndpointDefinition(
        ConfigNode parent,
        string httpMethod,
        string routeTemplate,
        Delegate handler,
        Type responseType,
        Action<RouteHandlerBuilder> applyResponseTyping)
        : base(parent)
    {
        HttpMethod = httpMethod;
        RouteTemplate = routeTemplate;
        Handler = handler;
        ResponseType = responseType;
        _applyResponseTyping = applyResponseTyping;
    }

    /// <summary>The HTTP method, e.g. <c>GET</c>.</summary>
    public string HttpMethod { get; }

    /// <summary>
    /// The route relative to the enclosing groups' segments — <c>"/{id:guid}"</c> under a group
    /// contributing <c>orders</c> serves <c>/orders/{id}</c>.
    /// </summary>
    public string RouteTemplate { get; }

    /// <summary>The handler delegate. Its parameters are bound by ASP.NET Core's own rules.</summary>
    public Delegate Handler { get; }

    /// <summary>The response body type, i.e. the <c>T</c> of the handler's <c>Result&lt;T&gt;</c>.</summary>
    public Type ResponseType { get; }

    /// <summary>
    /// The full route, groups' segments and the endpoint's own template combined, before any
    /// version prefix is applied.
    /// </summary>
    public string ResolveRoute()
    {
        IReadOnlyList<string> segments = Resolve().RouteSegments;
        string prefix = segments.Count == 0 ? string.Empty : "/" + string.Join('/', segments);
        string template = RouteTemplate.Trim();

        if (template is "" or "/")
        {
            return prefix.Length == 0 ? "/" : prefix;
        }

        return prefix + "/" + template.TrimStart('/');
    }

    /// <summary>
    /// Applies the response-typing that only the generic <c>Map*&lt;TResponse&gt;</c> call site
    /// knew: the typed <c>Result&lt;T&gt;</c>-to-HTTP conversion filter and the OpenAPI
    /// <c>Produces&lt;T&gt;</c> metadata.
    /// </summary>
    internal void ApplyResponseTyping(RouteHandlerBuilder builder) => _applyResponseTyping(builder);
}
