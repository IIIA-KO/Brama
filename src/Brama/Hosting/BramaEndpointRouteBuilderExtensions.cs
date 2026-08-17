using Brama.Configuration;
using Brama.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Brama.Hosting;

/// <summary>
/// Wires the config tree into ASP.NET Core's routing.
/// </summary>
public static class BramaEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps every endpoint declared in the tree, resolving each one's configuration exactly once
    /// and turning it into ordinary ASP.NET Core endpoint metadata. Nothing about the cascade
    /// survives into request handling.
    /// </summary>
    public static IEndpointRouteBuilder MapBrama(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        ApiConfig api = endpoints.ServiceProvider.GetRequiredService<ApiConfig>();
        IVersionStrategy? versionStrategy = api.VersioningConfig.Strategy;

        UseMiddlewareTheTreeAsksFor(endpoints, api);

        foreach (EndpointDefinition endpoint in api.AllEndpoints())
        {
            ResolvedConfig config = endpoint.Resolve();
            string route = ApplyVersion(endpoint.ResolveRoute(), config.Version, versionStrategy);

            RouteHandlerBuilder builder = endpoints.MapMethods(route, [endpoint.HttpMethod], endpoint.Handler);

            ApplyGroupName(builder, endpoint);

            if (endpoint.RouteName is { } routeName)
            {
                builder.WithName(routeName);
            }

            ApplyAuthorization(builder, config);
            ApplyCors(builder, config);
            ApplyRateLimiting(builder, config);
            ApplyOutputCache(builder, config);

            // Added before the user's filters, and therefore outermost: endpoint filters run in the
            // order they are added. Being outermost is what lets a user filter short-circuit with a
            // Result<T> of its own and still have it rendered as problem+json, and what puts the
            // exception boundary around the user filters rather than inside them.
            endpoint.ApplyResponseTyping(builder, config.DisallowLinks);

            ApplyResponseBehaviors(builder, config, route);
            ApplyUserFilters(builder, config);

            // Innermost, so a user filter can still rewrite what gets validated, and the handler
            // never runs against a request that failed a field-level check.
            builder.AddEndpointFilter<ValidationFilter>();
        }

        return endpoints;
    }

    /// <summary>
    /// Turns on the middleware the settings in the tree depend on.
    /// </summary>
    /// <remarks>
    /// Rate limiting, output caching and CORS are all endpoint metadata plus a piece of middleware
    /// that reads it; attaching one without the other silently does nothing. Since the tree already
    /// says which of them are in use, requiring the host to also remember three
    /// <c>Use…()</c> calls would only create a way to get it wrong. Each is added once, and only if
    /// something actually asked for it.
    /// </remarks>
    private static void UseMiddlewareTheTreeAsksFor(IEndpointRouteBuilder endpoints, ApiConfig api)
    {
        if (endpoints is not IApplicationBuilder app)
        {
            return;
        }

        ResolvedConfig[] configs = [.. api.AllEndpoints().Select(endpoint => endpoint.Resolve())];

        if (configs.Any(config => config.CorsPolicies.Count > 0))
        {
            app.UseCors();
        }

        if (configs.Any(config => config.RateLimitPolicy is not null || config.InlineRateLimit is not null))
        {
            app.UseRateLimiter();
        }

        if (configs.Any(config => config.OutputCacheDuration is not null))
        {
            app.UseOutputCache();
        }
    }

    private static string ApplyVersion(string route, int? version, IVersionStrategy? strategy) =>
        version is null || strategy is null ? route : strategy.ApplyVersion(route, version.Value);

    private static void ApplyGroupName(RouteHandlerBuilder builder, EndpointDefinition endpoint)
    {
        if (endpoint.Parent is EndpointGroup group)
        {
            builder.WithTags(group.Name);
        }
    }

    private static void ApplyAuthorization(RouteHandlerBuilder builder, ResolvedConfig config)
    {
        if (config.AllowAnonymous)
        {
            builder.AllowAnonymous();
            return;
        }

        if (config.AuthorizationPolicies.Count > 0)
        {
            builder.RequireAuthorization([.. config.AuthorizationPolicies]);
        }
    }

    private static void ApplyCors(RouteHandlerBuilder builder, ResolvedConfig config)
    {
        foreach (string policy in config.CorsPolicies)
        {
            builder.RequireCors(policy);
        }
    }

    private static void ApplyRateLimiting(RouteHandlerBuilder builder, ResolvedConfig config)
    {
        if (config.RateLimitPolicy is { } named)
        {
            builder.RequireRateLimiting(named);
        }

        if (config.InlineRateLimit is { } inline)
        {
            builder.RequireRateLimiting(RateLimitPolicyNaming.NameFor(inline));
        }
    }

    private static void ApplyOutputCache(RouteHandlerBuilder builder, ResolvedConfig config)
    {
        if (config.OutputCacheDuration is { } duration)
        {
            builder.CacheOutput(policy => policy.Expire(duration));
        }
    }

    private static void ApplyResponseBehaviors(RouteHandlerBuilder builder, ResolvedConfig config, string route)
    {
        if (!ResponseBehaviorsFilter.IsNeededFor(config))
        {
            return;
        }

        var filter = new ResponseBehaviorsFilter(config, route);
        builder.AddEndpointFilter(filter);
    }

    private static void ApplyUserFilters(RouteHandlerBuilder builder, ResolvedConfig config)
    {
        foreach (Type filterType in config.Filters)
        {
            builder.AddEndpointFilterFactory((factoryContext, next) =>
            {
                var filter = (IEndpointFilter)ActivatorUtilities.CreateInstance(
                    factoryContext.ApplicationServices,
                    filterType);

                return invocationContext => filter.InvokeAsync(invocationContext, next);
            });
        }
    }
}
