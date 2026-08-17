using Brama.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Brama.Configuration;

public abstract partial class ConfigNode
{
    private readonly List<EndpointDefinition> _endpoints = [];

    /// <summary>The endpoints declared directly on this node.</summary>
    public IReadOnlyList<EndpointDefinition> Endpoints => _endpoints;

    /// <summary>Maps a <c>GET</c> endpoint returning <typeparamref name="TResponse"/>.</summary>
    public EndpointDefinition MapGet<TResponse>(string route, Delegate handler, Action<EndpointDefinition>? configure = null) =>
        Map<TResponse>(HttpMethods.Get, route, handler, configure);

    /// <summary>Maps a <c>POST</c> endpoint returning <typeparamref name="TResponse"/>.</summary>
    public EndpointDefinition MapPost<TResponse>(string route, Delegate handler, Action<EndpointDefinition>? configure = null) =>
        Map<TResponse>(HttpMethods.Post, route, handler, configure);

    /// <summary>Maps a <c>PUT</c> endpoint returning <typeparamref name="TResponse"/>.</summary>
    public EndpointDefinition MapPut<TResponse>(string route, Delegate handler, Action<EndpointDefinition>? configure = null) =>
        Map<TResponse>(HttpMethods.Put, route, handler, configure);

    /// <summary>Maps a <c>PATCH</c> endpoint returning <typeparamref name="TResponse"/>.</summary>
    public EndpointDefinition MapPatch<TResponse>(string route, Delegate handler, Action<EndpointDefinition>? configure = null) =>
        Map<TResponse>(HttpMethods.Patch, route, handler, configure);

    /// <summary>Maps a <c>DELETE</c> endpoint returning <typeparamref name="TResponse"/>.</summary>
    public EndpointDefinition MapDelete<TResponse>(string route, Delegate handler, Action<EndpointDefinition>? configure = null) =>
        Map<TResponse>(HttpMethods.Delete, route, handler, configure);

    private EndpointDefinition Map<TResponse>(
        string httpMethod,
        string route,
        Delegate handler,
        Action<EndpointDefinition>? configure)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(handler);

        var endpoint = new EndpointDefinition(
            this,
            httpMethod,
            route,
            handler,
            typeof(TResponse),
            static builder => builder
                .AddEndpointFilter<ResultConversionFilter<TResponse>>()
                .Produces<TResponse>());

        _endpoints.Add(endpoint);
        configure?.Invoke(endpoint);

        return endpoint;
    }
}
