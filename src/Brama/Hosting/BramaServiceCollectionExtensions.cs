using System.Threading.RateLimiting;
using Brama.Configuration;
using Brama.Http;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Brama.Hosting;

/// <summary>
/// Registers Brama with the host's service collection.
/// </summary>
public static class BramaServiceCollectionExtensions
{
    /// <summary>
    /// Builds the config tree and registers everything <c>MapBrama()</c> will need.
    /// </summary>
    /// <remarks>
    /// The tree is built here, at service-registration time, rather than at mapping time, because
    /// inline-authored rate-limit and output-cache policies have to reach
    /// <see cref="IServiceCollection"/> before the host is built.
    /// </remarks>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configure">Declares the API's groups, endpoints and cascading settings.</param>
    public static IServiceCollection AddBrama(this IServiceCollection services, Action<ApiConfig> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var api = new ApiConfig();
        configure(api);

        GuardAgainstUnmappedErrorKinds(api);

        services.AddSingleton(api);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IEndpointContext, EndpointContext>();
        services.TryAddSingleton<ProblemDetailsWriter>();
        services.Replace(ServiceDescriptor
            .Singleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, BramaAuthorizationResultHandler>());

        RegisterInlineRateLimitPolicies(services, api);
        RegisterOutputCacheIfUsed(services, api);

        return services;
    }

    /// <summary>
    /// Fails startup when an <see cref="ErrorKind"/> has no status code mapped.
    /// </summary>
    /// <remarks>
    /// The source generator reports the same problem as <c>BRAMA001</c> at build time, which is
    /// where it should normally be caught. This guard covers the case the generator cannot see — a
    /// mapping table assembled by code it could not statically resolve — so the failure is a loud
    /// one at startup rather than a wrong status code on some rare path in production.
    /// </remarks>
    private static void GuardAgainstUnmappedErrorKinds(ApiConfig api)
    {
        IReadOnlyList<ErrorKind> unmapped = api.ProblemDetailsConfig.UnmappedKinds();
        if (unmapped.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "Every ErrorKind must be mapped to an HTTP status code before the application starts. Missing: "
            + string.Join(", ", unmapped.Select(kind => $"ErrorKind.{kind}"))
            + ". Add the missing .Map(...) calls to ProblemDetails(...).");
    }

    private static void RegisterInlineRateLimitPolicies(IServiceCollection services, ApiConfig api)
    {
        List<RateLimitPolicySpec> specs = [.. api.DescendantsAndSelf()
            .Select(node => node.Resolve().InlineRateLimit)
            .OfType<RateLimitPolicySpec>()
            .Distinct()];

        if (specs.Count == 0)
        {
            return;
        }

        services.AddRateLimiter(options =>
        {
            foreach (RateLimitPolicySpec spec in specs)
            {
                options.AddFixedWindowLimiter(RateLimitPolicyNaming.NameFor(spec), limiter =>
                {
                    limiter.PermitLimit = spec.PermitLimit;
                    limiter.Window = spec.Window;
                    limiter.QueueLimit = 0;
                    limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                });
            }
        });
    }

    private static void RegisterOutputCacheIfUsed(IServiceCollection services, ApiConfig api)
    {
        bool used = api.DescendantsAndSelf().Any(node => node.Resolve().OutputCacheDuration is not null);
        if (used)
        {
            services.AddOutputCache();
        }
    }
}
