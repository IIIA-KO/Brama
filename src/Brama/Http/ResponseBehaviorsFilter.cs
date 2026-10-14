using System.Diagnostics;
using System.Globalization;
using Brama.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Brama.Http;

/// <summary>
/// Applies the opt-in per-node behaviours that are just response headers or a log entry:
/// deprecation signalling, rate-limit headers, correlation-ID propagation and request logging.
/// </summary>
/// <remarks>
/// Constructed once per endpoint with that endpoint's already-resolved configuration, so nothing
/// about the config tree is consulted while a request is in flight. An endpoint that opted into
/// none of these is never given this filter at all.
/// </remarks>
internal sealed class ResponseBehaviorsFilter : IEndpointFilter
{
    private readonly ResolvedConfig _config;
    private readonly string _route;
    private readonly string? _sunset;

    public ResponseBehaviorsFilter(ResolvedConfig config, string route)
    {
        _config = config;
        _route = route;
        _sunset = config.Sunset?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            .ToString("r", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether any of these behaviours is turned on for a given endpoint.</summary>
    public static bool IsNeededFor(ResolvedConfig config) =>
        config.Deprecated
        || config.Sunset is not null
        || config.EmitRateLimitHeaders
        || config.CorrelationIdHeader is not null
        || config.LogRequests;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        HttpContext http = context.HttpContext;

        WriteDeprecationHeaders(http);
        WriteRateLimitHeaders(http);

        IDisposable? correlationScope = ApplyCorrelationId(http);
        long startedAt = Stopwatch.GetTimestamp();

        LogWhenResponseStarts(http, startedAt);

        try
        {
            return await next(context);
        }
        finally
        {
            correlationScope?.Dispose();
        }
    }

    private void WriteDeprecationHeaders(HttpContext http)
    {
        if (_config.Deprecated)
        {
            http.Response.Headers["Deprecation"] = "true";
        }

        if (_sunset is not null)
        {
            http.Response.Headers["Sunset"] = _sunset;
        }
    }

    /// <summary>
    /// Emits the IETF-draft rate-limit headers Brama can source honestly.
    /// </summary>
    /// <remarks>
    /// Only for a policy authored inline in the tree, because only then does Brama know the limit.
    /// <c>RateLimit-Remaining</c> is deliberately absent: it needs the live partition's lease count,
    /// which ASP.NET Core's rate limiter does not expose to an endpoint filter, and a number
    /// reconstructed from a parallel counter would drift from the limiter actually enforcing.
    /// </remarks>
    private void WriteRateLimitHeaders(HttpContext http)
    {
        if (!_config.EmitRateLimitHeaders || _config.InlineRateLimit is not { } limit)
        {
            return;
        }

        http.Response.Headers["RateLimit-Limit"] = limit.PermitLimit.ToString(CultureInfo.InvariantCulture);
        http.Response.Headers["RateLimit-Reset"] =
            ((int)limit.Window.TotalSeconds).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reads the correlation ID, or mints one, puts it on the logger for the rest of the request,
    /// and echoes it — on failures too, since that is when a caller most needs it.
    /// </summary>
    private IDisposable? ApplyCorrelationId(HttpContext http)
    {
        if (_config.CorrelationIdHeader is not { } header)
        {
            return null;
        }

        string correlationId = http.Request.Headers[header].FirstOrDefault() is { Length: > 0 } incoming
            ? incoming
            : Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

        http.Response.Headers[header] = correlationId;

        ILogger logger = LoggerFor(http);

        return logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
    }

    /// <summary>
    /// Logs as the response headers go out.
    /// </summary>
    /// <remarks>
    /// This is the last moment that is still ordered before the client sees anything, and the
    /// status code is already final. Logging on completion instead would be marginally more
    /// accurate about duration but would race every caller — including a test — that observes the
    /// response and then asks what was logged.
    /// </remarks>
    private void LogWhenResponseStarts(HttpContext http, long startedAt)
    {
        if (!_config.LogRequests)
        {
            return;
        }

        ILogger logger = LoggerFor(http);
        LogLevel level = _config.LogLevel ?? LogLevel.Information;
        string method = http.Request.Method;

        http.Response.OnStarting(() =>
        {
            if (logger.IsEnabled(level))
            {
                double elapsedMilliseconds = Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

                BramaLog.Request(
                    logger,
                    level,
                    method,
                    _route,
                    http.Response.StatusCode,
                    elapsedMilliseconds,
                    _config.Version);
            }

            return Task.CompletedTask;
        });
    }

    private static ILogger LoggerFor(HttpContext http) =>
        http.RequestServices.GetService(typeof(ILoggerFactory)) is ILoggerFactory factory
            ? factory.CreateLogger("Brama.Request")
            : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
}
